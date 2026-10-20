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
        group.MapPost("/enrollment", async (EnrollInput input, ClaimsPrincipal user, LearningService service) => { var e = await service.Enroll(user.UserId(), input); return new { e.Id, e.Level, e.RuleVersion }; });
        group.MapGet("/dashboard", (ClaimsPrincipal user, LearningService service) => service.Dashboard(user.UserId()));
        group.MapGet("/path", async (ClaimsPrincipal user, HubDbContext db) =>
        {
            var completedRoots = await db.LessonProgress.Where(x => x.UserId == user.UserId()).Select(x => x.ContentVersion.RootId).Distinct().ToArrayAsync();
            return await db.ContentVersions.AsNoTracking().Where(x => x.Root.Kind == ContentKind.Lesson && x.Root.CurrentPublishedVersionId == x.Id && x.Status == PublicationStatus.Published).OrderBy(x => x.CurriculumOrder)
                .Select(x => new { x.Id, x.RootId, x.Title, x.Summary, x.Skill, x.Minutes, completed = completedRoots.Contains(x.RootId) }).ToListAsync();
        });
        app.MapPost("/api/v1/lessons/{id:guid}/complete", (Guid id, CompleteLessonInput input, ClaimsPrincipal user, LearningService service) => service.CompleteLesson(user.UserId(), id, input)).RequireAuthorization().WithTags("Learning");
    }
}
