using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

/// <summary>VI: Gọi trong transaction khóa learning:user. EN: Call within the owner-scoped learning transaction, including profile writes.</summary>
public sealed class GoalPlanService(HubDbContext db, IClock clock)
{
    public async Task<PlanGoalRevision> Initialize(Enrollment enrollment)
    {
        var existing = await db.PlanGoalRevisions.Where(x => x.EnrollmentId == enrollment.Id).OrderByDescending(x => x.Number).FirstOrDefaultAsync();
        if (existing is not null) return existing;
        var now = clock.UtcNow;
        var revision = new PlanGoalRevision { EnrollmentId = enrollment.Id, Number = Math.Max(1, enrollment.PlanRevision), TargetScore = enrollment.TargetScore, MinutesPerDay = enrollment.MinutesPerDay, RecordedAt = now, EffectiveAt = now, EffectiveOn = StudyDate(now, enrollment.TimeZoneId), SchedulingTimeZoneId = enrollment.TimeZoneId, Reason = db.Entry(enrollment).State == EntityState.Added ? "enrollment" : "observed_baseline" };
        db.PlanGoalRevisions.Add(revision); await db.SaveChangesAsync(); return revision;
    }

    public async Task<PlanGoalRevision> Schedule(Enrollment enrollment, int target, int minutes, string zone, string reason = "profile_change")
    {
        var latest = await Initialize(enrollment);
        if (latest.TargetScore == target && latest.MinutesPerDay == minutes) return latest;
        var now = clock.UtcNow; var effective = NextMidnight(now, zone);
        var revision = new PlanGoalRevision { EnrollmentId = enrollment.Id, Number = checked(latest.Number + 1), TargetScore = target, MinutesPerDay = minutes, RecordedAt = now, EffectiveAt = effective, EffectiveOn = StudyDate(effective, zone), SchedulingTimeZoneId = zone, Reason = reason };
        db.PlanGoalRevisions.Add(revision);
        db.OutboxEvents.Add(new OutboxEvent { Type = "learning.plan_revision_scheduled", AggregateId = revision.Id, OccurredAt = now, PayloadJson = JsonSerializer.Serialize(new { userId = enrollment.UserId, revisionId = revision.Id, revision.Number, revision.EffectiveAt, targetScore = target, minutesPerDay = minutes }) });
        await db.SaveChangesAsync(); return revision;
    }

    public async Task<PlanGoalRevision> Resolve(Enrollment enrollment, AppUser user)
    {
        await Schedule(enrollment, user.TargetScore, user.MinutesPerDay, user.TimeZoneId, "observed_profile");
        var current = await db.PlanGoalRevisions.Where(x => x.EnrollmentId == enrollment.Id && x.EffectiveAt <= clock.UtcNow).OrderByDescending(x => x.Number).FirstAsync();
        enrollment.PlanRevision = current.Number; enrollment.TargetScore = current.TargetScore; enrollment.MinutesPerDay = current.MinutesPerDay;
        return current;
    }

    /// <summary>VI: Nửa đêm hợp lệ đầu tiên; xử lý khoảng giờ DST bị bỏ hoặc lặp. EN: Choose the first valid next midnight, handling skipped and ambiguous local times.</summary>
    public static DateTimeOffset NextMidnight(DateTimeOffset now, string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static DateOnly StudyDate(DateTimeOffset instant, string zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
}
