using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Api;

/// <summary>VI: Header không cấp quyền, chỉ chặn cookie đổi actor trước nghiệp vụ/CSRF retry. EN: The header never grants identity; a changed cookie must fail before business effects or CSRF retry.</summary>
public sealed class ActorPreconditionTests(TestApp app) : IClassFixture<TestApp>
{
    private const string Cards = "/api/v1/cards/";

    [Fact]
    public async Task Matching_actor_can_read_and_write_but_must_still_supply_csrf()
    {
        using var client = await app.Login(); var id = await Actor(client); client.DefaultRequestHeaders.Add(ActorPrecondition.Header, id);
        var body = new { clientRequestId = Guid.NewGuid(), front = $"Actor match {Guid.NewGuid():N}", back = "A private original fixture" };
        var created = await client.PostAsJsonAsync(Cards, body); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var read = await client.GetAsync(Cards); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.Equal("no-store", read.Headers.CacheControl!.ToString());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Error(await client.PostAsJsonAsync(Cards, body), HttpStatusCode.BadRequest, "invalid_csrf");
    }

    [Fact]
    public async Task Old_actor_cannot_create_under_a_new_cookie_even_with_fresh_csrf()
    {
        using var old = await app.Login(); var actor = await Actor(old);
        using var next = await app.Login("learner2@englishhub.local");
        var before = await next.GetStringAsync(Cards);
        next.DefaultRequestHeaders.Add(ActorPrecondition.Header, actor);
        await Error(await next.GetAsync(Cards), HttpStatusCode.Conflict, "session_changed");
        await Error(await next.PostAsJsonAsync(Cards, new { clientRequestId = Guid.NewGuid(), front = "Must never appear", back = "Wrong-owner tab" }), HttpStatusCode.Conflict, "session_changed");
        // VI: Mismatch phải thắng cả token sai để frontend không refresh rồi gửi sang B. EN: Actor mismatch precedes stale CSRF so retry cannot move the payload to B.
        next.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Error(await next.PostAsJsonAsync(Cards, new { clientRequestId = Guid.NewGuid(), front = "Still rejected", back = "No source" }), HttpStatusCode.Conflict, "session_changed");
        next.DefaultRequestHeaders.Remove(ActorPrecondition.Header);
        Assert.Equal(before, await next.GetStringAsync(Cards));
    }

    [Theory]
    [InlineData("Origin", "http://localhost")]
    [InlineData("Sec-Fetch-Mode", "cors")]
    public async Task Old_browser_write_requires_reload_before_even_checking_csrf(string header, string value)
    {
        using var client = await app.Login(); client.DefaultRequestHeaders.Add(header, value);
        await Error(await client.PostAsJsonAsync(Cards, new { clientRequestId = Guid.NewGuid(), front = "Old JS", back = "Reload required" }), (HttpStatusCode)428, "identity_precondition_required");
        client.DefaultRequestHeaders.Add(ActorPrecondition.Header, await Actor(client));
        (await client.PostAsJsonAsync(Cards, new { clientRequestId = Guid.NewGuid(), front = $"Current JS {Guid.NewGuid():N}", back = "Bound to the verified owner" })).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("11111111111111111111111111111111")]
    [InlineData("not-an-actor")]
    [InlineData("anonymous,anonymous")]
    public async Task Invalid_actor_is_rejected_with_a_stable_error(string header)
    {
        using var client = await app.Login(); client.DefaultRequestHeaders.TryAddWithoutValidation(ActorPrecondition.Header, header);
        await Error(await client.GetAsync(Cards), HttpStatusCode.BadRequest, "invalid_actor_precondition");
    }

    [Fact]
    public async Task Bootstrap_is_unbound_but_me_and_logout_honor_supplied_actor()
    {
        using var client = await app.Login(); client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add(ActorPrecondition.Header, Guid.NewGuid().ToString());
        (await client.GetAsync("/api/v1/auth/csrf")).EnsureSuccessStatusCode();
        await Error(await client.GetAsync("/api/v1/auth/me"), HttpStatusCode.Conflict, "session_changed");
        await Error(await client.PostAsJsonAsync("/api/v1/auth/logout", new { }), HttpStatusCode.Conflict, "session_changed");
        client.DefaultRequestHeaders.Remove(ActorPrecondition.Header);
        (await client.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Actor_does_not_override_authentication_or_roles_and_native_reads_remain_available()
    {
        using var learner = await app.Login(); learner.DefaultRequestHeaders.Add(ActorPrecondition.Header, await Actor(learner));
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/v1/admin/jobs")).StatusCode);
        using var anonymous = app.CreateClient(); anonymous.DefaultRequestHeaders.Add(ActorPrecondition.Header, await Actor(learner));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Cards)).StatusCode);
        learner.DefaultRequestHeaders.Remove(ActorPrecondition.Header); learner.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "no-cors");
        Assert.Contains((await learner.GetAsync($"/api/v1/media/{Guid.NewGuid()}/content")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable });
        learner.DefaultRequestHeaders.Add(ActorPrecondition.Header, "anonymous");
        await Error(await learner.GetAsync(Cards), HttpStatusCode.Conflict, "session_changed");
    }

    private static async Task<string> Actor(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetString()!;
    private static async Task Error(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
