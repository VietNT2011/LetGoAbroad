using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Sinh migration không cần khởi động API hoặc có mật khẩu thật. EN: Generate migrations without booting the API or requiring live credentials.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<HubDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Server=localhost;Database=EnglishHub;Integrated Security=true;TrustServerCertificate=true").Options);
}
