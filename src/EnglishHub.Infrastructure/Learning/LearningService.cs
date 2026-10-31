using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Learning;

public sealed record EnrollInput(bool StartFromBasics, Guid? PlacementAttemptId = null, Guid? PathVersionId = null);
public sealed record LessonAnswer(Guid QuestionId, Guid? SelectedOptionId, string? AnswerText = null);
public sealed record CompleteLessonInput(Guid ClientRequestId, LessonAnswer[] Answers);
public sealed record LessonFeedback(Guid QuestionId, bool IsCorrect, Guid? CorrectOptionId, string Explanation);
public sealed record LessonCheck(bool Completed, LessonFeedback[] Feedback);

/// <summary>VI: Tiến độ dựa trên quiz/kết quả đã lưu. EN: Completion comes from persisted quiz evidence, never from simply opening a page.</summary>
public sealed partial class LearningService(HubDbContext db, Transactions tx, Receipts receipts, ContentService content, IClock clock, PathService paths)
{
    /// <summary>VI: Khóa theo user ngăn hai enrollment từ nhiều tab. EN: A user-level lock prevents duplicate enrollment across tabs or API instances.</summary>
    public Task<Enrollment> Enroll(Guid userId, EnrollInput input) => tx.Run($"learning:{userId}", async () =>
    {
        var existing = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        if (existing is not null)
        {
            DomainError.Require(input.PathVersionId is null || input.PathVersionId == existing.PathVersionId, 409, "path_already_pinned", "Enrollment đang có không tự chuyển mẫu; người học cũ có thể chọn thao tác áp dụng mẫu riêng.");
            return existing;
        }
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var level = "foundation";
        if (!input.StartFromBasics)
        {
            var placement = await db.Attempts.Include(x => x.Result).Include(x => x.ContentVersion).ThenInclude(x => x.Root).SingleOrDefaultAsync(x => x.Id == input.PlacementAttemptId && x.UserId == userId && x.Mode == "placement");
            DomainError.Require(placement?.Result is not null && !placement.ContentVersion.IsFixture && placement.ContentVersion.Root.Slug == "placement-demo", 422, "placement_required", "Hoàn thành bài đánh giá đầu vào hoặc chọn bắt đầu từ căn bản.");
            level = Planner.PlacementLevel(placement!.Result!.CorrectCount, placement.Result.TotalCount);
        }
        var enrollment = new Enrollment { UserId = userId, Level = level, TargetScore = user.TargetScore, MinutesPerDay = user.MinutesPerDay, TimeZoneId = user.TimeZoneId, CreatedAt = clock.UtcNow };
        var path = input.PathVersionId is { } requested
            ? await db.PathVersions.Include(x => x.Template).SingleOrDefaultAsync(x => x.Id == requested && x.Status == PublicationStatus.Published)
            : await db.PathVersions.Include(x => x.Template).Where(x => x.Template.Level == level && x.Status == PublicationStatus.Published && x.Template.CurrentPublishedVersionId == x.Id).OrderBy(x => x.Template.Slug).FirstOrDefaultAsync();
        DomainError.Require(input.PathVersionId is null || path is not null, 422, "path_unavailable", "Chọn một version lộ trình đã publish.");
        enrollment.PlacementAttemptId = input.StartFromBasics ? null : input.PlacementAttemptId;
        enrollment.SelectionReason = input.PathVersionId is not null ? "manual_template" : input.StartFromBasics ? "learner_basics" : "placement_demo_v1";
        if (path is not null) PinPath(enrollment, path, user);
        db.Enrollments.Add(enrollment); return enrollment;
    });

