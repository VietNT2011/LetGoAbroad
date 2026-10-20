using EnglishHub.Domain;
using EnglishHub.Infrastructure.Operations;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

public sealed class JobLeaseTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Two_workers_cannot_claim_one_job_and_stale_worker_cannot_finish_after_reclaim()
    {
        _ = app.CreateClient();
        Guid id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var job = new BackgroundJob { ResourceId = Guid.NewGuid(), DedupKey = $"test:{Guid.NewGuid()}", DueAt = app.Clock.UtcNow };
            db.BackgroundJobs.Add(job); await db.SaveChangesAsync(); id = job.Id;
        }
        async Task<BackgroundJob?> Claim() { await using var scope = app.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<BackgroundQueue>().Claim(); }
        var claims = await Task.WhenAll(Claim(), Claim()); var first = Assert.Single(claims, x => x is not null)!;
        Assert.Equal(id, first.Id); app.Clock.Advance(TimeSpan.FromSeconds(91));
        var replacement = await Claim(); Assert.NotNull(replacement); Assert.NotEqual(first.LeaseToken, replacement.LeaseToken);
        await using var finalScope = app.Services.CreateAsyncScope(); var queue = finalScope.ServiceProvider.GetRequiredService<BackgroundQueue>();
        Assert.False(await queue.Heartbeat(id, first.LeaseToken!.Value));
        var stale = await Assert.ThrowsAsync<DomainError>(() => queue.Finish(id, first.LeaseToken.Value, () => Task.CompletedTask)); Assert.Equal("lease_lost", stale.Code);
        await queue.Finish(id, replacement.LeaseToken!.Value, () => Task.CompletedTask);
        Assert.Equal("completed", (await finalScope.ServiceProvider.GetRequiredService<HubDbContext>().BackgroundJobs.AsNoTracking().SingleAsync(x => x.Id == id)).State);
    }
}
