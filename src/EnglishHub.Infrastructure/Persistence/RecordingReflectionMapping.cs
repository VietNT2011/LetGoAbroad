using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<RecordingReflection> RecordingReflections => Set<RecordingReflection>();
    partial void ConfigureRecordingReflections(ModelBuilder b)
    {
        b.Entity<RecordingReflection>().HasOne(x => x.MediaAsset).WithOne().HasForeignKey<RecordingReflection>(x => x.MediaAssetId);
        b.Entity<RecordingReflection>().HasIndex(x => x.MediaAssetId).IsUnique();
        b.Entity<RecordingReflection>().HasIndex(x => new { x.UpdatedAt, x.Id });
        b.Entity<RecordingReflection>().Property(x => x.Notes).HasMaxLength(2000);
        b.Entity<RecordingReflection>().ToTable("RecordingReflections", t => t.HasCheckConstraint("CK_RecordingReflection_Rubric", "([Clarity] IS NULL OR [Clarity] BETWEEN 1 AND 5) AND ([Rhythm] IS NULL OR [Rhythm] BETWEEN 1 AND 5)"));
    }
}
