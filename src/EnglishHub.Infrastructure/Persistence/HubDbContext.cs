using EnglishHub.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EnglishHub.Infrastructure.Identity;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Ràng buộc quan hệ là lớp bảo vệ cuối. EN: Relational constraints enforce invariants even across API instances.</summary>
public sealed partial class HubDbContext(DbContextOptions<HubDbContext> options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ContentRoot> ContentRoots => Set<ContentRoot>();
    public DbSet<ContentVersion> ContentVersions => Set<ContentVersion>();
    public DbSet<QuestionGroup> QuestionGroups => Set<QuestionGroup>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(120);
        b.Entity<AppUser>().Property(x => x.TimeZoneId).HasMaxLength(80);
        b.Entity<ContentRoot>().Property(x => x.Slug).HasMaxLength(160);
        b.Entity<ContentRoot>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<ContentVersion>().Property(x => x.Title).HasMaxLength(240);
        b.Entity<ContentVersion>().Property(x => x.Skill).HasMaxLength(60);
        b.Entity<ContentVersion>().HasIndex(x => new { x.RootId, x.Version }).IsUnique();
        b.Entity<ContentVersion>().HasAlternateKey(x => new { x.RootId, x.Id });
        b.Entity<ContentVersion>().HasOne(x => x.Root).WithMany(x => x.Versions).HasForeignKey(x => x.RootId).OnDelete(DeleteBehavior.NoAction);
        // VI: Không thể trỏ con trỏ publish sang root khác. EN: Composite FK prevents publishing another root's version.
        b.Entity<ContentRoot>().HasOne(x => x.CurrentPublishedVersion).WithMany()
            .HasForeignKey(x => new { x.Id, x.CurrentPublishedVersionId }).HasPrincipalKey(x => new { x.RootId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.Entity<QuestionGroup>().HasOne(x => x.ContentVersion).WithMany(x => x.Groups).HasForeignKey(x => x.ContentVersionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<Question>().HasOne(x => x.Group).WithMany(x => x.Questions).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<QuestionOption>().HasOne(x => x.Question).WithMany(x => x.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<QuestionGroup>().HasIndex(x => new { x.ContentVersionId, x.Order }).IsUnique();
        b.Entity<Question>().HasIndex(x => new { x.GroupId, x.Order }).IsUnique();
        b.Entity<AuditEntry>().Property(x => x.Action).HasMaxLength(100);
        b.Entity<DevMail>().Property(x => x.Recipient).HasMaxLength(256);
        b.Entity<DevMail>().HasIndex(x => new { x.Recipient, x.CreatedAt });
        ConfigureAssessment(b);
        ConfigureLearning(b);
        ConfigureReview(b);
        ConfigureCommunity(b);
        ConfigureOperations(b);
        foreach (var type in b.Model.GetEntityTypes().Where(x => typeof(VersionedEntity).IsAssignableFrom(x.ClrType)))
        {
            var token = b.Entity(type.ClrType).Property(nameof(VersionedEntity.RowVersion)).IsConcurrencyToken();
            if (Database.IsSqlServer()) token.IsRowVersion();
            else token.ValueGeneratedNever();
        }
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys())) fk.DeleteBehavior = DeleteBehavior.NoAction;
        if (Database.IsSqlite())
            foreach (var property in b.Model.GetEntityTypes().SelectMany(x => x.GetProperties()).Where(x => x.ClrType == typeof(DateTimeOffset) || x.ClrType == typeof(DateTimeOffset?)))
                // VI: UTC ticks giữ nguyên precision; converter binary chuẩn làm mất phần lẻ và đổi response khi replay. EN: UTC ticks preserve precision so persisted replays match the original acknowledgement.
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero)));
    }

    partial void ConfigureAssessment(ModelBuilder b);
    partial void ConfigureLearning(ModelBuilder b);
    partial void ConfigureReview(ModelBuilder b);
    partial void ConfigureCommunity(ModelBuilder b);
    partial void ConfigureOperations(ModelBuilder b);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // VI: SQLite không có rowversion; chỉ profile test/demo dùng token ứng dụng. EN: SQLite uses an application token only in the demo/test profile.
        if (!Database.IsSqlServer())
            foreach (var entry in ChangeTracker.Entries<VersionedEntity>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                entry.Entity.RowVersion = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        return base.SaveChangesAsync(ct);
    }
}
