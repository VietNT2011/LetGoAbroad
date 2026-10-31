using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Kiểm tra quyền, anchor/version và retry mà không thay đổi nguồn hay tiến độ. EN: Exercise privacy, versioned anchors and retries without changing content or learning evidence.</summary>
public sealed class LessonNoteTests(TestApp app) : IClassFixture<TestApp>
{
    private const string Body = "A 🧠 learner writes a note.\n\nUnicode café and e\u0301 stay exactly as authored.\nA second paragraph.";

    [Theory]
    [InlineData(2, 3, false)]
    [InlineData(3, 4, false)]
    [InlineData(2, 4, true)]
    [InlineData(0, 1, true)]
    public void Utf16_anchors_do_not_split_surrogate_pairs(int start, int end, bool valid)
    {
        if (valid) TextAnchor.Validate(Body, "body", start, end, Body[start..end]);
        else Assert.Equal("invalid_lesson_anchor", Assert.Throws<DomainError>(() => TextAnchor.Validate(Body, "body", start, end, Body[start..end])).Code);
    }

    [Fact]
    public async Task Notes_are_private_and_leave_body_answers_and_progress_unchanged()
    {
        using var owner = await app.Login(); using var other = await app.Login("learner2@englishhub.local"); using var admin = await app.Login("admin@englishhub.local");
        var lesson = await Publish(admin); var sourceBefore = await owner.GetStringAsync($"/api/v1/lessons/{lesson}");
        var id = Guid.NewGuid(); var input = Input(); var saved = await Put(owner, lesson, id, input); saved.EnsureSuccessStatusCode();
        var note = (await saved.Content.ReadFromJsonAsync<LessonAnnotationView>())!; Assert.Equal(lesson, note.ContentVersionId); Assert.Equal(Body[..26], note.Quote);
        Assert.Single((await owner.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{lesson}/annotations/"))!);
        foreach (var outsider in new[] { other, admin })
        {
            Assert.Empty((await outsider.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{lesson}/annotations/"))!);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/lessons/{lesson}/annotations/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Put(outsider, lesson, id, input with { ExpectedEtag = note.Etag })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Remove(outsider, lesson, id, new(Guid.NewGuid(), note.Etag))).StatusCode);
            var list = await outsider.GetFromJsonAsync<JsonElement>("/api/v1/me/lesson-annotations"); Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        }
        Assert.Equal(sourceBefore, await owner.GetStringAsync($"/api/v1/lessons/{lesson}"));
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/lessons/{lesson}/annotations/")).StatusCode);
        using var noCsrf = await app.Login(); noCsrf.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await Put(noCsrf, lesson, Guid.NewGuid(), input)).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.LessonProgress.AnyAsync(x => x.ContentVersionId == lesson)); Assert.Equal(Body, (await db.ContentVersions.SingleAsync(x => x.Id == lesson)).Body);
    }

    [Fact]
    public async Task Stale_tabs_reject_overwrites_and_original_acknowledgements_survive_later_edits()
    {
        using var owner = await app.Login(); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin); var id = Guid.NewGuid(); var input = Input();
        var first = await Put(owner, lesson, id, input); first.EnsureSuccessStatusCode(); var firstAck = await first.Content.ReadAsStringAsync(); var note = (await first.Content.ReadFromJsonAsync<LessonAnnotationView>())!;
        var a = input with { ClientRequestId = Guid.NewGuid(), ExpectedEtag = note.Etag, NoteText = "Tab A" }; var b = a with { ClientRequestId = Guid.NewGuid(), NoteText = "Tab B" };
        var concurrent = await Task.WhenAll(Put(owner, lesson, id, a), Put(owner, lesson, id, b)); Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(firstAck, await (await Put(owner, lesson, id, input)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Put(owner, lesson, id, input with { NoteText = "Changed request" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(owner, lesson, Guid.NewGuid(), input)).StatusCode);
        var current = await owner.GetFromJsonAsync<LessonAnnotationView>($"/api/v1/lessons/{lesson}/annotations/{id}"); Assert.NotEqual(note.Etag, current!.Etag); Assert.Contains(current.NoteText, new[] { "Tab A", "Tab B" });
    }

    [Fact]
    public async Task Removal_is_reversible_and_retried_removal_does_not_delete_a_restored_note()
    {
        using var owner = await app.Login(); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin); var id = Guid.NewGuid(); var input = Input();
        var created = await Put(owner, lesson, id, input); var row = (await created.Content.ReadFromJsonAsync<LessonAnnotationView>())!;
        var remove = new RemoveLessonAnnotation(Guid.NewGuid(), row.Etag); var deleted = await Remove(owner, lesson, id, remove); deleted.EnsureSuccessStatusCode(); var deletedAck = await deleted.Content.ReadAsStringAsync(); var tombstone = (await deleted.Content.ReadFromJsonAsync<LessonAnnotationView>())!;
        Assert.True(tombstone.IsDeleted); Assert.Empty((await owner.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{lesson}/annotations/"))!);
        var restore = input with { ClientRequestId = Guid.NewGuid(), ExpectedEtag = tombstone.Etag }; (await Put(owner, lesson, id, restore)).EnsureSuccessStatusCode();
        Assert.Equal(deletedAck, await (await Remove(owner, lesson, id, remove)).Content.ReadAsStringAsync());
        Assert.False((await owner.GetFromJsonAsync<LessonAnnotationView>($"/api/v1/lessons/{lesson}/annotations/{id}"))!.IsDeleted);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Remove(owner, lesson, id, new(Guid.NewGuid(), row.Etag))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Remove(owner, lesson, id, remove with { ExpectedEtag = tombstone.Etag })).StatusCode);
    }

    [Fact]
    public async Task Malformed_anchors_unpublished_content_and_empty_notes_are_rejected_without_partial_rows()
    {
        using var owner = await app.Login(); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin);
        var input = Input(); var invalid = new[] { input with { BlockKey = "quiz" }, input with { AnchorStart = -1 }, input with { AnchorEnd = Body.Length + 1 }, input with { AnchorStart = 3, AnchorEnd = 4, Quote = Body[3..4] }, input with { Quote = "Incorrect source" }, input with { NoteText = new string('a', 4001) }, input with { IsHighlighted = false, NoteText = "   " }, input with { ClientRequestId = Guid.Empty } };
        foreach (var change in invalid) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, lesson, Guid.NewGuid(), change)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, lesson, Guid.Empty, input)).StatusCode);
        var draft = await admin.PostAsJsonAsync("/api/v1/admin/content", new CreateContentInput($"note-draft-{Guid.NewGuid():N}", ContentKind.Lesson, Content())); draft.EnsureSuccessStatusCode();
        var draftId = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.NotFound, (await Put(owner, draftId, Guid.NewGuid(), input)).StatusCode);
        var exam = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/catalog")).EnumerateArray().First(x => x.GetProperty("kind").GetString() == "assessment").GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.NotFound, (await Put(owner, exam, Guid.NewGuid(), input)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{lesson}/annotations/"))!);
    }

    [Fact]
    public async Task Publication_keeps_anchors_on_old_versions_and_archived_source_remains_readable_only_to_its_note_owner()
    {
        using var owner = await app.Login(); using var other = await app.Login("learner2@englishhub.local"); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin); var id = Guid.NewGuid();
        (await Put(owner, lesson, id, Input())).EnsureSuccessStatusCode();
        var old = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{lesson}");
        var next = await admin.PostAsJsonAsync($"/api/v1/admin/content/{old.GetProperty("rootId").GetGuid()}/versions", Content() with { Body = "A replacement body has different wording and offsets.", Title = "New lesson version" }); next.EnsureSuccessStatusCode();
        var draft = await next.Content.ReadFromJsonAsync<JsonElement>(); var nextId = draft.GetProperty("id").GetGuid(); await Transition(admin, nextId, "publish", draft.GetProperty("etag").GetString()!);
        Assert.Empty((await owner.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{nextId}/annotations/"))!);
        Assert.Equal(Body, (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{lesson}")).GetProperty("body").GetString());
        await Transition(admin, lesson, "archive", old.GetProperty("etag").GetString()!);
        var historical = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{lesson}"); Assert.True(historical.GetProperty("archived").GetBoolean()); Assert.Equal(Body, historical.GetProperty("body").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/lessons/{lesson}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/lessons/{lesson}/annotations/")).StatusCode);
        Assert.Equal(Body[..26], (await owner.GetFromJsonAsync<LessonAnnotationView>($"/api/v1/lessons/{lesson}/annotations/{id}"))!.Quote);
    }

    [Fact]
    public async Task Concurrent_creates_respect_active_quota_and_removal_allows_a_new_note()
    {
        using var owner = await app.Login(); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin); var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"); var user = me.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
            db.LessonAnnotations.AddRange(Enumerable.Range(0, 99).Select(i => new LessonAnnotation { UserId = user, ContentVersionId = lesson, AnchorStart = 0, AnchorEnd = 1, Quote = "A", NoteText = $"Note {i}", UpdatedAt = app.Clock.UtcNow })); await db.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Put(owner, lesson, Guid.NewGuid(), Input()), Put(owner, lesson, Guid.NewGuid(), Input())); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.UnprocessableEntity);
        var accepted = (await results.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<LessonAnnotationView>())!; Assert.Equal(100, (await owner.GetFromJsonAsync<LessonAnnotationView[]>($"/api/v1/lessons/{lesson}/annotations/"))!.Length);
        (await Remove(owner, lesson, accepted.Id, new(Guid.NewGuid(), accepted.Etag))).EnsureSuccessStatusCode(); (await Put(owner, lesson, Guid.NewGuid(), Input())).EnsureSuccessStatusCode();
        var tombstone = await owner.GetFromJsonAsync<LessonAnnotationView>($"/api/v1/lessons/{lesson}/annotations/{accepted.Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, lesson, accepted.Id, Input() with { ExpectedEtag = tombstone!.Etag })).StatusCode);
    }

    [Fact]
    public async Task Saved_list_paginates_without_including_removed_or_other_owner_rows()
    {
        using var owner = await app.Login("learner3@englishhub.local"); using var admin = await app.Login("admin@englishhub.local"); var lesson = await Publish(admin); var user = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.LessonAnnotations.AddRange(Enumerable.Range(0, 32).Select(i => new LessonAnnotation { UserId = user, ContentVersionId = lesson, AnchorStart = 0, AnchorEnd = 1, Quote = "A", IsDeleted = i == 31, UpdatedAt = app.Clock.UtcNow })); await db.SaveChangesAsync();
        }
        var a = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/lesson-annotations"); var b = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/lesson-annotations?offset=30"); Assert.Equal(30, a.GetProperty("items").GetArrayLength()); Assert.Single(b.GetProperty("items").EnumerateArray()); Assert.Equal(JsonValueKind.Null, b.GetProperty("nextOffset").ValueKind);
        Assert.Equal(31, a.GetProperty("items").EnumerateArray().Concat(b.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync("/api/v1/me/lesson-annotations?offset=-1")).StatusCode);
    }

    private static SaveLessonAnnotation Input() => new(Guid.NewGuid(), 0, 26, Body[..26], "Remember this exact passage.");
    private static ContentInput Content() => new("Annotated lesson", "Original reading context", Body, "reading", 10, 600, false, "EnglishHub original · CC0", []);
    private static async Task<Guid> Publish(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/api/v1/admin/content", new CreateContentInput($"lesson-note-{Guid.NewGuid():N}", ContentKind.Lesson, Content())); created.EnsureSuccessStatusCode(); var draft = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = draft.GetProperty("id").GetGuid(); await Transition(admin, id, "publish", draft.GetProperty("etag").GetString()!); return id;
    }
    private static async Task Transition(HttpClient admin, Guid version, string action, string etag)
    {
        using var command = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/versions/{version}/{action}"); command.Headers.TryAddWithoutValidation("If-Match", etag); (await admin.SendAsync(command)).EnsureSuccessStatusCode();
    }
    private static Task<HttpResponseMessage> Put(HttpClient client, Guid version, Guid id, SaveLessonAnnotation input) => client.PutAsJsonAsync($"/api/v1/lessons/{version}/annotations/{id}", input);
    private static Task<HttpResponseMessage> Remove(HttpClient client, Guid version, Guid id, RemoveLessonAnnotation input) => client.PostAsJsonAsync($"/api/v1/lessons/{version}/annotations/{id}/remove", input);
}
