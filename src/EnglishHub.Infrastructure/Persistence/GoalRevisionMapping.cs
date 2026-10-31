using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<PlanGoalRevision> PlanGoalRevisions => Set<PlanGoalRevision>();
    partial void ConfigureGoalRevisions(ModelBuilder b)
    {
        b.Entity<PlanGoalRevision>().HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => x.EnrollmentId);
        b.Entity<PlanGoalRevision>().HasIndex(x => new { x.EnrollmentId, x.Number }).IsUnique();
        b.Entity<PlanGoalRevision>().HasAlternateKey(x => new { x.Id, x.EnrollmentId });
        b.Entity<PlanGoalRevision>().Property(x => x.SchedulingTimeZoneId).HasMaxLength(80);
        b.Entity<PlanGoalRevision>().Property(x => x.Reason).HasMaxLength(40);
        b.Entity<PlanGoalRevision>().ToTable(t => t.HasCheckConstraint("CK_PlanGoalRevision_Values", "[Number] >= 1 AND [TargetScore] IN (450,650,800) AND [MinutesPerDay] IN (20,30,45) AND [EffectiveAt] >= [RecordedAt]"));
        // VI: Task không thể ghim revision của enrollment khác. EN: A task cannot pin a goal revision belonging to another enrollment.
        b.Entity<DailyTask>().HasOne(x => x.GoalRevision).WithMany().HasForeignKey(x => new { x.GoalRevisionId, x.EnrollmentId }).HasPrincipalKey(x => new { x.Id, x.EnrollmentId });
    }
}
