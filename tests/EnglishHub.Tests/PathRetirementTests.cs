using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Ngừng đăng ký mới không phá version/tiến độ đã ghim. EN: Template retirement closes admissions while preserving pinned payloads and learning evidence.</summary>
public sealed class PathRetirementTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Retired_templates_hide_and_reject_new_enrollments_and_adoptions_but_existing_learners_continue()
    {
        using var admin = await app.Login("admin@englishhub.local"); var path = await Create(admin); using var existing = await Learner(); (await Enroll(existing, path.Version)).EnsureSuccessStatusCode(); var before = await existing.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
        var version = await Get(admin, path.Version); var response = await Toggle(admin, path.Template, true, new(Guid.NewGuid(), version.GetProperty("templateEtag").GetString()!)); response.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await existing.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == path.Version);
        using var newcomer = await Learner(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Enroll(newcomer, path.Version)).StatusCode);
        using var legacy = await Learner(); await Legacy(legacy); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await legacy.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", new AdoptPathInput(path.Version, Guid.NewGuid()))).StatusCode);
        Assert.Equal(before.GetRawText(), (await existing.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan")).GetRawText()); (await Enroll(existing, path.Version)).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var questions = await db.Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == path.Lesson).ToArrayAsync(); (await existing.PostAsJsonAsync($"/api/v1/lessons/{path.Lesson}/complete", new CompleteLessonInput(Guid.NewGuid(), questions.Select(x => new LessonAnswer(x.Id, x.Options.Single(o => o.IsCorrect).Id)).ToArray()))).EnsureSuccessStatusCode(); }
        var after = await Get(admin, path.Version); Assert.Equal(version.GetProperty("etag").GetString(), after.GetProperty("etag").GetString()); Assert.Equal(version.GetProperty("stages").GetRawText(), after.GetProperty("stages").GetRawText()); Assert.True((await existing.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan")).GetProperty("nodes")[0].GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task Restore_reopens_admissions_and_replaying_old_retirement_cannot_retire_again()
    {
        using var admin = await app.Login("admin@englishhub.local"); var path = await Create(admin); var before = await Get(admin, path.Version); var retire = new PathLifecycleInput(Guid.NewGuid(), before.GetProperty("templateEtag").GetString()!); var first = await Toggle(admin, path.Template, true, retire); first.EnsureSuccessStatusCode(); var oldAck = await first.Content.ReadAsStringAsync(); var retired = await first.Content.ReadFromJsonAsync<PathLifecycleAck>();
        var restore = await Toggle(admin, path.Template, false, new(Guid.NewGuid(), retired!.Etag)); restore.EnsureSuccessStatusCode();
        var replay = await Toggle(admin, path.Template, true, retire); replay.EnsureSuccessStatusCode(); Assert.Equal(oldAck, await replay.Content.ReadAsStringAsync()); Assert.Equal(JsonValueKind.Null, (await Get(admin, path.Version)).GetProperty("retiredAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await Toggle(admin, path.Template, false, retire)).StatusCode); Assert.Equal(HttpStatusCode.PreconditionFailed, (await Toggle(admin, path.Template, true, retire with { ClientRequestId = Guid.NewGuid() })).StatusCode);
        using var learner = await Learner(); (await Enroll(learner, path.Version)).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.ResourceId == path.Template && x.Action == "path.retired")); Assert.Equal(1, await db.AuditEntries.CountAsync(x => x.ResourceId == path.Template && x.Action == "path.restored"));
    }

    [Fact]
    public async Task Drafts_remain_editable_but_publication_waits_for_restore()
    {
        using var admin = await app.Login("admin@englishhub.local"); var path = await Create(admin); var meta = await Get(admin, path.Version); (await Toggle(admin, path.Template, true, new(Guid.NewGuid(), meta.GetProperty("templateEtag").GetString()!))).EnsureSuccessStatusCode();
        var create = await admin.PostAsJsonAsync($"/api/v1/admin/paths/{path.Template}/versions", path.Input); create.EnsureSuccessStatusCode(); var draft = await create.Content.ReadFromJsonAsync<JsonElement>(); var id = draft.GetProperty("id").GetGuid(); var etag = draft.GetProperty("etag").GetString()!;
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(admin, id, etag)).StatusCode); Assert.Equal("draft", (await Get(admin, id)).GetProperty("status").GetString());
        var retired = await Get(admin, id); (await Toggle(admin, path.Template, false, new(Guid.NewGuid(), retired.GetProperty("templateEtag").GetString()!))).EnsureSuccessStatusCode(); (await Publish(admin, id, etag)).EnsureSuccessStatusCode();
        using var learner = await Learner(); Assert.Contains((await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Only_admin_with_csrf_and_the_current_template_version_can_change_admissions()
    {
        using var admin = await app.Login("admin@englishhub.local"); var path = await Create(admin); var meta = await Get(admin, path.Version); var input = new PathLifecycleInput(Guid.NewGuid(), meta.GetProperty("templateEtag").GetString()!);
        using var editor = await app.Login("editor@englishhub.local"); using var learner = await Learner(); using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Forbidden, (await Toggle(editor, path.Template, true, input)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Toggle(learner, path.Template, true, input)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Toggle(anonymous, path.Template, true, input)).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await Toggle(admin, path.Template, true, input)).StatusCode); await TestApp.Csrf(admin);
        var race = await Task.WhenAll(Toggle(admin, path.Template, true, input), Toggle(admin, path.Template, true, input with { ClientRequestId = Guid.NewGuid() })); Assert.Single(race, x => x.IsSuccessStatusCode); Assert.Single(race, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Enrollment_racing_retirement_is_either_pinned_before_closure_or_rejected_and_later_requests_stay_closed()
    {
        using var admin = await app.Login("admin@englishhub.local"); var path = await Create(admin); using var learner = await Learner(); var meta = await Get(admin, path.Version);
        var race = await Task.WhenAll(Toggle(admin, path.Template, true, new(Guid.NewGuid(), meta.GetProperty("templateEtag").GetString()!)), Enroll(learner, path.Version)); race[0].EnsureSuccessStatusCode(); Assert.Contains(race[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity });
        if (race[1].IsSuccessStatusCode) Assert.Equal(path.Version, (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan")).GetProperty("template").GetProperty("id").GetGuid());
        using var later = await Learner(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Enroll(later, path.Version)).StatusCode);
    }

    private sealed record PathInfo(Guid Template, Guid Version, Guid Lesson, PathInput Input);
    private async Task<PathInfo> Create(HttpClient admin)
    {
        var catalog = await admin.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var lesson = catalog.EnumerateArray().First(x => x.GetProperty("kind").GetString() == "lesson").GetProperty("id").GetGuid(); var content = new PathInput("Lifecycle test", "Original single-node fixture", "EnglishHub original · CC0", [new("start", "Start", [new("one", lesson, 1, "lesson_quiz", 100, [])])]);
        var response = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"lifecycle-{Guid.NewGuid():N}", "foundation", content)); response.EnsureSuccessStatusCode(); var draft = await response.Content.ReadFromJsonAsync<JsonElement>(); var version = draft.GetProperty("id").GetGuid(); (await Publish(admin, version, draft.GetProperty("etag").GetString()!)).EnsureSuccessStatusCode(); var meta = await Get(admin, version); return new(meta.GetProperty("templateId").GetGuid(), version, lesson, content);
    }
    private async Task<HttpClient> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"lifecycle-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, StudyDays = "0,1,2,3,4,5,6", DisplayName = "Lifecycle test" }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private async Task Legacy(HttpClient client)
    {
        var user = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Enrollments.Add(new Enrollment { UserId = user, TargetScore = 650, MinutesPerDay = 30, CreatedAt = app.Clock.UtcNow }); await db.SaveChangesAsync();
    }
    private static Task<JsonElement> Get(HttpClient client, Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/path-versions/{id}");
    private static Task<HttpResponseMessage> Toggle(HttpClient client, Guid id, bool retired, PathLifecycleInput input) => client.PostAsJsonAsync($"/api/v1/admin/paths/{id}/{(retired ? "retire" : "restore")}", input);
    private static Task<HttpResponseMessage> Enroll(HttpClient client, Guid id) => client.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: id));
    private static async Task<HttpResponseMessage> Publish(HttpClient client, Guid id, string etag) { using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{id}/publish"); request.Headers.TryAddWithoutValidation("If-Match", etag); return await client.SendAsync(request); }
}
