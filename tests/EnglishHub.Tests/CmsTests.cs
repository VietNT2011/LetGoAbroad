using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Domain;

public sealed class CmsTests(TestApp app) : IClassFixture<TestApp>
{
    private static ContentInput Lesson(string body = "Original lesson body with enough context for publication.") => new("Original lesson", "A lesson to test immutable versions.", body, "grammar", 10, 1200, false, "EnglishHub original · CC0", []);
    private static HttpRequestMessage Command(HttpMethod method, string route, string etag, object? body = null)
    {
        var request = new HttpRequestMessage(method, route); request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (body is not null) request.Content = JsonContent.Create(body); return request;
    }

    [Fact]
    public async Task Published_payload_is_immutable_and_only_new_versions_can_change_it()
    {
        using var client = await app.Login("admin@englishhub.local");
        var input = new CreateContentInput($"test-immutable-{Guid.NewGuid():N}", ContentKind.Lesson, Lesson());
        var created = await client.PostAsJsonAsync("/api/v1/admin/content", input); created.EnsureSuccessStatusCode();
        var draft = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = draft.GetProperty("id").GetGuid();
        var published = await client.SendAsync(Command(HttpMethod.Post,$"/api/v1/admin/versions/{id}/publish",draft.GetProperty("etag").GetString()!)); published.EnsureSuccessStatusCode();
        var pub = await published.Content.ReadFromJsonAsync<JsonElement>();
        var edit = await client.SendAsync(Command(HttpMethod.Put,$"/api/v1/admin/versions/{id}",pub.GetProperty("etag").GetString()!,Lesson("Changed published body must be rejected and preserve its original text.")));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        var old = await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{id}");
        Assert.Equal(input.Content.Body,old.GetProperty("body").GetString());
        var next = await client.PostAsJsonAsync($"/api/v1/admin/content/{old.GetProperty("rootId").GetGuid()}/versions",Lesson("A new authored version remains independent from the earlier published lesson.")); next.EnsureSuccessStatusCode();
        Assert.NotEqual(id,(await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Import_dry_run_does_not_persist_and_import_rolls_back_every_row_on_conflict()
    {
        using var client = await app.Login("admin@englishhub.local");
        var slug = $"test-import-{Guid.NewGuid():N}"; var item = new CreateContentInput(slug,ContentKind.Lesson,Lesson());
        (await client.PostAsJsonAsync("/api/v1/admin/imports/dry-run",new[] { item })).EnsureSuccessStatusCode();
        var imported = await client.PostAsJsonAsync("/api/v1/admin/imports",new ImportInput(Guid.NewGuid(),[item])); imported.EnsureSuccessStatusCode();
        var secondSlug = $"test-import-second-{Guid.NewGuid():N}";
        var conflict = await client.PostAsJsonAsync("/api/v1/admin/imports",new ImportInput(Guid.NewGuid(),[item with { Slug = secondSlug },item]));
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        // VI: Nếu rollback đúng, cùng slug thứ hai vẫn tạo mới được. EN: Successful creation proves the earlier row rolled back.
        (await client.PostAsJsonAsync("/api/v1/admin/content",item with { Slug = secondSlug })).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Draft_edits_require_current_etag_and_persist_the_replacement()
    {
        using var client=await app.Login("admin@englishhub.local"); var input=new CreateContentInput($"test-edit-{Guid.NewGuid():N}",ContentKind.Lesson,Lesson());
        var created=await client.PostAsJsonAsync("/api/v1/admin/content",input); created.EnsureSuccessStatusCode(); var v=await created.Content.ReadFromJsonAsync<JsonElement>();
        var id=v.GetProperty("id").GetGuid(); var etag=v.GetProperty("etag").GetString()!;
        var changed=Lesson("This draft has a new, complete body that the author can review before publishing.");
        (await client.SendAsync(Command(HttpMethod.Put,$"/api/v1/admin/versions/{id}",etag,changed))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.PreconditionFailed,(await client.SendAsync(Command(HttpMethod.Put,$"/api/v1/admin/versions/{id}",etag,Lesson()))).StatusCode);
        Assert.Equal(changed.Body,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{id}")).GetProperty("body").GetString());
    }
}
