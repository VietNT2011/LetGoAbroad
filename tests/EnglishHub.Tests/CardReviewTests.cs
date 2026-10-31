using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Kiểm chứng lịch xác định, replay và tranh chấp trên DB quan hệ thật. EN: Exercise deterministic schedules, receipts and competing devices on the relational profiles.</summary>
public sealed class CardReviewTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData(0, 0, CardRating.Good, 1, 1)]
    [InlineData(1, 1, CardRating.Good, 3, 2)]
    [InlineData(3, 2, CardRating.Good, 7, 3)]
    [InlineData(7, 3, CardRating.Good, 14, 4)]
    [InlineData(100, 7, CardRating.Good, 180, 8)]
    [InlineData(0, 0, CardRating.Hard, 1, 0)]
    [InlineData(3, 2, CardRating.Hard, 4, 2)]
    [InlineData(180, 9, CardRating.Hard, 180, 9)]
    [InlineData(0, 0, CardRating.Easy, 4, 1)]
    [InlineData(5, 2, CardRating.Easy, 13, 3)]
    [InlineData(100, 7, CardRating.Easy, 180, 8)]
    public void Policy_matches_each_learning_step_rounding_and_cap(int beforeInterval, int beforeStep, CardRating rating, int afterInterval, int afterStep)
    {
        var now = DateTimeOffset.Parse("2026-03-08T01:59:00-05:00"); var next = CardScheduler.Next(beforeInterval, beforeStep, rating, now);
        Assert.Equal(afterInterval, next.IntervalDays); Assert.Equal(afterStep, next.SuccessStep); Assert.Equal(now.AddDays(afterInterval), next.DueAt); Assert.Equal(TimeSpan.Zero, next.DueAt.Offset);
    }

    [Fact]
    public void Again_resets_the_interval_and_step_to_a_ten_minute_UTC_instant()
    {
        var now = DateTimeOffset.Parse("2026-11-17T23:55:00+14:00"); Assert.Equal(new CardSchedule(0, 0, now.ToUniversalTime().AddMinutes(10)), CardScheduler.Next(180, 10, CardRating.Again, now));
        Assert.Throws<DomainError>(() => CardScheduler.Next(-1, 0, CardRating.Good, now)); Assert.Throws<DomainError>(() => CardScheduler.Next(0, -1, CardRating.Good, now)); Assert.Throws<DomainError>(() => CardScheduler.Next(0, 0, (CardRating)99, now));
    }

    [Fact]
    public async Task Creation_is_payload_bound_deduplicates_canonical_text_and_preserves_additional_context()
    {
        using var learner = await Learner(); var command = new CreateCard(Guid.NewGuid(), " cafe\u0301 ", " quán cà phê ", " Original context "); var first = await Create(learner, command);
        var replay = await Create(learner, command); Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        var extra = await Create(learner, command with { ClientRequestId = Guid.NewGuid(), Front = "café", Back = "quán cà phê", SourceContext = "A second context" });
        Assert.Equal(first.Id, extra.Id); Assert.Equal("café", extra.Front); Assert.Equal(new[] { "Original context", "A second context" }.Order(), extra.Contexts.Order()); Assert.NotEqual(first.Etag, extra.Etag);
        var repeated = await Create(learner, command with { ClientRequestId = Guid.NewGuid() }); Assert.Equal(2, repeated.Contexts.Length);
        var changedMeaning = await Create(learner, command with { ClientRequestId = Guid.NewGuid(), Back = "cà phê" }); Assert.NotEqual(first.Id, changedMeaning.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/cards/", command with { Back = "changed" })).StatusCode);
        var page = await Page(learner); Assert.Equal(2, page.Items.Length); Assert.Equal(2, page.DueCount);
    }

    [Fact]
    public async Task Review_receipt_replays_after_later_reviews_and_archival_without_new_effects()
    {
        using var learner = await Learner(); var card = await Create(learner); var firstCommand = new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good); var first = await Review(learner, card.Id, firstCommand);
        Assert.True(card.CanReview); Assert.False(first.Card.CanReview); Assert.Equal(1, first.Card.IntervalDays); Assert.Equal(1, first.Card.SuccessStep); Assert.Equal(app.Clock.UtcNow.AddDays(1), first.Card.DueAt); Assert.NotEqual(card.Etag, first.Card.Etag);
        Assert.Equal(0, (await Page(learner, "?dueOnly=true")).DueCount);
        app.Clock.Advance(TimeSpan.FromDays(1)); var second = await Review(learner, card.Id, new(Guid.NewGuid(), first.Card.Etag, CardRating.Good)); Assert.Equal(3, second.Card.IntervalDays);
        await Archive(learner, second.Card, true);
        var replay = await Review(learner, card.Id, firstCommand); Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", firstCommand with { Rating = CardRating.Easy })).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var reviews = await db.CardReviews.Where(x => x.UserCardId == card.Id).OrderBy(x => x.ReviewedAt).ToArrayAsync();
        Assert.Equal(2, reviews.Length); Assert.Equal(2, await db.OutboxEvents.CountAsync(x => x.Type == "review.card_reviewed" && reviews.Select(r => r.Id).Contains(x.AggregateId)));
        Assert.Equal((0, 1, 0, 1), (reviews[0].BeforeIntervalDays, reviews[0].AfterIntervalDays, reviews[0].BeforeSuccessStep, reviews[0].AfterSuccessStep)); Assert.Equal((1, 3, 1, 2), (reviews[1].BeforeIntervalDays, reviews[1].AfterIntervalDays, reviews[1].BeforeSuccessStep, reviews[1].AfterSuccessStep));
        Assert.All(reviews, row => Assert.Equal(1, row.SchedulerVersion)); Assert.True((await learner.GetFromJsonAsync<CardView>($"/api/v1/cards/{card.Id}"))!.ArchivedAt is not null);
    }

    [Fact]
    public async Task Due_filter_and_review_use_exact_instant_even_across_local_midnight()
    {
        using var learner = await Learner("Pacific/Kiritimati"); var card = await Create(learner); var reviewed = await Review(learner, card.Id, new(Guid.NewGuid(), card.Etag, CardRating.Again));
        app.Clock.Advance(TimeSpan.FromMinutes(10).Subtract(TimeSpan.FromTicks(1)));
        Assert.Empty((await Page(learner, "?dueOnly=true")).Items);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), reviewed.Card.Etag, CardRating.Good))).StatusCode);
        app.Clock.Advance(TimeSpan.FromTicks(1)); Assert.Equal(card.Id, Assert.Single((await Page(learner, "?dueOnly=true")).Items).Id);
        Assert.Equal(1, (await Review(learner, card.Id, new(Guid.NewGuid(), reviewed.Card.Etag, CardRating.Good))).Card.IntervalDays);
    }

    [Fact]
    public async Task Competing_devices_accept_one_review_and_reject_the_other_stale_version()
    {
        using var learner = await Learner(); var card = await Create(learner); var first = new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good); var second = first with { ReviewEventId = Guid.NewGuid(), Rating = CardRating.Easy };
        var responses = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", first), learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", second));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(1, await db.CardReviews.CountAsync(x => x.UserCardId == card.Id));
        var history = await learner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/reviews"); Assert.Equal(1, history.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Duplicate_concurrent_event_has_one_review_outbox_and_identical_ACK()
    {
        using var learner = await Learner(); var card = await Create(learner); var input = new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Hard);
        var responses = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", input), learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", input));
        Assert.All(responses, response => response.EnsureSuccessStatusCode()); Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var row = await db.CardReviews.SingleAsync(x => x.UserCardId == card.Id); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.Type == "review.card_reviewed" && x.AggregateId == row.Id));
    }

    [Fact]
    public async Task Ownership_auth_and_csrf_apply_to_card_text_history_and_all_commands()
    {
        using var owner = await Learner(); using var other = await Learner(); using var admin = await app.Login("admin@englishhub.local"); var card = await Create(owner);
        foreach (var outsider in new[] { other, admin })
        {
            Assert.DoesNotContain((await Page(outsider)).Items, x => x.Id == card.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/cards/{card.Id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/cards/{card.Id}/reviews")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/cards/{card.Id}/archive", new ArchiveCard(Guid.NewGuid(), card.Etag, true))).StatusCode);
        }
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good))).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/cards/")).StatusCode);
    }

    [Fact]
    public async Task Archive_restore_retains_schedule_contexts_and_history_and_enforces_ETag()
    {
        using var learner = await Learner(); var input = new CreateCard(Guid.NewGuid(), "restore fixture", "original meaning", "context"); var card = await Create(learner, input); var review = await Review(learner, card.Id, new(Guid.NewGuid(), card.Etag, CardRating.Again));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/archive", new ArchiveCard(Guid.NewGuid(), card.Etag, true))).StatusCode);
        var archived = await Archive(learner, review.Card, true); Assert.Empty((await Page(learner)).Items); Assert.Single((await Page(learner, "?archived=true")).Items);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/cards/", input with { ClientRequestId = Guid.NewGuid() })).StatusCode);
        app.Clock.Advance(TimeSpan.FromMinutes(10)); Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), archived.Etag, CardRating.Good))).StatusCode);
        var restored = await Archive(learner, archived, false); Assert.Equal(review.Card.DueAt, restored.DueAt); Assert.Equal(review.Card.Contexts, restored.Contexts); Assert.Single((await Page(learner, "?dueOnly=true")).Items);
        Assert.Single((await learner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/reviews")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_text_rating_and_cross_card_event_reuse_have_no_partial_effects()
    {
        using var learner = await Learner(); var card = await Create(learner); var second = await Create(learner);
        foreach (var input in new[] { new CreateCard(Guid.NewGuid(), " ", "back"), new CreateCard(Guid.NewGuid(), new string('x', 501), "back"), new CreateCard(Guid.NewGuid(), "front", new string('x', 2001)), new CreateCard(Guid.NewGuid(), "front", "back", new string('x', 2001)) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/cards/", input)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), card.Etag, (CardRating)99))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.Empty, card.Etag, CardRating.Good))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await learner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), "foreign", CardRating.Good))).StatusCode);
        var command = new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good); await Review(learner, card.Id, command);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/cards/{second.Id}/reviews", command with { ExpectedEtag = second.Etag })).StatusCode);
        Assert.Equal(second.Etag, (await learner.GetFromJsonAsync<CardView>($"/api/v1/cards/{second.Id}"))!.Etag); Assert.Equal(2, (await Page(learner)).Items.Length);
    }

    [Fact]
    public async Task Personal_review_does_not_inflate_learning_statistics_activity_or_path_completion()
    {
        using var learner = await Learner(); var before = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); var activity = await learner.GetStringAsync("/api/v1/learning/activity?days=30");
        var card = await Create(learner); await Review(learner, card.Id, new(Guid.NewGuid(), card.Etag, CardRating.Good));
        Assert.Equal(activity, await learner.GetStringAsync("/api/v1/learning/activity?days=30")); var after = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(before.GetProperty("statistics").GetRawText(), after.GetProperty("statistics").GetRawText()); Assert.Equal(before.GetProperty("tasks").GetRawText(), after.GetProperty("tasks").GetRawText());
    }

    [Fact]
    public async Task Due_pages_are_bounded_owner_scoped_and_return_no_duplicates_on_a_stable_queue()
    {
        using var learner = await Learner(); for (var index = 0; index < 32; index++) await Create(learner);
        var first = await Page(learner, "?dueOnly=true"); var second = await Page(learner, "?dueOnly=true&offset=30"); Assert.Equal(30, first.Items.Length); Assert.Equal(2, second.Items.Length); Assert.Equal(32, first.DueCount); Assert.Equal(30, first.NextOffset); Assert.Null(second.NextOffset); Assert.Equal(32, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.GetAsync("/api/v1/cards/?offset=-1")).StatusCode);
    }

    private static async Task<CardPage> Page(HttpClient client, string query = "") => (await client.GetFromJsonAsync<CardPage>("/api/v1/cards/" + query))!;
    private static async Task<CardView> Create(HttpClient client, CreateCard? input = null) { var response = await client.PostAsJsonAsync("/api/v1/cards/", input ?? new CreateCard(Guid.NewGuid(), $"Original card {Guid.NewGuid():N}", "Personal meaning")); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardView>())!; }
    private static async Task<CardReviewAck> Review(HttpClient client, Guid id, ReviewCard input) { var response = await client.PostAsJsonAsync($"/api/v1/cards/{id}/reviews", input); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardReviewAck>())!; }
    private static async Task<CardView> Archive(HttpClient client, CardView card, bool archived) { var response = await client.PostAsJsonAsync($"/api/v1/cards/{card.Id}/archive", new ArchiveCard(Guid.NewGuid(), card.Etag, archived)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardView>())!; }
    private async Task<HttpClient> Learner(string zone = "UTC")
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"cards-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Personal review fixture", TimeZoneId = zone };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
}
