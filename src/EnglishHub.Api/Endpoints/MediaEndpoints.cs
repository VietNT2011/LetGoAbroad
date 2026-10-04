using EnglishHub.Domain;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Api.Endpoints;
public static class MediaEndpoints
{
    public static void MapMedia(this WebApplication app)
    {
        var group=app.MapGroup("/api/v1/media").RequireAuthorization().WithTags("Media");
        group.MapPost("/uploads",(CreateUpload input,HttpContext ctx,MediaService service) => service.Create(ctx.User.UserId(),ctx.User.IsInRole("Admin") || ctx.User.IsInRole("ContentEditor"),input));
        group.MapPost("/{id:guid}/complete",async (Guid id,HttpContext ctx,MediaService service) => { await service.Complete(ctx.User.UserId(),id); return Results.Accepted(); });
        group.MapGet("/{id:guid}",async (Guid id,HttpContext ctx,MediaService service) => { var a=await service.Owned(ctx.User.UserId(),id,ctx.User.IsInRole("Admin")); return new { a.Id,a.FileName,a.Status,a.FailureCode,a.DurationMs,a.Attribution,a.CreatedAt }; });
        group.MapGet("/{id:guid}/content",async (Guid id,HttpContext ctx,MediaService service) => { ctx.Response.Headers.CacheControl="no-store"; return Results.Redirect(await service.Playback(ctx.User.UserId(),id,ctx.User.IsInRole("Admin"))); });
        app.MapGet("/api/v1/admin/media",async (HubDbContext db) => await db.MediaAssets.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(50).Select(x => new { x.Id,x.FileName,x.Status,x.FailureCode,x.DurationMs,x.Attribution }).ToListAsync()).RequireAuthorization(p => p.RequireRole("Admin","ContentEditor"));
        app.MapGet("/api/v1/admin/jobs",async (HubDbContext db) => await db.BackgroundJobs.AsNoTracking().OrderByDescending(x => x.DueAt).Take(50).Select(x => new { x.Id,x.ResourceId,x.Kind,x.State,x.Attempts,x.DueAt,x.LeaseUntil,x.LastErrorCode }).ToListAsync()).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
