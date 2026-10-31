using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<QuestionAnnotation> QuestionAnnotations => Set<QuestionAnnotation>();
    partial void ConfigureReview(ModelBuilder b)
    {
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
