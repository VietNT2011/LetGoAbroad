using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Assessment;

public sealed record StartAttempt(Guid ContentVersionId, Guid ClientRequestId, string Mode = "mini", int? Part = null, int? QuestionCount = null);
public sealed record AnswerChange(Guid AttemptItemId, Guid? SelectedOptionId, string ExpectedEtag);
public sealed record SaveAnswers(Guid ClientRequestId, AnswerChange[] Answers);
public sealed record PublicOption(Guid Id, string Text);
public sealed record AnswerAck(Guid AttemptItemId, Guid? SelectedOptionId, string Etag);
public sealed record BatchAck(Guid ClientRequestId, DateTimeOffset ServerNow, AnswerAck[] Answers);
public sealed record ActiveItem(Guid Id, int Order, string Prompt, string Stimulus, PublicOption[] Options, Guid? SelectedOptionId, string Etag, int Part);
public sealed record AttemptView(Guid Id, Guid ContentVersionId, string Title, string Mode, bool IsFixture, string Status, DateTimeOffset StartedAt, DateTimeOffset DeadlineAt, DateTimeOffset ServerNow, ActiveItem[] Items, DateTimeOffset? ListeningDeadlineAt, string Section);

/// <summary>VI: Autosave, submit và expiry dùng cùng khóa attempt. EN: Autosave, manual submission and expiry serialize on the same attempt lock.</summary>
public sealed class AssessmentService(HubDbContext db, Transactions tx, Receipts receipts, ContentService content, IClock clock)
{
    private IQueryable<Attempt> Full => db.Attempts.Include(x => x.ContentVersion).Include(x => x.Result).Include(x => x.Items).ThenInclude(x => x.GradingKey);

    /// <summary>VI: Giữ snapshot đề và receipt trong cùng transaction. EN: Pin questions, private keys and the start receipt atomically.</summary>
    public async Task<AttemptView> Start(Guid user, StartAttempt input)
    {
        var id = await tx.Run($"attempt-start:{user}", async () =>
        {
            var hash = Receipts.Hash(input); var duplicate = await receipts.Find(user, "attempt-start", input.ClientRequestId, hash);
            if (duplicate is not null) return duplicate.ResourceId;
            DomainError.Require(input.Mode is "mini" or "practice" or "placement" or "fixture" or "full_mock" or "mistake_review", 422, "invalid_mode", "Chế độ không hỗ trợ.");
            var version = await content.FullVersions.SingleOrDefaultAsync(x => x.Id == input.ContentVersionId && x.Status == PublicationStatus.Published && x.Root.Kind == ContentKind.Assessment) ?? throw new DomainError(404, "exam_not_found", "Không tìm thấy đề đã publish.");
            DomainError.Require(!version.IsFixture || input.Mode == "fixture", 422, "fixture_requires_label", "Đề kỹ thuật phải dùng chế độ fixture.");
            var sectioned = input.Mode == "full_mock" || input.Mode == "fixture" && AssessmentBlueprint.IsFull(version);
            if (sectioned) DomainError.Require(AssessmentBlueprint.IsFull(version) && input.Part is null && input.QuestionCount is null, 422, "invalid_blueprint", "Full mock phải giữ đủ 200 câu theo blueprint.");
            var random = new Random(BitConverter.ToInt32(input.ClientRequestId.ToByteArray()));
            var groups = SelectGroups(version, input, random);
            var attempt = new Attempt { UserId = user, ContentVersionId = version.Id, Mode = input.Mode, StartedAt = clock.UtcNow, DeadlineAt = clock.UtcNow.AddSeconds(version.DurationSeconds) };
            if (sectioned) { attempt.ListeningDeadlineAt = attempt.StartedAt.AddMinutes(45); attempt.DeadlineAt = attempt.StartedAt.AddMinutes(120); }
            foreach (var group in groups)
            {
                if (input.QuestionCount is { } count && attempt.Items.Count >= count) break;
                foreach (var q in group.Questions.OrderBy(x => x.Order))
                    attempt.Items.Add(new AttemptItem
                    {
                        QuestionId = q.Id,
                        Order = attempt.Items.Count,
                        Prompt = q.Prompt,
                        Stimulus = group.Stimulus,
                        OptionsJson = JsonSerializer.Serialize(q.Options.OrderBy(_ => random.Next()).Select(o => new PublicOption(o.Id, o.Text))),
                        GradingKey = new GradingKey { CorrectOptionId = q.Options.Single(o => o.IsCorrect).Id, Explanation = q.Explanation, Skill = q.Skill, Part = group.Part }
                    });
            }
            db.Attempts.Add(attempt);
            receipts.Save(user, "attempt-start", input.ClientRequestId, hash, attempt.Id, new { attempt.Id });
            return attempt.Id;
        });
        return await Read(user, id);
    }

