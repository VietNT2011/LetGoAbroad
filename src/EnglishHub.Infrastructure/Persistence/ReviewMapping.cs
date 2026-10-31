using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<QuestionAnnotation> QuestionAnnotations => Set<QuestionAnnotation>();
    public DbSet<UserCard> UserCards => Set<UserCard>();
    public DbSet<CardSourceContext> CardSourceContexts => Set<CardSourceContext>();
    public DbSet<CardReview> CardReviews => Set<CardReview>();
    partial void ConfigureReview(ModelBuilder b)
    {
        b.Entity<UserCard>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<UserCard>().Property(x => x.CustomKey).HasMaxLength(64);
        b.Entity<UserCard>().Property(x => x.Front).HasMaxLength(500);
        b.Entity<UserCard>().Property(x => x.Back).HasMaxLength(2000);
        b.Entity<UserCard>().HasIndex(x => new { x.UserId, x.CustomKey }).IsUnique();
        b.Entity<UserCard>().HasIndex(x => new { x.UserId, x.ArchivedAt, x.DueAt, x.Id });
        b.Entity<UserCard>().ToTable(t => t.HasCheckConstraint("CK_UserCard_Schedule", "[IntervalDays] >= 0 AND [IntervalDays] <= 180 AND [SuccessStep] >= 0 AND [SchedulerVersion] = 1"));
        b.Entity<CardSourceContext>().HasOne<UserCard>().WithMany(x => x.Contexts).HasForeignKey(x => x.UserCardId);
        b.Entity<CardSourceContext>().Property(x => x.ContextKey).HasMaxLength(64);
        b.Entity<CardSourceContext>().Property(x => x.Text).HasMaxLength(2000);
        b.Entity<CardSourceContext>().HasIndex(x => new { x.UserCardId, x.ContextKey }).IsUnique();
        b.Entity<CardReview>().HasOne(x => x.Card).WithMany().HasForeignKey(x => x.UserCardId);
        b.Entity<CardReview>().HasIndex(x => new { x.UserCardId, x.ReviewEventId }).IsUnique();
        b.Entity<CardReview>().HasIndex(x => new { x.UserCardId, x.ReviewedAt, x.Id });
        b.Entity<CardReview>().ToTable(t => t.HasCheckConstraint("CK_CardReview_Schedule", "[BeforeIntervalDays] >= 0 AND [BeforeIntervalDays] <= 180 AND [AfterIntervalDays] >= 0 AND [AfterIntervalDays] <= 180 AND [BeforeSuccessStep] >= 0 AND [AfterSuccessStep] >= 0 AND [Rating] >= 0 AND [Rating] <= 3 AND [SchedulerVersion] = 1"));
        b.Entity<QuestionAnnotation>().Property(x => x.NoteText).HasMaxLength(4000);
        b.Entity<QuestionAnnotation>().Property(x => x.AnchorBlock).HasMaxLength(20);
        b.Entity<QuestionAnnotation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<QuestionAnnotation>().HasIndex(x => new { x.UserId, x.QuestionId }).IsUnique();
        b.Entity<QuestionAnnotation>().HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id });
        // VI: Nguồn và câu không thể trỏ sang hai version khác nhau. EN: A composite FK prevents an annotation from pointing at a different question than its source item.
        b.Entity<QuestionAnnotation>().HasOne(x => x.SourceItem).WithMany()
            .HasForeignKey(x => new { x.SourceAttemptItemId, x.QuestionId })
            .HasPrincipalKey(x => new { x.Id, x.QuestionId });
    }
}
