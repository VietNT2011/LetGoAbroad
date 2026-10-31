using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Fixture chỉ mô hình metadata; không giả có audio được upload. EN: Model media metadata explicitly; these tests neither upload speech nor claim blob playback.</summary>
public sealed class RecordingReflectionTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Empty_metadata_reads_are_private_and_have_no_write_or_storage_effects()
    {
        using var bank = await MakeBank(); var before = await Effects(); var view = await Read(bank);
        Assert.Null(view.Etag); Assert.Equal("", view.Notes); Assert.Null(view.Clarity); Assert.Null(view.UpdatedAt);
        Assert.Empty((await Page(bank.Client)).GetProperty("items").EnumerateArray()); Assert.Equal(before, await Effects());
        Assert.True((await bank.Client.GetAsync(Path(bank))).Headers.CacheControl?.NoStore); Assert.True((await bank.Client.GetAsync("/api/v1/me/recording-journal")).Headers.CacheControl?.NoStore);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(bank))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bank.Client.GetAsync("/api/v1/me/recording-journal?offset=-1")).StatusCode);
    }

    [Fact]
    public async Task Saved_subjective_feedback_replays_exactly_and_does_not_create_learning_progress()
    {
        using var bank = await MakeBank(); var input = Input(); var first = await Put(bank, input); first.EnsureSuccessStatusCode(); var body = await first.Content.ReadAsStringAsync(); var saved = JsonSerializer.Deserialize<RecordingReflectionView>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } })!;
        Assert.Equal("My own feedback", saved.Notes); Assert.Equal(3, saved.Clarity); Assert.Equal(4, saved.Rhythm); Assert.NotNull(saved.Etag);
        var replay = await Put(bank, input); replay.EnsureSuccessStatusCode(); Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Single(await db.RecordingReflections.Where(x => x.MediaAssetId == bank.Asset).ToArrayAsync()); Assert.Equal(1, await db.CommandReceipts.CountAsync(x => x.UserId == bank.User && x.Scope == "recording-reflection"));
        Assert.False(await db.Attempts.AnyAsync(x => x.UserId == bank.User)); Assert.False(await db.LessonProgress.AnyAsync(x => x.UserId == bank.User));
        var json = (await Page(bank.Client)).GetProperty("items")[0].GetRawText(); foreach (var key in new[] { "stagingKey", "sourceKey", "renditionKey", "uploadUrl", "sha256" }) Assert.DoesNotContain(key, json);
    }

    [Fact]
    public async Task Reused_command_with_a_different_payload_is_rejected_without_overwriting()
    {
        using var bank = await MakeBank(); var input = Input(); (await Put(bank, input)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Put(bank, input with { Notes = "Different payload" })).StatusCode); Assert.Equal(input.Notes, (await Read(bank)).Notes);
    }

    [Fact]
    public async Task Stale_creation_and_stale_edits_preserve_current_feedback()
    {
        using var bank = await MakeBank(); Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(bank, Input() with { ExpectedEtag = "old" })).StatusCode);
        (await Put(bank, Input())).EnsureSuccessStatusCode(); var first = await Read(bank);
        (await Put(bank, Input() with { ExpectedEtag = first.Etag, Notes = "Newer version" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(bank, Input() with { ExpectedEtag = first.Etag, Notes = "Unseen overwrite" })).StatusCode);
        Assert.Equal("Newer version", (await Read(bank)).Notes);
    }

    [Fact]
    public async Task Competing_edits_accept_only_one_expected_version()
    {
        using var bank = await MakeBank(); (await Put(bank, Input())).EnsureSuccessStatusCode(); var current = await Read(bank);
        var outcomes = await Task.WhenAll(Put(bank, Input() with { ExpectedEtag = current.Etag, Notes = "Device A" }), Put(bank, Input() with { ExpectedEtag = current.Etag, Notes = "Device B" }));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Contains((await Read(bank)).Notes, new[] { "Device A", "Device B" });
    }

    [Fact]
    public async Task Noop_retains_etag_and_update_time_but_real_clear_is_a_new_version()
    {
        using var bank = await MakeBank(); (await Put(bank, Input())).EnsureSuccessStatusCode(); var first = await Read(bank); app.Clock.Advance(TimeSpan.FromSeconds(1));
        (await Put(bank, Input() with { ExpectedEtag = first.Etag })).EnsureSuccessStatusCode(); var noop = await Read(bank); Assert.Equal(first.Etag, noop.Etag); Assert.Equal(first.UpdatedAt, noop.UpdatedAt);
        (await Put(bank, Input() with { ExpectedEtag = noop.Etag, Notes = "", Clarity = null, Rhythm = null })).EnsureSuccessStatusCode(); var cleared = await Read(bank); Assert.Equal("", cleared.Notes); Assert.Null(cleared.Clarity); Assert.NotEqual(first.Etag, cleared.Etag);
    }

    [Fact]
    public async Task Concurrent_first_saves_create_one_private_row_and_reject_the_unseen_version()
    {
        using var bank = await MakeBank(); var outcomes = await Task.WhenAll(Put(bank, Input() with { Notes = "First device" }), Put(bank, Input() with { Notes = "Second device" }));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
        using var scope = app.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<HubDbContext>().RecordingReflections.CountAsync(x => x.MediaAssetId == bank.Asset));
    }

    [Fact]
    public async Task Exact_note_length_boundary_accepts_unrated_text_without_trimming()
    {
        using var bank = await MakeBank(); var notes = " " + new string('x', 1998) + " "; (await Put(bank, Input() with { Notes = notes, Clarity = null, Rhythm = null })).EnsureSuccessStatusCode();
        var saved = await Read(bank); Assert.Equal(notes, saved.Notes); Assert.Null(saved.Clarity); Assert.Null(saved.Rhythm);
    }

    [Theory]
    [InlineData(MediaStatus.PendingUpload)] [InlineData(MediaStatus.Uploaded)] [InlineData(MediaStatus.Processing)] [InlineData(MediaStatus.Failed)] [InlineData(MediaStatus.DeletePending)] [InlineData(MediaStatus.Deleted)] [InlineData(MediaStatus.Expired)]
    public async Task New_feedback_requires_a_verified_ready_recording(MediaStatus status)
    {
        using var bank = await MakeBank(status); Assert.Equal(HttpStatusCode.Conflict, (await Put(bank, Input())).StatusCode); Assert.Null((await Read(bank)).Etag);
    }

    [Theory]
    [InlineData(0)] [InlineData(6)] [InlineData(-1)]
    public async Task Invalid_subjective_levels_are_not_accepted(int rating)
    {
        using var bank = await MakeBank(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(bank, Input() with { Clarity = rating })).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(bank, Input() with { Rhythm = rating })).StatusCode); Assert.Null((await Read(bank)).Etag);
    }

    [Fact]
    public async Task Empty_initial_feedback_and_oversized_notes_are_rejected_without_a_row()
    {
        using var bank = await MakeBank(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(bank, Input() with { Notes = " ", Clarity = null, Rhythm = null })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(bank, Input() with { Notes = new string('x', 2001) })).StatusCode); Assert.Null((await Read(bank)).Etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(bank, Input() with { ClientRequestId = Guid.Empty })).StatusCode);
    }

    [Fact]
    public async Task Other_owners_admin_and_lesson_audio_cannot_read_or_write_private_reflections()
    {
        using var bank = await MakeBank(); (await Put(bank, Input())).EnsureSuccessStatusCode(); using var other = await MakeBank(); using var admin = await app.Login("admin@englishhub.local");
        foreach (var client in new[] { other.Client, admin }) { Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(bank))).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Path(bank), Input())).StatusCode); }
        Assert.Empty((await Page(other.Client)).GetProperty("items").EnumerateArray());
        using var audio = await MakeBank(purpose: "lesson_audio"); Assert.Equal(HttpStatusCode.NotFound, (await audio.Client.GetAsync(Path(audio))).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Put(audio, Input())).StatusCode);
        bank.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await Put(bank, Input())).StatusCode);
    }

    [Fact]
    public async Task Deleted_audio_keeps_owner_notes_and_allows_clearing_without_restoring_media()
    {
        using var bank = await MakeBank(); var input = Input(); var original = await Put(bank, input); original.EnsureSuccessStatusCode(); var originalBody = await original.Content.ReadAsStringAsync(); var current = await Read(bank);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); await db.MediaAssets.Where(x => x.Id == bank.Asset).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, MediaStatus.Deleted));
        var row = (await Page(bank.Client)).GetProperty("items")[0]; Assert.Equal("deleted", row.GetProperty("status").GetString()); Assert.Equal(input.Notes, row.GetProperty("notes").GetString());
        var replay = await Put(bank, input); Assert.Equal(originalBody, await replay.Content.ReadAsStringAsync());
        (await Put(bank, Input() with { ExpectedEtag = current.Etag, Notes = "", Clarity = null, Rhythm = null })).EnsureSuccessStatusCode(); Assert.Equal(MediaStatus.Deleted, (await Read(bank)).Status); Assert.Equal(MediaStatus.Deleted, (await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == bank.Asset)).Status);
    }

    [Fact]
    public async Task Journal_pages_are_bounded_stable_and_include_deleted_metadata_without_owner_query_override()
    {
        using var bank = await MakeBank(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        for (var i = 0; i < 31; i++) { var asset = new MediaAsset { OwnerId = bank.User, Purpose = "recording", FileName = $"Journal {i}", Status = MediaStatus.Deleted, CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow }; db.MediaAssets.Add(asset); db.RecordingReflections.Add(new RecordingReflection { MediaAssetId = asset.Id, Notes = "Retained private reflection", UpdatedAt = app.Clock.UtcNow }); } await db.SaveChangesAsync();
        var first = await Page(bank.Client); Assert.Equal(30, first.GetProperty("items").GetArrayLength()); Assert.Equal(30, first.GetProperty("nextOffset").GetInt32()); Assert.Equal(first.GetRawText(), (await Page(bank.Client)).GetRawText());
        var second = await bank.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/recording-journal?offset=30&ownerId=00000000-0000-0000-0000-000000000000"); Assert.Single(second.GetProperty("items").EnumerateArray()); Assert.Equal(JsonValueKind.Null, second.GetProperty("nextOffset").ValueKind);
        Assert.Equal(31, first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("assetId").GetGuid()).Distinct().Count());
    }

    private sealed record Bank(Guid User, Guid Asset, HttpClient Client) : IDisposable { public void Dispose() => Client.Dispose(); }
    private async Task<Bank> MakeBank(MediaStatus status = MediaStatus.Ready, string purpose = "recording")
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var email = $"reflection-{Guid.NewGuid():N}@example.com";
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Reflection metadata fixture" }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded);
        var asset = new MediaAsset { OwnerId = user.Id, Purpose = purpose, FileName = "Private metadata fixture.wav", Status = status, CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow.AddMinutes(15) }; db.MediaAssets.Add(asset); await db.SaveChangesAsync(); return new(user.Id, asset.Id, await app.Login(email));
    }
    private static string Path(Bank bank) => $"/api/v1/media/{bank.Asset}/reflection";
    private static SaveRecordingReflection Input() => new(Guid.NewGuid(), null, "My own feedback", 3, 4);
    private static Task<HttpResponseMessage> Put(Bank bank, SaveRecordingReflection input) => bank.Client.PutAsJsonAsync(Path(bank), input);
    private static async Task<RecordingReflectionView> Read(Bank bank) => (await bank.Client.GetFromJsonAsync<RecordingReflectionView>(Path(bank), new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } }))!;
    private static Task<JsonElement> Page(HttpClient client) => client.GetFromJsonAsync<JsonElement>("/api/v1/me/recording-journal");
    private async Task<string> Effects() { using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); return JsonSerializer.Serialize(new { reflections = await db.RecordingReflections.CountAsync(), receipts = await db.CommandReceipts.CountAsync(), audits = await db.AuditEntries.CountAsync(), attempts = await db.Attempts.CountAsync(), lessons = await db.LessonProgress.CountAsync() }); }
}
