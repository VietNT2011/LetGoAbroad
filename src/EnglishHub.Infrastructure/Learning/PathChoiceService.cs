using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record SwitchPathInput(Guid ClientRequestId, Guid PathVersionId, string ExpectedEtag, string Reason);
public sealed record PathChoiceAck(Guid RevisionId, Guid? PathVersionId, int Number, DateTimeOffset EffectiveAt, bool Changed);

/// <summary>VI: Dùng dưới khóa learning; khi đặt chỗ mới phải lấy thêm path-catalog. EN: Use under the learning lock and also the catalog lock when reserving new admissions.</summary>
public sealed partial class PathChoiceService(HubDbContext db, IClock clock, Receipts receipts)
{
    public async Task<PathChoiceRevision> Initialize(Enrollment enrollment, string action = "observed_baseline")
    {
        var latest = await db.PathChoiceRevisions.Where(x => x.EnrollmentId == enrollment.Id).OrderByDescending(x => x.Number).FirstOrDefaultAsync();
        if (latest is not null) return latest;
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == enrollment.UserId);
        var now = clock.UtcNow;
        var baseline = Snapshot(enrollment, 1, now, action, "", enrollment.PathTimeZoneId ?? enrollment.TimeZoneId, enrollment.PathStudyDays ?? user.StudyDays, now);
        baseline.ActivatedAt = now;
        db.PathChoiceRevisions.Add(baseline); Apply(enrollment, baseline); await db.SaveChangesAsync(); return baseline;
    }

    public async Task<PathChoiceRevision> RecordAdoption(Enrollment enrollment)
    {
        var latest = await Initialize(enrollment);
        var now = clock.UtcNow;
        var revision = Snapshot(enrollment, checked(latest.Number + 1), now, "legacy_adoption", "Áp dụng mẫu có phiên bản", enrollment.PathTimeZoneId!, enrollment.PathStudyDays!, now);
        revision.ActivatedAt = now;
        db.PathChoiceRevisions.Add(revision); Apply(enrollment, revision); await db.SaveChangesAsync(); return revision;
    }

    /// <summary>VI: Chỉ đổi con trỏ enrollment; không sửa task, deadline hoặc nguồn kết quả cũ. EN: Activate only the enrollment pointer, leaving existing task/deadline/evidence snapshots intact.</summary>
    public async Task<PathChoiceRevision> Resolve(Enrollment enrollment)
    {
        await Initialize(enrollment);
        return await ResolveAt(enrollment, clock.UtcNow);
    }

    // VI: Một mốc quan sát cho cả lệnh tránh đổi ngày giữa các lần đọc clock. EN: One observation instant keeps an operation consistent when execution crosses midnight.
    private async Task<PathChoiceRevision> ResolveAt(Enrollment enrollment, DateTimeOffset observedAt)
    {
        var active = await db.PathChoiceRevisions.Where(x => x.EnrollmentId == enrollment.Id && x.EffectiveAt <= observedAt).OrderByDescending(x => x.Number).FirstAsync();
        if (enrollment.PathRevisionNumber != active.Number)
        {
            Apply(enrollment, active);
            active.ActivatedAt ??= observedAt;
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.path_choice_activated", AggregateId = active.Id, OccurredAt = observedAt, PayloadJson = JsonSerializer.Serialize(new { userId = enrollment.UserId, revisionId = active.Id, active.PathVersionId }) });
        }
        return active;
    }

    public async Task<PathChoiceAck> Schedule(Enrollment enrollment, SwitchPathInput input)
    {
        const string scope = "path-choice"; var hash = Receipts.Hash(input); var replay = await receipts.Find(enrollment.UserId, scope, input.ClientRequestId, hash);
        if (replay is not null) return JsonSerializer.Deserialize<PathChoiceAck>(replay.ResponseJson)!;
        DomainError.Require(input.PathVersionId != Guid.Empty && !string.IsNullOrWhiteSpace(input.Reason) && input.Reason.Length <= 500, 422, "invalid_path_choice", "Chọn mẫu và ghi lý do tối đa 500 ký tự.");
        await Initialize(enrollment);
        var now = clock.UtcNow;
        var active = await ResolveAt(enrollment, now); var latest = await Initialize(enrollment);
        DomainError.Require(active.PathVersionId is not null, 409, "use_path_adoption", "Hãy áp dụng mẫu đầu tiên bằng thao tác dành cho lịch chưa ghim.");
        DomainError.Require(input.ExpectedEtag == Token(enrollment, active, latest), 412, "path_choice_conflict", "Lựa chọn lộ trình đã đổi. Hãy xem lịch sử mới để so sánh trước khi gửi lại.");
        PathChoiceRevision choice; var changed = latest.PathVersionId != input.PathVersionId;
        if (!changed) choice = latest;
        else
        {
            DomainError.Require(latest.EffectiveAt <= now || latest.Action != "calendar_change", 409, "calendar_change_pending", "Hãy giữ lịch đang áp dụng trước khi đặt lịch chuyển mẫu.");
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == enrollment.UserId);
            var pending = latest.EffectiveAt > now;
            var zone = pending ? latest.SchedulingTimeZoneId : active.UsesPathCalendar ? active.TimeZoneId : user.TimeZoneId;
            var effective = pending ? latest.EffectiveAt : GoalPlanService.NextMidnight(now, zone);
            var cancel = input.PathVersionId == active.PathVersionId;
            if (cancel)
            {
                // VI: Hủy yêu cầu giữ nguyên ngày bắt đầu/calendar cũ, không reset tiến độ. EN: Cancellation copies the active calendar/start date instead of restarting the path.
                choice = Snapshot(enrollment, checked(latest.Number + 1), effective, "cancel_pending_switch", input.Reason.Trim(), active.TimeZoneId, active.StudyDays, now);
            }
            else
            {
                var target = await db.PathVersions.Include(x => x.Template).SingleOrDefaultAsync(x => x.Id == input.PathVersionId && x.Status == PublicationStatus.Published && x.Template.RetiredAt == null)
                    ?? throw new DomainError(422, "path_unavailable", "Mẫu chưa được publish hoặc đã ngừng nhận đăng ký mới.");
                choice = Snapshot(enrollment, checked(latest.Number + 1), effective, "manual_switch", input.Reason.Trim(), zone, active.UsesPathCalendar ? active.StudyDays : user.StudyDays, now);
                choice.PathVersionId = target.Id; choice.Level = target.Template.Level; choice.SelectionReason = "manual_switch";
                choice.PathStartedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(effective, zone).DateTime);
                choice.CalendarAnchorOn = choice.PathStartedOn; choice.CalendarElapsedDays = 0;
                // VI: Quay lại version đã học thì tiếp tục calendar cũ; reservation đã hủy không được tính là đã học. EN: Resume a previously activated version's calendar, excluding cancelled reservations.
                var previous = await db.PathChoiceRevisions.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id && x.PathVersionId == target.Id && x.ActivatedAt != null).OrderByDescending(x => x.Number).FirstOrDefaultAsync();
                if (previous is not null) { choice.PathStartedOn = previous.PathStartedOn; choice.TimeZoneId = previous.TimeZoneId; choice.StudyDays = previous.StudyDays; choice.CalendarAnchorOn = previous.CalendarAnchorOn; choice.CalendarElapsedDays = previous.CalendarElapsedDays; choice.UsesPathCalendar = previous.UsesPathCalendar; }
            }
            choice.SchedulingTimeZoneId = zone;
            db.PathChoiceRevisions.Add(choice);
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.path_choice_scheduled", AggregateId = choice.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId = enrollment.UserId, revisionId = choice.Id, choice.PathVersionId, choice.EffectiveAt, choice.Action }) });
            await db.SaveChangesAsync();
        }
        var ack = new PathChoiceAck(choice.Id, choice.PathVersionId, choice.Number, choice.EffectiveAt, changed);
        receipts.Save(enrollment.UserId, scope, input.ClientRequestId, hash, enrollment.Id, ack); return ack;
    }

    public async Task<object> History(Enrollment enrollment)
    {
        await Initialize(enrollment); var observedAt = clock.UtcNow;
        var active = await ResolveAt(enrollment, observedAt); var latest = await Initialize(enrollment);
        // VI: History đọc no-tracking phải thấy metadata vừa áp dụng trong transaction này. EN: Flush activation before projecting untracked history so its first response includes the observed timestamp.
        await db.SaveChangesAsync();
        var rows = await db.PathChoiceRevisions.AsNoTracking().Include(x => x.PathVersion).Where(x => x.EnrollmentId == enrollment.Id).OrderByDescending(x => x.Number).Take(20).ToListAsync();
        if (rows.All(x => x.Id != active.Id)) rows.Add(await db.PathChoiceRevisions.AsNoTracking().Include(x => x.PathVersion).SingleAsync(x => x.Id == active.Id));
        return new { expectedEtag = Token(enrollment, active, latest), activeRevisionId = active.Id, activePathVersionId = active.PathVersionId, revisions = rows.OrderByDescending(x => x.Number).Select(x => new { x.Id, x.Number, x.PathVersionId, title = x.PathVersion == null ? "Lịch theo danh mục" : x.PathVersion.Title, version = x.PathVersion == null ? (int?)null : x.PathVersion.Version, x.Level, x.PlacementAttemptId, x.PathStartedOn, x.TimeZoneId, x.StudyDays, x.UsesPathCalendar, x.CalendarAnchorOn, x.CalendarElapsedDays, x.RecordedAt, x.EffectiveAt, x.ActivatedAt, x.SchedulingTimeZoneId, x.Action, x.SelectionReason, x.UserReason, status = x.Id == active.Id ? "active" : rows.Any(newer => newer.Number > x.Number && newer.EffectiveAt <= x.EffectiveAt) ? "superseded" : x.EffectiveAt > observedAt ? "scheduled" : "past" }).ToArray() };
    }

    private static PathChoiceRevision Snapshot(Enrollment e, int number, DateTimeOffset effective, string action, string reason, string zone, string days, DateTimeOffset recordedAt) => new()
    {
        EnrollmentId = e.Id,
        UserId = e.UserId,
        Number = number,
        PathVersionId = e.PathVersionId,
        Level = e.Level,
        PlacementAttemptId = e.PlacementAttemptId,
        PathStartedOn = e.PathStartedOn,
        TimeZoneId = zone,
        StudyDays = days,
        UsesPathCalendar = e.UsesPathCalendar,
        CalendarAnchorOn = e.PathCalendarAnchorOn,
        CalendarElapsedDays = e.PathCalendarElapsedDays,
        RecordedAt = recordedAt,
        EffectiveAt = effective,
        SchedulingTimeZoneId = zone,
        Action = action,
        SelectionReason = e.SelectionReason,
        UserReason = reason
    };
    private static void Apply(Enrollment e, PathChoiceRevision revision)
    {
        e.PathRevisionNumber = revision.Number; e.PathVersionId = revision.PathVersionId; e.Level = revision.Level; e.SelectionReason = revision.SelectionReason; e.PathStartedOn = revision.PathStartedOn;
        e.PathTimeZoneId = revision.PathVersionId is null ? null : revision.TimeZoneId; e.PathStudyDays = revision.PathVersionId is null ? null : revision.StudyDays;
        e.UsesPathCalendar = revision.UsesPathCalendar; e.PathCalendarAnchorOn = revision.CalendarAnchorOn; e.PathCalendarElapsedDays = revision.CalendarElapsedDays;
    }
    private static string Token(Enrollment enrollment, PathChoiceRevision active, PathChoiceRevision latest) => $"\"path-choice-{Receipts.Hash(new { enrollment.Id, active = active.Id, latest = latest.Id })}\"";
}
