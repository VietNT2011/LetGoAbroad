using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<PathChoiceRevision> PathChoiceRevisions => Set<PathChoiceRevision>();
    partial void ConfigurePathChoices(ModelBuilder b)
    {
        var choice = b.Entity<PathChoiceRevision>();
        // VI: FK ghép không cho ghi lịch sử vào enrollment của người khác. EN: Composite ownership rejects a revision attached to another learner's enrollment.
        choice.HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => new { x.EnrollmentId, x.UserId }).HasPrincipalKey(x => new { x.Id, x.UserId });
        choice.HasOne(x => x.PathVersion).WithMany().HasForeignKey(x => x.PathVersionId);
        choice.HasOne<Attempt>().WithMany().HasForeignKey(x => x.PlacementAttemptId);
        choice.HasIndex(x => new { x.EnrollmentId, x.Number }).IsUnique();
        choice.HasAlternateKey(x => new { x.EnrollmentId, x.Id });
        b.Entity<DailyTask>().HasOne<PathChoiceRevision>().WithMany().HasForeignKey(x => new { x.EnrollmentId, x.PathChoiceRevisionId }).HasPrincipalKey(x => new { x.EnrollmentId, x.Id });
        choice.Property(x => x.Level).HasMaxLength(30);
        choice.Property(x => x.TimeZoneId).HasMaxLength(80);
        choice.Property(x => x.SchedulingTimeZoneId).HasMaxLength(80);
        choice.Property(x => x.StudyDays).HasMaxLength(20);
        choice.Property(x => x.Action).HasMaxLength(40);
        choice.Property(x => x.SelectionReason).HasMaxLength(50);
        choice.Property(x => x.UserReason).HasMaxLength(500);
        choice.ToTable(t => t.HasCheckConstraint("CK_PathChoice_Values", "[Number] >= 1 AND [CalendarElapsedDays] >= 0 AND [EffectiveAt] >= [RecordedAt] AND ([ActivatedAt] IS NULL OR [ActivatedAt] >= [EffectiveAt]) AND (([PathVersionId] IS NULL AND [PathStartedOn] IS NULL) OR ([PathVersionId] IS NOT NULL AND [PathStartedOn] IS NOT NULL))"));
        b.Entity<Enrollment>().ToTable(t => t.HasCheckConstraint("CK_Enrollment_Calendar", "[PathCalendarElapsedDays] >= 0"));
    }
}
