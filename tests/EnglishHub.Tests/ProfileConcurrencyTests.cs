using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Tab cũ không được ghi đè hồ sơ hoặc sinh revision thừa. EN: Stale profile writes cannot overwrite preferences or append plan side effects.</summary>
public sealed class ProfileConcurrencyTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Different_edits_from_one_baseline_have_one_winner_and_one_conflict()
    {
        using var client = await NewLearner(); await Enroll(client);
        var before = await Me(client); var old = before.GetProperty("profileEtag").GetString();
        var a = Edit(before) with { DisplayName = "Tab A", TargetScore = 450, MinutesPerDay = 45 };
        var b = Edit(before) with { DisplayName = "Tab B", TargetScore = 800, MinutesPerDay = 20 };
        var responses = await Task.WhenAll(client.PutAsJsonAsync("/api/v1/profile", a), client.PutAsJsonAsync("/api/v1/profile", b));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var current = await Me(client); var winner = responses[0].IsSuccessStatusCode ? a : b;
        Assert.Equal(winner.DisplayName, current.GetProperty("displayName").GetString()); Assert.Equal(winner.TargetScore, current.GetProperty("targetScore").GetInt32()); Assert.Equal(winner.MinutesPerDay, current.GetProperty("minutesPerDay").GetInt32()); Assert.NotEqual(old, current.GetProperty("profileEtag").GetString());
        Assert.Equal(2, (await History(client)).GetArrayLength());
        var user = before.GetProperty("id").GetGuid(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var enrollment = await db.Enrollments.SingleAsync(x => x.UserId == user);
        Assert.Equal(2, await db.PlanGoalRevisions.CountAsync(x => x.EnrollmentId == enrollment.Id));
    }

    [Fact]
    public async Task Missing_or_stale_versions_have_no_profile_plan_or_task_side_effects()
    {
        using var client = await NewLearner(); await Enroll(client); var before = await Me(client); var edit = Edit(before);
        var initial = await client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        Assert.Equal((HttpStatusCode)428, (await client.PutAsJsonAsync("/api/v1/profile", edit with { ExpectedEtag = null, TargetScore = 800 })).StatusCode);
        (await client.PutAsJsonAsync("/api/v1/profile", edit with { DisplayName = "Newer name" })).EnsureSuccessStatusCode();
        var after = await Me(client); var history = await History(client);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutAsJsonAsync("/api/v1/profile", edit with { TimeZoneId = "UTC", StudyDays = [0], TargetScore = 800, MinutesPerDay = 20 })).StatusCode);
        Assert.Equal(after.GetRawText(), (await Me(client)).GetRawText()); Assert.Equal(history.GetRawText(), (await History(client)).GetRawText());
        Assert.Equal(initial.GetProperty("tasks").GetRawText(), (await client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks").GetRawText());
    }

    [Fact]
    public async Task Exact_normalized_retry_acknowledges_current_version_without_duplicate_revisions()
    {
        using var client = await NewLearner(); await Enroll(client); var before = await Me(client); var edit = Edit(before) with { DisplayName = "  Normalized learner  ", TargetScore = 800, MinutesPerDay = 20, StudyDays = [6, 5, 4, 3, 2, 1, 0] };
        var first = await client.PutAsJsonAsync("/api/v1/profile", edit); first.EnsureSuccessStatusCode(); var ack = await first.Content.ReadFromJsonAsync<JsonElement>(); var history = await History(client);
        var retry = await client.PutAsJsonAsync("/api/v1/profile", edit); retry.EnsureSuccessStatusCode(); Assert.Equal(ack.GetRawText(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        Assert.Equal(history.GetRawText(), (await History(client)).GetRawText()); Assert.Equal("Normalized learner", (await Me(client)).GetProperty("displayName").GetString());
        Assert.Equal(ack.GetProperty("etag").GetString(), (await Me(client)).GetProperty("profileEtag").GetString());
    }

    [Fact]
    public async Task Explicit_comparison_can_rebase_then_save_a_different_draft()
    {
        using var client = await NewLearner(); await Enroll(client); var before = await Me(client); var draft = Edit(before) with { DisplayName = "My retained draft", TargetScore = 800 };
        (await client.PutAsJsonAsync("/api/v1/profile", Edit(before) with { DisplayName = "Other tab", TargetScore = 450 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutAsJsonAsync("/api/v1/profile", draft)).StatusCode);
        var compared = await Me(client); (await client.PutAsJsonAsync("/api/v1/profile", draft with { ExpectedEtag = compared.GetProperty("profileEtag").GetString() })).EnsureSuccessStatusCode();
        Assert.Equal(draft.DisplayName, (await Me(client)).GetProperty("displayName").GetString()); var history = await History(client); Assert.Equal(3, history.GetArrayLength()); Assert.Equal("superseded", history[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Profile_version_does_not_grant_another_identity_access_or_replace_csrf()
    {
        using var owner = await NewLearner(); using var other = await NewLearner(); var a = await Me(owner); var b = await Me(other);
        Assert.False(a.TryGetProperty("securityStamp", out _)); Assert.False(a.TryGetProperty("passwordHash", out _));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await other.PutAsJsonAsync("/api/v1/profile", Edit(b) with { DisplayName = "Foreign version", ExpectedEtag = a.GetProperty("profileEtag").GetString() })).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/v1/profile", Edit(a))).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); var rejected = await owner.PutAsJsonAsync("/api/v1/profile", Edit(a) with { DisplayName = "Without CSRF" }); Assert.Equal(HttpStatusCode.BadRequest, (rejected).StatusCode);
        Assert.Equal(a.GetRawText(), (await Me(owner)).GetRawText());
    }

    private sealed record ProfileEdit(string DisplayName, string TimeZoneId, int TargetScore, int MinutesPerDay, int[] StudyDays, string? ExpectedEtag);
    private static ProfileEdit Edit(JsonElement me) => new(me.GetProperty("displayName").GetString()!, me.GetProperty("timeZoneId").GetString()!, me.GetProperty("targetScore").GetInt32(), me.GetProperty("minutesPerDay").GetInt32(), me.GetProperty("studyDays").GetString()!.Split(',').Select(int.Parse).ToArray(), me.GetProperty("profileEtag").GetString());
    private static Task<JsonElement> Me(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
    private static async Task<JsonElement> History(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions")).GetProperty("revisions");
    private static async Task Enroll(HttpClient client) => (await client.PostAsJsonAsync("/api/v1/learning/enrollment", new { startFromBasics = true })).EnsureSuccessStatusCode();
    private async Task<HttpClient> NewLearner()
    {
        var email = $"profile-{Guid.NewGuid():N}@example.com"; using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Profile test", StudyDays = "0,1,2,3,4,5,6" }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
}
