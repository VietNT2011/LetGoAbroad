using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Assessment;

public sealed record SaveAnnotation(Guid ClientRequestId, bool IsBookmarked, string NoteText, string? ExpectedEtag = null, string AnchorBlock = "prompt", int AnchorStart = 0, int? AnchorEnd = null);
public sealed record AnnotationView(Guid Id, Guid QuestionId, Guid SourceAttemptId, Guid SourceAttemptItemId, Guid ContentVersionId, string Title, string Prompt, string Stimulus, bool IsBookmarked, string NoteText, string AnchorBlock, int AnchorStart, int AnchorEnd, string Etag, DateTimeOffset UpdatedAt);

/// <summary>VI: Không có endpoint đọc ghi chú người khác, kể cả Admin. EN: Every annotation operation is owner-scoped, including requests from an administrator.</summary>
public sealed class AnnotationService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    private IQueryable<QuestionAnnotation> Full => db.QuestionAnnotations.Include(x => x.SourceItem).ThenInclude(x => x.Attempt).ThenInclude(x => x.ContentVersion);

    public async Task<Dictionary<Guid, AnnotationView>> ForAttempt(Guid user, Guid attemptId)
    {
        var items = await db.AttemptItems.AsNoTracking().Where(x => x.AttemptId == attemptId && x.Attempt.UserId == user).Select(x => new { x.Id, x.QuestionId }).ToArrayAsync();
        DomainError.Require(items.Length > 0, 404, "attempt_not_found", "Không tìm thấy lần làm bài.");
        var ids = items.Select(x => x.QuestionId).ToArray();
        var annotations = await Full.AsNoTracking().Where(x => x.UserId == user && ids.Contains(x.QuestionId)).ToArrayAsync();
        return items.Where(item => annotations.Any(x => x.QuestionId == item.QuestionId)).ToDictionary(item => item.Id, item => View(annotations.Single(x => x.QuestionId == item.QuestionId)));
    }

    public async Task<object> List(Guid user, bool bookmarkedOnly, int offset = 0)
    {
        DomainError.Require(offset is >= 0 and <= 100000, 422, "invalid_offset", "Vị trí trang không hợp lệ.");
        var query = Full.AsNoTracking().Where(x => x.UserId == user && (x.IsBookmarked || x.NoteText != ""));
        if (bookmarkedOnly) query = query.Where(x => x.IsBookmarked);
        var page = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(offset).Take(31).ToArrayAsync();
        return new { items = page.Take(30).Select(View).ToArray(), nextOffset = page.Length > 30 ? offset + 30 : (int?)null };
    }

    public async Task<AnnotationView> Save(Guid user, Guid attemptId, Guid itemId, SaveAnnotation input)
    {
        var source = await db.AttemptItems.AsNoTracking().Include(x => x.Attempt).ThenInclude(x => x.ContentVersion).SingleOrDefaultAsync(x => x.Id == itemId && x.AttemptId == attemptId && x.Attempt.UserId == user)
            ?? throw new DomainError(404, "attempt_item_not_found", "Không tìm thấy câu hỏi của bạn.");
        // VI: Khóa theo user+câu, không theo attempt; hai lần làm cùng version dùng một ghi chú. EN: Lock by owner/question so concurrent attempts share one annotation safely.
        return await tx.Run($"annotation:{user}:{source.QuestionId}", async () =>
        {
            var scope = $"annotation:{source.QuestionId}"; var hash = Receipts.Hash(new { itemId, input });
            var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
            if (duplicate is not null) return JsonSerializer.Deserialize<AnnotationView>(duplicate.ResponseJson)!;
            DomainError.Require(input.NoteText.Length <= 4000, 422, "note_too_long", "Ghi chú tối đa 4000 ký tự.");
            DomainError.Require(input.AnchorBlock is "prompt" or "stimulus", 422, "invalid_anchor", "Anchor chỉ nhận prompt hoặc stimulus của snapshot.");
            var text = input.AnchorBlock == "prompt" ? source.Prompt : source.Stimulus;
            var end = input.AnchorEnd ?? text.Length;
            DomainError.Require(input.AnchorStart >= 0 && end > input.AnchorStart && end <= text.Length && Boundary(text, input.AnchorStart) && Boundary(text, end), 422, "invalid_anchor", "Khoảng UTF-16 phải nằm trong đúng block và không cắt đôi một ký tự Unicode.");
            var annotation = await Full.SingleOrDefaultAsync(x => x.UserId == user && x.QuestionId == source.QuestionId);
            if (annotation is null)
            {
                DomainError.Require(input.ExpectedEtag is null, 412, "stale_version", "Ghi chú chưa tồn tại. Hãy tải lại.");
                annotation = new QuestionAnnotation { UserId = user, QuestionId = source.QuestionId };
                db.QuestionAnnotations.Add(annotation);
            }
            else Etags.Check(annotation, input.ExpectedEtag);
            annotation.SourceAttemptItemId = source.Id;
            annotation.IsBookmarked = input.IsBookmarked; annotation.NoteText = input.NoteText.Trim();
            annotation.AnchorBlock = input.AnchorBlock; annotation.AnchorStart = input.AnchorStart; annotation.AnchorEnd = end; annotation.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync();
            // VI: View chỉ dùng source đã xác minh owner, không serialize navigation graph. EN: Project the already-authorized source explicitly rather than serializing the entity graph.
            var view = Project(annotation, source); receipts.Save(user, scope, input.ClientRequestId, hash, annotation.Id, view); return view;
        });
    }

    private static bool Boundary(string text, int offset) => offset <= 0 || offset >= text.Length || !char.IsLowSurrogate(text[offset]) || !char.IsHighSurrogate(text[offset - 1]);
    private static AnnotationView View(QuestionAnnotation annotation) => Project(annotation, annotation.SourceItem);
    private static AnnotationView Project(QuestionAnnotation annotation, AttemptItem source)
    {
        return new(annotation.Id, annotation.QuestionId, source.AttemptId, source.Id, source.Attempt.ContentVersionId, source.Attempt.ContentVersion.Title, source.Prompt, source.Stimulus, annotation.IsBookmarked, annotation.NoteText, annotation.AnchorBlock, annotation.AnchorStart, annotation.AnchorEnd, Etags.Of(annotation), annotation.UpdatedAt);
    }
}
