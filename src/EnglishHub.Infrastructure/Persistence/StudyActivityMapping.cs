using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<ActivitySession> ActivitySessions => Set<ActivitySession>();
    public DbSet<ActivityHeartbeat> ActivityHeartbeats => Set<ActivityHeartbeat>();
    public DbSet<ActivityInterval> ActivityIntervals => Set<ActivityInterval>();
    partial void ConfigureStudyActivity(ModelBuilder b)
    {
        b.Entity<ActivitySession>().HasAlternateKey(x => new { x.Id, x.UserId });
        b.Entity<ActivitySession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<ActivitySession>().HasIndex(x => new { x.UserId, x.ClientSessionId }).IsUnique();
        b.Entity<ActivitySession>().Property(x => x.Kind).HasMaxLength(24);
        b.Entity<ActivitySession>().ToTable(t => t.HasCheckConstraint("CK_ActivitySession_Values", "[LastSequence] >= 0 AND [LastSequence] <= 1000000 AND [ExpiresAt] > [CreatedAt] AND [Kind] IN ('lesson','assessment','cards')"));
        b.Entity<ActivityHeartbeat>().HasKey(x => new { x.SessionId, x.Sequence });
        b.Entity<ActivityHeartbeat>().HasAlternateKey(x => new { x.SessionId, x.Sequence, x.UserId });
        b.Entity<ActivityHeartbeat>().HasOne<ActivitySession>().WithMany().HasForeignKey(x => new { x.SessionId, x.UserId }).HasPrincipalKey(x => new { x.Id, x.UserId });
        b.Entity<ActivityHeartbeat>().ToTable(t => t.HasCheckConstraint("CK_ActivityHeartbeat_Values", "[Sequence] > 0 AND [Sequence] <= 1000000 AND [CreditedTicks] >= 0 AND [CreditedTicks] <= 300000000"));
        b.Entity<ActivityInterval>().HasOne<ActivityHeartbeat>().WithMany().HasForeignKey(x => new { x.SessionId, x.Sequence, x.UserId }).HasPrincipalKey(x => new { x.SessionId, x.Sequence, x.UserId });
        b.Entity<ActivityInterval>().HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
        b.Entity<ActivityInterval>().HasIndex(x => new { x.UserId, x.EndAt, x.StartAt });
        var duration = Database.IsSqlServer() ? "DATEDIFF_BIG(nanosecond,[StartAt],[EndAt]) / 100 = [DurationTicks]" : "[EndAt] - [StartAt] = [DurationTicks]";
        b.Entity<ActivityInterval>().ToTable(t => t.HasCheckConstraint("CK_ActivityInterval_Duration", $"[EndAt] > [StartAt] AND [DurationTicks] > 0 AND [DurationTicks] <= 300000000 AND ({duration})"));
    }
}
