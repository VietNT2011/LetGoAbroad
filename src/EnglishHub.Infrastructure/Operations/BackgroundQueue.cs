using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Operations;

/// <summary>VI: Claim ngắn trong SQL; tác vụ bên ngoài không giữ transaction. EN: Claim atomically in SQL, then perform external work without holding a transaction.</summary>
public sealed class BackgroundQueue(HubDbContext db, Transactions tx, IClock clock)
{
    public Task<BackgroundJob?> Claim() => tx.Run("jobs:claim", async () =>
    {
        var now = clock.UtcNow;
        var exhausted = await db.BackgroundJobs.Where(x => x.State == "running" && x.LeaseUntil <= now && x.Attempts >= 4).OrderBy(x => x.LeaseUntil).ThenBy(x => x.Id).Take(10).ToListAsync();
        foreach (var job in exhausted)
        {
            job.State = "failed"; job.LastErrorCode = "lease_exhausted";
            var media = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == job.ResourceId && x.Status == MediaStatus.Processing);
            if (media is not null) { media.Status = MediaStatus.Failed; media.FailureCode = "lease_exhausted"; }
        }
        var candidate = await db.BackgroundJobs.Where(x => (x.Kind == "process_media" || x.Kind == "delete_media") && x.Attempts < 4 && x.DueAt <= now && (x.State == "pending" || x.State == "running" && x.LeaseUntil <= now)).OrderBy(x => x.DueAt).ThenBy(x => x.Id).FirstOrDefaultAsync();
        if (candidate is null) return null;
        candidate.State = "running"; candidate.LeaseToken = Guid.NewGuid(); candidate.LeaseUntil = now.AddSeconds(90); candidate.Attempts++;
        return candidate;
    });

    public async Task<bool> Heartbeat(Guid id, Guid token)
    {
        var now = clock.UtcNow;
        return await db.BackgroundJobs.Where(x => x.Id == id && x.State == "running" && x.LeaseToken == token && x.LeaseUntil > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.AddSeconds(90))) == 1;
    }

    public Task<bool> Finish(Guid id, Guid token, Func<Task> commit, string? resource = null) => tx.Run(resource ?? $"job:{id}", async () =>
    {
        var job = await db.BackgroundJobs.SingleAsync(x => x.Id == id);
        DomainError.Require(job.State == "running" && job.LeaseToken == token && job.LeaseUntil > clock.UtcNow, 409, "lease_lost", "Worker không còn giữ lease.");
        await commit(); job.State = "completed"; job.LeaseUntil = null; return true;
    });

    public Task<bool> Fail(Guid id, Guid token, string code, bool permanent) => tx.Run($"job:{id}", async () =>
    {
        var job = await db.BackgroundJobs.SingleAsync(x => x.Id == id);
        if (job.LeaseToken != token || job.State != "running" || job.LeaseUntil <= clock.UtcNow) return false;
        job.LastErrorCode = code; job.LeaseUntil = null;
        job.State = permanent || job.Attempts >= 4 ? "failed" : "pending";
        var delays = new[] { 15, 60, 300, 300 }; job.DueAt = clock.UtcNow.AddSeconds(delays[Math.Min(job.Attempts - 1, 3)] + Random.Shared.Next(0, 4));
        var asset = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == job.ResourceId);
        if (asset is not null) { if (asset.Status is MediaStatus.Processing or MediaStatus.Uploaded) asset.Status = job.State == "failed" ? MediaStatus.Failed : MediaStatus.Uploaded; asset.FailureCode = code; }
        return true;
    });
}
