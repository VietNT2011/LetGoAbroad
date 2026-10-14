using EnglishHub.Application;
using EnglishHub.Domain;
using Microsoft.AspNetCore.Diagnostics;
using EnglishHub.Api.Endpoints;
using EnglishHub.Infrastructure;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEnglishHub(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)); o.SerializerOptions.RespectNullableAnnotations = true; o.SerializerOptions.RespectRequiredConstructorParameters = true; });
builder.Services.AddRateLimiter(o => o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
// VI: Mọi thao tác ghi browser đều cần CSRF, kể cả login. EN: Every unsafe browser API request, including login, needs an antiforgery token.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "invalid_csrf", extensions: new Dictionary<string, object?> { ["code"] = "invalid_csrf" }).ExecuteAsync(context); return; }
    }
    await next(context);
});
if (app.Configuration.GetValue<bool>("Database:Initialize"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
    if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    // VI: Role hệ thống cần tồn tại cả khi không dùng demo seed. EN: Registration roles must exist even when demo accounts are disabled.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { "Learner", "ContentEditor", "Admin" })
        if (!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    if (app.Configuration.GetValue<bool>("Seed:Demo") && !app.Environment.IsProduction())
    {
        await DemoSeed.Run(db, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
        await DemoLearningSeed.Ensure(db);
        await DemoAssessmentSeed.Ensure(db);
        await DemoFoundationSeed.Ensure(db);
    }
}
app.MapIdentity();
app.MapContent();
app.MapAssessment();
app.MapLearning();
app.MapMedia();
app.MapHealthChecks("/health/live");
app.MapGet("/health/ready",async (HubDbContext db,CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status="ready" }) : Results.StatusCode(503));
app.MapOpenApi();
app.MapGet("/api/v1/status", () => new { name = "EnglishHub", version = "0.1.0", aiEnabled = false, mediaEnabled = app.Configuration.GetValue<bool>("FeatureFlags:EnableMedia") });
app.MapFallbackToFile("index.html");
app.Map("/api/{**path}",() => Results.Problem(statusCode:404,title:"endpoint_not_found"));
app.Run();

/// <summary>VI: Điểm vào cho integration test. EN: Exposes the entry point to WebApplicationFactory.</summary>
public partial class Program;

/// <summary>VI: Trả lỗi có cấu trúc, không nuốt exception. EN: Produces explicit Problem Details for business errors.</summary>
public sealed class BusinessExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainError error) return false;
        context.Response.StatusCode = error.Status;
        await Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code, ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return true;
    }
}
