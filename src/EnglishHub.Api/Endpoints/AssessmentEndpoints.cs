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
        group.MapPost("/preview", (StartAttempt input, AssessmentService service) => service.Preview(input));
        app.MapGet("/api/v1/tests/{id:guid}/configuration", async (Guid id, HubDbContext db) =>
        {
            // VI: Task/history ghim version cũ; không bắt buộc nó còn là bản mới nhất trong catalogue. EN: Tasks pin older published versions, so do not require the latest catalogue pointer.
            var version = await db.ContentVersions.AsNoTracking().Include(x => x.Root).SingleOrDefaultAsync(x => x.Id == id && x.Status == EnglishHub.Domain.PublicationStatus.Published && x.Root.Kind == EnglishHub.Domain.ContentKind.Assessment);
            if (version is null) return Results.NotFound();
            var parts = await db.QuestionGroups.AsNoTracking().Where(x => x.ContentVersionId == id).GroupBy(x => x.Part).Select(g => new { part = g.Key, questionCount = g.Sum(x => x.Questions.Count) }).OrderBy(x => x.part).ToListAsync();
            return Results.Ok(new { version.Id, version.RootId, version.Root.Slug, version.Root.Kind, version.Title, version.Summary, version.Skill, version.Minutes, version.DurationSeconds, version.IsFixture, version.Version, questionCount = parts.Sum(x => x.questionCount), parts });
        }).RequireAuthorization().WithTags("Assessment");
        group.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, AssessmentService service) => service.Read(user.UserId(), id));
        group.MapPut("/{id:guid}/answers", (Guid id, SaveAnswers input, ClaimsPrincipal user, AssessmentService service) => service.Save(user.UserId(), id, input));
        group.MapPost("/{id:guid}/submit", (Guid id, SaveAnswers input, ClaimsPrincipal user, AssessmentService service) => service.Submit(user.UserId(), id, input));
        group.MapGet("/{id:guid}/result", (Guid id, ClaimsPrincipal user, AssessmentService service) => service.Result(user.UserId(), id));
        group.MapGet("/", async (ClaimsPrincipal user, HubDbContext db) => await db.Attempts.AsNoTracking().Where(x => x.UserId == user.UserId()).OrderByDescending(x => x.StartedAt).Take(30)
            .Select(x => new { x.Id, x.ContentVersion.Title, x.Mode, x.Status, x.StartedAt, x.DeadlineAt, correctCount = x.Result == null ? (int?)null : x.Result.CorrectCount, totalCount = x.Items.Count }).ToListAsync());
    }
}
