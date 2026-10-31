using System.Security.Claims;
using EnglishHub.Domain;

namespace EnglishHub.Api;

/// <summary>VI: Ràng buộc request với tài khoản mà tab đã xác minh, trước CSRF retry. EN: Bind requests to the tab's confirmed actor before antiforgery can be refreshed under a different cookie.</summary>
public static class ActorPrecondition
{
    public const string Header = "X-EnglishHub-Actor";
    private static readonly HashSet<string> UnboundPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth/csrf", "/api/v1/auth/login", "/api/v1/auth/register",
        "/api/v1/auth/verify-email", "/api/v1/auth/forgot-password", "/api/v1/auth/reset-password", "/api/v1/status"
    };

    public static async Task Invoke(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        var path = request.Path.Value!.TrimEnd('/');
        if (UnboundPaths.Contains(path)) { await next(context); return; }
        var present = request.Headers.TryGetValue(Header, out var values);
        if (!present)
        {
            var unsafeMethod = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method);
            // VI: Bản JS cũ phải reload trước khi ghi; GET media native không thể gắn header. EN: Old browser writes must reload; native media GETs cannot attach this header. Unmarked API clients retain their existing contract.
            DomainError.Require(!unsafeMethod || !(request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Sec-Fetch-Mode")), 428, "identity_precondition_required", "Phiên bản trang đã cũ. Hãy tải lại trang trước khi lưu.");
        }
        else
        {
            var raw = values.Count == 1 ? values[0] : null;
            var anonymous = raw == "anonymous";
            var expected = Guid.Empty;
            DomainError.Require(anonymous || (raw is not null && Guid.TryParseExact(raw, "D", out expected)), 400, "invalid_actor_precondition", "Thông tin phiên không hợp lệ. Hãy tải lại trang.");
            var authenticated = context.User.Identity?.IsAuthenticated == true;
            var actual = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            DomainError.Require(anonymous ? !authenticated : authenticated && Guid.TryParse(actual, out var id) && id == expected, 409, "session_changed", "Tài khoản đã thay đổi. Vui lòng kiểm tra lại phiên đăng nhập.");
        }
        await next(context);
    }
}
