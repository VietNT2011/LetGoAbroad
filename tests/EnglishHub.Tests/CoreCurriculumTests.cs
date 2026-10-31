using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Core phải có nội dung thật, ghim bất biến và checkpoint không bị vượt bằng bài luyện rút gọn. EN: Verify authored content, preserved pins and full-coverage checkpoint evidence on relational databases.</summary>
public sealed class CoreCurriculumTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Authored_core_has_distinct_valid_questions_and_an_eight_day_pinned_graph()
    {
        using var learner = await Learner(); var templates = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates");
        var core = templates.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == DemoCoreSeed.TemplateSlug);
        Assert.Equal("core", core.GetProperty("level").GetString()); Assert.Equal(10, core.GetProperty("nodeCount").GetInt32()); Assert.Equal(8, core.GetProperty("studyDays").GetInt32());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var versions = await db.ContentVersions.Include(x => x.Root).Include(x => x.Groups).ThenInclude(x => x.Questions).ThenInclude(x => x.Options).Where(x => x.Attribution == DemoCoreSeed.Attribution).ToArrayAsync();
        Assert.Equal(10, versions.Length); Assert.Equal(8, versions.Count(x => x.Root.Kind == ContentKind.Lesson)); Assert.Equal(28, versions.Sum(x => x.Groups.Sum(g => g.Questions.Count)));
        Assert.All(versions, v => { ContentService.Validate(v); Assert.False(v.IsFixture); Assert.Equal(v.Id, v.Root.CurrentPublishedVersionId); Assert.Equal(1, v.Version); });
        var questions = versions.SelectMany(x => x.Groups).SelectMany(x => x.Questions).ToArray(); Assert.Equal(28, questions.Select(x => x.Prompt).Distinct().Count());
        Assert.All(questions, q => Assert.Equal(4, q.Options.Select(x => x.Text).Distinct().Count()));
        Assert.All(versions.Where(x => x.Root.Kind == ContentKind.Lesson), v => Assert.Equal(2, v.Groups.Sum(g => g.Questions.Count)));
        var foundationNodes = await db.PathNodes.Where(x => x.Stage.PathVersion.Template.Slug == "foundation-fourteen-days").Select(x => x.ContentVersion.Root.Slug).ToArrayAsync();
        Assert.Equal(18, foundationNodes.Length); Assert.DoesNotContain(foundationNodes, x => x.StartsWith("core-"));
        foreach (var lesson in versions.Where(x => x.Root.Kind == ContentKind.Lesson))
        {
            var safe = await learner.GetStringAsync($"/api/v1/lessons/{lesson.Id}"); Assert.DoesNotContain("isCorrect", safe); Assert.DoesNotContain("correctOptionId", safe);
        }
    }

    [Fact]
    public async Task Reseeding_retains_cms_retirement_payloads_and_an_existing_foundation_pin()
    {
        using var learner = await Learner(); await Enroll(learner, "foundation-fourteen-days"); var before = await Plan(learner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var core = await db.PathTemplates.SingleAsync(x => x.Slug == DemoCoreSeed.TemplateSlug);
        core.RetiredAt = app.Clock.UtcNow; await db.SaveChangesAsync();
        var versions = await db.ContentVersions.AsNoTracking().Where(x => x.Attribution == DemoCoreSeed.Attribution).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.Body, x.PublishedAt }).ToArrayAsync(); var rootToken = core.RowVersion.ToArray();
        try
        {
            await DemoCoreSeed.Ensure(db); await DemoCoreSeed.Ensure(db);
            Assert.NotNull(core.RetiredAt); Assert.Equal(rootToken, core.RowVersion);
            var after = await db.ContentVersions.AsNoTracking().Where(x => x.Attribution == DemoCoreSeed.Attribution).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.Body, x.PublishedAt }).ToArrayAsync();
            Assert.Equal(JsonSerializer.Serialize(versions), JsonSerializer.Serialize(after)); Assert.Equal(before.GetRawText(), (await Plan(learner)).GetRawText());
        }
        finally { core.RetiredAt = null; await db.SaveChangesAsync(); }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(14)]
    public async Task Core_band_placement_automatically_pins_the_core_template(int correct)
    {
        using var learner = await Learner(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var placement = await db.ContentVersions.Where(x => x.Root.Slug == "placement-demo" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var attempt = await Start(learner, placement, "placement"); await Grade(learner, attempt, correct);
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, attempt.Id))).EnsureSuccessStatusCode();
        var plan = await Plan(learner); Assert.Equal(DemoCoreSeed.TemplateSlug, plan.GetProperty("template").GetProperty("slug").GetString()); Assert.Equal("core", plan.GetProperty("template").GetProperty("level").GetString());
        var dashboard = await Dashboard(learner); Assert.Single(dashboard.GetProperty("tasks").EnumerateArray()); Assert.All(dashboard.GetProperty("tasks").EnumerateArray(), x => Assert.Equal("lesson", x.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task All_eight_lessons_and_two_checkpoints_complete_only_with_new_sufficient_evidence()
    {
        using var learner = await Learner(); await Enroll(learner, DemoCoreSeed.TemplateSlug);
        for (var day = 1; day <= 8; day++)
        {
            var plan = await Plan(learner); var node = plan.GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("key").GetString() == $"lesson-{day:00}");
            Assert.True(node.GetProperty("unlocked").GetBoolean()); Assert.True(node.GetProperty("due").GetBoolean());
            var dashboard = await Dashboard(learner); Assert.Single(dashboard.GetProperty("tasks").EnumerateArray());
            await CompleteLesson(learner, node.GetProperty("contentVersionId").GetGuid());
            if (day is 6 or 8)
            {
                var current = await Dashboard(learner); var command = new RegeneratePlanInput(Guid.NewGuid(), DateOnly.Parse(current.GetProperty("localDate").GetString()!), current.GetProperty("planEtag").GetString()!);
                (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command)).EnsureSuccessStatusCode();
                var practice = (await Dashboard(learner)).GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "assessment");
                var version = practice.GetProperty("contentVersionId").GetGuid();
                await Grade(learner, await Start(learner, version), 3);
                var failed = await Plan(learner); Assert.False(failed.GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("contentVersionId").GetGuid() == version).GetProperty("completed").GetBoolean());
                if (day == 6) Assert.False(failed.GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "lesson-07").GetProperty("unlocked").GetBoolean());
                var passed = await Start(learner, version); await Grade(learner, passed, 4);
                Assert.True((await Plan(learner)).GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("contentVersionId").GetGuid() == version).GetProperty("completed").GetBoolean());
            }
            if (day < 8) app.Clock.Advance(TimeSpan.FromDays(1));
        }
        Assert.All((await Plan(learner)).GetProperty("nodes").EnumerateArray(), x => Assert.True(x.GetProperty("completed").GetBoolean()));
        var user = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
        using var check = app.Services.CreateScope(); var context = check.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(10, await context.TaskCompletions.CountAsync(x => x.DailyTask.UserId == user)); Assert.Equal(8, await context.LessonProgress.CountAsync(x => x.UserId == user));
        var ids = await context.TaskCompletions.Where(x => x.DailyTask.UserId == user && x.AttemptId != null).Select(x => x.AttemptId).ToArrayAsync(); Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public async Task Reading_whole_groups_remain_intact_but_a_partial_perfect_attempt_cannot_clear_the_full_checkpoint()
    {
        using var learner = await Learner(); var version = await Checkpoint(learner);
        var input = new StartAttempt(version, Guid.NewGuid(), "practice", QuestionCount: 1); var preview = await learner.PostAsJsonAsync("/api/v1/attempts/preview", input); preview.EnsureSuccessStatusCode(); Assert.Equal(3, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actualQuestionCount").GetInt32());
        var partial = await Start(learner, version, "practice", 1); Assert.Equal(3, partial.Items.Length); Assert.Single(partial.Items.Select(x => x.Stimulus).Distinct()); await Grade(learner, partial, 3);
        Assert.False(Assert.Single((await Plan(learner)).GetProperty("nodes").EnumerateArray()).GetProperty("completed").GetBoolean());
        var full = await Start(learner, version, "practice"); Assert.Equal(6, full.Items.Length); await Grade(learner, full, 4);
        Assert.True(Assert.Single((await Plan(learner)).GetProperty("nodes").EnumerateArray()).GetProperty("completed").GetBoolean());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.TaskCompletions.AnyAsync(x => x.AttemptId == partial.Id)); Assert.True(await db.TaskCompletions.AnyAsync(x => x.AttemptId == full.Id));
    }

    [Theory]
    [InlineData("fixture")]
    [InlineData("mistake_review")]
    public async Task Nonpractice_modes_do_not_clear_an_opted_in_checkpoint(string mode)
    {
        using var learner = await Learner(); var version = await Checkpoint(learner); await Grade(learner, await Start(learner, version, mode), 6);
        Assert.False(Assert.Single((await Plan(learner)).GetProperty("nodes").EnumerateArray()).GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task Cms_rejects_publication_of_the_full_assessment_rule_for_lesson_content()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var lesson = await db.ContentVersions.Where(x => x.Root.Slug == "core-present-perfect" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var input = new PathInput("Invalid completion kind", "A lesson cannot use assessment evidence", "Original test · CC0", [new("stage", "Start", [new("one", lesson, 1, "assessment_all_raw", 60, [])])]);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"bad-core-{Guid.NewGuid():N}", "core", input)); created.EnsureSuccessStatusCode(); var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/path-versions/{draft.GetProperty("id").GetGuid()}/validate", new { })).StatusCode);
        using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.GetProperty("id").GetGuid()}/publish"); publish.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.SendAsync(publish)).StatusCode);
    }

    private async Task<Guid> Checkpoint(HttpClient learner)
    {
        using var admin = await app.Login("admin@englishhub.local"); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var version = await db.ContentVersions.Where(x => x.Root.Slug == "core-practice-reading" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var input = new PathInput("Core checkpoint test", "Full coverage with preserved groups", "Original test · CC0", [new("one", "Practice", [new("checkpoint", version, 1, "assessment_all_raw", 60, [])])]);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"full-coverage-{Guid.NewGuid():N}", "core", input)); created.EnsureSuccessStatusCode(); var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.GetProperty("id").GetGuid()}/publish"); publish.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString()); (await admin.SendAsync(publish)).EnsureSuccessStatusCode();
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: draft.GetProperty("id").GetGuid()))).EnsureSuccessStatusCode(); await Dashboard(learner);
        // VI: Mẫu checkpoint riêng không trở thành lựa chọn mặc định cho test admission chạy sau. EN: Retire this synthetic catalog entry after pinning so admission tests do not depend on method order.
        var pathId = draft.GetProperty("id").GetGuid(); var template = await db.PathTemplates.SingleAsync(x => x.CurrentPublishedVersionId == pathId); template.RetiredAt = app.Clock.UtcNow; await db.SaveChangesAsync(); return version;
    }
    private async Task<HttpClient> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"core-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Core curriculum test", StudyDays = "0,1,2,3,4,5,6", MinutesPerDay = 30 };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private static Task<JsonElement> Dashboard(HttpClient learner) => learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static Task<JsonElement> Plan(HttpClient learner) => learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
    private static async Task Enroll(HttpClient learner, string slug)
    {
        var templates = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates"); var id = templates.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid(); (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: id))).EnsureSuccessStatusCode();
    }
    private static async Task<AttemptView> Start(HttpClient learner, Guid version, string mode = "mini", int? count = null)
    {
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version, Guid.NewGuid(), mode, QuestionCount: count)); started.EnsureSuccessStatusCode(); return (await started.Content.ReadFromJsonAsync<AttemptView>())!;
    }
    private async Task Grade(HttpClient learner, AttemptView attempt, int correct)
    {
        using var scope = app.Services.CreateScope(); var keys = await scope.ServiceProvider.GetRequiredService<HubDbContext>().AttemptItems.Where(x => x.AttemptId == attempt.Id).Select(x => new { x.Id, x.GradingKey.CorrectOptionId }).ToDictionaryAsync(x => x.Id, x => x.CorrectOptionId);
        var answers = attempt.Items.Select((item, index) => new AnswerChange(item.Id, index < correct ? keys[item.Id] : null, item.Etag)).ToArray();
        (await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
    private async Task CompleteLesson(HttpClient learner, Guid version)
    {
        using var scope = app.Services.CreateScope(); var questions = await scope.ServiceProvider.GetRequiredService<HubDbContext>().Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == version).ToArrayAsync();
        (await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", new CompleteLessonInput(Guid.NewGuid(), questions.Select(q => new LessonAnswer(q.Id, q.Options.Single(x => x.IsCorrect).Id)).ToArray()))).EnsureSuccessStatusCode();
    }
}
