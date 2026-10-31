using System.Security.Claims;
using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Api.Endpoints;

public static class LessonAnnotationEndpoints
{
    public static void MapLessonAnnotations(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/lessons/{version:guid}/annotations").RequireAuthorization().WithTags("Lesson notes");
        group.MapGet("/", (Guid version, ClaimsPrincipal user, LessonAnnotationService service) => service.ForLesson(user.UserId(), version));
        group.MapGet("/{id:guid}", (Guid version, Guid id, ClaimsPrincipal user, LessonAnnotationService service) => service.Read(user.UserId(), version, id));
        group.MapPut("/{id:guid}", (Guid version, Guid id, SaveLessonAnnotation input, ClaimsPrincipal user, LessonAnnotationService service) => service.Save(user.UserId(), version, id, input));
        group.MapPost("/{id:guid}/remove", (Guid version, Guid id, RemoveLessonAnnotation input, ClaimsPrincipal user, LessonAnnotationService service) => service.Remove(user.UserId(), version, id, input));
        app.MapGet("/api/v1/me/lesson-annotations", (ClaimsPrincipal user, LessonAnnotationService service, int offset = 0) => service.List(user.UserId(), offset)).RequireAuthorization().WithTags("Lesson notes");
    }
}
