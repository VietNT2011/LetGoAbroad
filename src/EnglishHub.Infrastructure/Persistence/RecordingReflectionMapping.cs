using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<RecordingReflection> RecordingReflections => Set<RecordingReflection>();
    public DbSet<RecordingReflectionRevision> RecordingReflectionRevisions => Set<RecordingReflectionRevision>();
    partial void ConfigureRecordingReflections(ModelBuilder b)
    {
        b.Entity<RecordingReflection>().HasOne(x => x.MediaAsset).WithOne().HasForeignKey<RecordingReflection>(x => x.MediaAssetId);
        b.Entity<RecordingReflection>().HasIndex(x => x.MediaAssetId).IsUnique();
        b.Entity<RecordingReflection>().HasIndex(x => new { x.UpdatedAt, x.Id });
        b.Entity<RecordingReflection>().Property(x => x.Notes).HasMaxLength(2000);
        b.Entity<RecordingReflection>().HasOne<ContentVersion>().WithMany().HasForeignKey(x => x.SourceContentVersionId);
        b.Entity<RecordingReflection>().HasOne<TranscriptSegment>().WithMany().HasForeignKey(x => new { x.SourceSegmentId, x.SourceContentVersionId }).HasPrincipalKey(x => new { x.Id, x.ContentVersionId });
        b.Entity<RecordingReflection>().ToTable("RecordingReflections", t => {
            t.HasCheckConstraint("CK_RecordingReflection_Rubric", "([Clarity] IS NULL OR [Clarity] BETWEEN 1 AND 5) AND ([Rhythm] IS NULL OR [Rhythm] BETWEEN 1 AND 5)");
            t.HasCheckConstraint("CK_RecordingReflection_Source", "[Revision] >= 0 AND ([SourceSegmentId] IS NULL OR [SourceContentVersionId] IS NOT NULL)");
        });
        var history = b.Entity<RecordingReflectionRevision>();
        history.HasOne(x => x.Reflection).WithMany().HasForeignKey(x => x.ReflectionId);
        history.HasIndex(x => new { x.ReflectionId, x.Number }).IsUnique();
        history.Property(x => x.Notes).HasMaxLength(2000); history.Property(x => x.Kind).HasMaxLength(30);
        history.HasOne<ContentVersion>().WithMany().HasForeignKey(x => x.SourceContentVersionId);
        history.HasOne<TranscriptSegment>().WithMany().HasForeignKey(x => new { x.SourceSegmentId, x.SourceContentVersionId }).HasPrincipalKey(x => new { x.Id, x.ContentVersionId });
        history.ToTable("RecordingReflectionRevisions", t => {
            t.HasCheckConstraint("CK_RecordingReflectionRevision_Rubric", "([Clarity] IS NULL OR [Clarity] BETWEEN 1 AND 5) AND ([Rhythm] IS NULL OR [Rhythm] BETWEEN 1 AND 5)");
            t.HasCheckConstraint("CK_RecordingReflectionRevision_Source", "[Number] >= 0 AND ([SourceSegmentId] IS NULL OR [SourceContentVersionId] IS NOT NULL)");
        });
    }
}
