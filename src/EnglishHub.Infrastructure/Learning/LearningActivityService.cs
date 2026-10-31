using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record LearningActivityCounts(int LessonsCompleted, int PracticeAttempts, int FullMockAttempts)
{
    public int TotalActivities => LessonsCompleted + PracticeAttempts + FullMockAttempts;
}
public sealed record LearningActivityDay(DateOnly Date, int LessonsCompleted, int PracticeAttempts, int FullMockAttempts)
{
    public int TotalActivities => LessonsCompleted + PracticeAttempts + FullMockAttempts;
}
public sealed record LearningActivitySnapshot(int PolicyVersion, int WindowDays, string TimeZoneId, DateOnly FromDate,
    DateOnly ToDate, int ActiveDays, LearningActivityCounts Totals, LearningActivityDay[] Days);

/// <summary>VI: Đếm nguồn hoàn thành thật trong cửa sổ ngày địa phương, không tạo activity khi đọc dashboard. EN: Read completion sources directly without producing activity or changing learning state.</summary>
public sealed class LearningActivityService(HubDbContext db, IClock clock)
{
    public async Task<LearningActivitySnapshot> Read(Guid user, int days = 7, CancellationToken ct = default)
    {
        DomainError.Require(days is 7 or 30, 422, "invalid_activity_window", "Chọn khoảng 7 hoặc 30 ngày.");
        var timeZoneId = await db.Users.AsNoTracking().Where(x => x.Id == user).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct)
            ?? throw new DomainError(401, "unauthenticated", "Vui lòng đăng nhập.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = today.AddDays(1 - days);
        // VI: Lọc UTC rộng tối đa 14 giờ rồi chốt ngày sau chuyển zone; không giả định mỗi ngày có 24 giờ hay midnight luôn tồn tại. EN: Widen the UTC bound by the maximum offset, then bucket local dates without assuming 24-hour days or a valid local midnight.
        var lowerBound = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(-14);
        var lessons = db.LessonProgress.AsNoTracking().Where(x => x.UserId == user && x.ContentVersion.Root.Kind == ContentKind.Lesson && !x.ContentVersion.IsFixture)
            .Select(x => new { At = x.CompletedAt, Kind = 0 });
        var attempts = db.Attempts.AsNoTracking().Where(x => x.UserId == user && x.Status == AttemptStatus.Graded && x.Result != null && x.Result.TotalCount > 0
                && x.Result.CorrectCount >= 0 && x.Result.CorrectCount <= x.Result.TotalCount && !x.ContentVersion.IsFixture
                && (x.Mode == "mini" || x.Mode == "practice" || x.Mode == "placement" || x.Mode == "full_mock"))
            .Select(x => new { At = x.Result!.FinalizedAt, Kind = x.Mode == "full_mock" ? 2 : 1 });
        // VI: UNION ALL chỉ lấy thời điểm/loại; không join câu hỏi hoặc task làm nhân số lượt, không đọc khóa đáp án. EN: A single narrow union avoids multiplying sources through questions/tasks and never reads grading keys.
        var events = await lessons.Concat(attempts).Where(x => x.At >= lowerBound && x.At <= now).ToArrayAsync(ct);
        var byDate = events.Select(x => new { Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.At, zone).DateTime), x.Kind })
            .Where(x => x.Date >= first && x.Date <= today).GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToArray());
        var result = Enumerable.Range(0, days).Select(offset =>
        {
            var date = first.AddDays(offset);
            if (!byDate.TryGetValue(date, out var items)) return new LearningActivityDay(date, 0, 0, 0);
            return new LearningActivityDay(date, items.Count(x => x.Kind == 0), items.Count(x => x.Kind == 1), items.Count(x => x.Kind == 2));
        }).ToArray();
        // VI: Đây là số lượt đã chấm, kể cả full mock có ôn/xem lời giải; độ chính xác chưa lộ đáp án vẫn do SkillEvidence quản lý riêng. EN: Participation counts include disclosed full mocks; SkillEvidence separately owns unseen-performance eligibility.
        return new(1, days, timeZoneId, first, today, result.Count(x => x.TotalActivities > 0),
            new(result.Sum(x => x.LessonsCompleted), result.Sum(x => x.PracticeAttempts), result.Sum(x => x.FullMockAttempts)), result);
    }
}