    /// <summary>VI: Chỉ owner đọc được DTO an toàn. EN: Return an owner-scoped DTO that cannot disclose grading keys.</summary>
    public async Task<AttemptView> Read(Guid user, Guid id)
    {
        var a = await Full.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new DomainError(404, "attempt_not_found", "Không tìm thấy lần làm bài.");
        // VI: Chọn DTO rõ ràng; không bao giờ trả GradingKey. EN: Explicit projection prevents accidental grading-key disclosure.
        return new(a.Id, a.ContentVersionId, a.ContentVersion.Title, a.Mode, a.ContentVersion.IsFixture, a.Status.ToString().ToLowerInvariant(), a.StartedAt, a.DeadlineAt, clock.UtcNow,
            a.Items.OrderBy(x => x.Order).Select(x => new ActiveItem(x.Id, x.Order, x.Prompt, x.Stimulus, JsonSerializer.Deserialize<PublicOption[]>(x.OptionsJson)!, x.SelectedOptionId, Etags.Of(x), x.GradingKey.Part)).ToArray(), a.ListeningDeadlineAt, a.ListeningDeadlineAt is null ? "all" : clock.UtcNow < a.ListeningDeadlineAt ? "listening" : "reading");
    }

    /// <summary>VI: Xem số câu thật trước khi khởi động đồng hồ. EN: Preview the seeded whole-group selection before starting the timer.</summary>
    public async Task<object> Preview(StartAttempt input)
    {
        DomainError.Require(input.Mode == "practice", 422, "invalid_mode", "Preview này dành cho chế độ practice.");
        var version = await content.FullVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.ContentVersionId && x.Status == PublicationStatus.Published && x.Root.Kind == ContentKind.Assessment) ?? throw new DomainError(404, "exam_not_found", "Không tìm thấy đề.");
        DomainError.Require(!version.IsFixture, 422, "fixture_requires_label", "Không dùng fixture kỹ thuật cho practice thường.");
        var groups = SelectGroups(version, input, new Random(BitConverter.ToInt32(input.ClientRequestId.ToByteArray())));
        return new { actualQuestionCount = groups.Sum(x => x.Questions.Count), groupCount = groups.Count, parts = groups.Select(x => x.Part).Distinct().ToArray() };
    }

    private static List<QuestionGroup> SelectGroups(ContentVersion version, StartAttempt input, Random random)
    {
        var groups = version.Groups.Where(x => input.Part is null || x.Part == input.Part).OrderBy(x => x.Order).ToList();
        var available = groups.Sum(x => x.Questions.Count);
        DomainError.Require(available > 0 && (input.QuestionCount is null || input.QuestionCount is > 0 and <= 200 && input.QuestionCount <= available), 422, "insufficient_questions", $"Có {available} câu phù hợp; hãy chọn lại số câu.");
        if (input.Mode == "practice") groups = groups.OrderBy(_ => random.Next()).ToList();
        var selected = new List<QuestionGroup>(); var count = 0;
        foreach (var group in groups) { if (input.QuestionCount is { } target && count >= target) break; selected.Add(group); count += group.Questions.Count; }
        return selected;
    }

    /// <summary>VI: Replay receipt trước khi xét ETag/deadline. EN: Check receipts first so an accepted retry survives later deadline or version changes.</summary>
    public Task<BatchAck> Save(Guid user, Guid id, SaveAnswers input) => tx.Run($"attempt:{id}", async () =>
    {
        var hash = Receipts.Hash(input); var scope = $"answers:{id}"; var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<BatchAck>(duplicate.ResponseJson)!;
        var a = await Owned(user, id);
        var now = clock.UtcNow; // VI: Đọc giờ sau khi lấy khóa. EN: Read authoritative time only after acquiring the lock.
        DomainError.Require(a.Status == AttemptStatus.InProgress && now < a.DeadlineAt, 409, "attempt_closed", "Đã nộp bài hoặc hết thời gian.");
        Apply(a, input.Answers, now);
        await db.SaveChangesAsync();
        var ack = new BatchAck(input.ClientRequestId, now, input.Answers.Select(c => { var item = a.Items.Single(x => x.Id == c.AttemptItemId); return new AnswerAck(item.Id, item.SelectedOptionId, Etags.Of(item)); }).ToArray());
        receipts.Save(user, scope, input.ClientRequestId, hash, id, ack);
        return ack;
    });

    /// <summary>VI: Submit và worker expiry dùng chung finalization. EN: Manual submission and worker expiry share one transactional finalization path.</summary>
    public Task<AttemptResult> Submit(Guid user, Guid id, SaveAnswers input) => tx.Run($"attempt:{id}", async () =>
    {
        var hash = Receipts.Hash(input); var scope = $"submit:{id}"; var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        var a = await Owned(user, id);
        if (duplicate is not null) return a.Result!;
        if (a.Result is not null) { receipts.Save(user, scope, input.ClientRequestId, hash, id, a.Result); return a.Result; }
        DomainError.Require(a.Status == AttemptStatus.InProgress, 409, "attempt_closed", "Lần làm bài đã đóng.");
        var now = clock.UtcNow;
        DomainError.Require(a.ListeningDeadlineAt is null || now >= a.ListeningDeadlineAt, 409, "listening_section_locked", "Full mock chưa cho chuyển khỏi Listening trước thời hạn.");
        if (now < a.DeadlineAt) Apply(a, input.Answers, now);
        var result = new AttemptResult
        {
            AttemptId = id,
            TotalCount = a.Items.Count,
            BlankCount = a.Items.Count(x => x.SelectedOptionId is null),
            CorrectCount = a.Items.Count(IsCorrect),
            ListeningRaw = a.Items.Count(x => x.GradingKey.Part <= 4 && IsCorrect(x)),
            ReadingRaw = a.Items.Count(x => x.GradingKey.Part >= 5 && IsCorrect(x)),
            FinalizedAt = now,
            Reason = now >= a.DeadlineAt ? "deadline" : "manual_submit"
        };
        a.Status = AttemptStatus.Graded; a.Result = result;
        db.OutboxEvents.Add(new OutboxEvent { Type = "assessment.graded", AggregateId = id, OccurredAt = now, PayloadJson = JsonSerializer.Serialize(new { userId = user, attemptId = id, mode = a.Mode, isFixture = a.ContentVersion.IsFixture }) });
        receipts.Save(user, scope, input.ClientRequestId, hash, id, result);
        return result;
    });

    /// <summary>VI: Chỉ mở đáp án sau khi kết quả được lưu. EN: Disclose explanations only after a durable result exists.</summary>
    public async Task<object> Result(Guid user, Guid id)
    {
        var a = await Full.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new DomainError(404, "attempt_not_found", "Không tìm thấy lần làm bài.");
        DomainError.Require(a.Result is not null, 409, "result_not_ready", "Bạn chưa nộp bài.");
        return new { a.Id, a.ContentVersionId, a.ContentVersion.Title, a.ContentVersion.IsFixture, a.Mode, result = a.Result, items = a.Items.OrderBy(x => x.Order).Select(x => new { x.Id, x.QuestionId, x.Prompt, x.Stimulus, options = JsonSerializer.Deserialize<PublicOption[]>(x.OptionsJson), x.SelectedOptionId, x.GradingKey.CorrectOptionId, x.GradingKey.Explanation, x.GradingKey.Skill, x.GradingKey.Part, isCorrect = IsCorrect(x) }) };
    }

    private async Task<Attempt> Owned(Guid user, Guid id) => await Full.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new DomainError(404, "attempt_not_found", "Không tìm thấy lần làm bài.");
    private static bool IsCorrect(AttemptItem item) => item.SelectedOptionId == item.GradingKey.CorrectOptionId;
    private static void Apply(Attempt attempt, AnswerChange[] changes, DateTimeOffset now)
    {
        DomainError.Require(changes.Length <= 20 && changes.Select(x => x.AttemptItemId).Distinct().Count() == changes.Length, 422, "invalid_batch", "Batch tối đa 20 câu duy nhất.");
        // VI: Xác thực toàn batch trước khi thay đổi tracked entities. EN: Validate the whole batch before applying any answer.
        foreach (var change in changes)
        {
            var item = attempt.Items.SingleOrDefault(x => x.Id == change.AttemptItemId) ?? throw new DomainError(422, "invalid_attempt_item", "Câu không thuộc snapshot.");
            Etags.Check(item, change.ExpectedEtag);
            if (attempt.ListeningDeadlineAt is { } boundary) DomainError.Require(now < boundary ? item.GradingKey.Part <= 4 : item.GradingKey.Part >= 5, 409, "section_closed", "Câu hỏi không thuộc section đang mở.");
            DomainError.Require(change.SelectedOptionId is null || JsonSerializer.Deserialize<PublicOption[]>(item.OptionsJson)!.Any(x => x.Id == change.SelectedOptionId), 422, "invalid_option", "Option không thuộc câu hỏi.");
        }
        foreach (var change in changes) { var item = attempt.Items.Single(x => x.Id == change.AttemptItemId); item.SelectedOptionId = change.SelectedOptionId; item.AcceptedAt = now; }
    }
}
