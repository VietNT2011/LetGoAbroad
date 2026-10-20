using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Media;

/// <summary>VI: Thu hồi quyền phát trước, xóa blob bằng job sau commit. EN: Revoke playback immediately, then delete blobs durably after commit.</summary>
public sealed class MediaLifecycle(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    public async Task Delete(Guid user, Guid id, bool admin)
    {
        var owner = await db.MediaAssets.AsNoTracking().Where(x => x.Id == id && (x.OwnerId == user || admin && x.Purpose == "lesson_audio")).Select(x => x.OwnerId).SingleOrDefaultAsync();
        DomainError.Require(owner != Guid.Empty, 404, "asset_not_found", "Không tìm thấy media.");
        await tx.Run($"media-quota:{owner}", async () =>
        {
            var asset = await db.MediaAssets.SingleAsync(x => x.Id == id);
            if (asset.Status == MediaStatus.Deleted) return true;
            DomainError.Require(!await db.ContentVersions.AnyAsync(x => x.MediaAssetId == id && x.Status == PublicationStatus.Published), 409, "media_in_use", "Media vẫn thuộc nội dung đã publish; hãy archive trước.");
            if (asset.Status != MediaStatus.DeletePending)
            {
                asset.Status = MediaStatus.DeletePending; asset.Generation = Guid.NewGuid();
                await db.BackgroundJobs.Where(x => x.ResourceId == id && x.Kind == "process_media" && (x.State == "pending" || x.State == "running")).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled").SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null));
            }
            if (!await db.BackgroundJobs.AnyAsync(x => x.ResourceId == id && x.Kind == "delete_media" && (x.State == "pending" || x.State == "running")))
                db.BackgroundJobs.Add(new BackgroundJob { Kind = "delete_media", ResourceId = id, DedupKey = $"delete:{id}:{Guid.NewGuid()}", DueAt = clock.UtcNow });
            return true;
        });
    }
    public async Task<Guid> Retry(Guid actor, Guid jobId, Guid commandId)
    {
        var owner = await db.BackgroundJobs.AsNoTracking().Where(x => x.Id == jobId).Join(db.MediaAssets, x => x.ResourceId, a => a.Id, (j, a) => a.OwnerId).SingleOrDefaultAsync();
        return await tx.Run($"media-quota:{owner}", async () =>
        {
            var hash = Receipts.Hash(new { jobId }); var duplicate = await receipts.Find(actor, "media-retry", commandId, hash); if (duplicate is not null) return duplicate.ResourceId;
            var job = await db.BackgroundJobs.SingleOrDefaultAsync(x => x.Id == jobId) ?? throw new DomainError(404, "job_not_found", "Không tìm thấy job.");
            DomainError.Require(job.State == "failed", 409, "job_not_failed", "Chỉ retry job đã thất bại.");
            var asset = await db.MediaAssets.SingleAsync(x => x.Id == job.ResourceId);
            DomainError.Require(asset.Status == MediaStatus.Failed || job.Kind == "delete_media" && asset.Status == MediaStatus.DeletePending, 409, "asset_state_changed", "Asset không còn ở trạng thái retry.");
            asset.Generation = Guid.NewGuid(); if (job.Kind == "process_media") asset.Status = MediaStatus.Uploaded; asset.FailureCode = null;
            var retry = new BackgroundJob { Kind = job.Kind, ResourceId = asset.Id, DedupKey = $"retry:{jobId}:{asset.Generation}", DueAt = clock.UtcNow }; db.BackgroundJobs.Add(retry);
            db.AuditEntries.Add(new AuditEntry { ActorId = actor, Action = "media.job_retry", ResourceId = jobId, OccurredAt = clock.UtcNow });
            receipts.Save(actor, "media-retry", commandId, hash, retry.Id, new { retry.Id }); return retry.Id;
        });
    }
}
