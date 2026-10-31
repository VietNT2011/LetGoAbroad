using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Media;

public sealed record SaveRecordingReflection(Guid ClientRequestId, string? ExpectedEtag, string Notes, int? Clarity, int? Rhythm);
public sealed record RecordingReflectionView(Guid AssetId, string FileName, MediaStatus Status, string? Etag, string Notes, int? Clarity, int? Rhythm, DateTimeOffset? UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecordingSourceView? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Revision = 0);

/// <summary>VI: Metadata không cần mở blob và không tạo evidence học/thi. EN: Reflections use metadata only, without resolving storage or creating learning/assessment evidence.</summary>
public sealed partial class RecordingReflectionService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    public async Task<RecordingReflectionView> Get(Guid user, Guid assetId)
    {
        var asset = await Owned(user, assetId);
        return await View(asset, await db.RecordingReflections.AsNoTracking().SingleOrDefaultAsync(x => x.MediaAssetId == assetId));
    }

    public async Task<object> List(Guid user, int offset)
    {
        DomainError.Require(offset is >= 0 and <= 100000, 422, "invalid_offset", "Vị trí trang không hợp lệ.");
        var rows = await db.RecordingReflections.AsNoTracking().Include(x => x.MediaAsset)
            .Where(x => x.MediaAsset.OwnerId == user && x.MediaAsset.Purpose == "recording")
            .OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(offset).Take(31).ToArrayAsync();
        var sources = await Sources(rows.Select(x => (x.SourceContentVersionId, x.SourceSegmentId)));
        return new { items = rows.Take(30).Select(x => MakeView(x.MediaAsset, x, Source(sources, x.SourceContentVersionId, x.SourceSegmentId))), nextOffset = rows.Length > 30 && offset <= 99970 ? offset + 30 : (int?)null };
    }

    public Task<RecordingReflectionView> Save(Guid user, Guid assetId, SaveRecordingReflection input) => tx.Run($"media-quota:{user}", async () =>
    {
        // VI: Cùng khóa với xóa media; ACL kiểm trước receipt, Admin không có override.
        // EN: Share deletion's owner lock, checking ownership before receipts; there is no administrative override.
        var asset = await Owned(user, assetId); var hash = Receipts.Hash(new { assetId, input });
        var duplicate = await receipts.Find(user, "recording-reflection", input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<RecordingReflectionView>(duplicate.ResponseJson)!;
        DomainError.Require(input.Notes.Length <= 2000 && (input.Clarity is null or >= 1 and <= 5) && (input.Rhythm is null or >= 1 and <= 5), 422, "invalid_reflection", "Ghi chú tối đa2000 ký tự; mức tự đánh giá từ1 đến5 hoặc bỏ trống.");
        var row = await db.RecordingReflections.SingleOrDefaultAsync(x => x.MediaAssetId == assetId);
        if (row is null)
        {
            DomainError.Require(input.ExpectedEtag is null, 412, "stale_version", "Chưa có bản tự đánh giá này; tải lại để so sánh.");
            DomainError.Require(asset.Status == MediaStatus.Ready, 409, "recording_not_ready", "Nghe bản thu Ready trước khi tạo tự đánh giá.");
            DomainError.Require(!string.IsNullOrWhiteSpace(input.Notes) || input.Clarity is not null || input.Rhythm is not null, 422, "empty_reflection", "Nhập ghi chú hoặc chọn một mức tự đánh giá.");
            row = new RecordingReflection { MediaAssetId = assetId }; db.RecordingReflections.Add(row);
        }
        else Etags.Check(row, input.ExpectedEtag);
        var changed = db.Entry(row).State == EntityState.Added || row.Notes != input.Notes || row.Clarity != input.Clarity || row.Rhythm != input.Rhythm;
        if (changed)
        {
            BeforeChange(row);
            row.Notes = input.Notes; row.Clarity = input.Clarity; row.Rhythm = input.Rhythm; row.UpdatedAt = clock.UtcNow;
            Append(row, "edit");
            db.AuditEntries.Add(new AuditEntry { ActorId = user, Action = "recording.reflection_saved", ResourceId = row.Id, OccurredAt = clock.UtcNow });
            await db.SaveChangesAsync();
        }
        // VI: Giữ metadata sau khi xóa file; không đổi trạng thái media hoặc cấp URL phát.
        // EN: Keep reflections after file deletion without changing media state or issuing playback access.
        var result = await View(asset, row); receipts.Save(user, "recording-reflection", input.ClientRequestId, hash, row.Id, result); return result;
    });

    private async Task<MediaAsset> Owned(Guid user, Guid id) => await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == user && x.Purpose == "recording")
        ?? throw new DomainError(404, "recording_not_found", "Không tìm thấy bản thu riêng.");
    private async Task<RecordingReflectionView> View(MediaAsset asset, RecordingReflection? row) => MakeView(asset, row, row is null ? null : Source(await Sources([(row.SourceContentVersionId, row.SourceSegmentId)]), row.SourceContentVersionId, row.SourceSegmentId));
    private static RecordingReflectionView MakeView(MediaAsset asset, RecordingReflection? row, RecordingSourceView? source) => new(asset.Id, asset.FileName, asset.Status, row is null ? null : Etags.Of(row), row?.Notes ?? "", row?.Clarity, row?.Rhythm, row?.UpdatedAt, source, row?.Revision ?? 0);
}
