using System.Security.Claims;
using EnglishHub.Application;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Media;
public sealed record CreateUpload(Guid ClientRequestId, string Purpose, string FileName, string ContentType, long Bytes, string Attribution);
public sealed record UploadGrant(Guid AssetId, string UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>VI: Quota được giữ chỗ trong transaction trước khi cấp SAS. EN: Reserve quota transactionally before issuing a scoped staging grant.</summary>
public sealed class MediaService(HubDbContext db, Transactions tx, Receipts receipts, IObjectStorage storage, IClock clock)
{
    public const long Quota = 1024L * 1024 * 1024;
    public async Task<UploadGrant> Create(Guid user, bool editor, CreateUpload input)
    {
        DomainError.Require(input.Purpose is "lesson_audio" or "recording",422,"unsupported_media_purpose","Bản hiện tại hỗ trợ lesson audio và recording.");
        DomainError.Require(input.Purpose != "lesson_audio" || editor,403,"editor_required","Chỉ biên tập viên upload audio bài học.");
        DomainError.Require(new[] { "audio/mpeg","audio/wav","audio/x-wav","audio/ogg","audio/webm","audio/mp4" }.Contains(input.ContentType) && input.Bytes > 0 && input.Bytes <= (input.Purpose == "recording" ? 20L : 100L) * 1024 * 1024,422,"invalid_upload","Loại hoặc kích thước audio không hợp lệ.");
        DomainError.Require(!string.IsNullOrWhiteSpace(input.Attribution) && input.Attribution.Length <= 2000,422,"source_required","Cần mô tả nguồn/quyền sử dụng.");
        await storage.Ensure(CancellationToken.None);
        var asset = await tx.Run($"media-quota:{user}", async () =>
        {
            var hash=Receipts.Hash(input); var duplicate=await receipts.Find(user,"upload-create",input.ClientRequestId,hash);
            if (duplicate is not null) return await db.MediaAssets.SingleAsync(x => x.Id==duplicate.ResourceId);
            var reserved=await db.MediaAssets.Where(x => x.OwnerId==user && x.Status!=MediaStatus.Deleted).SumAsync(x => x.ReservedBytes);
            DomainError.Require(reserved + input.Bytes*3 <= Quota,422,"quota_exceeded","Không đủ quota lưu trữ.");
            var a=new MediaAsset { OwnerId=user,Purpose=input.Purpose,FileName=Path.GetFileName(input.FileName)[..Math.Min(Path.GetFileName(input.FileName).Length,200)],DeclaredContentType=input.ContentType,ExpectedBytes=input.Bytes,ReservedBytes=input.Bytes*3,Attribution=input.Attribution,CreatedAt=clock.UtcNow,UploadExpiresAt=clock.UtcNow.AddMinutes(15) };
            db.MediaAssets.Add(a); receipts.Save(user,"upload-create",input.ClientRequestId,hash,a.Id,new { a.Id }); return a;
        });
        DomainError.Require(asset.Status==MediaStatus.PendingUpload && asset.UploadExpiresAt>clock.UtcNow,409,"upload_closed","Phiên upload đã kết thúc.");
        return new(asset.Id,storage.UploadGrant(asset.StagingKey,asset.UploadExpiresAt),asset.UploadExpiresAt);
    }

    public async Task Complete(Guid user, Guid id)
    {
        var before=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id==id && x.OwnerId==user) ?? throw new DomainError(404,"asset_not_found","Không tìm thấy upload.");
        if (before.Status!=MediaStatus.PendingUpload) return;
        var facts=await storage.InspectStaging(before.StagingKey,CancellationToken.None);
        await tx.Run($"media-quota:{user}",async () =>
        {
            var a=await db.MediaAssets.SingleAsync(x => x.Id==id && x.OwnerId==user);
            if (a.Status!=MediaStatus.PendingUpload) return true;
            DomainError.Require(clock.UtcNow<a.UploadExpiresAt && facts.Bytes==a.ExpectedBytes,422,"upload_mismatch","Kích thước hoặc thời hạn upload không khớp.");
            a.SourceETag=facts.ETag; a.Status=MediaStatus.Uploaded;
            db.BackgroundJobs.Add(new BackgroundJob { ResourceId=id,DedupKey=$"media:{id}:{a.Generation}",DueAt=clock.UtcNow });
            return true;
        });
    }
    public async Task<MediaAsset> Owned(Guid user, Guid id, bool admin) => await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id==id && (x.OwnerId==user || admin && x.Purpose=="lesson_audio")) ?? throw new DomainError(404,"asset_not_found","Không tìm thấy media.");
    public async Task<string> Playback(Guid user, Guid id, bool admin)
    {
        var a=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id==id && (x.OwnerId==user || admin && x.Purpose=="lesson_audio" || db.ContentVersions.Any(v => v.MediaAssetId==id && v.Status==PublicationStatus.Published))) ?? throw new DomainError(404,"asset_not_found","Không có quyền truy cập media.");
        DomainError.Require(a.Status==MediaStatus.Ready && a.RenditionKey is not null,409,"media_not_ready","Media chưa sẵn sàng.");
        return storage.PlaybackGrant(a.RenditionKey!,clock.UtcNow.AddMinutes(2));
    }
}
