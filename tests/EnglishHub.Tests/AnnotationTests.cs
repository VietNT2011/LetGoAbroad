using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Kiểm tra quyền riêng tư, phiên bản và hiệu ứng ghi chú độc lập. EN: Exercise annotation privacy, concurrency and independence from graded answers.</summary>
public sealed class AnnotationTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Notes_are_private_and_clearing_a_note_preserves_bookmark_and_graded_answers()
    {
        using var learner = await app.Login(); var attempt = await Start(learner); var item = attempt.Items[0];
        var command = new SaveAnnotation(Guid.NewGuid(), true, "Remember the subject and verb agreement.");
        var saved = await Put(learner, attempt, item.Id, command); saved.EnsureSuccessStatusCode();
        var note = (await saved.Content.ReadFromJsonAsync<AnnotationView>())!;
        Assert.Equal(attempt.ContentVersionId, note.ContentVersionId); Assert.Equal(item.Prompt.Length, note.AnchorEnd);
        var before = await learner.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}");
        Assert.Equal(item.Etag, before!.Items[0].Etag);
        var submit = await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), [new(item.Id, item.Options[0].Id, item.Etag)])); submit.EnsureSuccessStatusCode();
        var resultBefore = await learner.GetStringAsync($"/api/v1/attempts/{attempt.Id}/result");
        using var other = await app.Login("learner2@englishhub.local"); using var admin = await app.Login("admin@englishhub.local");
        foreach (var outsider in new[] { other, admin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/attempts/{attempt.Id}/annotations")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Put(outsider, attempt, item.Id, command)).StatusCode);
            var list = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/me/annotations");
            Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == note.Id);
        }
        var clear = new SaveAnnotation(Guid.NewGuid(), true, "", note.Etag);
        (await Put(learner, attempt, item.Id, clear)).EnsureSuccessStatusCode();
        Assert.Equal(resultBefore, await learner.GetStringAsync($"/api/v1/attempts/{attempt.Id}/result"));
        var annotations = await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{attempt.Id}/annotations");
        Assert.True(annotations![item.Id].IsBookmarked); Assert.Equal("", annotations[item.Id].NoteText);
        Assert.Contains((await learner.GetFromJsonAsync<JsonElement>("/api/v1/me/annotations?bookmarkedOnly=true")).GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == note.Id);
    }

    [Fact]
    public async Task Same_question_across_attempts_has_one_annotation_with_payload_bound_retries_and_etags()
    {
        using var learner = await app.Login(); var first = await Start(learner); var second = await Start(learner, first.ContentVersionId);
        var a = first.Items[1]; var b = second.Items[1];
        var create = new SaveAnnotation(Guid.NewGuid(), true, "First note.");
        var responses = await Task.WhenAll(Put(learner, first, a.Id, create), Put(learner, second, b.Id, new SaveAnnotation(Guid.NewGuid(), true, "Concurrent note.")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        var initial = await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{first.Id}/annotations");
        var shared = await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{second.Id}/annotations");
        Assert.Equal(initial![a.Id].Id, shared![b.Id].Id);
        // VI: Dùng command riêng sau cuộc đua để xác minh ACK replay cả khi ETag đã đổi. EN: A separate accepted command proves stable replay after the rowversion advances.
        var update = new SaveAnnotation(Guid.NewGuid(), false, "A deliberate update.", initial[a.Id].Etag);
        var updated = await Put(learner, first, a.Id, update); updated.EnsureSuccessStatusCode();
        var ack = await updated.Content.ReadAsStringAsync();
        Assert.Equal(ack, await (await Put(learner, first, a.Id, update)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Put(learner, first, a.Id, update with { NoteText = "Changed payload" })).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(learner, second, b.Id, new SaveAnnotation(Guid.NewGuid(), true, "Stale tab", initial[a.Id].Etag))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Equal(1, await db.QuestionAnnotations.CountAsync(x => x.Id == initial[a.Id].Id));
    }

    [Fact]
    public async Task Anchors_validate_unicode_boundaries_and_new_question_versions_keep_separate_notes()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await app.Login();
        var content = new ContentInput("Anchored content", "Original", "", "grammar", 5, 600, false, "EnglishHub original · CC0", [new(5, "Original stimulus", [new("A 🧠 question", "grammar", "Đáp án tự biên soạn.", [], QuestionType.FillBlank, ["ready"])])]);
        var draft = await admin.PostAsJsonAsync("/api/v1/admin/content", new CreateContentInput($"anchor-{Guid.NewGuid():N}", ContentKind.Assessment, content)); draft.EnsureSuccessStatusCode();
        var first = await draft.Content.ReadFromJsonAsync<JsonElement>(); await Publish(admin, first);
        var attempt = await Start(learner, first.GetProperty("id").GetGuid()); var item = attempt.Items.Single();
        foreach (var invalid in new[] {
            new SaveAnnotation(Guid.NewGuid(), true, "bad block", AnchorBlock: "private_key"),
            new SaveAnnotation(Guid.NewGuid(), true, "bad offset", AnchorStart: -1),
            new SaveAnnotation(Guid.NewGuid(), true, "outside", AnchorEnd: item.Prompt.Length+1),
            new SaveAnnotation(Guid.NewGuid(), true, "split pair", AnchorStart: 3, AnchorEnd: 4),
            new SaveAnnotation(Guid.NewGuid(), true, new string('a',4001)) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(learner, attempt, item.Id, invalid)).StatusCode);
        var empty = await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{attempt.Id}/annotations"); Assert.Empty(empty!);
        var valid = await Put(learner, attempt, item.Id, new SaveAnnotation(Guid.NewGuid(), true, "Whole emoji anchor", AnchorStart: 2, AnchorEnd: 4)); valid.EnsureSuccessStatusCode();
        var original = (await valid.Content.ReadFromJsonAsync<AnnotationView>())!;
        var version = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{attempt.ContentVersionId}");
        var next = await admin.PostAsJsonAsync($"/api/v1/admin/content/{version.GetProperty("rootId").GetGuid()}/versions", content with { Title = "A different published version" }); next.EnsureSuccessStatusCode();
        var second = await next.Content.ReadFromJsonAsync<JsonElement>(); await Publish(admin, second);
        var nextAttempt = await Start(learner, second.GetProperty("id").GetGuid());
        Assert.Empty((await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{nextAttempt.Id}/annotations"))!);
        var old = (await learner.GetFromJsonAsync<Dictionary<Guid, AnnotationView>>($"/api/v1/attempts/{attempt.Id}/annotations"))![item.Id];
        Assert.Equal(original, old); Assert.Equal("Anchored content", old.Title);
    }

    [Fact]
    public async Task Saved_list_paginates_and_cleared_rows_do_not_appear()
    {
        using var learner = await app.Login("learner3@englishhub.local");
        var catalog = await learner.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var grammar = await Start(learner); var reading = await Start(learner, catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-reading").GetProperty("id").GetGuid());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var ids = grammar.Items.Concat(reading.Items).Take(32).Select(x => x.Id).ToArray();
        var items = await db.AttemptItems.Include(x => x.Attempt).Where(x => ids.Contains(x.Id)).ToArrayAsync();
        foreach (var (item, index) in items.Select((item, index) => (item, index))) db.QuestionAnnotations.Add(new QuestionAnnotation { UserId = item.Attempt.UserId, QuestionId = item.QuestionId, SourceAttemptItemId = item.Id, IsBookmarked = index < 31, AnchorEnd = item.Prompt.Length, UpdatedAt = app.Clock.UtcNow });
        await db.SaveChangesAsync();
        var page1 = await learner.GetFromJsonAsync<JsonElement>("/api/v1/me/annotations");
        Assert.Equal(30, page1.GetProperty("items").GetArrayLength()); Assert.Equal(30, page1.GetProperty("nextOffset").GetInt32());
        var page2 = await learner.GetFromJsonAsync<JsonElement>("/api/v1/me/annotations?offset=30");
        Assert.Single(page2.GetProperty("items").EnumerateArray()); Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextOffset").ValueKind);
        Assert.Equal(31, page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("id").GetGuid()).Distinct().Count());
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, AttemptView attempt, Guid item, SaveAnnotation input) => client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/items/{item}/annotation", input);
    private static async Task<AttemptView> Start(HttpClient learner, Guid? version = null)
    {
        if (version is null) version = (await learner.GetFromJsonAsync<JsonElement>("/api/v1/catalog")).EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-grammar").GetProperty("id").GetGuid();
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version.Value, Guid.NewGuid())); started.EnsureSuccessStatusCode(); return (await started.Content.ReadFromJsonAsync<AttemptView>())!;
    }
    private static async Task Publish(HttpClient admin, JsonElement draft)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/versions/{draft.GetProperty("id").GetGuid()}/publish");
        request.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString()); (await admin.SendAsync(request)).EnsureSuccessStatusCode();
    }
}
