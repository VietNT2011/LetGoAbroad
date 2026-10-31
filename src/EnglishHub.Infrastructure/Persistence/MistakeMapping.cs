using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<MistakeItem> MistakeItems => Set<MistakeItem>();
    public DbSet<MistakeReviewTarget> MistakeReviewTargets => Set<MistakeReviewTarget>();
    public DbSet<MistakeProcessedAttempt> MistakeProcessedAttempts => Set<MistakeProcessedAttempt>();
    partial void ConfigureMistakes(ModelBuilder b)
    {
        b.Entity<MistakeItem>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<MistakeItem>().HasIndex(x => new { x.UserId, x.QuestionId }).IsUnique();
        b.Entity<MistakeItem>().HasAlternateKey(x => new { x.Id, x.UserId, x.QuestionId });
        b.Entity<MistakeItem>().HasOne(x => x.SourceItem).WithMany().HasForeignKey(x => new { x.SourceAttemptItemId, x.QuestionId }).HasPrincipalKey(x => new { x.Id, x.QuestionId });
        b.Entity<MistakeItem>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<MistakeItem>().Property(x => x.CycleTimeZoneId).HasMaxLength(80);
        b.Entity<MistakeItem>().HasIndex(x => new { x.UserId, x.Status, x.NextReviewAt, x.Id });
        b.Entity<MistakeItem>().ToTable(t => t.HasCheckConstraint("CK_Mistake_Values", "[WrongCount] > 0 AND [CorrectReviewDays] >= 0 AND [CorrectReviewDays] <= 2 AND (([Status] = 'learning' AND [NextReviewAt] IS NOT NULL) OR ([Status] = 'mastered' AND [NextReviewAt] IS NULL AND [CorrectReviewDays] = 2))"));
        b.Entity<MistakeReviewTarget>().HasKey(x => x.AttemptItemId);
        b.Entity<MistakeReviewTarget>().HasOne(x => x.Item).WithMany().HasForeignKey(x => new { x.AttemptItemId, x.QuestionId }).HasPrincipalKey(x => new { x.Id, x.QuestionId });
        b.Entity<MistakeReviewTarget>().HasOne(x => x.Mistake).WithMany().HasForeignKey(x => new { x.MistakeId, x.UserId, x.QuestionId }).HasPrincipalKey(x => new { x.Id, x.UserId, x.QuestionId });
        b.Entity<MistakeProcessedAttempt>().HasKey(x => x.AttemptId);
        b.Entity<MistakeProcessedAttempt>().HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId);
    }
}
