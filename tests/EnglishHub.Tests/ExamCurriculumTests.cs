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

/// <summary>VI: Kiểm chứng chương trình đọc/ngữ pháp nguyên gốc và bằng chứng đủ nhóm, không mô phỏng kết quả học thật. EN: Exercise authored reading/grammar and whole-bank evidence only in isolated relational fixtures.</summary>
public sealed class ExamCurriculumTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Published_exam_has_six_lessons_and_whole_context_and_document_groups_without_exposing_keys()
    {
        using var learner = await Learner();
        var templates = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates");
        var exam = templates.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == DemoExamSeed.TemplateSlug);
        Assert.Equal("exam_practice", exam.GetProperty("level").GetString()); Assert.Equal(8, exam.GetProperty("nodeCount").GetInt32()); Assert.Equal(6, exam.GetProperty("studyDays").GetInt32());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var versions = await db.ContentVersions.Include(x => x.Root).Include(x => x.Groups).ThenInclude(x => x.Questions).ThenInclude(x => x.Options).Where(x => x.Attribution == DemoExamSeed.Attribution).ToArrayAsync();
        Assert.Equal(8, versions.Length); Assert.Equal(6, versions.Count(x => x.Root.Kind == ContentKind.Lesson));
        Assert.All(versions, v => { ContentService.Validate(v); Assert.False(v.IsFixture); Assert.Equal(v.Id, v.Root.CurrentPublishedVersionId); Assert.Equal(1, v.Version); Assert.Equal(600, v.DurationSeconds); });
        var questions = versions.SelectMany(v => v.Groups).SelectMany(g => g.Questions).ToArray(); Assert.Equal(25, questions.Length); Assert.Equal(25, questions.Select(q => q.Prompt).Distinct().Count());
        Assert.All(questions, q => Assert.Equal(4, q.Options.Select(o => o.Text).Distinct().Count()));
        var context = versions.Single(x => x.Root.Slug == "exam-practice-context"); Assert.Equal(2, context.Groups.Count);
        Assert.All(context.Groups, g => { Assert.Equal(6, g.Part); Assert.Equal(4, g.Questions.Count); });
        Assert.Equal(6, context.Groups.SelectMany(g => g.Questions).Count(q => q.Skill == "grammar")); Assert.Equal(2, context.Groups.SelectMany(g => g.Questions).Count(q => q.Skill == "reading"));
        var documents = Assert.Single(versions.Single(x => x.Root.Slug == "exam-practice-documents").Groups); Assert.Equal(7, documents.Part); Assert.Equal(5, documents.Questions.Count); Assert.Contains("Document 3", documents.Stimulus);
        // VI: Thêm Exam không thay nội dung Foundation/Core đã ghim. EN: A new catalog level must not redirect existing published graphs to its content.
        foreach (var (slug, count) in new[] { ("foundation-fourteen-days", 18), (DemoCoreSeed.TemplateSlug, 10) })
        {
            var old = await db.PathNodes.Where(n => n.Stage.PathVersion.Template.Slug == slug).Select(n => n.ContentVersion.Root.Slug).ToArrayAsync();
            Assert.Equal(count, old.Length); Assert.DoesNotContain(old, x => x.StartsWith("exam-"));
        }
        foreach (var lesson in versions.Where(x => x.Root.Kind == ContentKind.Lesson))
        {
            Assert.Equal(2, lesson.Groups.Sum(g => g.Questions.Count)); var safe = await learner.GetStringAsync($"/api/v1/lessons/{lesson.Id}");
            Assert.DoesNotContain("isCorrect", safe); Assert.DoesNotContain("correctOptionId", safe); Assert.DoesNotContain("explanation", safe);
        }
    }

    [Fact]
    public async Task Shared_publisher_reseeds_without_rewriting_published_payloads_retirement_or_old_learner_pins()
    {
        using var foundation = await Learner(); using var core = await Learner();
        await Enroll(foundation, "foundation-fourteen-days"); await Enroll(core, DemoCoreSeed.TemplateSlug);
        var foundationPlan = (await Plan(foundation)).GetRawText(); var corePlan = (await Plan(core)).GetRawText();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var exam = await db.PathTemplates.SingleAsync(x => x.Slug == DemoExamSeed.TemplateSlug);
        exam.RetiredAt = app.Clock.UtcNow; await db.SaveChangesAsync(); var token = exam.RowVersion.ToArray(); var before = await Payloads(db);
        try
        {
            await DemoCoreSeed.Ensure(db); await DemoExamSeed.Ensure(db); await DemoExamSeed.Ensure(db);
            Assert.NotNull(exam.RetiredAt); Assert.Equal(token, exam.RowVersion); Assert.Equal(before, await Payloads(db));
            Assert.Equal(foundationPlan, (await Plan(foundation)).GetRawText()); Assert.Equal(corePlan, (await Plan(core)).GetRawText());
        }
        finally { exam.RetiredAt = null; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Reserved_slug_collision_fails_atomically_instead_of_adopting_existing_content()
    {
        using var learner = await Learner(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var exam = await db.PathTemplates.SingleAsync(x => x.Slug == DemoExamSeed.TemplateSlug); var pointer = exam.CurrentPublishedVersionId; var payloads = await Payloads(db); var roots = await db.ContentRoots.CountAsync(); var paths = await db.PathVersions.CountAsync();
        // VI: Tạo tình huống hỏng catalog trong DB test riêng, luôn khôi phục sau kiểm tra. EN: Simulate a missing template only inside this fixture and restore its identity afterwards.
        exam.Slug = $"exam-collision-{Guid.NewGuid():N}"; await db.SaveChangesAsync();
        try
        {
            var error = await Assert.ThrowsAsync<DomainError>(() => DemoExamSeed.Ensure(db)); Assert.Equal(409, error.Status); Assert.Equal("curriculum_seed_collision", error.Code);
            Assert.Equal(roots, await db.ContentRoots.CountAsync()); Assert.Equal(paths, await db.PathVersions.CountAsync()); Assert.Equal(pointer, exam.CurrentPublishedVersionId); Assert.Equal(payloads, await Payloads(db));
            Assert.False(await db.PathTemplates.AnyAsync(x => x.Slug == DemoExamSeed.TemplateSlug));
        }
        finally { exam.Slug = DemoExamSeed.TemplateSlug; await db.SaveChangesAsync(); }
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    public async Task Complete_placement_at_both_exam_band_boundaries_pins_the_new_exam_path(int correct)
    {
        using var learner = await Learner(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var version = await db.ContentVersions.Where(x => x.Root.Slug == "placement-demo" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var attempt = await Start(learner, version, "placement"); Assert.Equal(20, attempt.Items.Length); await Grade(learner, attempt, correct);
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, attempt.Id))).EnsureSuccessStatusCode();
        var plan = await Plan(learner); Assert.Equal(DemoExamSeed.TemplateSlug, plan.GetProperty("template").GetProperty("slug").GetString()); Assert.Equal("exam_practice", plan.GetProperty("template").GetProperty("level").GetString());
        Assert.Equal(8, plan.GetProperty("nodes").GetArrayLength()); var task = Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray()); Assert.Equal("lesson", task.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Six_day_journey_requires_five_of_eight_context_answers_and_three_of_five_document_answers()
    {
        using var learner = await Learner(); await Enroll(learner, DemoExamSeed.TemplateSlug);
        for (var day = 1; day <= 6; day++)
        {
            var node = (await Plan(learner)).GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("key").GetString() == $"lesson-{day:00}");
            Assert.True(node.GetProperty("unlocked").GetBoolean()); Assert.True(node.GetProperty("due").GetBoolean()); Assert.Single((await Dashboard(learner)).GetProperty("tasks").EnumerateArray());
            await CompleteLesson(learner, node.GetProperty("contentVersionId").GetGuid());
            if (day is 2 or 6)
            {
                var current = await Dashboard(learner);
                var command = new RegeneratePlanInput(Guid.NewGuid(), DateOnly.Parse(current.GetProperty("localDate").GetString()!), current.GetProperty("planEtag").GetString()!);
                (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command)).EnsureSuccessStatusCode();
                var task = (await Dashboard(learner)).GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "assessment"); var version = task.GetProperty("contentVersionId").GetGuid();
                var failed = await Start(learner, version); Assert.Equal(day == 2 ? 8 : 5, failed.Items.Length); await Grade(learner, failed, day == 2 ? 4 : 2);
                var plan = await Plan(learner); Assert.False(plan.GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("contentVersionId").GetGuid() == version).GetProperty("completed").GetBoolean());
                if (day == 2) Assert.False(plan.GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "lesson-03").GetProperty("unlocked").GetBoolean());
                var passed = await Start(learner, version); await Grade(learner, passed, day == 2 ? 5 : 3);
                Assert.True((await Plan(learner)).GetProperty("nodes").EnumerateArray().Single(x => x.GetProperty("contentVersionId").GetGuid() == version).GetProperty("completed").GetBoolean());
            }
            if (day < 6) app.Clock.Advance(TimeSpan.FromDays(1));
        }
        Assert.All((await Plan(learner)).GetProperty("nodes").EnumerateArray(), x => Assert.True(x.GetProperty("completed").GetBoolean()));
        var user = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid(); using var check = app.Services.CreateScope(); var context = check.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Equal(8, await context.TaskCompletions.CountAsync(x => x.DailyTask.UserId == user)); Assert.Equal(6, await context.LessonProgress.CountAsync(x => x.UserId == user));
        var sources = await context.TaskCompletions.Where(x => x.DailyTask.UserId == user && x.AttemptId != null).Select(x => x.AttemptId).ToArrayAsync(); Assert.Equal(2, sources.Length); Assert.Equal(2, sources.Distinct().Count());
    }

    [Fact]
    public async Task A_perfect_context_group_is_not_enough_to_complete_the_eight_question_checkpoint()
    {
        using var learner = await Learner(); var version = await IsolatedContextCheckpoint(learner);
        var preview = await learner.PostAsJsonAsync("/api/v1/attempts/preview", new StartAttempt(version, Guid.NewGuid(), "practice", QuestionCount: 1)); preview.EnsureSuccessStatusCode();
        Assert.Equal(4, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actualQuestionCount").GetInt32());
        var partial = await Start(learner, version, "practice", 1); Assert.Equal(4, partial.Items.Length); Assert.Single(partial.Items.Select(x => x.Stimulus).Distinct()); await Grade(learner, partial, 4);
        Assert.False(Assert.Single((await Plan(learner)).GetProperty("nodes").EnumerateArray()).GetProperty("completed").GetBoolean());
        var full = await Start(learner, version, "practice"); Assert.Equal(8, full.Items.Length); await Grade(learner, full, 5);
        Assert.True(Assert.Single((await Plan(learner)).GetProperty("nodes").EnumerateArray()).GetProperty("completed").GetBoolean());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.TaskCompletions.AnyAsync(x => x.AttemptId == partial.Id)); Assert.True(await db.TaskCompletions.AnyAsync(x => x.AttemptId == full.Id));
    }

    [Fact]
    public async Task Triple_document_preview_and_snapshot_keep_all_five_questions_with_the_same_stimulus()
    {
        using var learner = await Learner(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var version = await db.ContentVersions.Where(x => x.Root.Slug == "exam-practice-documents" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var preview = await learner.PostAsJsonAsync("/api/v1/attempts/preview", new StartAttempt(version, Guid.NewGuid(), "practice", QuestionCount: 1)); preview.EnsureSuccessStatusCode();
        Assert.Equal(5, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actualQuestionCount").GetInt32());
        var attempt = await Start(learner, version, "practice", 1); Assert.Equal(5, attempt.Items.Length); var stimulus = Assert.Single(attempt.Items.Select(x => x.Stimulus).Distinct());
        Assert.Contains("Document 1", stimulus); Assert.Contains("Document 2", stimulus); Assert.Contains("Document 3", stimulus);
        var safe = await learner.GetStringAsync($"/api/v1/attempts/{attempt.Id}"); Assert.DoesNotContain("correctOptionId", safe); Assert.DoesNotContain("isCorrect", safe); Assert.DoesNotContain("explanation", safe);
        Assert.All(await db.AttemptItems.Where(x => x.AttemptId == attempt.Id).Select(x => x.GradingKey.Part).ToArrayAsync(), part => Assert.Equal(7, part));
    }

    private async Task<Guid> IsolatedContextCheckpoint(HttpClient learner)
    {
        using var admin = await app.Login("admin@englishhub.local"); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var version = await db.ContentVersions.Where(x => x.Root.Slug == "exam-practice-context" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var input = new PathInput("Exam context checkpoint test", "Keep complete evidence and groups", "Original test · CC0", [new("context", "Context", [new("checkpoint", version, 1, "assessment_all_raw", 60, [])])]);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/paths", new CreatePathInput($"exam-context-{Guid.NewGuid():N}", "exam_practice", input)); created.EnsureSuccessStatusCode(); var draft = await created.Content.ReadFromJsonAsync<JsonElement>();
        using var publish = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/path-versions/{draft.GetProperty("id").GetGuid()}/publish"); publish.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString()); (await admin.SendAsync(publish)).EnsureSuccessStatusCode();
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: draft.GetProperty("id").GetGuid()))).EnsureSuccessStatusCode(); await Dashboard(learner);
        // VI: Mẫu test không tranh vị trí mặc định với curriculum ở ca admission khác; người đã ghim vẫn học được. EN: Keep this synthetic path out of later default admissions while preserving its enrolled learner's pin.
        var pathId = draft.GetProperty("id").GetGuid(); var template = await db.PathTemplates.SingleAsync(x => x.CurrentPublishedVersionId == pathId); template.RetiredAt = app.Clock.UtcNow; await db.SaveChangesAsync(); return version;
    }

    private static async Task<string> Payloads(HubDbContext db)
    {
        var versions = await db.ContentVersions.AsNoTracking().Include(v => v.Root).Include(v => v.Groups).ThenInclude(g => g.Questions).ThenInclude(q => q.Options).OrderBy(v => v.Id).ToArrayAsync();
        return JsonSerializer.Serialize(versions.Select(v => new { v.Id, v.RowVersion, v.Version, v.Status, v.Title, v.Body, v.Summary, v.PublishedAt, v.Attribution, v.Root.CurrentPublishedVersionId, Groups = v.Groups.OrderBy(g => g.Order).Select(g => new { g.Id, g.Part, g.Stimulus, Questions = g.Questions.OrderBy(q => q.Order).Select(q => new { q.Id, q.Prompt, q.Skill, q.Explanation, Options = q.Options.OrderBy(o => o.Id).Select(o => new { o.Id, o.Text, o.IsCorrect }) }) }) }));
    }
    private async Task<HttpClient> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"exam-{Guid.NewGuid():N}@example.com";
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Exam curriculum test", StudyDays = "0,1,2,3,4,5,6", MinutesPerDay = 30 };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private static Task<JsonElement> Dashboard(HttpClient learner) => learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static Task<JsonElement> Plan(HttpClient learner) => learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan");
    private static async Task Enroll(HttpClient learner, string slug)
    {
        var templates = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/templates"); var id = templates.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == slug).GetProperty("id").GetGuid();
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true, PathVersionId: id))).EnsureSuccessStatusCode();
    }
    private static async Task<AttemptView> Start(HttpClient learner, Guid version, string mode = "mini", int? count = null)
    {
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version, Guid.NewGuid(), mode, QuestionCount: count)); started.EnsureSuccessStatusCode(); return (await started.Content.ReadFromJsonAsync<AttemptView>())!;
    }
    private async Task Grade(HttpClient learner, AttemptView attempt, int correct)
    {
        // VI: Khóa chỉ đọc trong fixture qua projection, không xuất qua API. EN: Read private keys through a test-only projection, never through the learner response.
        using var scope = app.Services.CreateScope(); var keys = await scope.ServiceProvider.GetRequiredService<HubDbContext>().AttemptItems.Where(x => x.AttemptId == attempt.Id).Select(x => new { x.Id, x.GradingKey.CorrectOptionId }).ToDictionaryAsync(x => x.Id, x => x.CorrectOptionId);
        var answers = attempt.Items.Select((item, index) => new AnswerChange(item.Id, index < correct ? keys[item.Id] : null, item.Etag)).ToArray();
        (await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
    private async Task CompleteLesson(HttpClient learner, Guid version)
    {
        using var scope = app.Services.CreateScope(); var questions = await scope.ServiceProvider.GetRequiredService<HubDbContext>().Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == version).ToArrayAsync();
        (await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", new CompleteLessonInput(Guid.NewGuid(), questions.Select(q => new LessonAnswer(q.Id, q.Options.Single(o => o.IsCorrect).Id)).ToArray()))).EnsureSuccessStatusCode();
    }
}
