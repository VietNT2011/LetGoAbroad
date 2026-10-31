using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<PathTemplate> PathTemplates => Set<PathTemplate>();
    public DbSet<PathVersion> PathVersions => Set<PathVersion>();
    public DbSet<PathStage> PathStages => Set<PathStage>();
    public DbSet<PathNode> PathNodes => Set<PathNode>();
    public DbSet<PathPrerequisite> PathPrerequisites => Set<PathPrerequisite>();

    partial void ConfigurePaths(ModelBuilder b)
    {
        b.Entity<PathTemplate>().Property(x => x.Slug).HasMaxLength(120);
        b.Entity<PathTemplate>().Property(x => x.Level).HasMaxLength(30);
        b.Entity<PathTemplate>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<PathVersion>().HasOne(x => x.Template).WithMany(x => x.Versions).HasForeignKey(x => x.TemplateId);
        b.Entity<PathVersion>().HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
        b.Entity<PathVersion>().HasAlternateKey(x => new { x.TemplateId, x.Id });
        b.Entity<PathVersion>().Property(x => x.Title).HasMaxLength(240);
        b.Entity<PathVersion>().Property(x => x.Summary).HasMaxLength(2000);
        b.Entity<PathVersion>().Property(x => x.Attribution).HasMaxLength(2000);
        b.Entity<PathTemplate>().HasOne(x => x.CurrentPublishedVersion).WithMany().HasForeignKey(x => new { x.Id, x.CurrentPublishedVersionId }).HasPrincipalKey(x => new { x.TemplateId, x.Id });
        b.Entity<PathStage>().HasOne(x => x.PathVersion).WithMany(x => x.Stages).HasForeignKey(x => x.PathVersionId);
        b.Entity<PathStage>().HasAlternateKey(x => new { x.Id, x.PathVersionId });
        b.Entity<PathStage>().HasIndex(x => new { x.PathVersionId, x.Key }).IsUnique();
        b.Entity<PathStage>().HasIndex(x => new { x.PathVersionId, x.Order }).IsUnique();
        b.Entity<PathStage>().Property(x => x.Key).HasMaxLength(80);
        b.Entity<PathStage>().Property(x => x.Title).HasMaxLength(240);
        b.Entity<PathNode>().HasOne(x => x.Stage).WithMany(x => x.Nodes).HasForeignKey(x => new { x.StageId, x.PathVersionId }).HasPrincipalKey(x => new { x.Id, x.PathVersionId });
        b.Entity<PathNode>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<PathNode>().HasAlternateKey(x => new { x.PathVersionId, x.Id });
        b.Entity<PathNode>().HasAlternateKey(x => new { x.Id, x.ContentVersionId });
        b.Entity<PathNode>().HasIndex(x => new { x.PathVersionId, x.Key }).IsUnique();
        b.Entity<PathNode>().HasIndex(x => new { x.StageId, x.Order }).IsUnique();
        b.Entity<PathNode>().Property(x => x.Key).HasMaxLength(80);
        b.Entity<PathNode>().Property(x => x.CompletionRule).HasMaxLength(40);
        b.Entity<PathPrerequisite>().HasKey(x => new { x.NodeId, x.PrerequisiteNodeId });
        // VI: Hai đầu cạnh phải thuộc cùng version. EN: Composite foreign keys keep both ends of every edge in the same path version.
        b.Entity<PathPrerequisite>().HasOne(x => x.Node).WithMany(x => x.Prerequisites).HasForeignKey(x => new { x.PathVersionId, x.NodeId }).HasPrincipalKey(x => new { x.PathVersionId, x.Id });
        b.Entity<PathPrerequisite>().HasOne(x => x.PrerequisiteNode).WithMany().HasForeignKey(x => new { x.PathVersionId, x.PrerequisiteNodeId }).HasPrincipalKey(x => new { x.PathVersionId, x.Id });
        b.Entity<Enrollment>().HasOne(x => x.PathVersion).WithMany().HasForeignKey(x => x.PathVersionId);
        b.Entity<Enrollment>().HasOne<Attempt>().WithMany().HasForeignKey(x => x.PlacementAttemptId);
        b.Entity<Enrollment>().Property(x => x.SelectionReason).HasMaxLength(50);
        b.Entity<Enrollment>().Property(x => x.PathTimeZoneId).HasMaxLength(80);
        b.Entity<Enrollment>().Property(x => x.PathStudyDays).HasMaxLength(20);
        b.Entity<DailyTask>().HasOne(x => x.PathNode).WithMany().HasForeignKey(x => new { x.PathVersionId, x.PathNodeId }).HasPrincipalKey(x => new { x.PathVersionId, x.Id });
        b.Entity<DailyTask>().HasOne<PathNode>().WithMany().HasForeignKey(x => new { x.PathNodeId, x.ContentVersionId }).HasPrincipalKey(x => new { x.Id, x.ContentVersionId });
        b.Entity<DailyTask>().HasAlternateKey(x => new { x.Id, x.EnrollmentId });
        b.Entity<DailyTask>().HasOne<DailyTask>().WithMany().HasForeignKey(x => new { x.SourceTaskId, x.EnrollmentId }).HasPrincipalKey(x => new { x.Id, x.EnrollmentId });
        b.Entity<DailyTask>().Property(x => x.TimeZoneId).HasMaxLength(80);
        b.Entity<DailyTask>().HasIndex(x => new { x.EnrollmentId, x.PathNodeId }).IsUnique().HasFilter("[PathNodeId] IS NOT NULL AND [Status] = 'pending'");
        b.Entity<DailyTask>().ToTable(t => t.HasCheckConstraint("CK_DailyTask_PathPair", "([PathNodeId] IS NULL AND [PathVersionId] IS NULL) OR ([PathNodeId] IS NOT NULL AND [PathVersionId] IS NOT NULL)"));
        b.Entity<TaskCompletion>().HasIndex(x => x.AttemptId).IsUnique().HasFilter("[AttemptId] IS NOT NULL");
    }
}
