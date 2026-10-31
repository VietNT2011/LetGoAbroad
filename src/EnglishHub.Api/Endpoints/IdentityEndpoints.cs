using System.Security.Claims;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Identity;
using EnglishHub.Infrastructure.Persistence;
using EnglishHub.Infrastructure.Learning;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Api.Endpoints;

public sealed record Credentials(string Email, string Password);
public sealed record Registration(string Email, string Password, string DisplayName);
public sealed record ConfirmEmail(Guid UserId, string Token);
public sealed record ResetPassword(Guid UserId, string Token, string NewPassword);
public sealed record EmailInput(string Email);
public sealed record ProfileInput(string DisplayName, string TimeZoneId, int TargetScore, int MinutesPerDay, int[] StudyDays);

/// <summary>VI: Cookie + antiforgery; không trả JWT vào localStorage. EN: Cookie sessions and CSRF protection avoid browser-managed access secrets.</summary>
public static class IdentityEndpoints
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new DomainError(401, "unauthenticated", "Vui lòng đăng nhập."));

    public static void MapIdentity(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Identity");
        auth.MapGet("/csrf", (HttpContext ctx, IAntiforgery antiforgery) => new { requestToken = antiforgery.GetAndStoreTokens(ctx).RequestToken });
        auth.MapPost("/register", async (Registration input, UserManager<AppUser> users, MailDispatcher mail) =>
        {
            DomainError.Require(!string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Length <= 120 && input.Email.Length <= 256 && input.Email.Contains('@'), 422, "invalid_profile", "Tên/email không hợp lệ.");
            var user = new AppUser { Id = Guid.NewGuid(), Email = input.Email.Trim(), UserName = input.Email.Trim(), DisplayName = input.DisplayName.Trim() };
            Check(await users.CreateAsync(user, input.Password));
            Check(await users.AddToRoleAsync(user, "Learner"));
            await mail.Send(user.Email, "verify-email", user.Id, await users.GenerateEmailConfirmationTokenAsync(user));
            return Results.Accepted(value: new { message = "Kiểm tra email để xác minh / Check your email to verify your account." });
        }).RequireRateLimiting("auth");
        auth.MapPost("/login", async (Credentials input, SignInManager<AppUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(input.Email, input.Password, false, true);
            DomainError.Require(result.Succeeded, 401, "login_failed", "Email/mật khẩu không đúng, tài khoản bị khóa hoặc email chưa xác minh.");
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) => { await signIn.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
        auth.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal) ?? throw new DomainError(401, "unauthenticated", "Vui lòng đăng nhập.");
            return new { user.Id, user.Email, user.DisplayName, user.TimeZoneId, user.TargetScore, user.MinutesPerDay, user.StudyDays, roles = await users.GetRolesAsync(user) };
        }).RequireAuthorization();
        auth.MapPost("/verify-email", async (ConfirmEmail input, UserManager<AppUser> users) =>
        {
            var user = await users.FindByIdAsync(input.UserId.ToString()) ?? throw new DomainError(400, "invalid_token", "Liên kết không hợp lệ.");
            Check(await users.ConfirmEmailAsync(user, input.Token));
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        auth.MapPost("/forgot-password", async (EmailInput input, UserManager<AppUser> users, MailDispatcher mail) =>
        {
            var user = await users.FindByEmailAsync(input.Email);
            if (user is { EmailConfirmed: true }) await mail.Send(input.Email, "reset-password", user.Id, await users.GeneratePasswordResetTokenAsync(user));
            return Results.Accepted(value: new { message = "Nếu tài khoản hợp lệ, email đã được gửi." });
        }).RequireRateLimiting("auth");
        auth.MapPost("/reset-password", async (ResetPassword input, UserManager<AppUser> users) =>
        {
            var user = await users.FindByIdAsync(input.UserId.ToString()) ?? throw new DomainError(400, "invalid_token", "Liên kết không hợp lệ.");
            Check(await users.ResetPasswordAsync(user, input.Token, input.NewPassword));
            return Results.NoContent();
        }).RequireRateLimiting("auth");
        app.MapPut("/api/v1/profile", async (ProfileInput input, ClaimsPrincipal principal, UserManager<AppUser> users, HubDbContext db, Transactions tx, GoalPlanService goals) =>
        {
            DomainError.Require(input.TargetScore is 450 or 650 or 800 && input.MinutesPerDay is 20 or 30 or 45 && input.StudyDays.Length is >= 1 and <= 7 && input.StudyDays.All(x => x is >= 0 and <= 6) && !string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Length <= 120, 422, "invalid_profile", "Cấu hình học không hợp lệ.");
            DomainError.Require(!string.IsNullOrWhiteSpace(input.TimeZoneId) && input.TimeZoneId.Length <= 80, 422, "invalid_timezone", "Múi giờ không hợp lệ.");
            try { TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId); }
            catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new DomainError(422, "invalid_timezone", "Múi giờ không hợp lệ."); }
            return await tx.Run($"learning:{principal.UserId()}", async () =>
            {
                var user = (await users.GetUserAsync(principal))!;
                var zoneBeforeChange = user.TimeZoneId;
                var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == user.Id);
                if (enrollment is not null) await goals.Resolve(enrollment, user);
                user.DisplayName = input.DisplayName.Trim(); user.TimeZoneId = TimeZoneInfo.TryConvertWindowsIdToIanaId(input.TimeZoneId, out var canonicalZone) ? canonicalZone : input.TimeZoneId; user.TargetScore = input.TargetScore;
                user.MinutesPerDay = input.MinutesPerDay; user.StudyDays = string.Join(',', input.StudyDays.Distinct().Order());
                Check(await users.UpdateAsync(user));
                if (enrollment is not null) await goals.Schedule(enrollment, input.TargetScore, input.MinutesPerDay, zoneBeforeChange);
                return Results.NoContent();
            });
        }).RequireAuthorization();
        // VI: Không có route hộp thư trong production. EN: The local mailbox is never mapped in production.
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
            app.MapGet("/api/v1/dev/mailbox", async (string email, HubDbContext db) => await db.Set<DevMail>().Where(x => x.Recipient == email).OrderByDescending(x => x.CreatedAt).Take(5).Select(x => new { x.Subject, x.Body, x.CreatedAt }).ToListAsync());
    }

    private static void Check(IdentityResult result) => DomainError.Require(result.Succeeded, 422, "identity_validation", string.Join("; ", result.Errors.Select(x => x.Description)));
}
