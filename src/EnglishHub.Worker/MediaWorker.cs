using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
namespace EnglishHub.Worker;
/// <summary>VI: Media và expiry chạy ngoài request; crash không mất job SQL. EN: Media and expiry run off-request; SQL jobs survive worker restarts.</summary>
public sealed class MediaWorker(IServiceScopeFactory scopes, ILogger<MediaWorker> log, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var worked=config.GetValue<bool>("FeatureFlags:EnableMedia") && await scope.ServiceProvider.GetRequiredService<MediaPipeline>().RunOne(stoppingToken);
                var db=scope.ServiceProvider.GetRequiredService<HubDbContext>(); var clock=scope.ServiceProvider.GetRequiredService<IClock>();
                var expired=await db.Attempts.AsNoTracking().Where(x => x.Status==AttemptStatus.InProgress && x.DeadlineAt<=clock.UtcNow).Select(x => new { x.Id,x.UserId }).Take(20).ToListAsync(stoppingToken);
                foreach (var attempt in expired)
                {
                    await using var attemptScope=scopes.CreateAsyncScope();
                    await attemptScope.ServiceProvider.GetRequiredService<AssessmentService>().Submit(attempt.UserId,attempt.Id,new SaveAnswers(Guid.NewGuid(),[]));
                }
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { log.LogError("Worker iteration failed: {ErrorType}",error.GetType().Name); await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken); }
        }
    }
}
