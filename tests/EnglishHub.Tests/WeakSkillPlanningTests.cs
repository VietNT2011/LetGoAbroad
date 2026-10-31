using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Ưu tiên phải dựa trên mẫu hợp lệ và không vượt các rào chắn lịch học. EN: Reinforcement uses eligible evidence without bypassing scheduling safeguards.</summary>
public sealed class WeakSkillPlanningTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData(4, 0, false)]
    [InlineData(5, 2, true)]
    [InlineData(5, 3, false)]
    [InlineData(50, 29, true)]
    [InlineData(50, 30, false)]
    public void Policy_uses_minimum_sample_and_strict_raw_threshold(int observed, int correct, bool expected)
        => Assert.Equal(expected, WeakSkillPolicy.Rank([new("reading", observed, correct)]).Length == 1);

    [Fact]
    public void Scheduler_keeps_carryover_before_weak_nodes_and_ignores_unaffordable_priorities()
    {
        var carry = new PathCandidate(Guid.NewGuid(), Guid.NewGuid(), "lesson", 10, 1, 0, Guid.NewGuid());
        var weak = new PathCandidate(Guid.NewGuid(), Guid.NewGuid(), "lesson", 10, 1, 2, WeakSkillRank: 0);
        var strong = new PathCandidate(Guid.NewGuid(), Guid.NewGuid(), "lesson", 10, 1, 1);
        Assert.Equal([carry, weak], PathScheduler.Select([strong, weak, carry], 20));
        Assert.Equal([strong], PathScheduler.Select([weak with { Minutes = 30 }, strong], 10));
        var legacy = Planner.Select([new(weak.ContentVersionId, 30, "lesson", 0, 0), new(strong.ContentVersionId, 10, "lesson", 1)], [], 10);
        Assert.Equal(strong.ContentVersionId, Assert.Single(legacy).ContentVersionId);
        Assert.Equal(["grammar", "reading"], WeakSkillPolicy.Rank([new("reading", 10, 2), new("grammar", 5, 1)]).Select(x => x.Skill));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 3)]
    public async Task Sparse_or_at_threshold_evidence_preserves_authored_order(int count, int correct)
    {
        var s = await Setup(); using var learner = s.Client;
        await Seed(s, count, correct); var plan = await Dashboard(learner);
        var task = Assert.Single(plan.GetProperty("tasks").EnumerateArray());
        Assert.Equal(s.Strong, task.GetProperty("contentVersionId").GetGuid()); Assert.Equal("PREREQUISITE_READY", task.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task Weak_evidence_prioritizes_only_due_unlocked_content_and_snapshots_its_reason()
    {
        var s = await Setup(); using var learner = s.Client;
        await Seed(s, 5, 2); var before = await Dashboard(learner); var task = Assert.Single(before.GetProperty("tasks").EnumerateArray());
        Assert.Equal(s.Weak, task.GetProperty("contentVersionId").GetGuid()); Assert.Equal("WEAK_SKILL", task.GetProperty("reasonCode").GetString()); Assert.Contains("2/5", task.GetProperty("reasonText").GetString());
        Assert.DoesNotContain(before.GetProperty("tasks").EnumerateArray(), x => s.Blocked.Contains(x.GetProperty("contentVersionId").GetGuid()));
        await Seed(s, 5, 5, at: app.Clock.UtcNow.AddSeconds(5)); var fresh = await Dashboard(learner);
        // VI: Refresh không sửa lý do lịch sử hay thay task; chỉ lệnh regenerate mới cân nhắc mẫu mới. EN: Refresh preserves the assignment snapshot; only explicit regeneration reselects work.
        Assert.Equal(task.GetRawText(), Assert.Single(fresh.GetProperty("tasks").EnumerateArray()).GetRawText()); Assert.NotEqual(before.GetProperty("planEtag").GetString(), fresh.GetProperty("planEtag").GetString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before))).StatusCode);
        var changed = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(fresh)); changed.EnsureSuccessStatusCode(); Assert.True((await changed.Content.ReadFromJsonAsync<RegenerationAck>())!.Changed);
        Assert.Equal(s.Strong, Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray()).GetProperty("contentVersionId").GetGuid());
    }

    [Fact]
    public async Task Full_mock_review_and_technical_evidence_cannot_trigger_practice_priorities()
    {
        var s = await Setup(); using var learner = s.Client;
        await Seed(s, 5, 0, "full_mock"); await Seed(s, 5, 0, "mistake_review");
        var technical = await Content("reading", ContentKind.Assessment, 5, fixture: true); await Seed(s with { Bank = technical }, 5, 0, "fixture");
        var task = Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray()); Assert.Equal(s.Strong, task.GetProperty("contentVersionId").GetGuid());
        var evidence = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); Assert.Contains(evidence.GetProperty("groups").EnumerateArray(), x => x.GetProperty("track").GetString() == "full_mock_unseen");
        Assert.DoesNotContain(evidence.GetProperty("groups").EnumerateArray(), x => x.GetProperty("track").GetString() == "practice");
    }

    [Fact]
    public async Task Practice_selection_respects_weak_priority_and_remaining_budget()
    {
        var s = await Setup(45); using var learner = s.Client; await Seed(s, 5, 0);
        var tasks = (await Dashboard(learner)).GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Equal([s.Weak, s.WeakPractice], tasks.Select(x => x.GetProperty("contentVersionId").GetGuid()));
        Assert.Equal(40, tasks.Sum(x => x.GetProperty("estimatedMinutes").GetInt32())); Assert.All(tasks, x => Assert.Equal("WEAK_SKILL", x.GetProperty("reasonCode").GetString()));
        Assert.Equal(tasks.Length, tasks.Select(x => x.GetProperty("contentVersionId").GetGuid()).Distinct().Count());
    }

    [Fact]
    public async Task New_evidence_cannot_replace_started_work_or_rewrite_a_replayed_ack()
    {
        var s = await Setup(); using var learner = s.Client; var before = await Dashboard(learner); var task = Assert.Single(before.GetProperty("tasks").EnumerateArray());
        (await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{task.GetProperty("id").GetGuid()}/start", new { })).EnsureSuccessStatusCode();
        await Seed(s, 5, 0); var fresh = await Dashboard(learner); var command = Command(fresh); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<RegenerationAck>())!;
        Assert.False(ack.Changed); Assert.Equal([task.GetProperty("id").GetGuid()], ack.RetainedTaskIds); Assert.Empty(ack.NewTaskIds);
        await Seed(s, 5, 5, at: app.Clock.UtcNow.AddSeconds(5)); var retry = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); retry.EnsureSuccessStatusCode(); Assert.Equal(await response.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.TaskCompletions.AnyAsync(x => x.DailyTask.UserId == s.User)); Assert.Equal(1, await db.DailyTasks.CountAsync(x => x.UserId == s.User));
    }

    [Fact]
    public async Task Grading_an_unassigned_attempt_invalidates_the_loaded_plan_and_serializes_with_regeneration()
    {
        var s = await Setup(); using var learner = s.Client;
        var start = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(s.Bank, Guid.NewGuid(), "practice")); start.EnsureSuccessStatusCode(); var attempt = (await start.Content.ReadFromJsonAsync<AttemptView>())!;
        var before = await Dashboard(learner); var results = await Task.WhenAll(
            learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), [])),
            learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before)));
        results[0].EnsureSuccessStatusCode(); Assert.True(results[1].IsSuccessStatusCode || results[1].StatusCode == HttpStatusCode.PreconditionFailed);
        if (results[1].IsSuccessStatusCode) Assert.False((await results[1].Content.ReadFromJsonAsync<RegenerationAck>())!.Changed);
        var after = await Dashboard(learner); Assert.NotEqual(before.GetProperty("planEtag").GetString(), after.GetProperty("planEtag").GetString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before))).StatusCode);
        var fresh = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(after)); fresh.EnsureSuccessStatusCode(); Assert.True((await fresh.Content.ReadFromJsonAsync<RegenerationAck>())!.Changed);
        Assert.Equal(s.Weak, Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray()).GetProperty("contentVersionId").GetGuid());
    }

    [Fact]
    public async Task Legacy_selection_uses_the_same_practice_evidence_and_owner_boundary()
    {
        var s = await Setup(legacy: true); using var learner = s.Client; await Seed(s, 5, 0);
        var task = Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray()); Assert.Equal("WEAK_SKILL", task.GetProperty("reasonCode").GetString());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal("reading", (await db.ContentVersions.SingleAsync(x => x.Id == task.GetProperty("contentVersionId").GetGuid())).Skill);
        using var outsider = await app.Login("learner2@englishhub.local"); var other = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); Assert.Empty(other.GetProperty("groups").EnumerateArray());
    }

    private sealed record Scenario(HttpClient Client, Guid User, Guid Strong, Guid Weak, Guid WeakPractice, Guid Bank, Guid[] Blocked);
    private async Task<Scenario> Setup(int budget = 20, bool legacy = false)
    {
        var strong = await Content("grammar", ContentKind.Lesson); var weak = await Content("reading", ContentKind.Lesson); var future = await Content("reading", ContentKind.Lesson); var locked = await Content("reading", ContentKind.Lesson);
        var strongPractice = await Content("grammar", ContentKind.Assessment); var weakPractice = await Content("reading", ContentKind.Assessment); var bank = await Content("reading", ContentKind.Assessment, 5);
        Guid userId; string email = $"reinforcement-{Guid.NewGuid():N}@example.com";
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Reinforcement test", MinutesPerDay = budget, StudyDays = "0,1,2,3,4,5,6" }; userId = user.Id;
            Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded);
            if (legacy) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Enrollments.Add(new Enrollment { UserId = userId, TargetScore = 650, MinutesPerDay = budget, CreatedAt = app.Clock.UtcNow }); await db.SaveChangesAsync(); }
        }
        var learner = await app.Login(email);
        if (!legacy)
        {
            using var admin = await app.Login("admin@englishhub.local");
            var path = new PathInput("Reinforcement test", "Original eligible scheduling scenario", "EnglishHub original · CC0", [new("stage", "Start", [new("strong", strong, 1, "lesson_quiz", 100, []), new("weak", weak, 1, "lesson_quiz", 100, []), new("future", future, 365, "lesson_quiz", 100, []), new("locked", locked, 1, "lesson_quiz", 100, ["strong"]), new("practice-strong", strongPractice, 1, "assessment_raw", 50, []), new("practice-weak", weakPractice, 1, "assessment_raw", 50, [])])]);
            var response = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"reinforcement-{Guid.NewGuid():N}", "foundation", path)); response.EnsureSuccessStatusCode(); var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
            using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.GetProperty("id").GetGuid()}/publish"); publish.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString()); (await admin.SendAsync(publish)).EnsureSuccessStatusCode();
            (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: draft.GetProperty("id").GetGuid()))).EnsureSuccessStatusCode();
        }
        return new(learner, userId, strong, weak, weakPractice, bank, [future, locked]);
    }

    private async Task<Guid> Content(string skill, ContentKind kind, int count = 1, bool fixture = false)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var version = new ContentVersion { Title = $"Reinforcement {skill}", Skill = skill, Status = PublicationStatus.Published, IsFixture = fixture, Attribution = "Original test content · CC0", Body = "Original test lesson", Minutes = 20 };
        for (var i = 0; i < count; i++) version.Groups.Add(new QuestionGroup { Order = i, Questions = [new Question { Prompt = $"Original question {i}", Skill = skill, Explanation = "Original explanation", Options = [new() { Text = "Correct", IsCorrect = true }, new() { Text = "Other" }, new() { Text = "Third" }, new() { Text = "Fourth" }] }] });
        var root = new ContentRoot { Slug = $"reinforcement-{Guid.NewGuid():N}", Kind = kind, Versions = [version] }; db.ContentRoots.Add(root); await db.SaveChangesAsync(); root.CurrentPublishedVersionId = version.Id; await db.SaveChangesAsync(); return version.Id;
    }

    private async Task Seed(Scenario s, int count, int correct, string mode = "practice", DateTimeOffset? at = null)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var questions = await db.Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == s.Bank).OrderBy(x => x.Group.Order).Take(count).ToArrayAsync(); var now = at ?? app.Clock.UtcNow;
        var attempt = new Attempt { UserId = s.User, ContentVersionId = s.Bank, Mode = mode, StartedAt = now, DeadlineAt = now.AddHours(1), Status = AttemptStatus.Graded, EvidencePolicyVersion = 1 };
        foreach (var (question, index) in questions.Select((q, i) => (q, i))) { var key = question.Options.Single(x => x.IsCorrect).Id; attempt.Items.Add(new AttemptItem { QuestionId = question.Id, Order = index, SelectedOptionId = index < correct ? key : null, GradingKey = new GradingKey { Skill = question.Skill, Part = 5, CorrectOptionId = key } }); }
        attempt.Result = new AttemptResult { AttemptId = attempt.Id, TotalCount = questions.Length, CorrectCount = correct, BlankCount = questions.Length - correct, FinalizedAt = now }; db.Attempts.Add(attempt); await db.SaveChangesAsync();
    }
    private static Task<JsonElement> Dashboard(HttpClient learner) => learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static RegeneratePlanInput Command(JsonElement plan) => new(Guid.NewGuid(), DateOnly.Parse(plan.GetProperty("localDate").GetString()!), plan.GetProperty("planEtag").GetString()!);
}
