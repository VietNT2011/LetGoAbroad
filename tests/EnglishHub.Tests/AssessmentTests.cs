using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class AssessmentTests(TestApp app) : IClassFixture<TestApp>
{
    private async Task<(HttpClient Client, AttemptView Attempt)> Start()
    {
        var client = await app.Login();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var exam = catalog.EnumerateArray().First(x => x.GetProperty("slug").GetString() == "mini-grammar").GetProperty("id").GetGuid();
        var response = await client.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(exam, Guid.NewGuid()));
        response.EnsureSuccessStatusCode();
        return (client, (await response.Content.ReadFromJsonAsync<AttemptView>())!);
    }
    private static SaveAnswers Choose(AttemptView a, Guid? command = null) => new(command ?? Guid.NewGuid(), [new(a.Items[0].Id, a.Items[0].Options[0].Id, a.Items[0].Etag)]);

    [Fact]
    public async Task Start_retries_return_same_id_and_payload_conflicts_are_rejected()
    {
        var (client, a) = await Start(); using var _ = client;
        var input = new StartAttempt(a.ContentVersionId, Guid.NewGuid());
        var first = await client.PostAsJsonAsync("/api/v1/attempts/", input);
        var second = await client.PostAsJsonAsync("/api/v1/attempts/", input);
        Assert.Equal((await first.Content.ReadFromJsonAsync<AttemptView>())!.Id, (await second.Content.ReadFromJsonAsync<AttemptView>())!.Id);
        var conflict = await client.PostAsJsonAsync("/api/v1/attempts/", input with { Mode = "practice" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var active = await client.GetStringAsync($"/api/v1/attempts/{a.Id}");
        Assert.DoesNotContain("correctOption", active); Assert.DoesNotContain("explanation", active); Assert.DoesNotContain("gradingKey", active);
    }

    [Fact]
    public async Task Concurrent_answers_accept_only_one_version_and_retries_replay_original_ack()
    {
        var (client, a) = await Start(); using var _ = client;
        var first = Choose(a); var second = Choose(a);
        var responses = await Task.WhenAll(client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", first), client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", second));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var accepted = responses[0].IsSuccessStatusCode ? first : second;
        var original = await responses.Single(x => x.IsSuccessStatusCode).Content.ReadAsStringAsync();
        var retry = await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", accepted);
        Assert.Equal(original, await retry.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", accepted with { Answers = [] })).StatusCode);
    }

    [Fact]
    public async Task Batch_is_atomic_and_options_cannot_cross_questions()
    {
        var (client, a) = await Start(); using var _ = client;
        var batch = new SaveAnswers(Guid.NewGuid(), [Choose(a).Answers[0], new(a.Items[1].Id, a.Items[1].Options[0].Id, "stale")]);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", batch)).StatusCode);
        var current = await client.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{a.Id}");
        Assert.Null(current!.Items[0].SelectedOptionId);
        var foreign = new SaveAnswers(Guid.NewGuid(), [new(a.Items[0].Id, a.Items[1].Options[0].Id, a.Items[0].Etag)]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", foreign)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_submit_persists_one_result_and_one_outbox_event()
    {
        var (client, a) = await Start(); using var _ = client;
        var payload = new SaveAnswers(Guid.NewGuid(), []);
        var responses = await Task.WhenAll(client.PostAsJsonAsync($"/api/v1/attempts/{a.Id}/submit", payload), client.PostAsJsonAsync($"/api/v1/attempts/{a.Id}/submit", payload));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Equal(1, await db.AttemptResults.CountAsync(x => x.AttemptId == a.Id));
        Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == a.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", Choose(a))).StatusCode);
    }

    [Fact]
    public async Task Exact_deadline_rejects_new_answers_and_cross_user_access_is_hidden()
    {
        var (client, a) = await Start(); using var _ = client;
        using var other = await app.Login("learner2@englishhub.local");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/attempts/{a.Id}")).StatusCode);
        app.Clock.Advance(a.DeadlineAt - app.Clock.UtcNow);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/attempts/{a.Id}/answers", Choose(a))).StatusCode);
        var submitted = await client.PostAsJsonAsync($"/api/v1/attempts/{a.Id}/submit", new SaveAnswers(Guid.NewGuid(), []));
        var result = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deadline", result.GetProperty("reason").GetString()); Assert.Equal(20, result.GetProperty("blankCount").GetInt32());
    }
}
