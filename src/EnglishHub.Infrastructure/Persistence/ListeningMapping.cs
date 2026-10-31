using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<ListeningSession> ListeningSessions => Set<ListeningSession>();
    public DbSet<ListeningResume> ListeningResumes => Set<ListeningResume>();
    public DbSet<ListeningDisclosure> ListeningDisclosures => Set<ListeningDisclosure>();
    public DbSet<SegmentAnswer> SegmentAnswers => Set<SegmentAnswer>();
    partial void ConfigureListening(ModelBuilder b)
    {
        b.Entity<TranscriptSegment>().HasOne<ContentVersion>().WithMany(x => x.Segments).HasForeignKey(x => x.ContentVersionId);
        b.Entity<TranscriptSegment>().HasAlternateKey(x => new { x.Id, x.ContentVersionId });
        b.Entity<TranscriptSegment>().HasIndex(x => new { x.ContentVersionId, x.Order }).IsUnique();
        b.Entity<TranscriptSegment>().Property(x => x.TextEn).HasMaxLength(2000); b.Entity<TranscriptSegment>().Property(x => x.TextVi).HasMaxLength(2000);
        b.Entity<TranscriptSegment>().ToTable(t => t.HasCheckConstraint("CK_Segment_Timing", "[StartMs] >= 0 AND [EndMs] > [StartMs] AND [Order] >= 0 AND [NormalizationPolicyVersion] = 1"));
        b.Entity<ListeningSession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<ListeningSession>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<ListeningSession>().HasOne(x => x.Media).WithMany().HasForeignKey(x => x.MediaAssetId);
        b.Entity<ListeningSession>().HasAlternateKey(x => new { x.Id, x.UserId, x.ContentVersionId });
        b.Entity<ListeningResume>().HasIndex(x => new { x.UserId, x.ContentVersionId }).IsUnique();
        b.Entity<ListeningResume>().HasOne<ListeningSession>().WithMany().HasForeignKey(x => new { x.SessionId, x.UserId, x.ContentVersionId }).HasPrincipalKey(x => new { x.Id, x.UserId, x.ContentVersionId });
        b.Entity<ListeningResume>().ToTable(t => t.HasCheckConstraint("CK_Listening_Position", "[PositionMs] >= 0"));
        b.Entity<ListeningDisclosure>().HasKey(x => new { x.UserId, x.SegmentId });
        b.Entity<ListeningDisclosure>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<ListeningDisclosure>().HasOne<TranscriptSegment>().WithMany().HasForeignKey(x => x.SegmentId);
        b.Entity<SegmentAnswer>().HasOne<ListeningSession>().WithMany().HasForeignKey(x => new { x.SessionId, x.UserId, x.ContentVersionId }).HasPrincipalKey(x => new { x.Id, x.UserId, x.ContentVersionId });
        b.Entity<SegmentAnswer>().HasOne<TranscriptSegment>().WithMany().HasForeignKey(x => new { x.SegmentId, x.ContentVersionId }).HasPrincipalKey(x => new { x.Id, x.ContentVersionId });
        b.Entity<SegmentAnswer>().HasIndex(x => new { x.SessionId, x.SegmentId, x.Number }).IsUnique();
        b.Entity<SegmentAnswer>().Property(x => x.AnswerText).HasMaxLength(2000);
        b.Entity<SegmentAnswer>().ToTable(t => t.HasCheckConstraint("CK_SegmentAnswer_Number", "[Number] >= 1 AND [Number] <= 20"));
    }
}
