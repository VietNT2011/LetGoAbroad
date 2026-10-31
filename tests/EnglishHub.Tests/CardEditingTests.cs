using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Sửa thẻ phải giữ bằng chứng cũ, chống sửa trùng và không lẫn lịch học. EN: Exercise versioned text, replay, competing commands and preserved learning evidence on both relational providers.</summary>
public sealed class CardEditingTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Changed_text_resets_schedule_and_keeps_old_review_text_context_and_identity()
    {
        using var owner = await app.Login("learner3@englishhub.local"); var card = await Create(owner);
        var beforeStats = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        var command = new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good);
        var ack = await Review(owner, card, command);
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        var edit = new EditCard(Guid.NewGuid(), ack.Card.Etag, " café ", " New meaning ");
        var changed = await Edit(owner, card.Id, edit);
        Assert.Equal(card.Id, changed.Id); Assert.Equal("café", changed.Front); Assert.Equal("New meaning", changed.Back);
        Assert.Equal(2, changed.ContentVersion); Assert.Equal(0, changed.IntervalDays); Assert.Equal(0, changed.SuccessStep); Assert.Equal(app.Clock.UtcNow, changed.DueAt); Assert.True(changed.CanReview); Assert.Equal(card.Contexts, changed.Contexts);
        var firstHistory = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/reviews"); var old = Assert.Single(firstHistory.GetProperty("items").EnumerateArray());
        Assert.Equal(card.Front, old.GetProperty("front").GetString()); Assert.Equal(card.Back, old.GetProperty("back").GetString()); Assert.Equal(1, old.GetProperty("contentVersion").GetInt32());
        var replay = await Review(owner, card, command); Assert.Equal(JsonSerializer.Serialize(ack), JsonSerializer.Serialize(replay));
        await Review(owner, changed, new(Guid.NewGuid(), changed.Etag, CardRating.Hard));
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/reviews"); Assert.Equal(new[] { 1, 2 }, history.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("contentVersion").GetInt32()).Order());
        var afterStats = await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(beforeStats.GetProperty("statistics").GetRawText(), afterStats.GetProperty("statistics").GetRawText());
    }

    [Fact]
    public async Task Normalized_noop_preserves_etag_and_schedule_and_edit_receipt_survives_later_changes()
    {
        using var owner = await app.Login(); var card = await Create(owner);
        var reviewed = (await Review(owner, card, new(Guid.NewGuid(), card.Etag, CardRating.Easy))).Card;
        var noop = await Edit(owner, card.Id, new(Guid.NewGuid(), reviewed.Etag, $" {card.Front} ", $" {card.Back} "));
        Assert.Equal(JsonSerializer.Serialize(reviewed), JsonSerializer.Serialize(noop));
        var input = new EditCard(Guid.NewGuid(), noop.Etag, $"New {Guid.NewGuid():N}", "New text"); var changed = await Edit(owner, card.Id, input);
        var second = await Edit(owner, card.Id, input with { ClientRequestId = Guid.NewGuid(), ExpectedEtag = changed.Etag, Back = "Later text" });
        var archived = await owner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/archive", new ArchiveCard(Guid.NewGuid(), second.Etag, true)); archived.EnsureSuccessStatusCode();
        Assert.Equal(JsonSerializer.Serialize(changed), JsonSerializer.Serialize(await Edit(owner, card.Id, input)));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input with { Back = "Other payload" })).StatusCode);
        var versions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/versions"); Assert.Equal(3, versions.GetProperty("items").GetArrayLength());
        var archivedView = (await archived.Content.ReadFromJsonAsync<CardView>())!;
        var archivedEdit = await Edit(owner, card.Id, input with { ClientRequestId = Guid.NewGuid(), ExpectedEtag = archivedView.Etag, Back = "Archived edit" }); Assert.NotNull(archivedEdit.ArchivedAt); Assert.False(archivedEdit.CanReview);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_to_another_active_or_archived_card_rejects_without_merging(bool archived)
    {
        using var owner = await app.Login(); var card = await Create(owner); var target = await Create(owner);
        if (archived) (await owner.PostAsJsonAsync($"/api/v1/cards/{target.Id}/archive", new ArchiveCard(Guid.NewGuid(), target.Etag, true))).EnsureSuccessStatusCode();
        var response = await owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", new EditCard(Guid.NewGuid(), card.Etag, target.Front, target.Back)); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var current = await owner.GetFromJsonAsync<CardView>($"/api/v1/cards/{card.Id}"); Assert.Equal(JsonSerializer.Serialize(card), JsonSerializer.Serialize(current));
        var versions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/versions"); Assert.Single(versions.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Racing_edits_accept_one_new_version_and_one_outbox_and_reject_stale_writer()
    {
        using var owner = await app.Login(); var card = await Create(owner); var input = new EditCard(Guid.NewGuid(), card.Etag, card.Front, "Racing meaning");
        var responses = await Task.WhenAll(owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input), owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input with { ClientRequestId = Guid.NewGuid(), Back = "Other racer" }));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(2, await db.UserCardVersions.CountAsync(x => x.UserCardId == card.Id)); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == card.Id && x.Type == "review.card_edited"));
    }

    [Fact]
    public async Task Racing_edit_and_review_share_one_etag_and_cannot_rate_unseen_new_text()
    {
        using var owner = await app.Login(); var card = await Create(owner);
        var responses = await Task.WhenAll(owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", new EditCard(Guid.NewGuid(), card.Etag, card.Front, "Changed answer")), owner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", new ReviewCard(Guid.NewGuid(), card.Etag, CardRating.Good)));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        using var scope = app.Services.CreateScope(); var reviews = await scope.ServiceProvider.GetRequiredService<HubDbContext>().CardReviews.Where(x => x.UserCardId == card.Id).ToArrayAsync(); Assert.All(reviews, x => Assert.Equal(1, x.ContentVersion));
    }

    [Fact]
    public async Task Edit_and_history_are_owner_scoped_even_for_admin_and_require_auth_csrf_valid_input()
    {
        using var owner = await app.Login(); using var other = await app.Login("learner2@englishhub.local"); using var admin = await app.Login("admin@englishhub.local"); var card = await Create(owner); var input = new EditCard(Guid.NewGuid(), card.Etag, "Changed", "Answer");
        foreach (var client in new[] { other, admin }) { Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/cards/{card.Id}/versions")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input)).StatusCode); }
        foreach (var invalid in new[] { input with { Front = " " }, input with { Front = new string('x', 501) }, input with { Back = new string('x', 2001) }, input with { ClientRequestId = Guid.Empty } }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input with { ExpectedEtag = "foreign" })).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input)).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/cards/{card.Id}/versions")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_identical_edits_replay_one_version_and_keep_canonical_creation_deduplication()
    {
        using var owner = await app.Login(); var card = await Create(owner); var input = new EditCard(Guid.NewGuid(), card.Etag, $" cafe\u0301 {Guid.NewGuid():N} ", " new meaning ");
        var responses = await Task.WhenAll(owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input), owner.PutAsJsonAsync($"/api/v1/cards/{card.Id}", input)); Assert.All(responses, x => x.EnsureSuccessStatusCode()); Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        var current = (await responses[0].Content.ReadFromJsonAsync<CardView>())!;
        var created = await owner.PostAsJsonAsync("/api/v1/cards/", new CreateCard(Guid.NewGuid(), current.Front, current.Back)); created.EnsureSuccessStatusCode(); Assert.Equal(card.Id, (await created.Content.ReadFromJsonAsync<CardView>())!.Id);
    }

    [Fact]
    public async Task Version_pages_are_bounded_ordered_and_do_not_rewrite_context_or_learning_evidence()
    {
        using var owner = await app.Login(); var card = await Create(owner); var activity = await owner.GetStringAsync("/api/v1/learning/activity?days=30");
        for (var number = 2; number <= 32; number++) card = await Edit(owner, card.Id, new(Guid.NewGuid(), card.Etag, card.Front, $"Version {number}"));
        var first = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/versions"); var second = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/cards/{card.Id}/versions?offset=30"); Assert.Equal(30, first.GetProperty("items").GetArrayLength()); Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.Equal(32, first.GetProperty("items")[0].GetProperty("number").GetInt32()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextOffset").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"/api/v1/cards/{card.Id}/versions?offset=-1")).StatusCode); Assert.Equal(activity, await owner.GetStringAsync("/api/v1/learning/activity?days=30"));
    }

    private static async Task<CardView> Create(HttpClient owner) { var r = await owner.PostAsJsonAsync("/api/v1/cards/", new CreateCard(Guid.NewGuid(), $"Edit fixture {Guid.NewGuid():N}", "Original meaning", "Original context")); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<CardView>())!; }
    private static async Task<CardView> Edit(HttpClient owner, Guid id, EditCard input) { var r = await owner.PutAsJsonAsync($"/api/v1/cards/{id}", input); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<CardView>())!; }
    private static async Task<CardReviewAck> Review(HttpClient owner, CardView card, ReviewCard input) { var r = await owner.PostAsJsonAsync($"/api/v1/cards/{card.Id}/reviews", input); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<CardReviewAck>())!; }
}
