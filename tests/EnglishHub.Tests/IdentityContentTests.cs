using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class IdentityContentTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Anonymous_mutation_requires_csrf_and_admin_routes_require_role()
    {
        using var anonymous = app.CreateClient();
        var noCsrf = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = "learner@englishhub.local", password = "EnglishHub@2026" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var learner = await app.Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/v1/admin/content")).StatusCode);
    }

    [Fact]
    public async Task Registration_requires_verification_then_can_reset_password()
    {
        using var client = app.CreateClient(); await TestApp.Csrf(client);
        var email = $"test-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "FirstPassword@2026", displayName = "Kiểm thử Việt–Anh 🌱" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "FirstPassword@2026" })).StatusCode);
        var mailbox = await client.GetFromJsonAsync<JsonElement>($"/api/v1/dev/mailbox?email={email}");
        var body = JsonSerializer.Deserialize<JsonElement>(mailbox[0].GetProperty("body").GetString()!);
        (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId = body.GetProperty("userId").GetGuid(), token = body.GetProperty("token").GetString() })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "FirstPassword@2026" })).EnsureSuccessStatusCode();
        await TestApp.Csrf(client);
        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        mailbox = await client.GetFromJsonAsync<JsonElement>($"/api/v1/dev/mailbox?email={email}");
        body = JsonSerializer.Deserialize<JsonElement>(mailbox[0].GetProperty("body").GetString()!);
        var payload = new { userId = body.GetProperty("userId").GetGuid(), token = body.GetProperty("token").GetString(), newPassword = "SecondPassword@2026" };
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", payload)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/v1/auth/reset-password", payload)).StatusCode);
    }

    [Fact]
    public async Task Public_catalog_never_discloses_grading_keys()
    {
        using var client = app.CreateClient();
        var text = await client.GetStringAsync("/api/v1/catalog");
        Assert.DoesNotContain("isCorrect", text);
        Assert.DoesNotContain("explanation", text);
        Assert.Contains("Everyday introductions", text);
    }
}
