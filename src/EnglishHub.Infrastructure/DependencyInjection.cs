using EnglishHub.Application;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Identity;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>VI: SQL Server mặc định; SQLite phải chọn rõ. EN: SQL Server is the default; SQLite requires an explicit profile.</summary>
    public static IServiceCollection AddEnglishHub(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddDbContext<HubDbContext>(options =>
        {
            var connection = config.GetConnectionString("Database");
            if (config["Database:Provider"] == "Sqlite") options.UseSqlite(connection ?? "Data Source=englishhub.db");
            else options.UseSqlServer(connection ?? throw new InvalidOperationException("Set ConnectionStrings__Database or select Database__Provider=Sqlite."));
        });
        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequiredLength = 10;
            options.Lockout.MaxFailedAccessAttempts = 5;
        }).AddEntityFrameworkStores<HubDbContext>().AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "EnglishHub.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddScoped<Transactions>();
        services.AddScoped<ContentService>();
        services.AddScoped<MailDispatcher>();
        services.AddScoped<Receipts>();
        services.AddScoped<AssessmentService>();
        services.AddScoped<LearningService>();
        return services;
    }
}
