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

/// <summary>VI: Placement đủ 20 câu mới tạo gợi ý; giữ snapshot cũ hợp lệ nhưng không nhận một phần đề. EN: Admit only complete pinned placement evidence while preserving valid historical versions and accepted replays.</summary>
public sealed class PlacementIntegrityTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData(1, null)]
    [InlineData(19, null)]
    [InlineData(20, 5)]
    public async Task Shortened_or_part_filtered_placement_creates_no_attempt_or_receipt(int count, int? part)
    {
        using var learner = await Learner(); var user = await User(learner); var version = await Placement();
        var command = new StartAttempt(version, Guid.NewGuid(), "placement", part, count);
        var response = await learner.PostAsJsonAsync("/api/v1/attempts/", command); Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("placement_whole_required", await response.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.Attempts.AnyAsync(x => x.UserId == user)); Assert.False(await db.CommandReceipts.AnyAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task Another_bank_cannot_be_started_under_the_placement_label()
    {
        using var learner = await Learner(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var other = await db.ContentVersions.Where(x => x.Root.Slug == "mini-grammar" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(other, Guid.NewGuid(), "placement"))).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Whole_placement_allows_default_or_explicit_twenty_and_preserves_start_replay(bool explicitCount)
    {
        using var learner = await Learner(); var command = new StartAttempt(await Placement(), Guid.NewGuid(), "placement", QuestionCount: explicitCount ? 20 : null);
        var response = await learner.PostAsJsonAsync("/api/v1/attempts/", command); response.EnsureSuccessStatusCode(); var attempt = (await response.Content.ReadFromJsonAsync<AttemptView>())!;
        var replay = await learner.PostAsJsonAsync("/api/v1/attempts/", command); replay.EnsureSuccessStatusCode(); Assert.Equal(attempt.Id, (await replay.Content.ReadFromJsonAsync<AttemptView>())!.Id); Assert.Equal(20, attempt.Items.Length);
        await Grade(learner, attempt, 8);
        (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, attempt.Id))).EnsureSuccessStatusCode();
        var plan = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/path-plan"); Assert.Equal("core", plan.GetProperty("template").GetProperty("level").GetString());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("reported-twenty")]
    [InlineData("foreign-questions")]
    public async Task Legacy_incomplete_or_mismatched_snapshots_cannot_create_an_enrollment(string shape)
    {
        using var learner = await Learner(); var user = await User(learner); var version = await Placement();
        var id = await Legacy(user, version, shape == "foreign-questions" ? 20 : 13, shape == "short" ? 13 : 20, foreign: shape == "foreign-questions");
        var response = await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, id)); Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.Enrollments.AnyAsync(x => x.UserId == user)); Assert.False(await db.PlanGoalRevisions.AnyAsync(x => x.Enrollment.UserId == user)); Assert.True(await db.Attempts.AnyAsync(x => x.Id == id));
    }

    [Fact]
    public async Task An_old_accepted_partial_start_replays_but_does_not_become_valid_admission_evidence()
    {
        using var learner = await Learner(); var user = await User(learner); var command = new StartAttempt(await Placement(), Guid.NewGuid(), "placement", QuestionCount: 1);
        var id = await Legacy(user, command.ContentVersionId, 1, 1, replay: command);
        var response = await learner.PostAsJsonAsync("/api/v1/attempts/", command); response.EnsureSuccessStatusCode(); Assert.Equal(id, (await response.Content.ReadFromJsonAsync<AttemptView>())!.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, id))).StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<HubDbContext>().Attempts.CountAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task A_graded_practice_of_the_whole_bank_does_not_become_a_placement_attempt()
    {
        using var learner = await Learner(); var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(await Placement(), Guid.NewGuid(), "practice")); started.EnsureSuccessStatusCode(); var attempt = (await started.Content.ReadFromJsonAsync<AttemptView>())!;
        await Grade(learner, attempt, 10);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, attempt.Id))).StatusCode);
    }

    [Fact]
    public async Task A_complete_historical_version_still_qualifies_after_its_publication_pointer_changes()
    {
        using var learner = await Learner(); var version = await Placement(); var id = await Legacy(await User(learner), version, 20, 20);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var old = await db.ContentVersions.Include(x => x.Root).SingleAsync(x => x.Id == version); var status = old.Status; var pointer = old.Root.CurrentPublishedVersionId;
        var nextNumber = await db.ContentVersions.Where(x => x.RootId == old.RootId).MaxAsync(x => x.Version) + 1;
        var next = new ContentVersion { RootId = old.RootId, Root = old.Root, Version = nextNumber, Title = "Replacement placement fixture", Status = PublicationStatus.Published, Attribution = "Original test · CC0" };
        for (var index = 0; index < 20; index++) next.Groups.Add(new QuestionGroup { Order = index, Questions = [new Question { Prompt = $"Replacement question {index}", Explanation = "Test only", Options = [new() { Text = "One", IsCorrect = true }, new() { Text = "Two" }, new() { Text = "Three" }, new() { Text = "Four" }] }] });
        db.ContentVersions.Add(next); await db.SaveChangesAsync(); old.Root.CurrentPublishedVersionId = next.Id; old.Status = PublicationStatus.Archived; await db.SaveChangesAsync();
        try
        {
            (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(false, id))).EnsureSuccessStatusCode();
            var enrollment = await db.Enrollments.AsNoTracking().SingleAsync(x => x.PlacementAttemptId == id); Assert.Equal("exam_practice", enrollment.Level); Assert.Equal("placement_demo_v1", enrollment.SelectionReason);
        }
        finally { old.Root.CurrentPublishedVersionId = pointer; old.Status = status; next.Status = PublicationStatus.Archived; await db.SaveChangesAsync(); }
    }

    private async Task<Guid> Placement()
    {
        using var scope = app.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<HubDbContext>().ContentVersions.Where(x => x.Root.Slug == PlacementPolicy.ContentSlug && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
    }
    private async Task<HttpClient> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"placement-{Guid.NewGuid():N}@example.com";
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Placement integrity test" };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private static async Task<Guid> User(HttpClient learner) => (await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private async Task Grade(HttpClient learner, AttemptView attempt, int correct)
    {
        using var scope = app.Services.CreateScope(); var keys = await scope.ServiceProvider.GetRequiredService<HubDbContext>().AttemptItems.Where(x => x.AttemptId == attempt.Id).Select(x => new { x.Id, x.GradingKey.CorrectOptionId }).ToDictionaryAsync(x => x.Id, x => x.CorrectOptionId);
        var answers = attempt.Items.Select((item, index) => new AnswerChange(item.Id, index < correct ? keys[item.Id] : null, item.Etag)).ToArray();
        (await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), answers))).EnsureSuccessStatusCode();
    }
    // VI: Dữ liệu cũ được dựng riêng trong fixture, không chèn lịch sử giả vào demo runtime. EN: Build pre-guard records only in the isolated test database, never in the learner's demo runtime.
    private async Task<Guid> Legacy(Guid user, Guid version, int count, int reported, bool foreign = false, StartAttempt? replay = null)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var query = db.Questions.Include(x => x.Options).Where(x => foreign ? x.Group.ContentVersion.Root.Slug == "mini-grammar" && x.Group.ContentVersion.Root.CurrentPublishedVersionId == x.Group.ContentVersionId : x.Group.ContentVersionId == version);
        var questions = await query.OrderBy(x => x.Group.Order).Take(count).ToArrayAsync(); var now = app.Clock.UtcNow;
        var attempt = new Attempt { UserId = user, ContentVersionId = version, Mode = "placement", StartedAt = now, DeadlineAt = now.AddMinutes(20), Status = AttemptStatus.Graded, EvidencePolicyVersion = 0 };
        foreach (var (q, index) in questions.Select((q, index) => (q, index))) attempt.Items.Add(new AttemptItem { QuestionId = q.Id, Order = index, Prompt = q.Prompt, SelectedOptionId = q.Options.Single(x => x.IsCorrect).Id, OptionsJson = JsonSerializer.Serialize(q.Options.Select(x => new PublicOption(x.Id, x.Text))), GradingKey = new GradingKey { CorrectOptionId = q.Options.Single(x => x.IsCorrect).Id, Skill = q.Skill, Part = 5, Explanation = q.Explanation } });
        attempt.Result = new AttemptResult { AttemptId = attempt.Id, TotalCount = reported, CorrectCount = Math.Min(count, reported), FinalizedAt = now }; db.Attempts.Add(attempt);
        if (replay is not null) db.CommandReceipts.Add(new CommandReceipt { UserId = user, Scope = "attempt-start", CommandId = replay.ClientRequestId, PayloadHash = Receipts.Hash(replay), ResourceId = attempt.Id, ResponseJson = JsonSerializer.Serialize(new { attempt.Id }), CreatedAt = now });
        await db.SaveChangesAsync(); return attempt.Id;
    }
}
