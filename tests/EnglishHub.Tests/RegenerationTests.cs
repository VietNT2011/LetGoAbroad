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

/// <summary>VI: Giữ bằng chứng, task đang làm và ngân sách khi thay lịch. EN: Regeneration preserves evidence, started assignments and the daily budget under real relational races.</summary>
public sealed class RegenerationTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Starting_an_owned_assignment_is_idempotent_and_never_completes_it()
    {
        var setup = await Setup(); using var learner = setup.Client; var before = await Dashboard(learner); var id = TaskFor(before, setup.C).GetProperty("id").GetGuid();
        var first = await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{id}/start", new { }); first.EnsureSuccessStatusCode();
        var again = await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{id}/start", new { }); again.EnsureSuccessStatusCode(); Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        var current = await Dashboard(learner); Assert.NotEqual(before.GetProperty("planEtag").GetString(), current.GetProperty("planEtag").GetString()); Assert.Equal("pending", TaskFor(current, setup.C).GetProperty("status").GetString());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.Type == "learning.task_started" && x.AggregateId == id)); Assert.False(await db.TaskCompletions.AnyAsync(x => x.DailyTaskId == id));
        using var other = await app.Login("learner2@englishhub.local"); Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/learning/tasks/{id}/start", new { })).StatusCode);
    }

    [Fact]
    public async Task Identical_selection_keeps_ids_and_replay_after_midnight_returns_the_original_ack()
    {
        var setup = await Setup(); using var learner = setup.Client; var before = await Dashboard(learner); var command = Command(before);
        var first = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); first.EnsureSuccessStatusCode(); var ack = await first.Content.ReadAsStringAsync(); Assert.False((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        Assert.Equal(before.GetProperty("tasks").GetRawText(), (await Dashboard(learner)).GetProperty("tasks").GetRawText());
        app.Clock.Advance(TimeSpan.FromDays(1)); var next = await Dashboard(learner); var nextTasks = next.GetProperty("tasks").GetRawText();
        var retry = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); retry.EnsureSuccessStatusCode(); Assert.Equal(ack, await retry.Content.ReadAsStringAsync()); Assert.Equal(nextTasks, (await Dashboard(learner)).GetProperty("tasks").GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command with { LocalDate = command.LocalDate.AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command with { ClientRequestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Newly_unlocked_content_replaces_only_unstarted_tasks_and_reserves_completed_and_started_budget()
    {
        var setup = await Setup(); using var learner = setup.Client; var initial = await Dashboard(learner); var c = TaskFor(initial, setup.C).GetProperty("id").GetGuid(); var d = TaskFor(initial, setup.D).GetProperty("id").GetGuid();
        (await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{c}/start", new { })).EnsureSuccessStatusCode(); await Complete(learner, setup.A); var current = await Dashboard(learner); var a = TaskFor(current, setup.A).GetProperty("id").GetGuid();
        byte[] cVersion; using (var scope = app.Services.CreateScope()) cVersion = (await scope.ServiceProvider.GetRequiredService<HubDbContext>().DailyTasks.AsNoTracking().SingleAsync(x => x.Id == c)).RowVersion;
        var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(current)); response.EnsureSuccessStatusCode(); var ack = await response.Content.ReadFromJsonAsync<RegenerationAck>(); Assert.True(ack!.Changed); Assert.Contains(a, ack.RetainedTaskIds); Assert.Contains(c, ack.RetainedTaskIds); Assert.Equal([d], ack.SupersededTaskIds);
        var after = await Dashboard(learner); Assert.Equal(c, TaskFor(after, setup.C).GetProperty("id").GetGuid()); Assert.Equal(a, TaskFor(after, setup.A).GetProperty("id").GetGuid()); TaskFor(after, setup.B); Assert.DoesNotContain(after.GetProperty("tasks").EnumerateArray(), x => x.GetProperty("id").GetGuid() == d); AssertPlan(after, 30);
        using var check = app.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal("superseded", (await db.DailyTasks.SingleAsync(x => x.Id == d)).Status); Assert.Equal(cVersion, (await db.DailyTasks.SingleAsync(x => x.Id == c)).RowVersion); Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.DailyTaskId == a));
        Assert.Equal(after.GetProperty("planEtag").GetString(), ack.PlanEtag);
    }

    [Fact]
    public async Task An_attempt_started_outside_the_dashboard_is_also_protected_and_keeps_its_deadline()
    {
        var setup = await Setup(practice: true); using var learner = setup.Client; var initial = await Dashboard(learner); var c = TaskFor(initial, setup.C).GetProperty("id").GetGuid();
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(setup.C, Guid.NewGuid())); started.EnsureSuccessStatusCode(); var attempt = (await started.Content.ReadFromJsonAsync<AttemptView>())!;
        await Complete(learner, setup.A); var current = await Dashboard(learner); Assert.Equal(attempt.StartedAt, TaskFor(current, setup.C).GetProperty("startedAt").GetDateTimeOffset());
        var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(current)); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<RegenerationAck>())!; Assert.Contains(c, ack.RetainedTaskIds);
        Assert.Equal(attempt.DeadlineAt, (await learner.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}"))!.DeadlineAt); AssertPlan(await Dashboard(learner), 45);
    }

    [Fact]
    public async Task Two_regenerations_from_one_snapshot_have_one_winner_and_no_duplicate_assignments()
    {
        var setup = await Setup(); using var learner = setup.Client; await Dashboard(learner); await Complete(learner, setup.A); var current = await Dashboard(learner); var command = Command(current);
        var results = await Task.WhenAll(learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command), learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command with { ClientRequestId = Guid.NewGuid() }));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed); AssertPlan(await Dashboard(learner), 30);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var user = (await Me(learner)).GetProperty("id").GetGuid(); var enrollment = await db.Enrollments.SingleAsync(x => x.UserId == user); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == enrollment.Id && x.Type == "learning.plan_regenerated"));
    }

    [Fact]
    public async Task Starting_and_regenerating_the_same_assignment_serialize_without_losing_started_work()
    {
        var setup = await Setup(); using var learner = setup.Client; await Dashboard(learner); await Complete(learner, setup.A); var current = await Dashboard(learner); var id = TaskFor(current, setup.D).GetProperty("id").GetGuid();
        var results = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/learning/tasks/{id}/start", new { }), learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(current)));
        if (results[0].IsSuccessStatusCode) Assert.Equal(HttpStatusCode.PreconditionFailed, results[1].StatusCode);
        else { results[1].EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, results[0].StatusCode); }
        using var scope = app.Services.CreateScope(); var task = await scope.ServiceProvider.GetRequiredService<HubDbContext>().DailyTasks.SingleAsync(x => x.Id == id); Assert.False(task.StartedAt is not null && task.Status == "superseded"); AssertPlan(await Dashboard(learner), 30);
    }

    [Fact]
    public async Task Missing_or_foreign_versions_and_csrf_fail_without_regeneration_effects()
    {
        var setup = await Setup(); using var learner = setup.Client; var before = await Dashboard(learner); var command = Command(before);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command with { ExpectedEtag = "foreign" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command with { ClientRequestId = Guid.Empty })).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command)).StatusCode);
        learner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command)).StatusCode); await TestApp.Csrf(learner);
        Assert.Equal(before.GetRawText(), (await Dashboard(learner)).GetRawText());
    }

    [Fact]
    public async Task All_started_tasks_and_empty_content_are_stable_no_ops()
    {
        var setup = await Setup(); using var learner = setup.Client; var before = await Dashboard(learner);
        foreach (var task in before.GetProperty("tasks").EnumerateArray()) (await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{task.GetProperty("id").GetGuid()}/start", new { })).EnsureSuccessStatusCode();
        var held = await Dashboard(learner); var result = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(held)); result.EnsureSuccessStatusCode(); Assert.False((await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean()); Assert.Equal(held.GetRawText(), (await Dashboard(learner)).GetRawText());
        var future = await Setup(future: true); using var futureLearner = future.Client; var empty = await Dashboard(futureLearner); Assert.Empty(empty.GetProperty("tasks").EnumerateArray());
        var noContent = await futureLearner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(empty)); noContent.EnsureSuccessStatusCode(); Assert.Empty((await noContent.Content.ReadFromJsonAsync<RegenerationAck>())!.NewTaskIds);
    }

    [Fact]
    public async Task Five_actual_changes_per_rolling_day_are_allowed_and_the_next_is_atomic_rejection()
    {
        var setup = await Setup(); using var learner = setup.Client; await Dashboard(learner); var weekday = (int)LocalDate().DayOfWeek;
        for (var change = 0; change < 5; change++)
        {
            await Days(learner, Enumerable.Range(0, 7).Where(x => change % 2 == 1 || x != weekday).ToArray()); var snapshot = await Dashboard(learner); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(snapshot)); response.EnsureSuccessStatusCode(); Assert.True((await response.Content.ReadFromJsonAsync<RegenerationAck>())!.Changed);
        }
        await Days(learner, Enumerable.Range(0, 7).ToArray()); var before = await Dashboard(learner); Assert.Equal(HttpStatusCode.TooManyRequests, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before))).StatusCode); Assert.Equal(before.GetRawText(), (await Dashboard(learner)).GetRawText());
    }

    [Fact]
    public async Task Legacy_enrollments_share_budgeted_regeneration_and_keep_started_assignments()
    {
        using var learner = await Learner(30); var me = await Me(learner); var user = me.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Enrollments.Add(new Enrollment { UserId = user, MinutesPerDay = 30, TargetScore = 650, CreatedAt = app.Clock.UtcNow }); await db.SaveChangesAsync(); }
        var before = await Dashboard(learner); var id = before.GetProperty("tasks")[0].GetProperty("id").GetGuid(); (await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{id}/start", new { })).EnsureSuccessStatusCode(); var current = await Dashboard(learner);
        var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(current)); response.EnsureSuccessStatusCode(); Assert.Contains(id, (await response.Content.ReadFromJsonAsync<RegenerationAck>())!.RetainedTaskIds); AssertPlan(await Dashboard(learner), 30);
    }

    [Fact]
    public async Task A_profile_tracked_before_the_lock_cannot_hide_a_later_calendar_change()
    {
        var setup = await Setup(); using var learner = setup.Client; var before = await Dashboard(learner); var userId = (await Me(learner)).GetProperty("id").GetGuid();
        using var pendingRequest = app.Services.CreateScope(); var db = pendingRequest.ServiceProvider.GetRequiredService<HubDbContext>();
        // VI: Mô phỏng cookie validator đã cache entity trước khi request đợi khóa. EN: Reproduce a cookie validator tracking the user before a request waits for its owner lock.
        var cached = await db.Users.SingleAsync(x => x.Id == userId); Assert.Contains(((int)LocalDate().DayOfWeek).ToString(), cached.StudyDays.Split(','));
        await Days(learner, Enumerable.Range(0, 7).Where(x => x != (int)LocalDate().DayOfWeek).ToArray());
        var service = pendingRequest.ServiceProvider.GetRequiredService<LearningService>(); var error = await Assert.ThrowsAsync<DomainError>(() => service.Regenerate(userId, Command(before))); Assert.Equal(412, error.Status);
        Assert.Equal(before.GetProperty("tasks").GetRawText(), (await Dashboard(learner)).GetProperty("tasks").GetRawText());
    }

    private sealed record Scenario(HttpClient Client, Guid A, Guid B, Guid C, Guid D);
    private async Task<Scenario> Setup(bool practice = false, bool future = false)
    {
        using var admin = await app.Login("admin@englishhub.local"); var catalog = await admin.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var lessons = catalog.EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "lesson").Take(4).Select(x => x.GetProperty("id").GetGuid()).ToArray(); var c = practice ? catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-fill-blank").GetProperty("id").GetGuid() : lessons[2]; var day = future ? 365 : 1;
        var input = new PathInput("Regeneration test", "Original bounded scheduling scenario", "EnglishHub original · CC0", [new("one", "Start", [new("a", lessons[0], day, "lesson_quiz", 100, []), new("b", lessons[1], day, "lesson_quiz", 100, ["a"]), new("c", c, day, practice ? "assessment_raw" : "lesson_quiz", practice ? 50 : 100, []), new("d", lessons[3], day, "lesson_quiz", 100, [])])]);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"regen-{Guid.NewGuid():N}", "foundation", input)); created.EnsureSuccessStatusCode(); var draft = await created.Content.ReadFromJsonAsync<JsonElement>(); var version = draft.GetProperty("id").GetGuid(); using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{version}/publish"); publish.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString()); (await admin.SendAsync(publish)).EnsureSuccessStatusCode();
        var learner = await Learner(practice ? 45 : 30); (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: version))).EnsureSuccessStatusCode(); return new(learner, lessons[0], lessons[1], c, lessons[3]);
    }
    private async Task<HttpClient> Learner(int minutes)
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"regen-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Regeneration test", StudyDays = "0,1,2,3,4,5,6", MinutesPerDay = minutes }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private async Task Complete(HttpClient learner, Guid version)
    {
        using var scope = app.Services.CreateScope(); var questions = await scope.ServiceProvider.GetRequiredService<HubDbContext>().Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == version).ToArrayAsync(); var answers = questions.Select(q => new LessonAnswer(q.Id, q.Options.Single(x => x.IsCorrect).Id)).ToArray(); (await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", new CompleteLessonInput(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
    private static Task<JsonElement> Dashboard(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static Task<JsonElement> Me(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
    private static RegeneratePlanInput Command(JsonElement data) => new(Guid.NewGuid(), DateOnly.Parse(data.GetProperty("localDate").GetString()!), data.GetProperty("planEtag").GetString()!);
    private static JsonElement TaskFor(JsonElement data, Guid version) => data.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("contentVersionId").GetGuid() == version);
    private static void AssertPlan(JsonElement data, int budget) { var tasks = data.GetProperty("tasks").EnumerateArray().ToArray(); Assert.InRange(tasks.Length, 0, 3); Assert.Equal(tasks.Length, tasks.Select(x => x.GetProperty("contentVersionId").GetGuid()).Distinct().Count()); Assert.True(tasks.Sum(x => x.GetProperty("estimatedMinutes").GetInt32()) <= budget); }
    private DateOnly LocalDate() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(app.Clock.UtcNow, "Asia/Ho_Chi_Minh").DateTime);
    private static async Task Days(HttpClient client, int[] days) { var me = await Me(client); (await client.PutAsJsonAsync("/api/v1/profile", new { displayName = me.GetProperty("displayName").GetString(), timeZoneId = "Asia/Ho_Chi_Minh", targetScore = 650, minutesPerDay = 30, studyDays = days, expectedEtag = me.GetProperty("profileEtag").GetString() })).EnsureSuccessStatusCode(); }
}
