using System.Security.Claims;
using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Api.Endpoints;

public static class ListeningEndpoints
{
    public static void MapListening(this WebApplication app)
    {
        var lessons=app.MapGroup("/api/v1/lessons").RequireAuthorization().WithTags("Listening");
        lessons.MapGet("/{version:guid}/listening",(Guid version,ClaimsPrincipal user,ListeningService service)=>service.Info(user.UserId(),version));
        lessons.MapPost("/{version:guid}/listening",async(Guid version,StartListening input,ClaimsPrincipal user,ListeningService service)=>await service.Read(user.UserId(),await service.Start(user.UserId(),version,input)));
        var sessions=app.MapGroup("/api/v1/listening").RequireAuthorization().WithTags("Listening");
        sessions.MapGet("/{id:guid}",(Guid id,ClaimsPrincipal user,ListeningService service)=>service.Read(user.UserId(),id));
        sessions.MapPut("/{id:guid}/position",(Guid id,SaveListeningPosition input,ClaimsPrincipal user,ListeningService service)=>service.SavePosition(user.UserId(),id,input));
        sessions.MapPost("/{id:guid}/segments/{segment:guid}/reveal",(Guid id,Guid segment,RevealTranscript input,ClaimsPrincipal user,ListeningService service)=>service.Reveal(user.UserId(),id,segment,input));
        sessions.MapPost("/{id:guid}/segments/{segment:guid}/answers",(Guid id,Guid segment,CheckDictation input,ClaimsPrincipal user,ListeningService service)=>service.Answer(user.UserId(),id,segment,input));
    }
}
