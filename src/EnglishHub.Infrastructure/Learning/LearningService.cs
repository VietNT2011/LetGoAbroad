using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Learning;

public sealed record EnrollInput(bool StartFromBasics, Guid? PlacementAttemptId = null);
public sealed record LessonAnswer(Guid QuestionId, Guid SelectedOptionId);
public sealed record CompleteLessonInput(Guid ClientRequestId, LessonAnswer[] Answers);
public sealed record LessonFeedback(Guid QuestionId, bool IsCorrect, Guid CorrectOptionId, string Explanation);
public sealed record LessonCheck(bool Completed, LessonFeedback[] Feedback);

/// <summary>VI: Tiến độ dựa trên quiz/kết quả đã lưu. EN: Completion comes from persisted quiz evidence, never from simply opening a page.</summary>
public sealed class LearningService(HubDbContext db, Transactions tx, Receipts receipts, ContentService content, IClock clock)
{
    public Task<Enrollment> Enroll(Guid userId, EnrollInput input) => tx.Run($"learning:{userId}", async () =>
    {
        var existing = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        if (existing is not null) return existing;
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var level = "foundation";
        if (!input.StartFromBasics)
        {
            var placement = await db.Attempts.Include(x => x.Result).Include(x => x.ContentVersion).ThenInclude(x => x.Root).SingleOrDefaultAsync(x => x.Id == input.PlacementAttemptId && x.UserId == userId && x.Mode == "placement");
            DomainError.Require(placement?.Result is not null && !placement.ContentVersion.IsFixture && placement.ContentVersion.Root.Slug == "placement-demo", 422, "placement_required", "Hoàn thành bài đánh giá đầu vào hoặc chọn bắt đầu từ căn bản.");
            level = Planner.PlacementLevel(placement!.Result!.CorrectCount, placement.Result.TotalCount);
        }
        var enrollment = new Enrollment { UserId = userId, Level = level, TargetScore = user.TargetScore, MinutesPerDay = user.MinutesPerDay, TimeZoneId = user.TimeZoneId, CreatedAt = clock.UtcNow };
        db.Enrollments.Add(enrollment); return enrollment;
    });

