using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;

public sealed partial class HubDbContext
{
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<DailyTask> DailyTasks => Set<DailyTask>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<TaskCompletion> TaskCompletions => Set<TaskCompletion>();
    partial void ConfigureLearning(ModelBuilder b)
    {
        b.Entity<Enrollment>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<Enrollment>().HasAlternateKey(x => new { x.Id, x.UserId });
        b.Entity<Enrollment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Enrollment>().Property(x => x.Level).HasMaxLength(30);
        b.Entity<Enrollment>().Property(x => x.TimeZoneId).HasMaxLength(80);
        b.Entity<DailyTask>().HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => new { x.EnrollmentId, x.UserId }).HasPrincipalKey(x => new { x.Id, x.UserId });
        b.Entity<DailyTask>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<DailyTask>().HasIndex(x => new { x.EnrollmentId, x.LocalDate, x.SlotKey }).IsUnique();
        b.Entity<DailyTask>().HasIndex(x => new { x.UserId, x.LocalDate, x.Status });
        b.Entity<DailyTask>().Property(x => x.Status).HasMaxLength(25);
        b.Entity<DailyTask>().Property(x => x.ReasonCode).HasMaxLength(50);
        b.Entity<LessonProgress>().HasIndex(x => new { x.UserId, x.ContentVersionId }).IsUnique();
        b.Entity<LessonProgress>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<LessonProgress>().HasOne(x => x.ContentVersion).WithMany().HasForeignKey(x => x.ContentVersionId);
        b.Entity<TaskCompletion>().HasKey(x => x.DailyTaskId);
        b.Entity<TaskCompletion>().Property(x => x.TimeZoneId).HasMaxLength(80);
        b.Entity<TaskCompletion>().HasOne(x => x.DailyTask).WithOne().HasForeignKey<TaskCompletion>(x => x.DailyTaskId);
        b.Entity<TaskCompletion>().HasOne<LessonProgress>().WithMany().HasForeignKey(x => x.LessonProgressId);
        b.Entity<TaskCompletion>().HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId);
        b.Entity<TaskCompletion>().ToTable(t => t.HasCheckConstraint("CK_TaskCompletion_OneSource", "([LessonProgressId] IS NOT NULL AND [AttemptId] IS NULL) OR ([LessonProgressId] IS NULL AND [AttemptId] IS NOT NULL)"));
    }
}
