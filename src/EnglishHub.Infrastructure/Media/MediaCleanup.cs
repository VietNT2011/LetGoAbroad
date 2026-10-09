using EnglishHub.Application;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Media;
/// <summary>VI: Cleanup có checkpoint SQL và age buffer. EN: SQL checkpoints and an age buffer make cleanup resumable and conservative.</summary>
public sealed class MediaCleanup(HubDbContext db,Transactions tx,IObjectStorage storage,IClock clock)
{
    public async Task Run(CancellationToken ct)
    {
        var cutoff=clock.UtcNow.AddHours(-24);
        var assets=await db.MediaAssets.AsNoTracking().Where(x => x.UploadExpiresAt<cutoff && (x.LastCleanupAt==null || x.LastCleanupAt<cutoff) && x.Status!=MediaStatus.Uploaded && x.Status!=MediaStatus.Processing && x.Status!=MediaStatus.DeletePending)
            .OrderBy(x => x.LastCleanupAt).ThenBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
        foreach(var snapshot in assets)
        {
            await storage.Cleanup(snapshot.Id,snapshot.StagingKey,snapshot.SourceKey,snapshot.RenditionKey,cutoff,ct);
            await tx.Run($"media-quota:{snapshot.OwnerId}",async () =>
            {
                var row=await db.MediaAssets.SingleAsync(x => x.Id==snapshot.Id,ct);
                if(row.Status==MediaStatus.PendingUpload && row.UploadExpiresAt<cutoff) { row.Status=MediaStatus.Expired; row.ReservedBytes=0; }
                row.LastCleanupAt=clock.UtcNow; return true;
            },ct);
        }
    }
}
