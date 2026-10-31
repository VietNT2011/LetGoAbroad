using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: SQL window phải giữ mẫu thật, tách disclosure và không lộ grading key. EN: Exercise bounded samples, disclosure eligibility and safe public aggregates on relational providers.</summary>
public sealed class SkillEvidenceTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Latest_fifty_distinct_question_versions_replace_older_answers_without_inflating_sample_size()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("window", 60);
        await Seed(user, bank, bank.Questions[..10], 10, app.Clock.UtcNow.AddMinutes(-5));
        await Seed(user, bank, bank.Questions[10..], 25, app.Clock.UtcNow.AddMinutes(-4));
        await Seed(user, bank, bank.Questions[10..], 50, app.Clock.UtcNow.AddMinutes(-3));
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); var group = Group(data, "window");
        Assert.Equal(50, group.GetProperty("observedCount").GetInt32()); Assert.Equal(50, group.GetProperty("correctCount").GetInt32()); Assert.Equal(100, group.GetProperty("accuracyPercent").GetInt32());
        Assert.False(group.TryGetProperty("correctOptionId", out _)); Assert.False(group.TryGetProperty("answerText", out _));
    }

    [Fact]
    public async Task Sparse_samples_show_counts_and_no_percentage_while_fixtures_review_and_unfinished_attempts_are_excluded()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("sparse", 4);
        await Seed(user, bank, bank.Questions, 2, app.Clock.UtcNow);
        await Seed(user, bank, bank.Questions, 4, app.Clock.UtcNow.AddMinutes(1), "mistake_review");
        await Seed(user, bank, bank.Questions, 4, app.Clock.UtcNow.AddMinutes(2), "fixture");
        var fixture = await Bank("technical", 6, true); await Seed(user, fixture, fixture.Questions, 6, app.Clock.UtcNow);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Attempts.Add(new Attempt { UserId = user, ContentVersionId = bank.Version, StartedAt = app.Clock.UtcNow, DeadlineAt = app.Clock.UtcNow.AddHours(1) }); await db.SaveChangesAsync(); }
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); var group = Group(data, "sparse");
        Assert.Equal(4, group.GetProperty("observedCount").GetInt32()); Assert.Equal(2, group.GetProperty("correctCount").GetInt32()); Assert.Equal(2, group.GetProperty("blankCount").GetInt32()); Assert.Equal("insufficient_data", group.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, group.GetProperty("accuracyPercent").ValueKind);
        Assert.DoesNotContain(data.GetProperty("groups").EnumerateArray(), g => g.GetProperty("skill").GetString() == "technical");
    }

    [Fact]
    public async Task Fill_blank_uses_pinned_normalization_for_aggregate_correctness()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("typed", 5);
        var id = await Seed(user, bank, bank.Questions, 0, app.Clock.UtcNow);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var items = await db.AttemptItems.Include(x => x.GradingKey).Where(x => x.AttemptId == id).OrderBy(x => x.Order).ToArrayAsync();
            foreach (var (item, index) in items.Select((item, index) => (item, index))) { item.Type = QuestionType.FillBlank; item.AnswerText = index < 3 ? " I  AM ready. " : index == 3 ? "I redy" : " \t"; item.GradingKey.CorrectOptionId = null; item.GradingKey.AcceptedAnswerVariantsJson = "[\"I am ready\"]"; item.GradingKey.IgnoreTerminalPunctuation = true; }
            await db.SaveChangesAsync();
        }
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"), "typed"); Assert.Equal(3, group.GetProperty("correctCount").GetInt32()); Assert.Equal(1, group.GetProperty("blankCount").GetInt32()); Assert.Equal(60, group.GetProperty("accuracyPercent").GetInt32());
    }

    [Fact]
    public async Task Full_mock_track_excludes_questions_disclosed_before_start_and_conservative_legacy_history()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("unseen", 6); var now = app.Clock.UtcNow;
        await Seed(user, bank, bank.Questions[..2], 2, now.AddMinutes(-5), disclosed: now.AddMinutes(-4));
        await Seed(user, bank, bank.Questions[2..3], 1, now.AddMinutes(-3), policy: 0);
        await Seed(user, bank, bank.Questions, 6, now, "full_mock");
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); var full = Group(data, "unseen", "full_mock_unseen");
        Assert.Equal(3, full.GetProperty("observedCount").GetInt32()); Assert.Equal("insufficient_data", full.GetProperty("status").GetString()); Assert.Equal(3, Group(data, "unseen").GetProperty("observedCount").GetInt32());
        // VI: Mở lời giải sau lúc bắt đầu không đổi điều kiện lịch sử của lần làm đó. EN: Disclosure after the start does not retroactively change that attempt's starting condition.
        await Seed(user, bank, bank.Questions[3..], 3, now.AddMinutes(-2), disclosed: now.AddMinutes(1));
        var after = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); Assert.Equal(3, Group(after, "unseen", "full_mock_unseen").GetProperty("observedCount").GetInt32());
        var legacy = await Bank("legacy-full", 5); await Seed(user, legacy, legacy.Questions, 5, now, "full_mock", policy: 0);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence")).GetProperty("groups").EnumerateArray(), g => g.GetProperty("skill").GetString() == "legacy-full");
    }

    [Fact]
    public async Task First_result_disclosure_is_owner_scoped_and_saved_once()
    {
        using var owner = await app.Login(); using var outsider = await app.Login("learner2@englishhub.local"); var user = await User(owner); var bank = await Bank("disclosure", 1);
        var id = await Seed(user, bank, bank.Questions, 1, app.Clock.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/attempts/{id}/result")).StatusCode);
        using (var scope = app.Services.CreateScope()) Assert.Null((await scope.ServiceProvider.GetRequiredService<HubDbContext>().Attempts.SingleAsync(x => x.Id == id)).FirstDisclosedAt);
        (await owner.GetAsync($"/api/v1/attempts/{id}/result")).EnsureSuccessStatusCode(); var first = app.Clock.UtcNow; app.Clock.Advance(TimeSpan.FromMinutes(1)); (await owner.GetAsync($"/api/v1/attempts/{id}/result")).EnsureSuccessStatusCode();
        using (var scope = app.Services.CreateScope()) Assert.Equal(first, (await scope.ServiceProvider.GetRequiredService<HubDbContext>().Attempts.SingleAsync(x => x.Id == id)).FirstDisclosedAt);
        var other = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"); Assert.DoesNotContain(other.GetProperty("groups").EnumerateArray(), g => g.GetProperty("skill").GetString() == "disclosure");
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/skill-evidence")).StatusCode);
    }

    [Fact]
    public async Task Comparison_empty_reads_do_not_create_learning_sources_or_disclose_results()
    {
        using var owner = await app.Login("learner3@englishhub.local"); var before = await ReadEffects();
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison");
        Assert.Equal(1, data.GetProperty("policyVersion").GetInt32()); Assert.Equal(50, data.GetProperty("windowSize").GetInt32()); Assert.Equal(5, data.GetProperty("minimumObservations").GetInt32()); Assert.Empty(data.GetProperty("groups").EnumerateArray());
        await owner.GetAsync("/api/v1/learning/skill-comparison"); Assert.Equal(before, await ReadEffects());
    }

    [Fact]
    public async Task Comparison_caps_two_disjoint_samples_and_latest_repeats_replace_instead_of_adding_observations()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-bound", 110); var now = app.Clock.UtcNow;
        await Seed(user, bank, bank.Questions, 55, now);
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), "cmp-bound");
        var recent = group.GetProperty("recent"); var previous = group.GetProperty("previous");
        Assert.Equal(50, recent.GetProperty("observedCount").GetInt32()); Assert.Equal(0, recent.GetProperty("correctCount").GetInt32()); Assert.Equal(45, previous.GetProperty("correctCount").GetInt32()); Assert.Equal(-90, group.GetProperty("differencePercentagePoints").GetInt32());
        Assert.Equal(Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-evidence"), "cmp-bound").GetRawText(), recent.GetRawText());
        await Seed(user, bank, bank.Questions[..10], 10, now.AddMinutes(1));
        group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), "cmp-bound");
        Assert.Equal(10, group.GetProperty("recent").GetProperty("correctCount").GetInt32()); Assert.Equal(35, group.GetProperty("previous").GetProperty("correctCount").GetInt32()); Assert.Equal(50, group.GetProperty("previous").GetProperty("observedCount").GetInt32()); Assert.Equal(-50, group.GetProperty("differencePercentagePoints").GetInt32());
    }

    [Theory]
    [InlineData(54, false)]
    [InlineData(55, true)]
    public async Task Comparison_needs_five_observations_in_each_sample_before_showing_difference(int count, bool sufficient)
    {
        using var owner = await app.Login(); var bank = await Bank($"cmp-threshold-{count}", count); await Seed(await User(owner), bank, bank.Questions, count, app.Clock.UtcNow);
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), $"cmp-threshold-{count}"); var previous = group.GetProperty("previous");
        Assert.Equal(count - 50, previous.GetProperty("observedCount").GetInt32()); Assert.Equal(sufficient ? "observed" : "insufficient_data", previous.GetProperty("status").GetString());
        Assert.Equal(sufficient ? JsonValueKind.Number : JsonValueKind.Null, group.GetProperty("differencePercentagePoints").ValueKind);
        if (sufficient) Assert.Equal(0, group.GetProperty("differencePercentagePoints").GetInt32());
    }

    [Fact]
    public async Task Comparison_difference_subtracts_the_displayed_rounded_percentages()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-round", 58);
        await Seed(user, bank, bank.Questions[..8], 1, app.Clock.UtcNow.AddMinutes(-2)); await Seed(user, bank, bank.Questions[8..], 49, app.Clock.UtcNow);
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), "cmp-round");
        Assert.Equal(98, group.GetProperty("recent").GetProperty("accuracyPercent").GetInt32()); Assert.Equal(13, group.GetProperty("previous").GetProperty("accuracyPercent").GetInt32()); Assert.Equal(85, group.GetProperty("differencePercentagePoints").GetInt32());
    }

    [Fact]
    public async Task Comparison_uses_pinned_typed_normalization_blanks_and_parts_in_both_samples()
    {
        using var owner = await app.Login(); var bank = await Bank("cmp-typed", 55); var id = await Seed(await User(owner), bank, bank.Questions, 0, app.Clock.UtcNow);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var items = await db.AttemptItems.Include(x => x.GradingKey).Where(x => x.AttemptId == id).OrderBy(x => x.Order).ToArrayAsync();
            foreach (var (item, index) in items.Select((item, index) => (item, index)))
            {
                item.Type = QuestionType.FillBlank; item.GradingKey.CorrectOptionId = null; item.GradingKey.AcceptedAnswerVariantsJson = "[\"I am ready\"]"; item.GradingKey.IgnoreTerminalPunctuation = true;
                item.AnswerText = index < 3 || (index >= 5 && index < 30) ? " I  AM ready. " : index == 54 ? " \t" : "I redy"; item.GradingKey.Part = index >= 5 && index < 30 ? 2 : 5;
            }
            await db.SaveChangesAsync();
        }
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), "cmp-typed"); var recent = group.GetProperty("recent"); var previous = group.GetProperty("previous");
        Assert.Equal(25, recent.GetProperty("correctCount").GetInt32()); Assert.Equal(1, recent.GetProperty("blankCount").GetInt32()); Assert.Equal(3, previous.GetProperty("correctCount").GetInt32()); Assert.Equal(0, previous.GetProperty("blankCount").GetInt32()); Assert.Equal(-10, group.GetProperty("differencePercentagePoints").GetInt32());
        Assert.Equal(25, recent.GetProperty("parts").EnumerateArray().Single(x => x.GetProperty("part").GetInt32() == 2).GetProperty("correctCount").GetInt32());
        Assert.DoesNotContain("I am ready", group.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Comparison_full_mock_samples_share_disclosure_and_legacy_eligibility_without_mixing_practice()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-unseen", 110); var now = app.Clock.UtcNow;
        await Seed(user, bank, bank.Questions[..5], 5, now.AddMinutes(-5), disclosed: now.AddMinutes(-4)); await Seed(user, bank, bank.Questions[5..10], 5, now.AddMinutes(-3), policy: 0);
        await Seed(user, bank, bank.Questions, 110, now, "full_mock", disclosed: now.AddMinutes(1));
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"); var full = Group(data, "cmp-unseen", "full_mock_unseen");
        Assert.Equal(50, full.GetProperty("recent").GetProperty("observedCount").GetInt32()); Assert.Equal(50, full.GetProperty("previous").GetProperty("observedCount").GetInt32()); Assert.Equal(0, full.GetProperty("differencePercentagePoints").GetInt32()); Assert.Equal(10, Group(data, "cmp-unseen").GetProperty("recent").GetProperty("observedCount").GetInt32());
        var legacy = await Bank("cmp-old-full", 60); await Seed(user, legacy, legacy.Questions, 60, now, "full_mock", policy: 0);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison")).GetProperty("groups").EnumerateArray(), x => x.GetProperty("skill").GetString() == "cmp-old-full");
    }

    [Fact]
    public async Task Comparison_excludes_fixture_review_and_unfinished_sources_but_retains_archived_versions()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-modes", 60); var now = app.Clock.UtcNow;
        await Seed(user, bank, bank.Questions, 30, now.AddMinutes(-5)); await Seed(user, bank, bank.Questions, 60, now, "mistake_review"); await Seed(user, bank, bank.Questions, 60, now, "fixture");
        var unfinished = await Seed(user, bank, bank.Questions, 60, now); var technical = await Bank("cmp-fixture", 60, true); await Seed(user, technical, technical.Questions, 60, now);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); (await db.Attempts.SingleAsync(x => x.Id == unfinished)).Status = AttemptStatus.InProgress; (await db.ContentVersions.SingleAsync(x => x.Id == bank.Version)).Status = PublicationStatus.Archived; await db.SaveChangesAsync(); }
        var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"); var group = Group(data, "cmp-modes");
        Assert.Equal(20, group.GetProperty("recent").GetProperty("correctCount").GetInt32()); Assert.Equal(10, group.GetProperty("previous").GetProperty("correctCount").GetInt32()); Assert.Equal(-60, group.GetProperty("differencePercentagePoints").GetInt32()); Assert.DoesNotContain(data.GetProperty("groups").EnumerateArray(), x => x.GetProperty("skill").GetString() == "cmp-fixture");
    }

    [Fact]
    public async Task Comparison_equal_timestamps_use_attempt_id_then_item_order_deterministically()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-tie", 55); var now = app.Clock.UtcNow;
        await Seed(user, bank, bank.Questions, 0, now, id: Guid.Parse("00000000-0000-0000-0000-000000000001")); await Seed(user, bank, bank.Questions, 55, now, id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var group = Group(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/skill-comparison"), "cmp-tie");
        Assert.Equal(50, group.GetProperty("recent").GetProperty("correctCount").GetInt32()); Assert.Equal(5, group.GetProperty("previous").GetProperty("correctCount").GetInt32()); Assert.Equal(0, group.GetProperty("differencePercentagePoints").GetInt32());
    }

    [Fact]
    public async Task Comparison_is_owner_scoped_even_for_admin_and_exposes_only_safe_aggregates()
    {
        using var owner = await app.Login(); var user = await User(owner); var bank = await Bank("cmp-private", 55); await Seed(user, bank, bank.Questions, 30, app.Clock.UtcNow);
        var before = await ReadEffects(); var data = await owner.GetStringAsync($"/api/v1/learning/skill-comparison?windowSize=99999&userId={Guid.NewGuid()}");
        foreach (var key in new[] { "selectedOptionId", "correctOptionId", "answerText", "acceptedAnswerVariantsJson", "sampleRank", "Question 0", "Original test fixture" }) Assert.DoesNotContain(key, data, StringComparison.OrdinalIgnoreCase);
        foreach (var email in new[] { "learner2@englishhub.local", "admin@englishhub.local" }) { using var other = await app.Login(email); Assert.DoesNotContain((await other.GetFromJsonAsync<JsonElement>($"/api/v1/learning/skill-comparison?userId={user}")).GetProperty("groups").EnumerateArray(), x => x.GetProperty("skill").GetString() == "cmp-private"); }
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/skill-comparison")).StatusCode); Assert.Equal(before, await ReadEffects());
    }

    private async Task<string> ReadEffects()
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        return JsonSerializer.Serialize(new { attempts = await db.Attempts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.FirstDisclosedAt }).ToArrayAsync(), lessons = await db.LessonProgress.CountAsync(), tasks = await db.DailyTasks.CountAsync(), receipts = await db.Set<CommandReceipt>().CountAsync(), outbox = await db.Set<OutboxEvent>().CountAsync() });
    }

    private sealed record TestBank(Guid Version, Question[] Questions);
    private async Task<TestBank> Bank(string skill, int count, bool fixture = false)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var version = new ContentVersion { Title = $"Evidence {skill}", Status = PublicationStatus.Published, IsFixture = fixture };
        var root = new ContentRoot { Slug = $"evidence-{Guid.NewGuid():N}", Kind = ContentKind.Assessment, Versions = [version] };
        for (var i = 0; i < count; i++) version.Groups.Add(new QuestionGroup { Order = i, Part = 5, Questions = [new Question { Prompt = $"Question {i}", Skill = skill, Explanation = "Original test fixture", Options = [new() { Text = "Correct", IsCorrect = true }, new() { Text = "Other" }, new() { Text = "Third" }, new() { Text = "Fourth" }] }] });
        db.ContentRoots.Add(root); await db.SaveChangesAsync(); return new(version.Id, version.Groups.SelectMany(x => x.Questions).ToArray());
    }
    private async Task<Guid> Seed(Guid user, TestBank bank, Question[] questions, int correct, DateTimeOffset at, string mode = "practice", DateTimeOffset? disclosed = null, int policy = 1, Guid? id = null)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var attempt = new Attempt { Id = id ?? Guid.NewGuid(), UserId = user, ContentVersionId = bank.Version, Mode = mode, StartedAt = at, DeadlineAt = at.AddHours(1), Status = AttemptStatus.Graded, FirstDisclosedAt = disclosed, EvidencePolicyVersion = policy };
        foreach (var (q, index) in questions.Select((q, index) => (q, index))) { var key = q.Options.Single(x => x.IsCorrect).Id; attempt.Items.Add(new AttemptItem { QuestionId = q.Id, Order = index, Prompt = q.Prompt, SelectedOptionId = index < correct ? key : null, GradingKey = new GradingKey { CorrectOptionId = key, Skill = q.Skill, Part = 5, Explanation = q.Explanation } }); }
        attempt.Result = new AttemptResult { AttemptId = attempt.Id, CorrectCount = correct, TotalCount = questions.Length, BlankCount = questions.Length - correct, FinalizedAt = at.AddSeconds(1) }; db.Attempts.Add(attempt); await db.SaveChangesAsync(); return attempt.Id;
    }
    private static async Task<Guid> User(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private static JsonElement Group(JsonElement data, string skill, string track = "practice") => data.GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("skill").GetString() == skill && x.GetProperty("track").GetString() == track);
}
