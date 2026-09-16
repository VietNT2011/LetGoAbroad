using System.Security.Claims;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Api.Endpoints;

public static class AssessmentEndpoints
{
    public static void MapAssessment(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/attempts").RequireAuthorization().WithTags("Assessment");
        group.MapPost("/", (StartAttempt input, ClaimsPrincipal user, AssessmentService service) => service.Start(user.UserId(), input));
        group.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, AssessmentService service) => service.Read(user.UserId(), id));
        group.MapPut("/{id:guid}/answers", (Guid id, SaveAnswers input, ClaimsPrincipal user, AssessmentService service) => service.Save(user.UserId(), id, input));
        group.MapPost("/{id:guid}/submit", (Guid id, SaveAnswers input, ClaimsPrincipal user, AssessmentService service) => service.Submit(user.UserId(), id, input));
        group.MapGet("/{id:guid}/result", (Guid id, ClaimsPrincipal user, AssessmentService service) => service.Result(user.UserId(), id));
        group.MapGet("/", async (ClaimsPrincipal user, HubDbContext db) => await db.Attempts.AsNoTracking().Where(x => x.UserId == user.UserId()).OrderByDescending(x => x.StartedAt).Take(30)
            .Select(x => new { x.Id, x.ContentVersion.Title, x.Mode, x.Status, x.StartedAt, x.DeadlineAt, correctCount = x.Result == null ? (int?)null : x.Result.CorrectCount, totalCount = x.Items.Count }).ToListAsync());
    }
}
