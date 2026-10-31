using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Ôn đến hạn không vượt slot/ngân sách và chỉ hoàn thành bằng nguồn review thật. EN: Due review shares core slots/budget and requires actual review evidence.</summary>
public sealed class DailyReviewPlanningTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData(20, 6)]
    [InlineData(30, 9)]
    [InlineData(45, 13)]
    public async Task Allocation_caps_review_at_thirty_percent_and_shares_three_core_slots(int budget, int expected)
    {
        var scenario = await Learner(budget); using var learner = scenario.Client;
        for (var index = 0; index < 16; index++) await Card(learner);
        var future = await Card(learner); await Rate(learner, future); var archived = await Card(learner); await Archive(learner, archived);
        var outsider = await Learner(); using var other = outsider.Client; var foreign = await Card(other);
        var plan = await Dashboard(learner); var review = Assert.Single(Reviews(plan));
        Assert.Equal(expected, review.GetProperty("totalCards").GetInt32()); Assert.Equal(expected, review.GetProperty("estimatedMinutes").GetInt32()); Assert.Equal("DUE_REVIEW", review.GetProperty("reasonCode").GetString());
        Assert.True(plan.GetProperty("tasks").GetArrayLength() + Reviews(plan).Length <= 3); Assert.True(plan.GetProperty("tasks").EnumerateArray().Sum(x => x.GetProperty("estimatedMinutes").GetInt32()) + expected <= budget);
        var batch = await learner.GetFromJsonAsync<JsonElement>($"/api/v1/review-batches/{Id(review)}"); var ids = batch.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("cardId").GetGuid()).ToArray();
        Assert.DoesNotContain(future.Id, ids); Assert.DoesNotContain(archived.Id, ids); Assert.DoesNotContain(foreign.Id, ids);
        Assert.Equal(review.GetRawText(), Assert.Single(Reviews(await Dashboard(learner))).GetRawText());
    }

    [Fact]
    public async Task New_due_cards_invalidate_the_token_but_require_explicit_regeneration_to_replace_a_saved_plan()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var before = await Dashboard(learner); Assert.Empty(Reviews(before));
        await Card(learner); var fresh = await Dashboard(learner); Assert.Equal(before.GetProperty("tasks").GetRawText(), fresh.GetProperty("tasks").GetRawText()); Assert.Empty(Reviews(fresh));
        Assert.NotEqual(before.GetProperty("planEtag").GetString(), fresh.GetProperty("planEtag").GetString()); Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before))).StatusCode);
        var command = Command(fresh); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<RegenerationAck>())!; Assert.True(ack.Changed);
        var current = await Dashboard(learner); var batch = Assert.Single(Reviews(current)); Assert.Contains(Id(batch), ack.NewTaskIds);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var enrollment = current.GetProperty("enrollment").GetProperty("id").GetGuid();
            var audit = await db.OutboxEvents.SingleAsync(x => x.Type == "learning.plan_regenerated" && x.AggregateId == enrollment);
            Assert.Contains(Id(batch), JsonDocument.Parse(audit.PayloadJson).RootElement.GetProperty("created").EnumerateArray().Select(x => x.GetGuid()));
        }
        app.Clock.Advance(TimeSpan.FromDays(1)); var replay = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", command); replay.EnsureSuccessStatusCode(); Assert.Equal(await response.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task No_op_regeneration_keeps_batch_item_ids_and_original_reason()
    {
        var scenario = await Learner(); using var learner = scenario.Client; await Card(learner); var before = await Dashboard(learner); var batch = Assert.Single(Reviews(before));
        var original = await learner.GetStringAsync($"/api/v1/review-batches/{Id(batch)}"); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before)); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<RegenerationAck>())!;
        Assert.False(ack.Changed); Assert.Contains(Id(batch), ack.RetainedTaskIds); Assert.Empty(ack.NewTaskIds); Assert.Equal(original, await learner.GetStringAsync($"/api/v1/review-batches/{Id(batch)}"));
    }

    [Fact]
    public async Task Three_started_content_tasks_prevent_new_review_slots_or_extra_budget()
    {
        var scenario = await Learner(30, legacy: true); using var learner = scenario.Client; var before = await Dashboard(learner); Assert.Equal(3, before.GetProperty("tasks").GetArrayLength());
        foreach (var task in before.GetProperty("tasks").EnumerateArray()) (await learner.PostAsJsonAsync($"/api/v1/learning/tasks/{Id(task)}/start", new { })).EnsureSuccessStatusCode();
        await Card(learner); var fresh = await Dashboard(learner); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(fresh)); response.EnsureSuccessStatusCode();
        var after = await Dashboard(learner); Assert.Empty(Reviews(after)); Assert.Equal(fresh.GetProperty("tasks").GetRawText(), after.GetProperty("tasks").GetRawText()); Assert.False((await response.Content.ReadFromJsonAsync<RegenerationAck>())!.Changed);
    }

    [Fact]
    public async Task Archival_replaces_only_an_unstarted_batch_and_old_batch_cannot_start()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var first = await Card(learner); await Card(learner); var before = await Dashboard(learner); var old = Id(Assert.Single(Reviews(before)));
        await Archive(learner, first); Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before))).StatusCode);
        var fresh = await Dashboard(learner); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(fresh)); response.EnsureSuccessStatusCode(); var ack = (await response.Content.ReadFromJsonAsync<RegenerationAck>())!; Assert.Contains(old, ack.SupersededTaskIds);
        var current = Assert.Single(Reviews(await Dashboard(learner))); Assert.NotEqual(old, Id(current)); Assert.Equal(1, current.GetProperty("totalCards").GetInt32()); Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/review-batches/{old}/start", new { })).StatusCode);
    }

    [Fact]
    public async Task Partial_review_starts_and_protects_batch_then_actual_sources_complete_it_once()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var first = await Card(learner); var second = await Card(learner); var before = await Dashboard(learner); var batch = Id(Assert.Single(Reviews(before))); var statistics = before.GetProperty("statistics").GetRawText();
        var command = new ReviewCard(Guid.NewGuid(), first.Etag, CardRating.Again); var firstReview = await Rate(learner, first, command); var partial = Assert.Single(Reviews(await Dashboard(learner))); Assert.Equal(1, partial.GetProperty("completedCards").GetInt32()); Assert.Equal(JsonValueKind.String, partial.GetProperty("startedAt").ValueKind);
        var fresh = await Dashboard(learner); var regen = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(fresh)); regen.EnsureSuccessStatusCode(); Assert.Contains(batch, (await regen.Content.ReadFromJsonAsync<RegenerationAck>())!.RetainedTaskIds);
        await Rate(learner, second); await Rate(learner, first, command); var completed = await Dashboard(learner); var final = Assert.Single(Reviews(completed)); Assert.Equal("completed", final.GetProperty("status").GetString()); Assert.Equal(2, final.GetProperty("completedCards").GetInt32()); Assert.Equal(statistics, completed.GetProperty("statistics").GetRawText());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var items = await db.DailyReviewItems.Where(x => x.BatchId == batch).ToArrayAsync(); Assert.All(items, item => Assert.NotNull(item.CardReviewId));
        Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.Type == "learning.review_batch_completed" && x.AggregateId == batch)); Assert.False(await db.TaskCompletions.AnyAsync(x => x.DailyTask.UserId == scenario.User));
        foreach (var item in items) Assert.Equal(item.UserCardId, (await db.CardReviews.SingleAsync(x => x.Id == item.CardReviewId)).UserCardId);
        Assert.Equal(firstReview.ReviewId, items.Single(x => x.UserCardId == first.Id).CardReviewId);
    }

    [Fact]
    public async Task Started_batch_survives_midnight_and_regeneration_reserves_its_minutes_and_slot()
    {
        var scenario = await Learner(); using var learner = scenario.Client; await Card(learner); await Card(learner); var before = await Dashboard(learner); var batch = Id(Assert.Single(Reviews(before)));
        (await learner.PostAsJsonAsync($"/api/v1/review-batches/{batch}/start", new { })).EnsureSuccessStatusCode(); app.Clock.Advance(TimeSpan.FromDays(1));
        var next = await Dashboard(learner); Assert.Equal(batch, Id(Assert.Single(Reviews(next)))); var response = await learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(next)); response.EnsureSuccessStatusCode(); var after = await Dashboard(learner); Assert.Equal(batch, Id(Assert.Single(Reviews(after))));
        Assert.True(after.GetProperty("tasks").GetArrayLength() + Reviews(after).Length <= 3); Assert.True(after.GetProperty("tasks").EnumerateArray().Sum(x => x.GetProperty("estimatedMinutes").GetInt32()) + Assert.Single(Reviews(after)).GetProperty("estimatedMinutes").GetInt32() <= 30);
    }

    [Fact]
    public async Task Unstarted_missed_batch_yields_one_new_pending_batch_without_fake_completion()
    {
        var scenario = await Learner(); using var learner = scenario.Client; await Card(learner); var old = Id(Assert.Single(Reviews(await Dashboard(learner)))); app.Clock.Advance(TimeSpan.FromDays(1)); var next = await Dashboard(learner); var current = Id(Assert.Single(Reviews(next))); Assert.NotEqual(old, current);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal("missed", (await db.DailyReviewBatches.SingleAsync(x => x.Id == old)).Status); Assert.Equal(1, await db.DailyReviewBatches.CountAsync(x => x.UserId == scenario.User && x.Status == "pending")); Assert.False(await db.CardReviews.AnyAsync(x => x.Card.UserId == scenario.User));
    }

    [Fact]
    public async Task Review_and_regeneration_share_a_lock_and_do_not_lose_partial_evidence()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var card = await Card(learner); await Card(learner); var before = await Dashboard(learner); var batch = Id(Assert.Single(Reviews(before)));
        var responses = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good)), learner.PostAsJsonAsync("/api/v1/learning/plan/regenerate", Command(before)));
        responses[0].EnsureSuccessStatusCode(); Assert.True(responses[1].IsSuccessStatusCode || responses[1].StatusCode == HttpStatusCode.PreconditionFailed);
        var after = Assert.Single(Reviews(await Dashboard(learner))); Assert.Equal(batch, Id(after)); Assert.Equal(1, after.GetProperty("completedCards").GetInt32());
    }

    [Fact]
    public async Task Batch_owner_csrf_and_composite_card_ownership_are_enforced()
    {
        var scenario = await Learner(); using var learner = scenario.Client; await Card(learner); var before = await Dashboard(learner); var batch = Id(Assert.Single(Reviews(before))); var otherScenario = await Learner(); using var other = otherScenario.Client; var foreign = await Card(other);
        using var admin = await app.Login("admin@englishhub.local"); foreach (var outsider in new[] { other, admin }) { Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/review-batches/{batch}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/review-batches/{batch}/start", new { })).StatusCode); }
        learner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await learner.PostAsJsonAsync($"/api/v1/review-batches/{batch}/start", new { })).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var current = await db.DailyReviewItems.SingleAsync(x => x.BatchId == batch); current.UserCardId = foreign.Id; await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed record Scenario(HttpClient Client, Guid User);
    private async Task<Scenario> Learner(int budget = 30, bool legacy = false)
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"daily-review-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Review allocation fixture", MinutesPerDay = budget, StudyDays = "0,1,2,3,4,5,6" };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded);
        var client = await app.Login(email);
        if (legacy) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.Enrollments.Add(new Enrollment { UserId = user.Id, TargetScore = 650, MinutesPerDay = budget, CreatedAt = app.Clock.UtcNow }); await db.SaveChangesAsync(); }
        else (await client.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true))).EnsureSuccessStatusCode();
        return new(client, user.Id);
    }
    private static Task<JsonElement> Dashboard(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
    private static JsonElement[] Reviews(JsonElement plan) => plan.GetProperty("reviewBatches").EnumerateArray().ToArray();
    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();
    private static RegeneratePlanInput Command(JsonElement plan) => new(Guid.NewGuid(), DateOnly.Parse(plan.GetProperty("localDate").GetString()!), plan.GetProperty("planEtag").GetString()!);
    private static async Task<CardView> Card(HttpClient client) { var response = await client.PostAsJsonAsync("/api/v1/cards/", new CreateCard(Guid.NewGuid(), $"Original review card {Guid.NewGuid():N}", "Meaning")); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardView>())!; }
    private static async Task<CardReviewAck> Rate(HttpClient client, CardView card, ReviewCard? input = null) { var response = await client.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", input ?? new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardReviewAck>())!; }
    private static async Task Archive(HttpClient client, CardView card) => (await client.PostAsJsonAsync($"/api/v1/cards/{card.Id}/archive", new ArchiveCard(Guid.NewGuid(), card.Etag, true))).EnsureSuccessStatusCode();
}
