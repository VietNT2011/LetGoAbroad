using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record MistakeCommand(Guid ClientRequestId);
public sealed record MistakeImportAck(int AttemptsProcessed, bool HasMore);

/// <summary>VI: Lượt ôn có snapshot/điểm riêng, không trộn vào tiến độ và độ chính xác bài gốc. EN: Recall attempts own their snapshots/results and never inflate ordinary learning evidence.</summary>
public sealed class MistakeService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    public async Task<object> List(Guid user, string status = "learning", bool dueOnly = false, int? part = null, string? skill = null, int offset = 0)
    {
        DomainError.Require(status is "learning" or "mastered" or "all" && offset is >= 0 and <= 100000 && (part is null || part is >= 1 and <= 7) && (skill?.Length ?? 0) <= 60, 422, "invalid_mistake_filter", "Bộ lọc sổ câu sai không hợp lệ.");
        var now = clock.UtcNow;
        var query = db.MistakeItems.AsNoTracking().Where(x => x.UserId == user);
        if (status != "all") query = query.Where(x => x.Status == status);
        if (dueOnly) query = query.Where(x => x.NextReviewAt <= now);
        if (part is not null) query = query.Where(x => x.SourceItem.GradingKey.Part == part);
        if (!string.IsNullOrWhiteSpace(skill)) query = query.Where(x => x.SourceItem.GradingKey.Skill == skill);
        var rows = await query.OrderByDescending(x => x.LastWrongAt).ThenBy(x => x.Id).Skip(offset).Take(31).Select(x => new
        {
            x.Id, x.QuestionId, x.SourceItem.Prompt, x.SourceItem.GradingKey.Part, x.SourceItem.GradingKey.Skill,
            sourceAttemptId = x.SourceItem.AttemptId, x.WrongCount, x.LastWrongAt, x.Status, x.NextReviewAt, x.CorrectReviewDays, x.CycleTimeZoneId,
            canReview = x.Status == "learning" && x.NextReviewAt <= now,
            activeAttemptId = db.MistakeReviewTargets.Where(t => t.MistakeId == x.Id && t.Item.Attempt.Status == AttemptStatus.InProgress).OrderBy(t => t.Item.Attempt.StartedAt).ThenBy(t => t.Item.AttemptId).Select(t => (Guid?)t.Item.AttemptId).FirstOrDefault(),
            groupQuestionCount = db.AttemptItems.Count(i => i.AttemptId == x.SourceItem.AttemptId && i.Question.GroupId == x.SourceItem.Question.GroupId)
        }).ToArrayAsync();
        return new { policyVersion = 1, items = rows.Take(30).ToArray(), nextOffset = rows.Length > 30 ? offset + 30 : (int?)null };
    }

    public Task<Guid> Start(Guid user, Guid id, MistakeCommand input) => tx.Run($"learning:{user}", async () =>
    {
        const string scope = "mistake-start"; var hash = Receipts.Hash(new { id, input });
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (replay is not null) return replay.ResourceId;
        var mistake = await db.MistakeItems.SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new DomainError(404, "mistake_not_found", "Không tìm thấy câu sai của bạn.");
        DomainError.Require(mistake.Status == "learning" && mistake.NextReviewAt <= clock.UtcNow, 409, "mistake_not_due", "Câu chưa đến hạn ôn hoặc đã nắm.");
        DomainError.Require(!await db.MistakeReviewTargets.AnyAsync(x => x.MistakeId == id && x.Item.Attempt.Status == AttemptStatus.InProgress), 409, "mistake_review_active", "Câu này đang có phiên ôn. Hãy mở phiên đang làm.");
        var source = await db.AttemptItems.AsNoTracking().Include(x => x.Question).SingleAsync(x => x.Id == mistake.SourceAttemptItemId && x.Attempt.UserId == user);
        var items = await db.AttemptItems.AsNoTracking().Include(x => x.GradingKey).Where(x => x.AttemptId == source.AttemptId && x.Question.GroupId == source.Question.GroupId).OrderBy(x => x.Order).ToArrayAsync();
        // VI: Giữ trọn nhóm từ snapshot gốc, kể cả đề đã cất; snapshot legacy thiếu ngữ cảnh bị từ chối. EN: Retain complete frozen context even after archival, rejecting legacy partial groups.
        var expected = await db.Questions.CountAsync(x => x.GroupId == source.Question.GroupId);
        DomainError.Require(items.Length is > 0 and <= 20 && items.Length == expected, 409, "mistake_context_incomplete", "Snapshot cũ không có trọn nhóm câu; hãy mở lời giải gốc.");
        var version = await db.Attempts.Where(x => x.Id == source.AttemptId).Select(x => x.ContentVersionId).SingleAsync();
        var attempt = new Attempt { UserId = user, ContentVersionId = version, Mode = "mistake_review", StartedAt = clock.UtcNow, DeadlineAt = clock.UtcNow.AddMinutes(20) };
        foreach (var original in items)
        {
            var k = original.GradingKey;
            var item = new AttemptItem { QuestionId = original.QuestionId, Order = attempt.Items.Count, Prompt = original.Prompt, Stimulus = original.Stimulus, Type = original.Type, OptionsJson = original.OptionsJson,
                GradingKey = new GradingKey { CorrectOptionId = k.CorrectOptionId, AcceptedAnswerVariantsJson = k.AcceptedAnswerVariantsJson, NormalizationPolicyVersion = k.NormalizationPolicyVersion, IgnoreTerminalPunctuation = k.IgnoreTerminalPunctuation, Explanation = k.Explanation, Skill = k.Skill, Part = k.Part, GradingPolicyVersion = k.GradingPolicyVersion } };
            attempt.Items.Add(item);
            if (item.QuestionId == mistake.QuestionId) db.MistakeReviewTargets.Add(new MistakeReviewTarget { AttemptItemId = item.Id, Item = item, MistakeId = id, UserId = user, QuestionId = item.QuestionId });
        }
        db.Attempts.Add(attempt); receipts.Save(user, scope, input.ClientRequestId, hash, attempt.Id, new { attempt.Id }); return attempt.Id;
    });

    public Task<MistakeImportAck> Import(Guid user, MistakeCommand input) => tx.Run($"learning:{user}", async () =>
    {
        const string scope = "mistake-import"; var hash = Receipts.Hash(input);
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (replay is not null) return JsonSerializer.Deserialize<MistakeImportAck>(replay.ResponseJson)!;
        var candidates = await db.Attempts.Include(x => x.ContentVersion).Include(x => x.Result).Include(x => x.Items).ThenInclude(x => x.GradingKey)
            .Where(x => x.UserId == user && x.Status == AttemptStatus.Graded && x.Result != null && !x.ContentVersion.IsFixture && (x.Mode == "mini" || x.Mode == "practice" || x.Mode == "placement" || x.Mode == "full_mock") && !db.MistakeProcessedAttempts.Any(p => p.AttemptId == x.Id))
            .OrderBy(x => x.Result!.FinalizedAt).ThenBy(x => x.Id).Take(51).ToArrayAsync();
        foreach (var attempt in candidates.Take(50)) await Record(user, attempt, historical: true);
        var ack = new MistakeImportAck(Math.Min(50, candidates.Length), candidates.Length > 50); receipts.Save(user, scope, input.ClientRequestId, hash, user, ack); return ack;
    });

    /// <summary>VI: Gọi trong transaction learning của submit/import; marker chống đếm lại. EN: Call inside the grading/import learning transaction; markers prevent recounting.</summary>
    public async Task Record(Guid user, Attempt attempt, bool historical = false)
    {
        if (attempt.Result is null || attempt.ContentVersion.IsFixture || await db.MistakeProcessedAttempts.AnyAsync(x => x.AttemptId == attempt.Id)) return;
        var regular = attempt.Mode is "mini" or "practice" or "placement" or "full_mock";
        if (!regular && attempt.Mode != "mistake_review") return;
        db.MistakeProcessedAttempts.Add(new MistakeProcessedAttempt { AttemptId = attempt.Id });
        var zoneId = await db.Users.Where(x => x.Id == user).Select(x => x.TimeZoneId).SingleAsync();
        var at = attempt.Result.FinalizedAt;
        MistakeReviewTarget[] targets = regular ? [] : await db.MistakeReviewTargets.Include(x => x.Mistake).Where(x => x.UserId == user && x.Item.AttemptId == attempt.Id).ToArrayAsync();
        var ids = attempt.Items.Select(x => x.QuestionId).ToArray();
        var existing = await db.MistakeItems.Where(x => x.UserId == user && ids.Contains(x.QuestionId)).ToDictionaryAsync(x => x.QuestionId);
        foreach (var item in attempt.Items)
        {
            var target = targets.SingleOrDefault(x => x.AttemptItemId == item.Id);
            if (!regular && target is null) continue;
            var correct = item.Type == QuestionType.FillBlank ? AnswerNormalization.Matches(item.AnswerText, JsonSerializer.Deserialize<string[]>(item.GradingKey.AcceptedAnswerVariantsJson)!, item.GradingKey.NormalizationPolicyVersion, item.GradingKey.IgnoreTerminalPunctuation)
                : item.SelectedOptionId is not null && item.SelectedOptionId == item.GradingKey.CorrectOptionId;
            existing.TryGetValue(item.QuestionId, out var mistake);
            if (!correct)
            {
                var isNew = mistake is null;
                if (mistake is null) { mistake = new MistakeItem { UserId = user, QuestionId = item.QuestionId }; db.MistakeItems.Add(mistake); existing.Add(item.QuestionId, mistake); }
                if (mistake.WrongCount < int.MaxValue) mistake.WrongCount++;
                if (isNew || at >= mistake.LastWrongAt) { mistake.LastWrongAt = at; mistake.SourceAttemptItemId = item.Id; }
                // VI: Import nguồn cũ cộng lịch sử sai nhưng không ghi đè chu kỳ ôn mới hơn. EN: Importing older evidence counts the wrong answer without overwriting a newer recall cycle.
                if (isNew || at > mistake.LastEventAt || !historical && at == mistake.LastEventAt)
                {
                    mistake.LastEventAt = at; mistake.Status = "learning";
                    mistake.CorrectReviewDays = 0; mistake.LastCorrectDate = null; mistake.CycleTimeZoneId = zoneId; mistake.NextReviewAt = regular ? at : at.AddMinutes(10);
                }
            }
            else if (!regular && mistake is not null && at >= mistake.LastEventAt)
            {
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(mistake.CycleTimeZoneId)).DateTime);
                if (mistake.LastCorrectDate is null || date > mistake.LastCorrectDate) { mistake.CorrectReviewDays = Math.Min(2, mistake.CorrectReviewDays + 1); mistake.LastCorrectDate = date; }
                mistake.LastEventAt = at; mistake.Status = mistake.CorrectReviewDays >= 2 ? "mastered" : "learning"; mistake.NextReviewAt = mistake.Status == "mastered" ? null : at.AddDays(1);
            }
        }
        // VI: Lưu ngay để lần import tiếp theo trong cùng batch thấy marker và index mới. EN: Flush within the transaction so subsequent imported attempts see updated markers/index rows.
        await db.SaveChangesAsync();
    }
}
