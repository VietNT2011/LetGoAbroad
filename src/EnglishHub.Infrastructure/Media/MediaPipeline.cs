using System.Text.Json;
using Azure;
using EnglishHub.Application;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Operations;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace EnglishHub.Infrastructure.Media;

/// <summary>VI: Token lease là hàng rào commit sau FFmpeg/blob. EN: Fence every final database commit with the current, unexpired worker lease.</summary>
public sealed class MediaPipeline(HubDbContext db, BackgroundQueue queue, Transactions tx, IObjectStorage storage, IMediaProcessor processor, IClock clock, IServiceScopeFactory scopes, ILogger<MediaPipeline> log)
{
    public async Task<bool> RunOne(CancellationToken ct)
    {
        var job=await queue.Claim(); if (job is null) return false;
        var token=job.LeaseToken!.Value;
        using var leaseStop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var processingStop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat=KeepLease(job.Id,token,leaseStop.Token,processingStop);
        var workspace=Path.Combine(Path.GetTempPath(),"englishhub-media",job.Id.ToString("N"),token.ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var asset=await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id==job.ResourceId,ct);
            DomainError.Require(asset.Status is MediaStatus.Uploaded or MediaStatus.Processing,409,"asset_state_changed","Asset không còn chờ xử lý.");
            var sourceKey=asset.SourceKey ?? $"{asset.Id:N}/{asset.Generation:N}/source";
            await tx.Run($"media-quota:{asset.OwnerId}",async () =>
            {
                await db.Entry(job).ReloadAsync(ct);
                DomainError.Require(job.LeaseToken==token && job.State=="running" && job.LeaseUntil>clock.UtcNow,409,"lease_lost","Worker không còn giữ lease.");
                var row=await db.MediaAssets.SingleAsync(x => x.Id==asset.Id,ct);
                DomainError.Require(row.Generation==asset.Generation && row.Status is MediaStatus.Uploaded or MediaStatus.Processing,409,"asset_state_changed","Asset đã thay đổi.");
                job.LeaseUntil=clock.UtcNow.AddSeconds(90); row.Status=MediaStatus.Processing; row.SourceKey=sourceKey; return true;
            },ct);
            await storage.Freeze(asset.StagingKey,asset.SourceETag!,sourceKey,processingStop.Token);
            var source=Path.Combine(workspace,"source"); var output=Path.Combine(workspace,"playback.mp3");
            await storage.Download(sourceKey,source,processingStop.Token);
            var probe=await processor.Process(source,output,asset.Purpose=="recording" ? 180 : 3600,processingStop.Token);
            var renditionKey=$"{asset.Id:N}/{asset.Generation:N}/{token:N}/{probe.Sha256}.mp3";
            await storage.Upload(renditionKey,output,processingStop.Token);
            await leaseStop.CancelAsync(); await StopHeartbeat(heartbeat);
            // VI: Xóa tracker cũ vì heartbeat đã thay rowversion. EN: Reload the claim after heartbeat updates its concurrency token.
            db.ChangeTracker.Clear();
            await queue.Finish(job.Id,token,async () =>
            {
                var row=await db.MediaAssets.SingleAsync(x => x.Id==asset.Id,ct);
                DomainError.Require(row.Generation==asset.Generation && row.Status==MediaStatus.Processing,409,"asset_state_changed","Generation đã thay đổi.");
                var used=await db.MediaAssets.Where(x => x.OwnerId==row.OwnerId && x.Id!=row.Id && x.Status!=MediaStatus.Deleted).SumAsync(x => x.ReservedBytes,ct);
                DomainError.Require(used+asset.ExpectedBytes+probe.OutputBytes<=MediaService.Quota,422,"quota_exceeded","Rendition vượt quota còn lại.");
                row.Status=MediaStatus.Ready; row.RenditionKey=renditionKey; row.SourceKey=sourceKey; row.DurationMs=probe.DurationMs; row.Sha256=probe.Sha256; row.StoredBytes=asset.ExpectedBytes+probe.OutputBytes; row.ReservedBytes=row.StoredBytes; row.FailureCode=null;
                db.OutboxEvents.Add(new OutboxEvent { Type="media.asset_ready",AggregateId=row.Id,OccurredAt=clock.UtcNow,PayloadJson=JsonSerializer.Serialize(new { assetId=row.Id,ownerId=row.OwnerId }) });
            },$"media-quota:{asset.OwnerId}");
            log.LogInformation("Media asset {AssetId} became ready in job {JobId}",asset.Id,job.Id);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            await leaseStop.CancelAsync(); await StopHeartbeat(heartbeat); db.ChangeTracker.Clear();
            var code=error is DomainError business ? business.Code : error is RequestFailedException { Status:412 } ? "staging_changed" : "media_processing_failed";
            var permanent=error is DomainError { Status:422 } || error is RequestFailedException { Status:412 };
            await queue.Fail(job.Id,token,code,permanent);
            log.LogWarning("Media job {JobId} failed with {Code}",job.Id,code);
        }
        finally
        {
            await leaseStop.CancelAsync(); await StopHeartbeat(heartbeat);
            // VI: Chỉ dọn thư mục server vừa sinh dưới temp root. EN: Delete only this server-generated per-claim temporary workspace.
            if (Directory.Exists(workspace)) { try { Directory.Delete(workspace,true); } catch (IOException) { log.LogWarning("Could not clean temporary workspace for job {JobId}",job.Id); } }
        }
        return true;
    }
    private async Task KeepLease(Guid id, Guid token, CancellationToken stop, CancellationTokenSource work)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20),stop);
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<BackgroundQueue>().Heartbeat(id,token)) { await work.CancelAsync(); return; }
            }
            catch (Exception error) when (error is not OperationCanceledException) { log.LogWarning("Lease heartbeat failed for job {JobId}",id); await work.CancelAsync(); return; }
        }
    }
    private static async Task StopHeartbeat(Task heartbeat) { try { await heartbeat; } catch (OperationCanceledException) { /* VI: Dừng heartbeat theo lifecycle. EN: Expected lease-loop shutdown. */ } }
}
