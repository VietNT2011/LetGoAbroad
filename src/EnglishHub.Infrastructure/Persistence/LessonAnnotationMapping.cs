using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<LessonAnnotation> LessonAnnotations => Set<LessonAnnotation>();
    partial void ConfigureLessonAnnotations(ModelBuilder b)
    {
        b.Entity<LessonAnnotation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<LessonAnnotation>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<LessonAnnotation>().Property(x => x.BlockKey).HasMaxLength(40);
        b.Entity<LessonAnnotation>().Property(x => x.Quote).HasMaxLength(TextAnchor.MaxQuoteLength);
        b.Entity<LessonAnnotation>().Property(x => x.NoteText).HasMaxLength(4000);
        b.Entity<LessonAnnotation>().HasIndex(x => new { x.UserId, x.ContentVersionId, x.IsDeleted });
        b.Entity<LessonAnnotation>().HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id });
        b.Entity<LessonAnnotation>().ToTable(t => t.HasCheckConstraint("CK_LessonAnnotation_Anchor", "[BlockKey] = 'body' AND [AnchorStart] >= 0 AND [AnchorEnd] > [AnchorStart] AND [AnchorEnd] - [AnchorStart] <= 4000"));
    }
}