    public Task<object> Dashboard(Guid userId) => tx.Run($"learning:{userId}", async () =>
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(user.TimeZoneId)).DateTime);
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        if (enrollment is not null)
        {
            if (enrollment.TargetScore != user.TargetScore || enrollment.MinutesPerDay != user.MinutesPerDay || enrollment.TimeZoneId != user.TimeZoneId)
            { enrollment.PlanRevision++; enrollment.TargetScore = user.TargetScore; enrollment.MinutesPerDay = user.MinutesPerDay; enrollment.TimeZoneId = user.TimeZoneId; }
            var tasksExist = await db.DailyTasks.AnyAsync(x => x.EnrollmentId == enrollment.Id && x.LocalDate == today);
            if (!tasksExist && user.StudyDays.Split(',').Contains(((int)today.DayOfWeek).ToString()))
            {
                var doneRoots = await db.LessonProgress.Where(x => x.UserId == userId).Select(x => x.ContentVersion.RootId).Distinct().ToArrayAsync();
                var versions = await db.ContentVersions.Include(x => x.Root).Where(x => x.Status == PublicationStatus.Published && x.Root.CurrentPublishedVersionId == x.Id && !x.IsFixture).ToListAsync();
                var lessons = versions.Where(x => x.Root.Kind == ContentKind.Lesson && !doneRoots.Contains(x.RootId)).Select(x => new PlanCandidate(x.Id, x.Minutes, "lesson", x.CurriculumOrder));
                var practices = versions.Where(x => x.Root.Kind == ContentKind.Assessment && x.Root.Slug != "placement-demo").Select((x,i) => new PlanCandidate(x.Id, x.Minutes, "practice", i));
                var selected = Planner.Select(lessons, practices, user.MinutesPerDay);
                foreach (var (item, slot) in selected.Select((item, slot) => (item, slot)))
                    db.DailyTasks.Add(new DailyTask { EnrollmentId = enrollment.Id, UserId = userId, LocalDate = today, SlotKey = slot, ContentVersionId = item.ContentVersionId, EstimatedMinutes = item.Minutes, CreatedAt = clock.UtcNow,
                        ReasonCode = item.Kind == "lesson" ? "FOUNDATION_NEXT" : "SHORT_PRACTICE", ReasonText = item.Kind == "lesson" ? "Bài tiếp theo chưa hoàn thành trong lộ trình; quy tắc v1." : "Luyện ngắn để củng cố kiến thức, vừa ngân sách thời gian; quy tắc v1." });
                await db.SaveChangesAsync();
            }
            await SyncCompleted(userId, today, user.TimeZoneId);
        }
        var tasks = await db.DailyTasks.AsNoTracking().Where(x => x.UserId == userId && x.LocalDate == today).OrderBy(x => x.SlotKey).Select(x => new { x.Id, x.ContentVersionId, x.ContentVersion.Title, kind = x.ContentVersion.Root.Kind, x.EstimatedMinutes, x.Status, x.ReasonCode, x.ReasonText }).ToListAsync();
        var lessonsCompleted = await db.LessonProgress.CountAsync(x => x.UserId == userId);
        var attempts = await db.Attempts.Where(x => x.UserId == userId && x.Status == AttemptStatus.Graded && !x.ContentVersion.IsFixture && x.Mode != "mistake_review").Select(x => x.Result!).ToListAsync();
        return (object)new { localDate = today, timeZoneId = user.TimeZoneId, enrollment = enrollment is null ? null : new { enrollment.Id, enrollment.Level, enrollment.TargetScore, enrollment.MinutesPerDay, enrollment.RuleVersion, enrollment.PlanRevision }, tasks,
            statistics = new { lessonsCompleted, attemptsCompleted = attempts.Count, questionsCorrect = attempts.Sum(x => x.CorrectCount), questionsObserved = attempts.Sum(x => x.TotalCount) } };
    });

    public Task<LessonCheck> CompleteLesson(Guid user, Guid versionId, CompleteLessonInput input) => tx.Run($"learning:{user}", async () =>
    {
        var scope = $"lesson:{versionId}"; var hash = Receipts.Hash(input); var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<LessonCheck>(duplicate.ResponseJson)!;
        var lesson = await content.FullVersions.SingleOrDefaultAsync(x => x.Id == versionId && x.Root.Kind == ContentKind.Lesson && x.Status == PublicationStatus.Published) ?? throw new DomainError(404, "lesson_not_found", "Không tìm thấy bài học.");
        var questions = lesson.Groups.SelectMany(x => x.Questions).ToArray();
        DomainError.Require(questions.Length > 0 && input.Answers.Length == questions.Length && input.Answers.Select(x => x.QuestionId).Distinct().Count() == questions.Length && input.Answers.All(a => questions.Any(q => q.Id == a.QuestionId && q.Options.Any(o => o.Id == a.SelectedOptionId))), 422, "invalid_lesson_answers", "Hãy trả lời đầy đủ quiz của bài học.");
        var feedback = questions.Select(q => new LessonFeedback(q.Id, input.Answers.Single(a => a.QuestionId == q.Id).SelectedOptionId == q.Options.Single(o => o.IsCorrect).Id, q.Options.Single(o => o.IsCorrect).Id, q.Explanation)).ToArray();
        var passed = feedback.All(x => x.IsCorrect);
        if (passed && !await db.LessonProgress.AnyAsync(x => x.UserId == user && x.ContentVersionId == versionId))
        {
            var progress = new LessonProgress { UserId = user, ContentVersionId = versionId, CompletedAt = clock.UtcNow }; db.LessonProgress.Add(progress);
            db.OutboxEvents.Add(new OutboxEvent { Type = "lesson.completed", AggregateId = progress.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId = user, contentVersionId = versionId }) });
        }
        var result = new LessonCheck(passed, feedback);
        receipts.Save(user, scope, input.ClientRequestId, hash, versionId, result);
        return result;
    });

    private async Task SyncCompleted(Guid user, DateOnly date, string timezone)
    {
        var tasks = await db.DailyTasks.Where(x => x.UserId == user && x.LocalDate == date && x.Status != "completed").ToListAsync();
        foreach (var task in tasks)
        {
            var progress = await db.LessonProgress.FirstOrDefaultAsync(x => x.UserId == user && x.ContentVersionId == task.ContentVersionId);
            var attempt = progress is null ? await db.Attempts.FirstOrDefaultAsync(x => x.UserId == user && x.ContentVersionId == task.ContentVersionId && x.Status == AttemptStatus.Graded && x.Result!.FinalizedAt >= task.CreatedAt) : null;
            if (progress is null && attempt is null) continue;
            task.Status = "completed";
            db.TaskCompletions.Add(new TaskCompletion { DailyTaskId = task.Id, LessonProgressId = progress?.Id, AttemptId = attempt?.Id, CompletedAt = clock.UtcNow, LocalDate = date, TimeZoneId = timezone });
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.task_completed", AggregateId = task.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId = user, taskId = task.Id, localDate = date, timeZoneId = timezone }) });
        }
        await db.SaveChangesAsync();
    }
}
