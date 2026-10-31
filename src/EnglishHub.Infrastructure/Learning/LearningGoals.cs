using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed partial class LearningService
{
    /// <summary>VI: History riêng theo owner; baseline legacy ghi thời điểm quan sát thật. EN: Owner-scoped history records the actual observation time for legacy baselines.</summary>
    public Task<object> GoalHistory(Guid userId) => tx.Run($"learning:{userId}", async () =>
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId);
        if (enrollment is null) return (object)new { activeRevisionId = (Guid?)null, revisions = Array.Empty<object>() };
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var active = await goals.Resolve(enrollment, user);
        var rows = await db.PlanGoalRevisions.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id).OrderByDescending(x => x.Number).Take(20).ToListAsync();
        if (rows.All(x => x.Id != active.Id)) rows.Add(active);
        return (object)new { activeRevisionId = active.Id, revisions = rows.OrderByDescending(x => x.Number).Select(x => new { x.Id, x.Number, x.TargetScore, x.MinutesPerDay, x.RecordedAt, x.EffectiveAt, x.EffectiveOn, x.SchedulingTimeZoneId, x.Reason, status = x.Id == active.Id ? "active" : rows.Any(newer => newer.Number > x.Number && newer.EffectiveAt <= x.EffectiveAt) ? "superseded" : x.EffectiveAt > clock.UtcNow ? "scheduled" : "past" }).ToArray() };
    });
}
