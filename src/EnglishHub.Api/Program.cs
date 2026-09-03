using EnglishHub.Application;
using EnglishHub.Domain;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BusinessExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health/live");
app.MapOpenApi();
app.MapGet("/api/v1/status", () => new { name = "EnglishHub", version = "0.1.0", aiEnabled = false });
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
