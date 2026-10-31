using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record SaveLessonAnnotation(Guid ClientRequestId, int AnchorStart, int AnchorEnd, string Quote, string NoteText, string? ExpectedEtag = null, bool IsHighlighted = true, string BlockKey = "body");
public sealed record RemoveLessonAnnotation(Guid ClientRequestId, string ExpectedEtag);
public sealed record LessonAnnotationView(Guid Id, Guid ContentVersionId, string Title, int Version, string BlockKey, int AnchorStart, int AnchorEnd, string Quote, string NoteText, bool IsHighlighted, bool IsDeleted, string Etag, DateTimeOffset UpdatedAt);

/// <summary>VI: Owner lấy từ session; Admin cũng chỉ đọc dữ liệu của chính mình. EN: Owner identity always comes from the session, including administrator requests.</summary>
public sealed class LessonAnnotationService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    public async Task<LessonAnnotationView[]> ForLesson(Guid user, Guid version)
    {
        await Source(user, version);
        var notes = await db.LessonAnnotations.AsNoTracking().Include(x => x.ContentVersion).Where(x => x.UserId == user && x.ContentVersionId == version && !x.IsDeleted).OrderBy(x => x.AnchorStart).ThenBy(x => x.Id).ToArrayAsync();
        return notes.Select(View).ToArray();
    }

    public async Task<object> List(Guid user, int offset)
    {
        DomainError.Require(offset is >= 0 and <= 100000, 422, "invalid_offset", "Vị trí trang không hợp lệ.");
        var notes = await db.LessonAnnotations.AsNoTracking().Include(x => x.ContentVersion).Where(x => x.UserId == user && !x.IsDeleted).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(offset).Take(31).ToArrayAsync();
        return new { items = notes.Take(30).Select(View).ToArray(), nextOffset = notes.Length > 30 ? offset + 30 : (int?)null };
    }

    public async Task<LessonAnnotationView> Read(Guid user, Guid version, Guid id)
    {
        var row = await db.LessonAnnotations.AsNoTracking().Include(x => x.ContentVersion).SingleOrDefaultAsync(x => x.Id == id && x.UserId == user && x.ContentVersionId == version) ?? throw new DomainError(404, "lesson_note_not_found", "Không tìm thấy ghi chú của bạn.");
        return View(row);
    }

    public Task<LessonAnnotationView> Save(Guid user, Guid version, Guid id, SaveLessonAnnotation input) => tx.Run($"lesson-notes:{user}:{version}", async () =>
    {
        DomainError.Require(id != Guid.Empty, 422, "invalid_annotation_id", "Mã ghi chú không được rỗng.");
        var scope = $"lesson-note:{version}"; var hash = Receipts.Hash(new { id, input });
        var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<LessonAnnotationView>(duplicate.ResponseJson)!;
        var source = await Source(user, version);
        TextAnchor.Validate(source.Body, input.BlockKey, input.AnchorStart, input.AnchorEnd, input.Quote);
        DomainError.Require(input.NoteText.Length <= 4000 && (input.IsHighlighted || !string.IsNullOrWhiteSpace(input.NoteText)), 422, "invalid_lesson_note", "Ghi chú tối đa 4000 ký tự; cần tô sáng hoặc nội dung ghi chú.");
        var row = await db.LessonAnnotations.SingleOrDefaultAsync(x => x.Id == id);
        if (row is not null)
        {
            DomainError.Require(row.UserId == user && row.ContentVersionId == version, 404, "lesson_note_not_found", "Không tìm thấy ghi chú của bạn.");
            Etags.Check(row, input.ExpectedEtag);
            if (row.IsDeleted) await CheckQuota(user, version, false);
        }
        else
        {
            DomainError.Require(input.ExpectedEtag is null, 412, "stale_version", "Ghi chú chưa tồn tại. Hãy tải lại.");
            await CheckQuota(user, version, true);
            row = new LessonAnnotation { Id = id, UserId = user, ContentVersionId = version }; db.LessonAnnotations.Add(row);
        }
        row.AnchorStart = input.AnchorStart; row.AnchorEnd = input.AnchorEnd; row.BlockKey = input.BlockKey;
        row.Quote = input.Quote; row.NoteText = input.NoteText.Trim(); row.IsHighlighted = input.IsHighlighted; row.IsDeleted = false; row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        var view = View(row, source); receipts.Save(user, scope, input.ClientRequestId, hash, id, view); return view;
    });

    /// <summary>VI: Tombstone và receipt giúp retry không xóa lại bản đã khôi phục. EN: A tombstone and receipt make retries safe even after an explicit restoration.</summary>
    public Task<LessonAnnotationView> Remove(Guid user, Guid version, Guid id, RemoveLessonAnnotation input) => tx.Run($"lesson-notes:{user}:{version}", async () =>
    {
        var scope = $"lesson-note-remove:{version}"; var hash = Receipts.Hash(new { id, input });
        var duplicate = await receipts.Find(user, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<LessonAnnotationView>(duplicate.ResponseJson)!;
        var row = await db.LessonAnnotations.Include(x => x.ContentVersion).SingleOrDefaultAsync(x => x.Id == id && x.ContentVersionId == version && x.UserId == user) ?? throw new DomainError(404, "lesson_note_not_found", "Không tìm thấy ghi chú của bạn.");
        Etags.Check(row, input.ExpectedEtag); row.IsDeleted = true; row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(); var view = View(row); receipts.Save(user, scope, input.ClientRequestId, hash, id, view); return view;
    });

    private async Task<ContentVersion> Source(Guid user, Guid version) => await db.ContentVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == version && x.Root.Kind == ContentKind.Lesson && (x.Status == PublicationStatus.Published || x.Status == PublicationStatus.Archived && db.LessonAnnotations.Any(n => n.UserId == user && n.ContentVersionId == version)))
        ?? throw new DomainError(404, "lesson_not_found", "Không tìm thấy phiên bản bài học.");

    private async Task CheckQuota(Guid user, Guid version, bool newRow)
    {
        DomainError.Require(await db.LessonAnnotations.CountAsync(x => x.UserId == user && x.ContentVersionId == version && !x.IsDeleted) < 100, 422, "lesson_note_limit", "Tối đa 100 ghi chú đang lưu cho mỗi phiên bản bài học.");
        if (newRow) DomainError.Require(await db.LessonAnnotations.CountAsync(x => x.UserId == user && x.ContentVersionId == version) < 500, 422, "lesson_note_history_limit", "Đã đạt 500 ghi chú lịch sử; hãy khôi phục hoặc sửa ghi chú có sẵn.");
    }

    private static LessonAnnotationView View(LessonAnnotation row) => View(row, row.ContentVersion);
    private static LessonAnnotationView View(LessonAnnotation row, ContentVersion source) => new(row.Id, source.Id, source.Title, source.Version, row.BlockKey, row.AnchorStart, row.AnchorEnd, row.Quote, row.NoteText, row.IsHighlighted, row.IsDeleted, Etags.Of(row), row.UpdatedAt);
}
