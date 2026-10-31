using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Chuyển pin có lịch sử, giữ task/kết quả và không tái dùng evidence practice. EN: Scheduled choices preserve old snapshots and cannot reuse practice evidence for different nodes.</summary>
public sealed class PathChoiceTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Future_switch_preserves_today_and_lesson_progress_but_new_practice_nodes_need_new_attempts()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); await Dashboard(learner); await CompleteLesson(learner, paths.A.Lesson); var attempt = await Start(learner, paths.A.Practice); await Finish(learner, attempt);
        var before = await Dashboard(learner); var history = await History(learner); var command = Choice(history, paths.B.Id); var response = await Switch(learner, command); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<PathChoiceAck>())!;
        Assert.True(ack.Changed); Assert.True(ack.EffectiveAt > app.Clock.UtcNow); Assert.Equal(before.GetRawText(), (await Dashboard(learner)).GetRawText());
        app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); var plan = await Plan(learner); Assert.Equal(paths.B.Id, plan.GetProperty("template").GetProperty("id").GetGuid()); Assert.True(plan.GetProperty("nodes")[0].GetProperty("completed").GetBoolean()); Assert.False(plan.GetProperty("nodes")[1].GetProperty("completed").GetBoolean());
        var after = await Dashboard(learner); Assert.All(after.GetProperty("tasks").EnumerateArray(), x => Assert.Equal(paths.B.Id, x.GetProperty("pathVersionId").GetGuid())); Assert.All(after.GetProperty("tasks").EnumerateArray(), x => Assert.Equal("pending", x.GetProperty("status").GetString())); Assert.Equal(650, after.GetProperty("enrollment").GetProperty("targetScore").GetInt32()); Assert.Equal(30, after.GetProperty("enrollment").GetProperty("minutesPerDay").GetInt32());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task Cancel_keeps_the_original_calendar_start_and_supersedes_the_pending_choice()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); var original = (await Plan(learner)).GetProperty("template").GetRawText(); var before = await History(learner);
        (await Switch(learner, Choice(before, paths.B.Id))).EnsureSuccessStatusCode(); var pending = await History(learner); var scheduled = pending.GetProperty("revisions")[0];
        var cancelled = await Switch(learner, Choice(pending, paths.A.Id)); cancelled.EnsureSuccessStatusCode(); var ack = (await cancelled.Content.ReadFromJsonAsync<PathChoiceAck>())!; Assert.Equal(scheduled.GetProperty("effectiveAt").GetDateTimeOffset(), ack.EffectiveAt);
        var history = await History(learner); Assert.Equal("superseded", history.GetProperty("revisions")[1].GetProperty("status").GetString()); Assert.Equal("cancel_pending_switch", history.GetProperty("revisions")[0].GetProperty("action").GetString());
        app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); Assert.Equal(original, (await Plan(learner)).GetProperty("template").GetRawText()); Assert.Equal("active", (await History(learner)).GetProperty("revisions")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Two_tabs_have_one_winner_and_receipt_replay_after_activation_never_appends_another_choice()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); var before = await History(learner); var a = Choice(before, paths.B.Id); var b = Choice(before, paths.C.Id);
        var results = await Task.WhenAll(Switch(learner, a), Switch(learner, b)); Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var winner = results[0].IsSuccessStatusCode ? a : b; var first = results.Single(x => x.IsSuccessStatusCode); var text = await first.Content.ReadAsStringAsync(); var ack = (await first.Content.ReadFromJsonAsync<PathChoiceAck>())!; app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); await Plan(learner);
        var retry = await Switch(learner, winner); retry.EnsureSuccessStatusCode(); Assert.Equal(text, await retry.Content.ReadAsStringAsync()); Assert.Equal(2, (await History(learner)).GetProperty("revisions").GetArrayLength()); Assert.Equal(HttpStatusCode.Conflict, (await Switch(learner, winner with { Reason = "Changed payload" })).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == ack.RevisionId && x.Type == "learning.path_choice_activated"));
    }

    [Fact]
    public async Task Choices_are_owner_scoped_csrf_protected_and_invalid_inputs_have_no_effect()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var owner = await Learner(); using var other = await Learner(); await Enroll(owner, paths.A.Id); await Enroll(other, paths.A.Id); var before = await History(owner); var command = Choice(before, paths.B.Id);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Switch(other, command)).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Switch(owner, command with { PathVersionId = Guid.NewGuid() })).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Switch(owner, command with { Reason = " " })).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Switch(owner, command with { Reason = new string('x', 501) })).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await Switch(anonymous, command)).StatusCode); owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await Switch(owner, command)).StatusCode); await TestApp.Csrf(owner);
        Assert.Equal(before.GetRawText(), (await History(owner)).GetRawText()); var privateId = before.GetProperty("activeRevisionId").GetGuid(); Assert.DoesNotContain((await History(other)).GetProperty("revisions").EnumerateArray(), x => x.GetProperty("id").GetGuid() == privateId);
    }

    [Fact]
    public async Task Admission_reserved_before_retirement_still_activates_but_new_reservations_are_rejected()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var reserved = await Learner(); using var later = await Learner(); await Enroll(reserved, paths.A.Id); await Enroll(later, paths.A.Id);
        var response = await Switch(reserved, Choice(await History(reserved), paths.B.Id)); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<PathChoiceAck>())!; await Retire(admin, paths.B);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Switch(later, Choice(await History(later), paths.B.Id))).StatusCode);
        var same = await Switch(reserved, Choice(await History(reserved), paths.B.Id)); same.EnsureSuccessStatusCode(); Assert.False((await same.Content.ReadFromJsonAsync<PathChoiceAck>())!.Changed);
        app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); Assert.Equal(paths.B.Id, (await Plan(reserved)).GetProperty("template").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Cancel_can_keep_a_current_template_that_has_stopped_new_admissions()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); (await Switch(learner, Choice(await History(learner), paths.B.Id))).EnsureSuccessStatusCode(); await Retire(admin, paths.A);
        var cancel = await Switch(learner, Choice(await History(learner), paths.A.Id)); cancel.EnsureSuccessStatusCode(); var ack = (await cancel.Content.ReadFromJsonAsync<PathChoiceAck>())!; app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); Assert.Equal(paths.A.Id, (await Plan(learner)).GetProperty("template").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Old_database_baselines_record_observation_time_and_legacy_adoption_appends_history()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var legacy = await Learner(); var user = (await legacy.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Enrollments.Add(new Enrollment { UserId = user, TargetScore = 650, MinutesPerDay = 30, CreatedAt = app.Clock.UtcNow.AddDays(-30) }); await db.SaveChangesAsync(); }
        var baseline = (await History(legacy)).GetProperty("revisions")[0]; Assert.Equal("observed_baseline", baseline.GetProperty("action").GetString()); Assert.Equal(app.Clock.UtcNow, baseline.GetProperty("recordedAt").GetDateTimeOffset()); Assert.Equal(JsonValueKind.Null, baseline.GetProperty("pathVersionId").ValueKind);
        var adopt = new AdoptPathInput(paths.A.Id, Guid.NewGuid()); (await legacy.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", adopt)).EnsureSuccessStatusCode(); (await legacy.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", adopt)).EnsureSuccessStatusCode(); var history = await History(legacy); Assert.Equal(2, history.GetProperty("revisions").GetArrayLength()); Assert.Equal("legacy_adoption", history.GetProperty("revisions")[0].GetProperty("action").GetString()); Assert.Equal(paths.A.Id, (await Plan(legacy)).GetProperty("template").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Switching_at_midnight_keeps_a_live_old_attempt_and_never_credits_it_to_new_nodes()
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(app.Clock.UtcNow, "Asia/Ho_Chi_Minh").DateTime); var late = new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 58)), TimeSpan.FromHours(7)).ToUniversalTime(); if (late <= app.Clock.UtcNow) late = late.AddDays(1); app.Clock.Advance(late - app.Clock.UtcNow);
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); var before = await Dashboard(learner); var attempt = await Start(learner, paths.A.Practice); var scheduled = await Switch(learner, Choice(await History(learner), paths.B.Id)); scheduled.EnsureSuccessStatusCode(); app.Clock.Advance(TimeSpan.FromMinutes(3));
        var plan = await Plan(learner); Assert.Equal(paths.B.Id, plan.GetProperty("template").GetProperty("id").GetGuid()); var carried = await Dashboard(learner); var task = Assert.Single(carried.GetProperty("tasks").EnumerateArray()); Assert.Equal(paths.A.Id, task.GetProperty("pathVersionId").GetGuid()); Assert.Equal(attempt.Id, task.GetProperty("activeAttemptId").GetGuid()); Assert.Equal(attempt.DeadlineAt, (await learner.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}"))!.DeadlineAt);
        await Finish(learner, attempt); var after = await Dashboard(learner); Assert.All(after.GetProperty("tasks").EnumerateArray(), x => Assert.Equal(paths.B.Id, x.GetProperty("pathVersionId").GetGuid())); Assert.False((await Plan(learner)).GetProperty("nodes")[1].GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task A_later_publication_does_not_change_the_reserved_version_or_original_placement_record()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); var catalog = await learner.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var placementVersion = catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "placement-demo").GetProperty("id").GetGuid(); var placement = await Start(learner, placementVersion, "placement"); await Finish(learner, placement); (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, placement.Id, paths.A.Id))).EnsureSuccessStatusCode();
        var response = await Switch(learner, Choice(await History(learner), paths.B.Id)); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<PathChoiceAck>())!; var next = await admin.PostAsJsonAsync($"/api/v1/admin/paths/{paths.B.Template}/versions", paths.B.Input with { Title = "Newer publication" }); next.EnsureSuccessStatusCode(); var draft = await next.Content.ReadFromJsonAsync<JsonElement>(); (await Publish(admin, draft.GetProperty("id").GetGuid(), draft.GetProperty("etag").GetString()!)).EnsureSuccessStatusCode();
        app.Clock.Advance(ack.EffectiveAt - app.Clock.UtcNow); Assert.Equal(paths.B.Id, (await Plan(learner)).GetProperty("template").GetProperty("id").GetGuid()); Assert.All((await History(learner)).GetProperty("revisions").EnumerateArray(), x => Assert.Equal(placement.Id, x.GetProperty("placementAttemptId").GetGuid()));
    }

    [Fact]
    public async Task Same_choice_is_a_no_op_and_relational_ownership_rejects_a_foreign_revision()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var first = await Learner(); using var second = await Learner(); await Enroll(first, paths.A.Id); await Enroll(second, paths.A.Id); var before = await History(first); var response = await Switch(first, Choice(before, paths.A.Id)); response.EnsureSuccessStatusCode(); Assert.False((await response.Content.ReadFromJsonAsync<PathChoiceAck>())!.Changed); Assert.Equal(before.GetRawText(), (await History(first)).GetRawText());
        var a = (await first.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid(); var b = (await second.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var enrollment = await db.Enrollments.SingleAsync(x => x.UserId == a); db.PathChoiceRevisions.Add(new PathChoiceRevision { EnrollmentId = enrollment.Id, UserId = b, Number = 999, PathVersionId = paths.A.Id, PathStartedOn = enrollment.PathStartedOn, TimeZoneId = "Asia/Ho_Chi_Minh", SchedulingTimeZoneId = "Asia/Ho_Chi_Minh", StudyDays = "0,1,2,3,4,5,6", RecordedAt = app.Clock.UtcNow, EffectiveAt = app.Clock.UtcNow, Action = "test", SelectionReason = "manual_template" }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Returning_to_a_previously_activated_version_resumes_its_original_calendar()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); var original = (await Plan(learner)).GetProperty("template").GetProperty("pathStartedOn").GetString();
        app.Clock.Advance(TimeSpan.FromDays(5)); var toB = await Switch(learner, Choice(await History(learner), paths.B.Id)); toB.EnsureSuccessStatusCode(); var b = (await toB.Content.ReadFromJsonAsync<PathChoiceAck>())!; app.Clock.Advance(b.EffectiveAt - app.Clock.UtcNow); await Plan(learner); app.Clock.Advance(TimeSpan.FromDays(2));
        var toA = await Switch(learner, Choice(await History(learner), paths.A.Id)); toA.EnsureSuccessStatusCode(); var a = (await toA.Content.ReadFromJsonAsync<PathChoiceAck>())!; Assert.Equal(original, (await History(learner)).GetProperty("revisions")[0].GetProperty("pathStartedOn").GetString()); app.Clock.Advance(a.EffectiveAt - app.Clock.UtcNow); var resumed = await Plan(learner); Assert.Equal(original, resumed.GetProperty("template").GetProperty("pathStartedOn").GetString()); Assert.True(resumed.GetProperty("studyDay").GetInt32() > 1);
    }

    [Fact]
    public async Task A_cancelled_reservation_is_not_treated_as_a_previously_activated_calendar()
    {
        using var admin = await app.Login("admin@englishhub.local"); var paths = await Paths(admin); using var learner = await Learner(); await Enroll(learner, paths.A.Id); (await Switch(learner, Choice(await History(learner), paths.B.Id))).EnsureSuccessStatusCode(); var reserved = (await History(learner)).GetProperty("revisions")[0].GetProperty("pathStartedOn").GetString();
        var cancel = await Switch(learner, Choice(await History(learner), paths.A.Id)); cancel.EnsureSuccessStatusCode(); var cancellation = (await cancel.Content.ReadFromJsonAsync<PathChoiceAck>())!; app.Clock.Advance(cancellation.EffectiveAt - app.Clock.UtcNow + TimeSpan.FromDays(2)); await Plan(learner);
        var cancelled = (await History(learner)).GetProperty("revisions").EnumerateArray().Single(x => x.GetProperty("pathVersionId").GetGuid() == paths.B.Id); Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("activatedAt").ValueKind);
        (await Switch(learner, Choice(await History(learner), paths.B.Id))).EnsureSuccessStatusCode(); Assert.NotEqual(reserved, (await History(learner)).GetProperty("revisions")[0].GetProperty("pathStartedOn").GetString());
    }

    private sealed record PathInfo(Guid Id, Guid Template, Guid Lesson, Guid Practice, PathInput Input);
    private async Task<(PathInfo A, PathInfo B, PathInfo C)> Paths(HttpClient admin)
    {
        var catalog = await admin.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var lesson = catalog.EnumerateArray().First(x => x.GetProperty("kind").GetString() == "lesson").GetProperty("id").GetGuid(); var practice = catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-fill-blank").GetProperty("id").GetGuid();
        async Task<PathInfo> Make(string title, string level) { var content = new PathInput(title, "Original switch scenario", "EnglishHub original · CC0", [new("one", "Start", [new("lesson", lesson, 1, "lesson_quiz", 100, []), new("practice", practice, 1, "assessment_raw", 100, [])])]); var response = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"choice-{Guid.NewGuid():N}", level, content)); response.EnsureSuccessStatusCode(); var draft = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = draft.GetProperty("id").GetGuid(); (await Publish(admin, id, draft.GetProperty("etag").GetString()!)).EnsureSuccessStatusCode(); var meta = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/path-versions/{id}"); return new(id, meta.GetProperty("templateId").GetGuid(), lesson, practice, content); }
        return (await Make("Choice A", "foundation"), await Make("Choice B", "core"), await Make("Choice C", "exam_practice"));
    }
    private async Task<HttpClient> Learner() { using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"choice-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Path choice test", StudyDays = "0,1,2,3,4,5,6" }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email); }
    private static SwitchPathInput Choice(JsonElement history, Guid target) => new(Guid.NewGuid(), target, history.GetProperty("expectedEtag").GetString()!, "Tập trung vào mục tiêu học mới");
    private static Task<HttpResponseMessage> Switch(HttpClient client, SwitchPathInput input) => client.PostAsJsonAsync("/api/v1/learning/enrollment/switch-path", input);
    private static Task<JsonElement> History(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-revisions");
    private static Task<JsonElement> Plan(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
    private static Task<JsonElement> Dashboard(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static async Task Enroll(HttpClient client, Guid path) => (await client.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: path))).EnsureSuccessStatusCode();
    private static async Task<HttpResponseMessage> Publish(HttpClient client, Guid id, string etag) { using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{id}/publish"); request.Headers.TryAddWithoutValidation("If-Match", etag); return await client.SendAsync(request); }
    private static async Task Retire(HttpClient client, PathInfo path) { var meta = await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/path-versions/{path.Id}"); (await client.PostAsJsonAsync($"/api/v1/admin/paths/{path.Template}/retire", new PathLifecycleInput(Guid.NewGuid(), meta.GetProperty("templateEtag").GetString()!))).EnsureSuccessStatusCode(); }
    private static async Task<AttemptView> Start(HttpClient client, Guid version, string mode = "mini") { var response = await client.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version, Guid.NewGuid(), mode)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AttemptView>())!; }
    private async Task Finish(HttpClient client, AttemptView attempt) { using var scope = app.Services.CreateScope(); var keys = await scope.ServiceProvider.GetRequiredService<HubDbContext>().AttemptItems.Include(x => x.GradingKey).Where(x => x.AttemptId == attempt.Id).ToDictionaryAsync(x => x.Id); var answers = attempt.Items.Select(x => new AnswerChange(x.Id, keys[x.Id].GradingKey.CorrectOptionId, x.Etag, x.Type == "fill_blank" ? JsonSerializer.Deserialize<string[]>(keys[x.Id].GradingKey.AcceptedAnswerVariantsJson)![0] : null)).ToArray(); (await client.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), answers))).EnsureSuccessStatusCode(); }
    private async Task CompleteLesson(HttpClient client, Guid version) { using var scope = app.Services.CreateScope(); var questions = await scope.ServiceProvider.GetRequiredService<HubDbContext>().Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == version).ToArrayAsync(); (await client.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", new CompleteLessonInput(Guid.NewGuid(), questions.Select(x => new LessonAnswer(x.Id, x.Options.Single(o => o.IsCorrect).Id)).ToArray()))).EnsureSuccessStatusCode(); }
}
