using System.Security.Claims;
using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Api.Endpoints;

public static class StudyActivityEndpoints
{
    public static void MapStudyActivity(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/learning").RequireAuthorization().WithTags("Estimated study time");
        group.MapPost("/study-sessions", (ClaimsPrincipal user, StartStudySession input, StudyActivityService service) => service.Start(user.UserId(), input));
        group.MapGet("/study-sessions/{id:guid}", (Guid id, ClaimsPrincipal user, StudyActivityService service) => service.Get(user.UserId(), id));
        group.MapPost("/study-sessions/{id:guid}/heartbeat", (Guid id, ClaimsPrincipal user, StudyPulse input, StudyActivityService service) => service.Pulse(user.UserId(), id, input));
        group.MapGet("/study-time", (ClaimsPrincipal user, StudyActivityService service, int? days) => service.Read(user.UserId(), days ?? 7));
        group.MapGet("/study-time/calendar", (ClaimsPrincipal user, StudyActivityService service, int? days) => service.ReadCalendar(user.UserId(), days ?? 7));
    }
}
