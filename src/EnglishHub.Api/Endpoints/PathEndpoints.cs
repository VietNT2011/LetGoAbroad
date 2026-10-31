using System.Security.Claims;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Api.Endpoints;

public static class PathEndpoints
{
    public static void MapPaths(this WebApplication app)
    {
        var learner = app.MapGroup("/api/v1/learning").RequireAuthorization().WithTags("Learning paths");
        learner.MapGet("/templates", async (HubDbContext db) => await db.PathVersions.AsNoTracking().Where(x => x.Status == PublicationStatus.Published && x.Template.CurrentPublishedVersionId == x.Id).OrderBy(x => x.Template.Slug)
            .Select(x => new { x.Id, x.TemplateId, x.Template.Slug, x.Template.Level, x.Version, x.Title, x.Summary, nodeCount = x.Stages.Sum(s => s.Nodes.Count), studyDays = x.Stages.SelectMany(s => s.Nodes).Max(n => n.StudyDay) }).ToListAsync());
        learner.MapGet("/path-plan", (ClaimsPrincipal user, LearningService service) => service.PathPlan(user.UserId()));
        learner.MapPost("/enrollment/adopt-path", async (AdoptPathInput input, ClaimsPrincipal user, LearningService service) => new { pathVersionId = await service.AdoptPath(user.UserId(), input) });
        var cms = app.MapGroup("/api/v1/admin").RequireAuthorization(p => p.RequireRole("Admin", "ContentEditor")).WithTags("Path CMS");
        cms.MapGet("/paths", async (HubDbContext db) => await db.PathVersions.AsNoTracking().OrderBy(x => x.Template.Slug).ThenByDescending(x => x.Version).Take(100).Select(x => new { x.Id, x.TemplateId, x.Template.Slug, x.Template.Level, x.Version, x.Title, x.Status, etag = Convert.ToBase64String(x.RowVersion) }).ToListAsync());
        cms.MapGet("/path-versions/{id:guid}", async (Guid id, PathService service) =>
        {
            var version = await service.Full.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "path_not_found", "Không tìm thấy lộ trình.");
            var keys = version.Stages.SelectMany(s => s.Nodes).ToDictionary(n => n.Id, n => n.Key);
            return new
            {
                version.Id,
                version.TemplateId,
                version.Title,
                version.Summary,
                version.Attribution,
                version.Version,
                version.Status,
                etag = Etags.Of(version),
                stages = version.Stages.OrderBy(s => s.Order).Select(s => new { s.Key, s.Title, nodes = s.Nodes.OrderBy(n => n.Order).Select(n => new { n.Key, n.ContentVersionId, n.StudyDay, n.CompletionRule, n.MinimumCorrectPercent, prerequisites = n.Prerequisites.Select(p => keys[p.PrerequisiteNodeId]).Order().ToArray() }) })
            };
        });
        cms.MapPost("/paths", async (CreatePathInput input, ClaimsPrincipal user, PathService service) => { var version = await service.Create(input, user.UserId()); return Results.Created($"/api/v1/admin/path-versions/{version.Id}", new { version.Id, etag = Etags.Of(version) }); });
        cms.MapPost("/paths/{id:guid}/versions", async (Guid id, PathInput input, ClaimsPrincipal user, PathService service) => { var version = await service.NewVersion(id, input, user.UserId()); return new { version.Id, etag = Etags.Of(version) }; });
        cms.MapPut("/path-versions/{id:guid}", async (Guid id, PathInput input, HttpContext context, PathService service) => { var version = await service.Update(id, input, context.Request.Headers.IfMatch, context.User.UserId()); return new { version.Id, etag = Etags.Of(version) }; });
        cms.MapPost("/path-versions/{id:guid}/validate", async (Guid id, PathService service) => { var version = await service.Full.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "path_not_found", "Không tìm thấy lộ trình."); return new { valid = true, topologicalOrder = await service.Validate(version) }; });
        cms.MapPost("/path-versions/{id:guid}/publish", async (Guid id, HttpContext context, PathService service) => { var version = await service.Publish(id, context.Request.Headers.IfMatch, context.User.UserId()); return new { version.Id, etag = Etags.Of(version) }; }).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
