using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record RegeneratePlanInput(Guid ClientRequestId, DateOnly LocalDate, string ExpectedEtag);
public sealed record RegenerationAck(DateOnly LocalDate, string PlanEtag, Guid[] RetainedTaskIds, Guid[] SupersededTaskIds, Guid[] NewTaskIds, bool Changed);
public sealed record TaskStartAck(Guid Id, DateTimeOffset? StartedAt, string Status);

public sealed partial class LearningService
{
    /// <summary>VI: Ghi bắt đầu không ghi hoàn thành; cùng khóa với regenerate. EN: Starting is not completion and shares the regeneration transaction lock.</summary>
    public Task<TaskStartAck> StartTask(Guid userId, Guid taskId) => tx.Run($"learning:{userId}", async () =>
    {
        var task = await db.DailyTasks.SingleOrDefaultAsync(x => x.Id == taskId && x.UserId == userId)
            ?? throw new DomainError(404, "task_not_found", "Không tìm thấy nhiệm vụ.");
        DomainError.Require(task.Status is "pending" or "completed", 409, "task_unavailable", "Nhiệm vụ đã được thay hoặc quá ngày. Hãy tải lại lịch.");
        if (task.StartedAt is null && task.Status == "pending")
        {
            var zone = task.TimeZoneId ?? (await db.Users.SingleAsync(x => x.Id == userId)).TimeZoneId;
            var active = await db.Attempts.AnyAsync(a => a.UserId == userId && a.ContentVersionId == task.ContentVersionId && a.StartedAt >= task.CreatedAt && a.Status == AttemptStatus.InProgress && a.DeadlineAt > clock.UtcNow);
            DomainError.Require(task.LocalDate == LocalDate(clock.UtcNow, zone) || active, 409, "task_day_closed", "Hãy tải lại nhiệm vụ của ngày đang học.");
            task.StartedAt = clock.UtcNow;
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.task_started", AggregateId = task.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId, taskId }) });
        }
        return new TaskStartAck(task.Id, task.StartedAt, task.Status);
    });

    /// <summary>VI: Receipt trước kiểm tra ngày giúp retry sau nửa đêm trả đúng ACK cũ. EN: Replay before date checks so a midnight retry cannot mutate the next plan.</summary>
    public Task<RegenerationAck> Regenerate(Guid userId, RegeneratePlanInput input) => tx.Run($"learning:{userId}", async () =>
    {
        const string scope = "plan-regenerate"; var hash = Receipts.Hash(input);
        var duplicate = await receipts.Find(userId, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<RegenerationAck>(duplicate.ResponseJson)!;
        // VI: Cookie validation có thể đã track hồ sơ trước khi khóa được lấy. EN: Read fresh preferences after the lock even if cookie validation tracked an older profile.
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
        var today = LocalDate(clock.UtcNow, user.TimeZoneId);
        DomainError.Require(input.LocalDate == today, 409, "plan_day_changed", "Ngày học đã đổi. Hãy tải lịch mới trước khi lập lại.");
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId) ?? throw new DomainError(409, "enrollment_required", "Hãy tạo lộ trình trước.");
        await pathChoices.Resolve(enrollment);
        var goal = await goals.Resolve(enrollment, user);
        await SyncCompleted(userId, today, user.TimeZoneId); await MarkMissed(userId, user.TimeZoneId);
        DomainError.Require(input.ExpectedEtag == await PlanEtag(enrollment, today, user.TimeZoneId, user.StudyDays), 412, "plan_conflict", "Nhiệm vụ đã đổi ở phiên khác. Hãy tải lại để xem trước khi lập lại.");
        var current = await db.DailyTasks.Where(x => x.EnrollmentId == enrollment.Id && x.Status != "superseded" && (x.LocalDate == today || x.Status == "pending")).OrderBy(x => x.LocalDate).ThenBy(x => x.SlotKey).ToArrayAsync();
        var attempted = await db.DailyTasks.Where(x => x.EnrollmentId == enrollment.Id && db.Attempts.Any(a => a.UserId == userId && a.ContentVersionId == x.ContentVersionId && a.StartedAt >= x.CreatedAt)).Select(x => x.Id).ToArrayAsync();
        var retained = current.Where(x => x.Status == "completed" || x.StartedAt is not null || attempted.Contains(x.Id) || x.LocalDate != today).ToArray();
        var replace = current.Except(retained).ToArray();
        var remaining = Math.Max(0, enrollment.MinutesPerDay - retained.Sum(x => x.EstimatedMinutes)); var limit = Math.Max(0, 3 - retained.Length);
        var lastSlot = await db.DailyTasks.Where(x => x.EnrollmentId == enrollment.Id && x.LocalDate == today).Select(x => (int?)x.SlotKey).MaxAsync() ?? -1;
        var proposed = user.StudyDays.Split(',').Contains(((int)today.DayOfWeek).ToString()) && remaining > 0 && limit > 0
            ? await BuildDailyTasks(enrollment, user, today, goal.Id, remaining, limit, checked(lastSlot + 1), retained.Select(x => x.ContentVersionId).ToArray(), replace.Select(x => x.Id).ToArray()) : [];
        // VI: Cùng lựa chọn giữ nguyên ID, timestamp và ngân sách; không ghi lịch sử thay vô ích. EN: Identical assignments retain their IDs/timestamps without a needless replacement effect.
        var changed = !replace.Select(Assignment).Order().SequenceEqual(proposed.Select(Assignment).Order());
        if (changed)
        {
            DomainError.Require(await db.OutboxEvents.CountAsync(x => x.Type == "learning.plan_regenerated" && x.AggregateId == enrollment.Id && x.OccurredAt >= clock.UtcNow.AddHours(-24)) < 5, 429, "regeneration_limit", "Tối đa 5 lần thay nhiệm vụ trong 24 giờ. Lịch đang có vẫn dùng được.");
            foreach (var task in replace) task.Status = "superseded";
            // VI: Giải phóng unique pending-node trước khi thêm phiên bản thay thế, vẫn trong một transaction. EN: Release pending-node uniqueness before inserting replacements in the same transaction.
            await db.SaveChangesAsync(); db.DailyTasks.AddRange(proposed); await db.SaveChangesAsync();
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.plan_regenerated", AggregateId = enrollment.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId, localDate = today, retained = retained.Select(x => x.Id), superseded = replace.Select(x => x.Id), created = proposed.Select(x => x.Id) }) });
        }
        var ack = new RegenerationAck(today, await PlanEtag(enrollment, today, user.TimeZoneId, user.StudyDays), changed ? retained.Select(x => x.Id).ToArray() : current.Select(x => x.Id).ToArray(), changed ? replace.Select(x => x.Id).ToArray() : [], changed ? proposed.Select(x => x.Id).ToArray() : [], changed);
        receipts.Save(userId, scope, input.ClientRequestId, hash, enrollment.Id, ack); return ack;
    });

