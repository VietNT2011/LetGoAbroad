using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Kiểm tra graph, version ghim và completion từ bằng chứng thật. EN: Verify DAGs, version pinning and completion from persisted quiz/attempt evidence.</summary>
public sealed class PathTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("self", "cyclic_path")]
    [InlineData("cycle", "cyclic_path")]
    [InlineData("missing", "invalid_prerequisite")]
    [InlineData("duplicate", "invalid_path_nodes")]
    [InlineData("future", "invalid_schedule")]
    public void Graph_rejects_cycles_missing_keys_duplicates_and_reversed_days(string scenario, string code)
    {
        GraphNode[] nodes = scenario switch
        {
            "self" => [new("a", 1, ["a"])],
            "cycle" => [new("a", 1, ["b"]), new("b", 1, ["a"])],
            "missing" => [new("a", 1, ["unknown"])],
            "duplicate" => [new("a", 1, []), new("a", 1, [])],
            _ => [new("a", 2, []), new("b", 1, ["a"])]
        };
        Assert.Equal(code, Assert.Throws<DomainError>(() => PathGraph.Validate(nodes)).Code);
    }

    [Fact]
    public void Valid_dag_has_deterministic_parent_first_order()
    {
        var order = PathGraph.Validate([new("last", 3, ["a", "b"]), new("b", 2, ["root"]), new("a", 2, ["root"]), new("root", 1, [])]);
        Assert.Equal(new[] { "root", "a", "b", "last" }, order);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(45)]
    public void Scheduler_prioritizes_carryover_and_respects_budget_and_unique_content(int budget)
    {
        var carry = new PathCandidate(Guid.NewGuid(), Guid.NewGuid(), "assessment", 15, 1, 0, Guid.NewGuid());
        var lesson = new PathCandidate(Guid.NewGuid(), Guid.NewGuid(), "lesson", 10, 2, 1);
        var duplicate = lesson with { NodeId = Guid.NewGuid(), Order = 2 };
        var plan = PathScheduler.Select([lesson, duplicate, carry, new(Guid.NewGuid(), Guid.NewGuid(), "assessment", 5, 2, 3)], budget);
        Assert.Equal(carry, plan[0]); Assert.InRange(plan.Count, 1, 3); Assert.True(plan.Sum(x => x.Minutes) <= budget);
        Assert.Equal(plan.Count, plan.Select(x => x.ContentVersionId).Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(7, 6)]
    public void Authored_study_days_skip_days_outside_the_pinned_weekly_calendar(int offset, int expected)
        => Assert.Equal(expected, PathScheduler.StudyDay(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9).AddDays(offset), "1,2,3,4,5"));

    [Fact]
    public async Task Cms_validates_before_publish_and_graph_only_edits_advance_etags()
    {
        using var admin = await app.Login("admin@englishhub.local"); var content = await ContentIds(admin);
        var input = Plan(content.Lessons[0], content.Lessons[1]);
        var cyclic = input with { Stages = [new("start", "Start", [Node("a", content.Lessons[0], 1, ["b"]), Node("b", content.Lessons[1], 1, ["a"])])] };
        var draft = await Create(admin, cyclic);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/path-versions/{draft.Id}/validate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Command(admin, HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.Id}/publish", draft.Etag)).StatusCode);
        var edited = await Command(admin, HttpMethod.Put, $"/api/v1/admin/path-versions/{draft.Id}", draft.Etag, input); edited.EnsureSuccessStatusCode();
        var next = await edited.Content.ReadFromJsonAsync<JsonElement>(); var etag = next.GetProperty("etag").GetString()!; Assert.NotEqual(draft.Etag, etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Command(admin, HttpMethod.Put, $"/api/v1/admin/path-versions/{draft.Id}", draft.Etag, input)).StatusCode);
        (await admin.PostAsJsonAsync($"/api/v1/admin/path-versions/{draft.Id}/validate", new { })).EnsureSuccessStatusCode();
        var published = await Command(admin, HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.Id}/publish", etag); published.EnsureSuccessStatusCode();
        var finalEtag = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("etag").GetString()!;
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, HttpMethod.Put, $"/api/v1/admin/path-versions/{draft.Id}", finalEtag, input)).StatusCode);
        using var learner = await NewLearner(); Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/v1/admin/paths")).StatusCode);
        using var editor = await app.Login("editor@englishhub.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await Command(editor, HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.Id}/publish", finalEtag)).StatusCode);
        var bad = Plan(Guid.NewGuid(), content.Lessons[1]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"test-{Guid.NewGuid():N}", "foundation", bad))).StatusCode);
    }

    [Fact]
    public async Task Enrollment_stays_on_its_published_path_after_a_new_version_and_content_archive_is_blocked()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); using var newcomer = await NewLearner();
        var content = await ContentIds(admin); var input = Plan(content.Lessons[0], content.Lessons[1]); var first = await Create(admin, input, true);
        await Enroll(learner, first.Id);
        var v = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/path-versions/{first.Id}");
        var secondContent = input with { Title = "Updated path", Stages = [new("start", "Start", [Node("new-first", content.Lessons[1], 1, [])])] };
        var next = await admin.PostAsJsonAsync($"/api/v1/admin/paths/{v.GetProperty("templateId").GetGuid()}/versions", secondContent); next.EnsureSuccessStatusCode();
        var second = await next.Content.ReadFromJsonAsync<JsonElement>();
        (await Command(admin, HttpMethod.Post, $"/api/v1/admin/path-versions/{second.GetProperty("id").GetGuid()}/publish", second.GetProperty("etag").GetString()!)).EnsureSuccessStatusCode();
        await Enroll(newcomer, second.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: second.GetProperty("id").GetGuid()))).StatusCode);
        using var basics = await NewLearner();
        var basicsEnrollment = await basics.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, Guid.NewGuid(), first.Id)); basicsEnrollment.EnsureSuccessStatusCode();
        Assert.Equal("manual_template", (await basicsEnrollment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selectionReason").GetString());
        var oldPlan = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); var newPlan = await newcomer.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
        Assert.Equal(first.Id, oldPlan.GetProperty("template").GetProperty("id").GetGuid()); Assert.Equal(content.Lessons[0], oldPlan.GetProperty("nodes")[0].GetProperty("contentVersionId").GetGuid());
        Assert.Equal("Updated path", newPlan.GetProperty("template").GetProperty("title").GetString());
        var catalogVersion = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{content.Lessons[0]}");
        var changedContent = JsonNode.Parse(catalogVersion.GetRawText())!; changedContent["title"] = "A newly authored version";
        var createdContent = await admin.PostAsJsonAsync($"/api/v1/admin/content/{catalogVersion.GetProperty("rootId").GetGuid()}/versions", changedContent); createdContent.EnsureSuccessStatusCode();
        var newContent = await createdContent.Content.ReadFromJsonAsync<JsonElement>();
        (await Command(admin, HttpMethod.Post, $"/api/v1/admin/versions/{newContent.GetProperty("id").GetGuid()}/publish", newContent.GetProperty("etag").GetString()!)).EnsureSuccessStatusCode();
        var afterContentPublication = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
        Assert.Equal(catalogVersion.GetProperty("title").GetString(), afterContentPublication.GetProperty("nodes")[0].GetProperty("title").GetString());
        Assert.Equal(content.Lessons[0], afterContentPublication.GetProperty("nodes")[0].GetProperty("contentVersionId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Command(admin, HttpMethod.Post, $"/api/v1/admin/versions/{content.Lessons[0]}/archive", catalogVersion.GetProperty("etag").GetString()!)).StatusCode);
        var tasks = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks");
        Assert.All(tasks.EnumerateArray(), task => Assert.Equal(first.Id, task.GetProperty("pathVersionId").GetGuid()));
    }

    [Fact]
    public async Task Prerequisites_require_quiz_evidence_and_practice_requires_its_configured_raw_score()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var input = new PathInput("Evidence path", "Original schedule", "EnglishHub original · CC0", [new("start", "Start", [Node("a", content.Lessons[0], 1, []), new("practice", content.Practice, 1, "assessment_raw", 50, ["a"]), Node("b", content.Lessons[1], 2, ["practice"])])]);
        var path = await Create(admin, input, true); await Enroll(learner, path.Id);
        var first = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Single(first.GetProperty("tasks").EnumerateArray());
        var before = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.False(before.GetProperty("nodes")[1].GetProperty("unlocked").GetBoolean());
        await CompleteLesson(learner, content.Lessons[0]);
        app.Clock.Advance(TimeSpan.FromDays(1));
        var practiceDay = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(content.Practice, practiceDay.GetProperty("tasks")[0].GetProperty("contentVersionId").GetGuid());
        var wrong = await FinishPractice(learner, content.Practice, false);
        var afterWrong = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.False(afterWrong.GetProperty("nodes")[1].GetProperty("completed").GetBoolean()); Assert.False(afterWrong.GetProperty("nodes")[2].GetProperty("unlocked").GetBoolean());
        var correct = await FinishPractice(learner, content.Practice, true);
        var after = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.True(after.GetProperty("nodes")[1].GetProperty("completed").GetBoolean()); Assert.True(after.GetProperty("nodes")[2].GetProperty("unlocked").GetBoolean());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.False(await db.TaskCompletions.AnyAsync(x => x.AttemptId == wrong)); Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.AttemptId == correct));
        app.Clock.Advance(TimeSpan.FromDays(1)); var next = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        Assert.Equal(content.Lessons[1], next.GetProperty("tasks")[0].GetProperty("contentVersionId").GetGuid());
    }

    [Fact]
    public async Task Missed_tasks_form_one_successor_chain_and_only_the_active_successor_is_completed()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var path = await Create(admin, Plan(content.Lessons[0], content.Lessons[1]), true); await Enroll(learner, path.Id);
        var first = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks")[0];
        app.Clock.Advance(TimeSpan.FromDays(1));
        var concurrent = await Task.WhenAll(learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"), learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"));
        Assert.Equal(concurrent[0].GetProperty("tasks").GetRawText(), concurrent[1].GetProperty("tasks").GetRawText());
        var second = concurrent[0].GetProperty("tasks")[0]; Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("sourceTaskId").GetGuid()); Assert.Equal("PATH_CARRYOVER", second.GetProperty("reasonCode").GetString());
        app.Clock.Advance(TimeSpan.FromDays(1));
        var third = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks")[0]; Assert.Equal(second.GetProperty("id").GetGuid(), third.GetProperty("sourceTaskId").GetGuid());
        await CompleteLesson(learner, content.Lessons[0]); await learner.GetAsync("/api/v1/learning/dashboard");
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var node = third.GetProperty("pathNodeId").GetGuid();
        Assert.Equal(2, await db.DailyTasks.CountAsync(x => x.PathNodeId == node && x.Status == "missed")); Assert.Equal(1, await db.DailyTasks.CountAsync(x => x.PathNodeId == node && x.Status == "completed")); Assert.False(await db.DailyTasks.AnyAsync(x => x.PathNodeId == node && x.Status == "pending"));
        Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.DailyTask.PathNodeId == node));
    }

    [Fact]
    public async Task One_graded_attempt_cannot_complete_two_repeated_practice_nodes()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var path = await Create(admin, new("Repeated practice", "Repeat on separate days", "Original · CC0", [new("start", "Start", [new("one", content.Practice, 1, "assessment_raw", 50, []), new("two", content.Practice, 2, "assessment_raw", 50, ["one"])])]), true);
        await Enroll(learner, path.Id); await learner.GetAsync("/api/v1/learning/dashboard"); var first = await FinishPractice(learner, content.Practice, true); await learner.GetAsync("/api/v1/learning/dashboard");
        app.Clock.Advance(TimeSpan.FromDays(1)); var day2 = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal("pending", day2.GetProperty("tasks")[0].GetProperty("status").GetString());
        var second = await FinishPractice(learner, content.Practice, true); await learner.GetAsync("/api/v1/learning/dashboard");
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.AttemptId == first)); Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.AttemptId == second));
    }

    [Fact]
    public async Task Database_rejects_cross_version_edges_and_task_content_mismatching_its_node()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var one = await Create(admin, Plan(content.Lessons[0], content.Lessons[1]), true); var two = await Create(admin, Plan(content.Lessons[0], content.Lessons[1]), true);
        await Enroll(learner, one.Id);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
            var a = await db.PathNodes.FirstAsync(x => x.PathVersionId == one.Id && x.Key == "a"); var b = await db.PathNodes.FirstAsync(x => x.PathVersionId == two.Id && x.Key == "a");
            db.PathPrerequisites.Add(new PathPrerequisite { PathVersionId = one.Id, NodeId = a.Id, PrerequisiteNodeId = b.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        var task = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks")[0];
        var taskId = task.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var source = await db.DailyTasks.SingleAsync(x => x.Id == taskId);
            db.DailyTasks.Add(new DailyTask { EnrollmentId = source.EnrollmentId, UserId = source.UserId, LocalDate = source.LocalDate, SlotKey = 2, ContentVersionId = content.Lessons[1], PathVersionId = one.Id, PathNodeId = source.PathNodeId, Status = "missed", CreatedAt = app.Clock.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Legacy_adoption_is_explicit_replayable_and_preserves_existing_daily_tasks()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var me = await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"); var userId = me.GetProperty("id").GetGuid(); Guid taskId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var enrollment = new Enrollment { UserId = userId, CreatedAt = app.Clock.UtcNow, MinutesPerDay = 30, TargetScore = 650 };
            var task = new DailyTask { EnrollmentId = enrollment.Id, UserId = userId, LocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(app.Clock.UtcNow, "Asia/Ho_Chi_Minh").DateTime), ContentVersionId = content.Lessons[0], EstimatedMinutes = 10, CreatedAt = app.Clock.UtcNow };
            taskId = task.Id; db.Enrollments.Add(enrollment); db.DailyTasks.Add(task); await db.SaveChangesAsync();
        }
        var template = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates")).EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "foundation-fourteen-days").GetProperty("id").GetGuid();
        var command = new AdoptPathInput(template, Guid.NewGuid()); var first = await learner.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", command); first.EnsureSuccessStatusCode();
        Assert.Equal(await first.Content.ReadAsStringAsync(), await (await learner.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", command)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/learning/enrollment/adopt-path", command with { PathVersionId = Guid.NewGuid() })).StatusCode);
        var dashboard = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(taskId, dashboard.GetProperty("tasks")[0].GetProperty("id").GetGuid());
        var plan = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.Equal(18, plan.GetProperty("nodes").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Midnight_preserves_active_attempts_and_recovers_results_committed_during_rollover(bool simulateRolloverRace)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.UtcNow, zone).DateTime);
        var nearMidnight = new DateTimeOffset(date.ToDateTime(new TimeOnly(23, 58)), TimeSpan.FromHours(7)).ToUniversalTime();
        if (nearMidnight <= app.Clock.UtcNow) nearMidnight = nearMidnight.AddDays(1);
        app.Clock.Advance(nearMidnight - app.Clock.UtcNow);
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await NewLearner(); var content = await ContentIds(admin);
        var path = await Create(admin, new("Midnight path", "Original clock boundary scenario", "EnglishHub original · CC0", [new("start", "Start", [new("one", content.Practice, 1, "assessment_raw", 50, []), new("two", content.Practice, 2, "assessment_raw", 50, ["one"])])]), true);
        await Enroll(learner, path.Id);
        var first = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks")[0]; var taskId = first.GetProperty("id").GetGuid();
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(content.Practice, Guid.NewGuid())); started.EnsureSuccessStatusCode();
        var attempt = (await started.Content.ReadFromJsonAsync<AttemptView>())!;
        app.Clock.Advance(TimeSpan.FromMinutes(3)); Guid? successorId = null;
        if (simulateRolloverRace)
        {
            // VI: Mô phỏng interleaving: rollover chưa thấy result sắp commit và đã tạo successor. EN: Reproduce the interleaving where rollover misses an about-to-commit result and creates a successor.
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var original = await db.DailyTasks.SingleAsync(x => x.Id == taskId);
            original.Status = "missed"; await db.SaveChangesAsync();
            var successor = new DailyTask { EnrollmentId = original.EnrollmentId, UserId = original.UserId, ContentVersionId = original.ContentVersionId, PathVersionId = original.PathVersionId, PathNodeId = original.PathNodeId, SourceTaskId = original.Id, LocalDate = original.LocalDate.AddDays(1), TimeZoneId = original.TimeZoneId, EstimatedMinutes = original.EstimatedMinutes, CreatedAt = app.Clock.UtcNow };
            successorId = successor.Id; db.DailyTasks.Add(successor); await db.SaveChangesAsync();
        }
        else
        {
            var afterMidnight = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("tasks")[0];
            Assert.Equal(taskId, afterMidnight.GetProperty("id").GetGuid()); Assert.Equal(attempt.Id, afterMidnight.GetProperty("activeAttemptId").GetGuid()); Assert.True(afterMidnight.GetProperty("carriedFromPreviousDate").GetBoolean());
        }
        await SubmitPractice(learner, attempt, true); await learner.GetAsync("/api/v1/learning/dashboard");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var completion = await db.TaskCompletions.SingleAsync(x => x.AttemptId == attempt.Id);
            Assert.Equal(taskId, completion.DailyTaskId); Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(app.Clock.UtcNow, zone).DateTime), completion.LocalDate);
            if (successorId is { } successor) Assert.Equal("superseded", (await db.DailyTasks.SingleAsync(x => x.Id == successor)).Status);
        }
        var plan = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.True(plan.GetProperty("nodes")[0].GetProperty("completed").GetBoolean()); Assert.False(plan.GetProperty("nodes")[1].GetProperty("completed").GetBoolean());
    }

    private static PathNodeInput Node(string key, Guid content, int day, string[] prerequisites) => new(key, content, day, "lesson_quiz", 100, prerequisites);
    private static PathInput Plan(Guid a, Guid b) => new("Original path", "Two authored study days", "EnglishHub original · CC0", [new("start", "Start", [Node("a", a, 1, []), Node("b", b, 2, ["a"])])]);
    private async Task<HttpClient> NewLearner()
    {
        var email = $"path-{Guid.NewGuid():N}@example.com";
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Path test", StudyDays = "0,1,2,3,4,5,6", MinutesPerDay = 30 };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded);
        return await app.Login(email);
    }
    private static async Task<(Guid[] Lessons, Guid Practice)> ContentIds(HttpClient client)
    {
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var items = catalog.EnumerateArray().ToArray();
        return (items.Where(x => x.GetProperty("kind").GetString() == "lesson").Take(2).Select(x => x.GetProperty("id").GetGuid()).ToArray(), items.Single(x => x.GetProperty("slug").GetString() == "mini-fill-blank").GetProperty("id").GetGuid());
    }
    private static async Task<(Guid Id, string Etag)> Create(HttpClient admin, PathInput input, bool publish = false)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"test-{Guid.NewGuid():N}", "foundation", input)); response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = draft.GetProperty("id").GetGuid(); var etag = draft.GetProperty("etag").GetString()!;
        if (publish) { var done = await Command(admin, HttpMethod.Post, $"/api/v1/admin/path-versions/{id}/publish", etag); done.EnsureSuccessStatusCode(); etag = (await done.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("etag").GetString()!; }
        return (id, etag);
    }
    private static Task<HttpResponseMessage> Command(HttpClient client, HttpMethod method, string route, string etag, object? body = null)
    {
        var request = new HttpRequestMessage(method, route); request.Headers.TryAddWithoutValidation("If-Match", etag); if (body is not null) request.Content = JsonContent.Create(body); return client.SendAsync(request);
    }
    private static async Task Enroll(HttpClient learner, Guid version) => (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: version))).EnsureSuccessStatusCode();
    private async Task CompleteLesson(HttpClient client, Guid lesson)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var questions = await db.Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == lesson).ToArrayAsync();
        var answers = questions.Select(q => new LessonAnswer(q.Id, q.Options.Single(x => x.IsCorrect).Id)).ToArray();
        (await client.PostAsJsonAsync($"/api/v1/lessons/{lesson}/complete", new CompleteLessonInput(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
    private async Task<Guid> FinishPractice(HttpClient client, Guid version, bool correct)
    {
        var started = await client.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version, Guid.NewGuid())); started.EnsureSuccessStatusCode(); var attempt = (await started.Content.ReadFromJsonAsync<AttemptView>())!;
        await SubmitPractice(client, attempt, correct); return attempt.Id;
    }
    private async Task SubmitPractice(HttpClient client, AttemptView attempt, bool correct)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var keys = await db.AttemptItems.Include(x => x.GradingKey).Where(x => x.AttemptId == attempt.Id).ToDictionaryAsync(x => x.Id);
        var answers = correct ? attempt.Items.Select(item => new AnswerChange(item.Id, null, item.Etag, JsonSerializer.Deserialize<string[]>(keys[item.Id].GradingKey.AcceptedAnswerVariantsJson)![0])).ToArray() : [];
        (await client.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
}
