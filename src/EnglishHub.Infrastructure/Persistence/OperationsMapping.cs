using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;
public sealed partial class HubDbContext
{
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    partial void ConfigureOperations(ModelBuilder b)
    {
        b.Entity<MediaAsset>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId);
        b.Entity<MediaAsset>().Property(x => x.Purpose).HasMaxLength(40);
        b.Entity<MediaAsset>().Property(x => x.FileName).HasMaxLength(200);
        b.Entity<MediaAsset>().Property(x => x.DeclaredContentType).HasMaxLength(80);
        b.Entity<MediaAsset>().HasIndex(x => new { x.OwnerId,x.Status });
        b.Entity<ContentVersion>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.MediaAssetId);
        b.Entity<BackgroundJob>().Property(x => x.Kind).HasMaxLength(50);
        b.Entity<BackgroundJob>().Property(x => x.State).HasMaxLength(20);
        b.Entity<BackgroundJob>().Property(x => x.DedupKey).HasMaxLength(150);
        b.Entity<BackgroundJob>().HasIndex(x => x.DedupKey).IsUnique();
        b.Entity<BackgroundJob>().HasIndex(x => new { x.State,x.DueAt,x.LeaseUntil });
    }
}
