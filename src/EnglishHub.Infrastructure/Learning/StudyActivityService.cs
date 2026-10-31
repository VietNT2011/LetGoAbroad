using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record StartStudySession(Guid ClientSessionId, string Kind);
public sealed record StudyPulse(int Sequence, bool IsActive);
public sealed record StudySessionView(Guid Id, int LastSequence, DateTimeOffset ExpiresAt, bool IsExpired);
public sealed record StudyPulseAck(Guid SessionId, int Sequence, DateTimeOffset ReceivedAt, long CreditedMilliseconds);
public sealed record StudyTimeSummary(int PolicyVersion, int WindowDays, DateTimeOffset FromUtc, DateTimeOffset ToUtc, long EstimatedSeconds);

/// <summary>VI: Heartbeat là tín hiệu tự báo có giới hạn, không chứng minh sự chú ý hay cộng XP. EN: Bounded self-reported activity estimates neither prove attention nor award XP.</summary>
public sealed class StudyActivityService(HubDbContext db, Transactions tx, IClock clock)
{
    public Task<StudySessionView> Start(Guid user, StartStudySession input) => tx.Run($"activity:{user}", async () =>
    {
        DomainError.Require(input.ClientSessionId != Guid.Empty && input.Kind is "lesson" or "assessment" or "cards", 422, "invalid_study_session", "Phiên học cần ID và loại hợp lệ.");
        var existing = await db.ActivitySessions.SingleOrDefaultAsync(x => x.UserId == user && x.ClientSessionId == input.ClientSessionId);
        if (existing is not null)
        {
            DomainError.Require(existing.Kind == input.Kind, 409, "idempotency_conflict", "ID phiên đã được dùng với loại học khác.");
            return View(existing);
        }
        var now = clock.UtcNow;
        var session = new ActivitySession { UserId = user, ClientSessionId = input.ClientSessionId, Kind = input.Kind, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddHours(24) };
        db.ActivitySessions.Add(session); return View(session);
    });

    public async Task<StudySessionView> Get(Guid user, Guid id) => View(await Owned(user, id, false));

    public Task<StudyPulseAck> Pulse(Guid user, Guid id, StudyPulse input) => tx.Run($"activity:{user}", async () =>
    {
        var session = await Owned(user, id);
        DomainError.Require(input.Sequence is > 0 and <= 1000000, 422, "invalid_activity_sequence", "Sequence không hợp lệ.");
        // VI: Replay trước expiry/sequence, không cập nhật last-seen hay nhận thêm thời gian. EN: Replay precedes expiry/order checks and never refreshes last-seen or credits a second interval.
        var duplicate = await db.ActivityHeartbeats.SingleOrDefaultAsync(x => x.SessionId == id && x.Sequence == input.Sequence && x.UserId == user);
        if (duplicate is not null)
        {
            DomainError.Require(duplicate.IsActive == input.IsActive, 409, "idempotency_conflict", "Cùng sequence nhưng trạng thái khác.");
            return Ack(duplicate);
        }
        var now = clock.UtcNow;
        DomainError.Require(now < session.ExpiresAt, 409, "activity_session_expired", "Phiên đo đã hết hạn; hãy mở phiên mới.");
        DomainError.Require(input.Sequence == session.LastSequence + 1, 409, "activity_sequence_conflict", "Heartbeat phải đi theo thứ tự.");
        var start = session.LastSeenAt > now.AddSeconds(-30) ? session.LastSeenAt : now.AddSeconds(-30);
        var credited = session.WasActive && now > start ? now.UtcTicks - start.UtcTicks : 0;
        var pulse = new ActivityHeartbeat { SessionId = id, UserId = user, Sequence = input.Sequence, IsActive = input.IsActive, ReceivedAt = now, CreditedTicks = credited };
        db.ActivityHeartbeats.Add(pulse);
        if (credited > 0) db.ActivityIntervals.Add(new ActivityInterval { UserId = user, SessionId = id, Sequence = input.Sequence, StartAt = start, EndAt = now, DurationTicks = credited });
        // VI: Clock lùi không tạo khoảng âm hay làm lùi mốc đã thấy. EN: A backward server clock neither creates negative time nor rewinds the observed boundary.
        if (now > session.LastSeenAt) session.LastSeenAt = now;
        session.LastSequence = input.Sequence; session.WasActive = input.IsActive;
        return Ack(pulse);
    });

    public async Task<StudyTimeSummary> Read(Guid user, int days = 7)
    {
        DomainError.Require(days is 7 or 30, 422, "invalid_activity_window", "Chọn khoảng7 hoặc30 ngày.");
        var now = clock.UtcNow.ToUniversalTime(); var from = now.AddDays(-days);
        var intervals = await db.ActivityIntervals.AsNoTracking().Where(x => x.UserId == user && x.EndAt > from && x.StartAt < now)
            .Select(x => new { x.StartAt, x.EndAt }).ToArrayAsync();
        var ticks = StudyIntervalUnion.Ticks(intervals.Select(x => new StudyRange(x.StartAt, x.EndAt)), from, now);
        return new(1, days, from, now, ticks / TimeSpan.TicksPerSecond);
    }

    public async Task<StudyCalendarSnapshot> ReadCalendar(Guid user, int days = 7)
    {
        var zoneId = await db.Users.AsNoTracking().Where(x => x.Id == user).Select(x => x.TimeZoneId).SingleOrDefaultAsync()
            ?? throw new DomainError(401, "unauthenticated", "Vui lòng đăng nhập.");
        var window = StudyCalendarProjector.Window(TimeZoneInfo.FindSystemTimeZoneById(zoneId), clock.UtcNow, days);
        var intervals = await db.ActivityIntervals.AsNoTracking().Where(x => x.UserId == user && x.EndAt > window.FromUtc && x.StartAt < window.ToUtc)
            .Select(x => new { x.StartAt, x.EndAt }).ToArrayAsync();
        return StudyCalendarProjector.Project(intervals.Select(x => new StudyRange(x.StartAt, x.EndAt)), window);
    }

    private async Task<ActivitySession> Owned(Guid user, Guid id, bool tracking = true) => await (tracking ? db.ActivitySessions : db.ActivitySessions.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id && x.UserId == user)
        ?? throw new DomainError(404, "study_session_not_found", "Không tìm thấy phiên học của bạn.");
    private StudySessionView View(ActivitySession session) => new(session.Id, session.LastSequence, session.ExpiresAt, clock.UtcNow >= session.ExpiresAt);
    private static StudyPulseAck Ack(ActivityHeartbeat pulse) => new(pulse.SessionId, pulse.Sequence, pulse.ReceivedAt, pulse.CreditedTicks / TimeSpan.TicksPerMillisecond);
}
