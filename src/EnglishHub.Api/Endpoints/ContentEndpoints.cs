using System.Security.Claims;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Api.Endpoints;

public static class ContentEndpoints
{
    public static void MapContent(this WebApplication app)
    {
        // VI: DTO danh mục không chứa answer key. EN: Public catalogue projections never include answer keys.
        app.MapGet("/api/v1/catalog", async (HubDbContext db, string? skill, string? search) =>
        {
            var query = db.ContentVersions.AsNoTracking().Where(x => x.Status == PublicationStatus.Published && x.Root.CurrentPublishedVersionId == x.Id);
            if (!string.IsNullOrWhiteSpace(skill)) query = query.Where(x => x.Skill == skill);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search));
            return await query.OrderBy(x => x.Root.Slug).Take(100).Select(x => new { x.Id, x.RootId, x.Root.Slug, x.Root.Kind, x.Title, x.Summary, x.Skill, x.Minutes, x.DurationSeconds, x.IsFixture, x.Version, questionCount = x.Groups.Sum(g => g.Questions.Count) }).ToListAsync();
        }).WithTags("Content");
        app.MapGet("/api/v1/lessons/{id:guid}", async (Guid id, HubDbContext db) =>
        {
            var lesson = await db.ContentVersions.AsNoTracking().Where(x => x.Id == id && x.Status == PublicationStatus.Published && x.Root.Kind == ContentKind.Lesson).Select(x => new { x.Id, x.Title, x.Summary, x.Body, x.Skill, x.Minutes, x.Attribution, x.Version,
                quiz = x.Groups.SelectMany(g => g.Questions).Select(q => new { q.Id, q.Prompt, options = q.Options.Select(o => new { o.Id, o.Text }) }) }).SingleOrDefaultAsync();
            return lesson is null ? Results.NotFound() : Results.Ok(lesson);
        }).RequireAuthorization().WithTags("Content");
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(p => p.RequireRole("Admin", "ContentEditor")).WithTags("CMS");
        admin.MapGet("/content", async (HubDbContext db) => await db.ContentVersions.AsNoTracking().OrderByDescending(x => x.Version).Select(x => new { x.Id, x.RootId, x.Root.Slug, x.Title, x.Status, x.Version, x.Root.Kind, etag = Convert.ToBase64String(x.RowVersion) }).Take(100).ToListAsync());
        admin.MapGet("/versions/{id:guid}", async (Guid id, ContentService service) =>
        {
            var v = await service.FullVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "content_not_found", "Không tìm thấy nội dung.");
            return new { v.Id, v.RootId, v.Title, v.Summary, v.Body, v.Skill, v.Minutes, v.DurationSeconds, v.IsFixture, v.Attribution, v.Version, v.Status, etag = Etags.Of(v), groups = v.Groups.OrderBy(x => x.Order).Select(g => new { g.Part, g.Stimulus, questions = g.Questions.OrderBy(x => x.Order).Select(q => new { q.Prompt, q.Skill, q.Explanation, options = q.Options.Select(o => new { o.Text, o.IsCorrect }) }) }) };
        });
        admin.MapPost("/content", async (CreateContentInput input, ClaimsPrincipal user, ContentService service) =>
        {
            var v = await service.Create(input, user.UserId()); return Results.Created($"/api/v1/admin/versions/{v.Id}", new { v.Id, etag = Etags.Of(v) });
        });
        admin.MapPost("/content/{rootId:guid}/versions", async (Guid rootId, ContentInput input, ClaimsPrincipal user, ContentService service) =>
        {
            var v = await service.NewVersion(rootId, input, user.UserId()); return Results.Created($"/api/v1/admin/versions/{v.Id}", new { v.Id, etag = Etags.Of(v) });
        });
        admin.MapPost("/versions/{id:guid}/publish", async (Guid id, HttpContext ctx, ContentService service) =>
        {
            var v = await service.Publish(id, ctx.Request.Headers.IfMatch, ctx.User.UserId()); return Results.Ok(new { v.Id, v.Status, etag = Etags.Of(v) });
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
