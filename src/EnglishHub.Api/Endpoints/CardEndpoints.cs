using System.Security.Claims;
using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Api.Endpoints;

public static class CardEndpoints
{
    public static void MapCards(this WebApplication app)
    {
        app.MapGet("/api/v1/review-batches/{id:guid}", (Guid id, ClaimsPrincipal user, DailyReviewService service) => service.Get(user.UserId(), id)).RequireAuthorization();
        app.MapPost("/api/v1/review-batches/{id:guid}/start", (Guid id, ClaimsPrincipal user, DailyReviewService service) => service.Start(user.UserId(), id)).RequireAuthorization();
        var group = app.MapGroup("/api/v1/cards").RequireAuthorization().WithTags("Personal review");
        group.MapGet("/", (ClaimsPrincipal user, CardService service, bool dueOnly = false, bool archived = false, int offset = 0) => service.List(user.UserId(), dueOnly, archived, offset));
        group.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, CardService service) => service.Get(user.UserId(), id));
        group.MapGet("/{id:guid}/reviews", (Guid id, ClaimsPrincipal user, CardService service, int offset = 0) => service.History(user.UserId(), id, offset));
        group.MapPost("/", (CreateCard input, ClaimsPrincipal user, CardService service) => service.Create(user.UserId(), input));
        group.MapPost("/{id:guid}/reviews", (Guid id, ReviewCard input, ClaimsPrincipal user, CardService service) => service.Review(user.UserId(), id, input));
        group.MapPost("/{id:guid}/archive", (Guid id, ArchiveCard input, ClaimsPrincipal user, CardService service) => service.Archive(user.UserId(), id, input));
    }
}