    private static string Assignment(DailyTask task) => Receipts.Hash(new { task.ContentVersionId, task.PathVersionId, task.PathNodeId, task.SourceTaskId, task.GoalRevisionId, task.TimeZoneId, task.EstimatedMinutes });

    /// <summary>VI: Token ràng buộc lịch, revision mục tiêu và phiên bản task theo owner. EN: Bind the owner calendar, active goal and relevant task versions into one plan token.</summary>
    private async Task<string> PlanEtag(Enrollment enrollment, DateOnly today, string zone, string studyDays)
    {
        var tasks = await db.DailyTasks.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id && (x.LocalDate == today || x.Status == "pending")).Select(x => new { x.Id, x.RowVersion }).ToArrayAsync();
        return $"\"plan-{Receipts.Hash(new { today, zone, studyDays, enrollment.Id, enrollment.PathVersionId, enrollment.PathRevisionNumber, enrollment.PlanRevision, tasks = tasks.OrderBy(x => x.Id).ToArray() })}\"";
    }

    /// <summary>VI: Dashboard và regenerate dùng chung lựa chọn; phần đã giữ tiêu ngân sách trước. EN: Dashboard and regeneration share selection after reserving protected assignments' budget.</summary>
    private async Task<DailyTask[]> BuildDailyTasks(Enrollment enrollment, AppUser user, DateOnly today, Guid goal, int budget, int limit, int slotOffset, Guid[] reservedContent, Guid[] ignoredTasks)
    {
        if (enrollment.PathVersionId is not null) return await BuildPathTasks(enrollment, user, today, goal, budget, limit, slotOffset, reservedContent, ignoredTasks);
        var doneRoots = await db.LessonProgress.Where(x => x.UserId == user.Id).Select(x => x.ContentVersion.RootId).Distinct().ToArrayAsync();
        var versions = await db.ContentVersions.Include(x => x.Root).Where(x => x.Status == PublicationStatus.Published && x.Root.CurrentPublishedVersionId == x.Id && !x.IsFixture && !reservedContent.Contains(x.Id)).OrderBy(x => x.Root.Slug).ToArrayAsync();
        var lessons = versions.Where(x => x.Root.Kind == ContentKind.Lesson && !doneRoots.Contains(x.RootId)).Select(x => new PlanCandidate(x.Id, x.Minutes, "lesson", x.CurriculumOrder));
        var practice = versions.Where(x => x.Root.Kind == ContentKind.Assessment && x.Root.Slug != "placement-demo").Select((x, i) => new PlanCandidate(x.Id, x.Minutes, "practice", i));
        return Planner.Select(lessons, practice, budget).Take(limit).Select((item, slot) => new DailyTask { EnrollmentId = enrollment.Id, UserId = user.Id, LocalDate = today, SlotKey = checked(slotOffset + slot), ContentVersionId = item.ContentVersionId, EstimatedMinutes = item.Minutes, CreatedAt = clock.UtcNow, TimeZoneId = user.TimeZoneId, GoalRevisionId = goal, ReasonCode = item.Kind == "lesson" ? "FOUNDATION_NEXT" : "SHORT_PRACTICE", ReasonText = item.Kind == "lesson" ? "Bài tiếp theo chưa hoàn thành trong lộ trình; quy tắc v1." : "Luyện ngắn để củng cố kiến thức, vừa ngân sách thời gian; quy tắc v1." }).ToArray();
    }
}