    /// <summary>VI: Ngày học tính trong timezone hồ sơ và được ghim vào task. EN: Resolve the local study date from the profile timezone and pin it to generated tasks.</summary>
    public Task<object> Dashboard(Guid userId) => tx.Run($"learning:{userId}", async () =>
    {
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(user.TimeZoneId)).DateTime);
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        if (enrollment is not null)
        {
            if (enrollment.TargetScore != user.TargetScore || enrollment.MinutesPerDay != user.MinutesPerDay || enrollment.TimeZoneId != user.TimeZoneId)
            { enrollment.PlanRevision++; enrollment.TargetScore = user.TargetScore; enrollment.MinutesPerDay = user.MinutesPerDay; enrollment.TimeZoneId = user.TimeZoneId; }
            await SyncCompleted(userId, today, user.TimeZoneId);
            await MarkMissed(userId, user.TimeZoneId);
            var tasksExist = await db.DailyTasks.AnyAsync(x => x.EnrollmentId == enrollment.Id && (x.LocalDate == today || x.Status == "pending" && x.LocalDate < today));
            if (!tasksExist && user.StudyDays.Split(',').Contains(((int)today.DayOfWeek).ToString()))
            {
                if (enrollment.PathVersionId is not null) await GeneratePathTasks(enrollment, user, today);
                else
                {
                    var doneRoots = await db.LessonProgress.Where(x => x.UserId == userId).Select(x => x.ContentVersion.RootId).Distinct().ToArrayAsync();
                    var versions = await db.ContentVersions.Include(x => x.Root).Where(x => x.Status == PublicationStatus.Published && x.Root.CurrentPublishedVersionId == x.Id && !x.IsFixture).OrderBy(x => x.Root.Slug).ToListAsync();
                    var lessons = versions.Where(x => x.Root.Kind == ContentKind.Lesson && !doneRoots.Contains(x.RootId)).Select(x => new PlanCandidate(x.Id, x.Minutes, "lesson", x.CurriculumOrder));
                    var practices = versions.Where(x => x.Root.Kind == ContentKind.Assessment && x.Root.Slug != "placement-demo").Select((x, i) => new PlanCandidate(x.Id, x.Minutes, "practice", i));
                    var selected = Planner.Select(lessons, practices, user.MinutesPerDay);
                    foreach (var (item, slot) in selected.Select((item, slot) => (item, slot)))
                        db.DailyTasks.Add(new DailyTask
                        {
                            EnrollmentId = enrollment.Id,
                            UserId = userId,
                            LocalDate = today,
                            SlotKey = slot,
                            ContentVersionId = item.ContentVersionId,
                            EstimatedMinutes = item.Minutes,
                            CreatedAt = clock.UtcNow,
                            TimeZoneId = user.TimeZoneId,
                            ReasonCode = item.Kind == "lesson" ? "FOUNDATION_NEXT" : "SHORT_PRACTICE",
                            ReasonText = item.Kind == "lesson" ? "Bài tiếp theo chưa hoàn thành trong lộ trình; quy tắc v1." : "Luyện ngắn để củng cố kiến thức, vừa ngân sách thời gian; quy tắc v1."
                        });
                }
                await db.SaveChangesAsync();
            }
            await SyncCompleted(userId, today, user.TimeZoneId);
        }
        var tasks = await db.DailyTasks.AsNoTracking().Where(x => x.UserId == userId && x.Status != "superseded" && (x.LocalDate == today || x.Status == "pending" && x.LocalDate < today)).OrderBy(x => x.LocalDate).ThenBy(x => x.SlotKey).Select(x => new
        {
            x.Id,
            x.ContentVersionId,
            x.ContentVersion.Title,
            kind = x.ContentVersion.Root.Kind,
            x.EstimatedMinutes,
            x.Status,
            x.ReasonCode,
            x.ReasonText,
            x.PathVersionId,
            x.PathNodeId,
            x.SourceTaskId,
            x.TimeZoneId,
            x.LocalDate,
            carriedFromPreviousDate = x.LocalDate < today,
            activeAttemptId = db.Attempts.Where(a => a.UserId == userId && a.ContentVersionId == x.ContentVersionId && a.Status == AttemptStatus.InProgress && a.StartedAt >= x.CreatedAt && a.DeadlineAt > clock.UtcNow).OrderByDescending(a => a.StartedAt).Select(a => (Guid?)a.Id).FirstOrDefault()
        }).ToListAsync();
        var lessonsCompleted = await db.LessonProgress.CountAsync(x => x.UserId == userId);
        var attempts = await db.Attempts.Where(x => x.UserId == userId && x.Status == AttemptStatus.Graded && !x.ContentVersion.IsFixture && x.Mode != "mistake_review").Select(x => x.Result!).ToListAsync();
        return (object)new
        {
            localDate = today,
            timeZoneId = user.TimeZoneId,
            enrollment = enrollment is null ? null : new { enrollment.Id, enrollment.Level, enrollment.TargetScore, enrollment.MinutesPerDay, enrollment.RuleVersion, enrollment.PlanRevision, enrollment.PathVersionId, enrollment.SelectionReason, enrollment.PathStartedOn },
            tasks,
            statistics = new { lessonsCompleted, attemptsCompleted = attempts.Count, questionsCorrect = attempts.Sum(x => x.CorrectCount), questionsObserved = attempts.Sum(x => x.TotalCount) }
        };
    });

    /// <summary>VI: Lượt kiểm tra sai chỉ trả feedback; lượt đúng mới ghi progress. EN: Incorrect checks return feedback; only successful evidence creates progress.</summary>
    public Task<LessonCheck> CompleteLesson(Guid user, Guid versionId, CompleteLessonInput input) => tx.Run($"learning:{user}", async () =>
    {
        var scope = $"lesson:{versionId}"; var hash = Receipts.Hash(input); var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<LessonCheck>(duplicate.ResponseJson)!;
        var lesson = await content.FullVersions.SingleOrDefaultAsync(x => x.Id == versionId && x.Root.Kind == ContentKind.Lesson && x.Status == PublicationStatus.Published) ?? throw new DomainError(404, "lesson_not_found", "Không tìm thấy bài học.");
        var questions = lesson.Groups.SelectMany(x => x.Questions).ToArray();
        DomainError.Require(questions.Length > 0 && input.Answers.Length == questions.Length && input.Answers.Select(x => x.QuestionId).Distinct().Count() == questions.Length && input.Answers.All(a => questions.Any(q => q.Id == a.QuestionId && (q.Type == QuestionType.FillBlank ? a.SelectedOptionId is null && !string.IsNullOrWhiteSpace(a.AnswerText) && a.AnswerText.Length <= AnswerNormalization.MaxAnswerLength : a.AnswerText is null && q.Options.Any(o => o.Id == a.SelectedOptionId)))), 422, "invalid_lesson_answers", "Hãy trả lời đầy đủ quiz bằng đúng loại đáp án.");
        // VI: Quiz dùng cùng policy với attempt, không chấm theo text phía client. EN: Lesson quizzes use the same server-side normalization policy as assessments.
        var feedback = questions.Select(q =>
        {
            var answer = input.Answers.Single(a => a.QuestionId == q.Id);
            var correctOption = q.Type == QuestionType.SingleChoice ? q.Options.Single(o => o.IsCorrect).Id : (Guid?)null;
            var correct = q.Type == QuestionType.FillBlank ? AnswerNormalization.Matches(answer.AnswerText, JsonSerializer.Deserialize<string[]>(q.AcceptedAnswerVariantsJson)!, q.NormalizationPolicyVersion, q.IgnoreTerminalPunctuation) : answer.SelectedOptionId == correctOption;
            return new LessonFeedback(q.Id, correct, correctOption, q.Explanation);
        }).ToArray();
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

    /// <summary>VI: Dữ liệu dẫn xuất có nguồn FK thật và outbox duy nhất theo task. EN: Complete tasks from real source foreign keys with one completion/outbox effect.</summary>
    private async Task SyncCompleted(Guid user, DateOnly date, string timezone)
    {
        var completedNodes = (await db.TaskCompletions.Where(x => x.DailyTask.UserId == user && x.DailyTask.PathNodeId != null).Select(x => x.DailyTask.PathNodeId!.Value).ToArrayAsync()).ToHashSet();
        var tasks = await db.DailyTasks.Include(x => x.PathNode).Where(x => x.UserId == user && (x.Status == "pending" || x.Status == "missed" && x.PathNodeId != null)).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync();
        foreach (var task in tasks)
        {
            if (task.PathNodeId is { } done && completedNodes.Contains(done)) continue;
            var progress = await db.LessonProgress.FirstOrDefaultAsync(x => x.UserId == user && x.ContentVersionId == task.ContentVersionId);
            var minimum = task.PathNode?.MinimumCorrectPercent ?? 0;
            var attempt = progress is null ? await db.Attempts.Include(x => x.Result).Where(x => x.UserId == user && x.ContentVersionId == task.ContentVersionId && x.Status == AttemptStatus.Graded && !x.ContentVersion.IsFixture && x.Mode != "mistake_review" && x.StartedAt >= task.CreatedAt && x.Result!.TotalCount > 0 && x.Result.CorrectCount * 100 >= minimum * x.Result.TotalCount && !db.TaskCompletions.Any(c => c.AttemptId == x.Id)).OrderBy(x => x.Result!.FinalizedAt).ThenBy(x => x.Id).FirstOrDefaultAsync() : null;
            if (progress is null && attempt is null) continue;
            task.Status = "completed";
            if (task.PathNodeId is { } nodeId)
            {
                completedNodes.Add(nodeId);
                // VI: Result có thể commit đúng lúc rollover; nhận đúng nguồn cũ và hủy successor chưa cần nữa. EN: A result can commit during rollover; credit its original source and supersede an unnecessary pending successor.
                var successors = await db.DailyTasks.Where(x => x.EnrollmentId == task.EnrollmentId && x.PathNodeId == nodeId && x.Id != task.Id && x.Status == "pending").ToListAsync();
                foreach (var successor in successors) successor.Status = "superseded";
            }
            var zone = task.TimeZoneId ?? timezone; var completedAt = progress?.CompletedAt ?? attempt!.Result!.FinalizedAt;
            var completedDate = LocalDate(completedAt, zone);
            db.TaskCompletions.Add(new TaskCompletion { DailyTaskId = task.Id, LessonProgressId = progress?.Id, AttemptId = attempt?.Id, CompletedAt = completedAt, LocalDate = completedDate, TimeZoneId = zone });
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.task_completed", AggregateId = task.Id, OccurredAt = clock.UtcNow, PayloadJson = JsonSerializer.Serialize(new { userId = user, taskId = task.Id, localDate = completedDate, timeZoneId = zone }) });
            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
    }
}
