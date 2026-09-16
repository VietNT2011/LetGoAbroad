using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptItem> AttemptItems => Set<AttemptItem>();
    public DbSet<AttemptResult> AttemptResults => Set<AttemptResult>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    partial void ConfigureAssessment(ModelBuilder b)
    {
        b.Entity<Attempt>().Property(x => x.Mode).HasMaxLength(40);
        b.Entity<Attempt>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Attempt>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<Attempt>().HasIndex(x => new { x.UserId, x.StartedAt, x.Id });
        b.Entity<AttemptItem>().HasOne(x => x.Attempt).WithMany(x => x.Items).HasForeignKey(x => x.AttemptId);
        b.Entity<AttemptItem>().HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId);
        b.Entity<AttemptItem>().HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        b.Entity<AttemptItem>().HasIndex(x => new { x.AttemptId, x.Order }).IsUnique();
        b.Entity<QuestionOption>().HasAlternateKey(x => new { x.QuestionId, x.Id });
        // VI: Option được chọn phải thuộc đúng câu. EN: A selected option cannot belong to a different question.
        b.Entity<AttemptItem>().HasOne<QuestionOption>().WithMany().HasForeignKey(x => new { x.QuestionId, x.SelectedOptionId }).HasPrincipalKey(x => new { x.QuestionId, x.Id });
        b.Entity<GradingKey>().HasKey(x => x.AttemptItemId);
        b.Entity<AttemptItem>().HasOne(x => x.GradingKey).WithOne().HasForeignKey<GradingKey>(x => x.AttemptItemId);
        b.Entity<AttemptResult>().HasKey(x => x.AttemptId);
        b.Entity<Attempt>().HasOne(x => x.Result).WithOne().HasForeignKey<AttemptResult>(x => x.AttemptId);
        b.Entity<CommandReceipt>().Property(x => x.Scope).HasMaxLength(100);
        b.Entity<CommandReceipt>().Property(x => x.PayloadHash).HasMaxLength(64);
        b.Entity<CommandReceipt>().HasIndex(x => new { x.UserId, x.Scope, x.CommandId }).IsUnique();
        b.Entity<OutboxEvent>().Property(x => x.Type).HasMaxLength(100);
        b.Entity<OutboxEvent>().HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
    }
}
