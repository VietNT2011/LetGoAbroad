using System.Security.Claims;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Api.Endpoints;

public static class LearningEndpoints
{
    public static void MapLearning(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/learning").RequireAuthorization().WithTags("Learning");
        group.MapPost("/enrollment", async (EnrollInput input, ClaimsPrincipal user, LearningService service) => { var e = await service.Enroll(user.UserId(), input); return new { e.Id, e.Level, e.RuleVersion, e.PathVersionId, e.SelectionReason, e.PathStartedOn }; });
        group.MapGet("/dashboard", (ClaimsPrincipal user, LearningService service) => service.Dashboard(user.UserId()));
        group.MapPost("/tasks/{id:guid}/start", (Guid id, ClaimsPrincipal user, LearningService service) => service.StartTask(user.UserId(), id));
        group.MapPost("/plan/regenerate", (RegeneratePlanInput input, ClaimsPrincipal user, LearningService service) => service.Regenerate(user.UserId(), input));
        group.MapGet("/plan-revisions", (ClaimsPrincipal user, LearningService service) => service.GoalHistory(user.UserId()));
        group.MapGet("/skill-evidence", (ClaimsPrincipal user, SkillEvidenceService service) => service.Read(user.UserId()));
        group.MapGet("/activity", (int? days, ClaimsPrincipal user, LearningActivityService service, CancellationToken ct) => service.Read(user.UserId(), days ?? 7, ct));
        group.MapGet("/path", async (ClaimsPrincipal user, HubDbContext db) =>
        {
            var completedRoots = await db.LessonProgress.Where(x => x.UserId == user.UserId()).Select(x => x.ContentVersion.RootId).Distinct().ToArrayAsync();
            return await db.ContentVersions.AsNoTracking().Where(x => x.Root.Kind == ContentKind.Lesson && x.Root.CurrentPublishedVersionId == x.Id && x.Status == PublicationStatus.Published).OrderBy(x => x.CurriculumOrder)
                .Select(x => new { x.Id, x.RootId, x.Title, x.Summary, x.Skill, x.Minutes, completed = completedRoots.Contains(x.RootId) }).ToListAsync();
        });
        app.MapPost("/api/v1/lessons/{id:guid}/complete", (Guid id, CompleteLessonInput input, ClaimsPrincipal user, LearningService service) => service.CompleteLesson(user.UserId(), id, input)).RequireAuthorization().WithTags("Learning");
    }
}
