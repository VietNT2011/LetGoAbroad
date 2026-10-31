using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Fixture mô hình metadata và quyền; không khẳng định đã upload giọng nói. EN: Metadata fixtures exercise ownership/history without claiming uploaded human speech.</summary>
public sealed class RecordingContextTests(TestApp app) : IClassFixture<TestApp>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } };

    [Fact]
    public async Task Reading_empty_history_is_private_noncacheable_and_does_not_create_a_baseline()
    {
        using var bank = await Bank(); var before = await Counts();
        var result = await bank.Client.GetAsync(HistoryPath(bank)); result.EnsureSuccessStatusCode(); Assert.True(result.Headers.CacheControl?.NoStore);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>(); Assert.Empty(body.GetProperty("items").EnumerateArray()); Assert.Equal(before, await Counts());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await bank.Client.GetAsync(HistoryPath(bank) + "?before=-1")).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(HistoryPath(bank))).StatusCode);
    }

    [Fact]
    public async Task Real_note_edits_append_immutable_versions_while_noops_and_replays_do_not()
    {
        using var bank = await Bank(); var input = Note("First raw note"); var first = await Save(bank, input); first.EnsureSuccessStatusCode(); var firstBody = await first.Content.ReadAsStringAsync();
        var one = await Read(bank); Assert.Equal(1, one.Revision);
        (await Save(bank, Note("Second note", one.Etag))).EnsureSuccessStatusCode(); var two = await Read(bank);
        (await Save(bank, Note("Second note", two.Etag))).EnsureSuccessStatusCode(); Assert.Equal(two.Etag, (await Read(bank)).Etag);
        (await Save(bank, Note("", two.Etag))).EnsureSuccessStatusCode(); var three = await Read(bank); Assert.Equal(3, three.Revision);
        var replay = await Save(bank, input); Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());
        var rows = (await History(bank)).GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(new[] { 3, 2, 1 }, rows.Select(x => x.GetProperty("number").GetInt32()).ToArray());
        Assert.Equal("First raw note", rows[2].GetProperty("notes").GetString()); Assert.Equal("Second note", rows[1].GetProperty("notes").GetString()); Assert.Equal("", rows[0].GetProperty("notes").GetString());
    }

    [Fact]
    public async Task An_unversioned_existing_note_is_captured_only_on_its_first_real_edit()
    {
        using var bank = await Bank(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var oldTime = app.Clock.UtcNow.AddDays(-2);
        var row = new RecordingReflection { MediaAssetId = bank.Asset, Notes = "Legacy actual note", UpdatedAt = oldTime }; db.RecordingReflections.Add(row); await db.SaveChangesAsync();
        var initial = await Read(bank); Assert.Equal(0, initial.Revision); Assert.Empty((await History(bank)).GetProperty("items").EnumerateArray());
        (await Save(bank, Note(row.Notes, initial.Etag))).EnsureSuccessStatusCode(); Assert.Empty((await History(bank)).GetProperty("items").EnumerateArray());
        (await Save(bank, Note("New edit", initial.Etag))).EnsureSuccessStatusCode(); var history = (await History(bank)).GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(2, history.Length);
        Assert.Equal("legacy_snapshot", history[1].GetProperty("kind").GetString()); Assert.Equal(0, history[1].GetProperty("number").GetInt32()); Assert.Equal(oldTime, history[1].GetProperty("updatedAt").GetDateTimeOffset()); Assert.Equal(app.Clock.UtcNow, history[1].GetProperty("capturedAt").GetDateTimeOffset()); Assert.Equal("Legacy actual note", history[1].GetProperty("notes").GetString());
    }

    [Fact]
    public async Task Old_note_receipt_hash_and_response_shape_remain_replayable_after_upgrade()
    {
        using var bank = await Bank(); var input = Note("Old accepted payload"); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var row = new RecordingReflection { MediaAssetId = bank.Asset, Notes = input.Notes, UpdatedAt = app.Clock.UtcNow }; db.RecordingReflections.Add(row); await db.SaveChangesAsync();
        var old = new { AssetId = bank.Asset, FileName = "Private recording fixture", Status = MediaStatus.Ready, Etag = Etags.Of(row), Notes = row.Notes, Clarity = (int?)null, Rhythm = (int?)null, UpdatedAt = (DateTimeOffset?)row.UpdatedAt };
        var oldHash = Receipts.Hash(new { assetId = bank.Asset, input = new { input.ClientRequestId, input.ExpectedEtag, input.Notes, input.Clarity, input.Rhythm } });
        new Receipts(db, app.Clock).Save(bank.User, "recording-reflection", input.ClientRequestId, oldHash, row.Id, old); await db.SaveChangesAsync();
        var replay = await Save(bank, input); replay.EnsureSuccessStatusCode(); var json = await replay.Content.ReadAsStringAsync(); Assert.DoesNotContain("revision", json); Assert.DoesNotContain("source", json); Assert.Equal(input.Notes, (await replay.Content.ReadFromJsonAsync<RecordingReflectionView>(Json))!.Notes); Assert.Empty((await History(bank)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Linking_a_sample_creates_context_without_notes_scores_or_transcript_disclosure()
    {
        using var bank = await Bank(); var source = await Sample(); var input = Link(source.Version, source.Segment); var accepted = await SetSource(bank, input); accepted.EnsureSuccessStatusCode(); var firstBody = await accepted.Content.ReadAsStringAsync();
        var current = await Read(bank); Assert.Equal(1, current.Revision); Assert.Equal("", current.Notes); Assert.Null(current.Clarity); Assert.Equal(source.Version, current.Source!.VersionId); Assert.Equal(source.Segment, current.Source.SegmentId); Assert.Equal(0, current.Source.SegmentOrder); Assert.True(current.Source.AudioAvailable);
        Assert.DoesNotContain("Secret authored sentence", firstBody); Assert.DoesNotContain("acceptedAnswers", firstBody); Assert.DoesNotContain("textEn", firstBody);
        var replay = await SetSource(bank, input); Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.LessonProgress.AnyAsync(x => x.UserId == bank.User)); Assert.False(await db.Attempts.AnyAsync(x => x.UserId == bank.User)); Assert.False(await db.ListeningDisclosures.AnyAsync(x => x.UserId == bank.User));
        Assert.Single((await History(bank)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Source_changes_preserve_server_notes_and_each_revision_keeps_its_own_version()
    {
        using var bank = await Bank(); var firstSource = await Sample(); var secondSource = await Sample(); (await Save(bank, Note("Saved notes"))).EnsureSuccessStatusCode(); var first = await Read(bank);
        (await SetSource(bank, Link(firstSource.Version, firstSource.Segment, first.Etag))).EnsureSuccessStatusCode(); var linked = await Read(bank); Assert.Equal("Saved notes", linked.Notes);
        (await Save(bank, Note("Edited notes", linked.Etag))).EnsureSuccessStatusCode(); var edited = await Read(bank);
        (await SetSource(bank, Link(secondSource.Version, null, edited.Etag))).EnsureSuccessStatusCode(); var rebound = await Read(bank);
        (await SetSource(bank, Link(null, null, rebound.Etag))).EnsureSuccessStatusCode(); var cleared = await Read(bank); Assert.Null(cleared.Source); Assert.Equal("Edited notes", cleared.Notes); Assert.Equal(5, cleared.Revision);
        var history = (await History(bank)).GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(secondSource.Version, history[1].GetProperty("source").GetProperty("versionId").GetGuid()); Assert.Equal(firstSource.Version, history[2].GetProperty("source").GetProperty("versionId").GetGuid()); Assert.Equal("Saved notes", history[3].GetProperty("notes").GetString());
    }

    [Fact]
    public async Task Source_noops_stale_edits_and_changed_payload_receipts_do_not_add_history()
    {
        using var bank = await Bank(); var source = await Sample(); var input = Link(source.Version); (await SetSource(bank, input)).EnsureSuccessStatusCode(); var first = await Read(bank);
        (await SetSource(bank, Link(source.Version, null, first.Etag))).EnsureSuccessStatusCode(); Assert.Equal(first.Etag, (await Read(bank)).Etag);
        Assert.Equal(HttpStatusCode.Conflict, (await SetSource(bank, input with { SourceSegmentId = source.Segment })).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Save(bank, Note("Stale draft", null))).StatusCode);
        Assert.Single((await History(bank)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_source_kinds_states_and_cross_version_segments_are_atomic_rejections()
    {
        using var bank = await Bank(); var valid = await Sample(); var different = await Sample(); var invalid = new[] {
            await Sample(PublicationStatus.Draft), await Sample(PublicationStatus.Archived), await Sample(fixture: true), await Sample(kind: ContentKind.Assessment), await Sample(purpose: "recording"), await Sample(mediaStatus: MediaStatus.Failed), await Sample(withMedia: false)
        };
        foreach (var source in invalid) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetSource(bank, Link(source.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetSource(bank, Link(valid.Version, different.Segment))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetSource(bank, Link(null, valid.Segment))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetSource(bank, Link(Guid.NewGuid()))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetSource(bank, Link(null))).StatusCode);
        Assert.Null((await Read(bank)).Etag); Assert.Empty((await History(bank)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Source_history_and_writes_obey_owner_admin_csrf_and_ready_boundaries()
    {
        using var bank = await Bank(); using var other = await Bank(); using var admin = await app.Login("admin@englishhub.local"); var source = await Sample(); (await SetSource(bank, Link(source.Version))).EnsureSuccessStatusCode();
        foreach (var client in new[] { other.Client, admin }) { Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(HistoryPath(bank))).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(SourcePath(bank), Link(source.Version))).StatusCode); }
        using var unready = await Bank(MediaStatus.Uploaded); Assert.Equal(HttpStatusCode.Conflict, (await SetSource(unready, Link(source.Version))).StatusCode);
        bank.Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await SetSource(bank, Link(null))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_note_and_source_changes_share_one_expected_version()
    {
        using var bank = await Bank(); var source = await Sample(); (await Save(bank, Note("Original"))).EnsureSuccessStatusCode(); var current = await Read(bank);
        var outcomes = await Task.WhenAll(Save(bank, Note("New text", current.Etag)), SetSource(bank, Link(source.Version, null, current.Etag)));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.PreconditionFailed); Assert.Equal(2, (await Read(bank)).Revision); Assert.Equal(2, (await History(bank)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Deleted_recordings_keep_source_history_and_replay_without_restoring_playback()
    {
        using var bank = await Bank(); var source = await Sample(); var input = Link(source.Version); var accepted = await SetSource(bank, input); var original = await accepted.Content.ReadAsStringAsync();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); await db.MediaAssets.Where(a => a.Id == bank.Asset).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, MediaStatus.Deleted));
        var replay = await SetSource(bank, input); Assert.Equal(original, await replay.Content.ReadAsStringAsync()); var current = await Read(bank); Assert.Equal(MediaStatus.Deleted, current.Status); Assert.NotNull(current.Source);
        (await SetSource(bank, Link(null, null, current.Etag))).EnsureSuccessStatusCode(); Assert.Equal(2, (await History(bank)).GetProperty("items").GetArrayLength()); Assert.Equal(MediaStatus.Deleted, (await Read(bank)).Status);
    }

    [Fact]
    public async Task Archived_samples_remain_readable_via_owned_history_after_rebinding_and_clearing()
    {
        using var bank = await Bank(); using var other = await Bank(); var source = await Sample(); (await SetSource(bank, Link(source.Version))).EnsureSuccessStatusCode(); var first = await Read(bank); (await SetSource(bank, Link(null, null, first.Etag))).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); await db.ContentVersions.Where(v => v.Id == source.Version).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, PublicationStatus.Archived));
        Assert.Equal(HttpStatusCode.OK, (await bank.Client.GetAsync($"/api/v1/lessons/{source.Version}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/lessons/{source.Version}")).StatusCode);
        var storage = new MetadataGrantStorage(); var service = new MediaService(db, new Transactions(db), new Receipts(db, app.Clock), storage, app.Clock);
        Assert.Equal("metadata-test-grant", await service.Playback(bank.User, source.Media, false)); Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => service.Playback(other.User, source.Media, false))).Status);
        var afterClear = await Read(bank); (await SetSource(bank, Link(source.Version, source.Segment, afterClear.Etag))).EnsureSuccessStatusCode(); Assert.True((await Read(bank)).Source!.Archived);
        await db.MediaAssets.Where(a => a.Id == source.Media).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, MediaStatus.Deleted)); Assert.False((await Read(bank)).Source!.AudioAvailable); Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => service.Playback(bank.User, source.Media, false))).Status);
    }

    [Fact]
    public async Task History_cursor_is_stable_bounded_and_excludes_newer_edits_between_pages()
    {
        using var bank = await Bank(); string? etag = null;
        for (var i = 1; i <= 35; i++) { var response = await Save(bank, Note($"Version {i}", etag)); response.EnsureSuccessStatusCode(); etag = (await response.Content.ReadFromJsonAsync<RecordingReflectionView>(Json))!.Etag; }
        var first = await History(bank); Assert.Equal(30, first.GetProperty("items").GetArrayLength()); Assert.Equal(6, first.GetProperty("nextBefore").GetInt32());
        (await Save(bank, Note("New head", etag))).EnsureSuccessStatusCode(); var second = await bank.Client.GetFromJsonAsync<JsonElement>(HistoryPath(bank) + "?before=6"); Assert.Equal(5, second.GetProperty("items").GetArrayLength()); Assert.Equal(35, first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("number").GetInt32()).Distinct().Count());
    }

    [Fact]
    public async Task Composite_source_foreign_keys_reject_cross_version_segments_in_direct_writes()
    {
        using var bank = await Bank(); var one = await Sample(); var two = await Sample(); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        db.RecordingReflections.Add(new RecordingReflection { MediaAssetId = bank.Asset, Notes = "Invalid fixture only", SourceContentVersionId = one.Version, SourceSegmentId = two.Segment, UpdatedAt = app.Clock.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task An_owned_historical_listening_session_can_supply_its_archived_sample()
    {
        using var bank = await Bank(); var source = await Sample();
        var started = await bank.Client.PostAsJsonAsync($"/api/v1/lessons/{source.Version}/listening", new EnglishHub.Infrastructure.Learning.StartListening(Guid.NewGuid())); started.EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); await db.ContentVersions.Where(v => v.Id == source.Version).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, PublicationStatus.Archived));
        (await SetSource(bank, Link(source.Version, source.Segment))).EnsureSuccessStatusCode(); Assert.True((await Read(bank)).Source!.Archived);
    }

    [Fact]
    public async Task Recording_sample_timing_is_version_bound_read_only_and_never_reveals_transcript()
    {
        using var bank = await Bank(); var source = await Sample(); var before = await Counts();
        var result = await bank.Client.GetAsync($"/api/v1/lessons/{source.Version}?segment={source.Segment}"); result.EnsureSuccessStatusCode();
        var body = await result.Content.ReadFromJsonAsync<JsonElement>(); var timing = body.GetProperty("recordingSegment");
        Assert.Equal(new[] { "endMs", "id", "order", "startMs" }, timing.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(source.Segment, timing.GetProperty("id").GetGuid()); Assert.Equal(0, timing.GetProperty("startMs").GetInt64()); Assert.Equal(3000, timing.GetProperty("endMs").GetInt64());
        Assert.DoesNotContain("Secret authored sentence", body.GetRawText()); Assert.DoesNotContain("acceptedAnswers", body.GetRawText()); Assert.Equal(before, await Counts());
        var ordinary = await bank.Client.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{source.Version}"); Assert.Equal(JsonValueKind.Null, ordinary.GetProperty("recordingSegment").ValueKind);
    }

    [Fact]
    public async Task Recording_sample_timing_rejects_other_versions_and_keeps_archived_owner_access()
    {
        using var bank = await Bank(); using var stranger = await Bank(); var source = await Sample(); var other = await Sample();
        Assert.Equal(HttpStatusCode.NotFound, (await bank.Client.GetAsync($"/api/v1/lessons/{source.Version}?segment={other.Segment}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bank.Client.GetAsync($"/api/v1/lessons/{source.Version}?segment={Guid.NewGuid()}")).StatusCode);
        (await SetSource(bank, Link(source.Version, source.Segment))).EnsureSuccessStatusCode(); var row = await Read(bank); (await SetSource(bank, Link(null, null, row.Etag))).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); await db.ContentVersions.Where(v => v.Id == source.Version).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, PublicationStatus.Archived));
        (await bank.Client.GetAsync($"/api/v1/lessons/{source.Version}?segment={source.Segment}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/v1/lessons/{source.Version}?segment={source.Segment}")).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/lessons/{source.Version}?segment={source.Segment}")).StatusCode);
    }

    private sealed record RecordingBank(Guid User, Guid Asset, HttpClient Client) : IDisposable { public void Dispose() => Client.Dispose(); }
    private async Task<RecordingBank> Bank(MediaStatus status = MediaStatus.Ready)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"context-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Context metadata fixture" };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); var asset = new MediaAsset { OwnerId = user.Id, Purpose = "recording", Status = status, FileName = "Private recording fixture", CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow }; db.MediaAssets.Add(asset); await db.SaveChangesAsync(); return new(user.Id, asset.Id, await app.Login(email));
    }
    private sealed record SampleBank(Guid Version, Guid Segment, Guid Media);
    private async Task<SampleBank> Sample(PublicationStatus status = PublicationStatus.Published, bool fixture = false, ContentKind kind = ContentKind.Lesson, string purpose = "lesson_audio", MediaStatus mediaStatus = MediaStatus.Ready, bool withMedia = true)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var owner = await db.Users.Where(u => u.Email == "admin@englishhub.local").Select(u => u.Id).SingleAsync(); var media = new MediaAsset { OwnerId = owner, Purpose = purpose, Status = mediaStatus, DurationMs = 10000, RenditionKey = "metadata-fixture.mp3", CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow }; db.MediaAssets.Add(media);
        var root = new ContentRoot { Slug = $"source-{Guid.NewGuid():N}", Kind = kind }; var version = new ContentVersion { Root = root, RootId = root.Id, Title = "Original sample fixture", Body = "Original authored lesson body without hidden answer text.", MediaAssetId = withMedia ? media.Id : null, Status = status, IsFixture = fixture, Attribution = "Metadata-only original fixture", PublishedAt = app.Clock.UtcNow };
        var segment = new TranscriptSegment { ContentVersionId = version.Id, StartMs = 0, EndMs = 3000, TextEn = "Secret authored sentence.", TextVi = "Câu biên soạn.", AcceptedAnswersJson = "[\"Secret authored sentence.\"]" }; version.Segments.Add(segment); root.Versions.Add(version); db.ContentRoots.Add(root); await db.SaveChangesAsync(); if (status == PublicationStatus.Published) { root.CurrentPublishedVersionId = version.Id; await db.SaveChangesAsync(); } return new(version.Id, segment.Id, media.Id);
    }
    private static string Path(RecordingBank b) => $"/api/v1/media/{b.Asset}/reflection";
    private static string SourcePath(RecordingBank b) => Path(b) + "/source";
    private static string HistoryPath(RecordingBank b) => Path(b) + "/history";
    private static SaveRecordingReflection Note(string text, string? etag = null) => new(Guid.NewGuid(), etag, text, null, null);
    private static SetRecordingSource Link(Guid? version, Guid? segment = null, string? etag = null) => new(Guid.NewGuid(), etag, version, segment);
    private static Task<HttpResponseMessage> Save(RecordingBank b, SaveRecordingReflection input) => b.Client.PutAsJsonAsync(Path(b), input);
    private static Task<HttpResponseMessage> SetSource(RecordingBank b, SetRecordingSource input) => b.Client.PutAsJsonAsync(SourcePath(b), input);
    private static async Task<RecordingReflectionView> Read(RecordingBank b) => (await b.Client.GetFromJsonAsync<RecordingReflectionView>(Path(b), Json))!;
    private static Task<JsonElement> History(RecordingBank b) => b.Client.GetFromJsonAsync<JsonElement>(HistoryPath(b));
    private async Task<string> Counts() { using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); return JsonSerializer.Serialize(new { metadata = await db.RecordingReflections.CountAsync(), history = await db.RecordingReflectionRevisions.CountAsync(), receipts = await db.CommandReceipts.CountAsync(), listening = await db.ListeningSessions.CountAsync(), disclosures = await db.ListeningDisclosures.CountAsync(), attempts = await db.Attempts.CountAsync(), progress = await db.LessonProgress.CountAsync() }); }
    private sealed class MetadataGrantStorage : IObjectStorage
    {
        public string PlaybackGrant(string key, DateTimeOffset until) => "metadata-test-grant";
        public Task Ensure(CancellationToken ct) => throw new NotSupportedException(); public string UploadGrant(string key, DateTimeOffset until) => throw new NotSupportedException(); public Task<BlobFacts> InspectStaging(string key, CancellationToken ct) => throw new NotSupportedException(); public Task Freeze(string key, string etag, string target, CancellationToken ct) => throw new NotSupportedException(); public Task Download(string key, string file, CancellationToken ct) => throw new NotSupportedException(); public Task Upload(string key, string file, CancellationToken ct) => throw new NotSupportedException(); public Task DeleteAsset(Guid id, string key, CancellationToken ct) => throw new NotSupportedException(); public Task Cleanup(Guid id, string key, string? source, string? rendition, DateTimeOffset time, CancellationToken ct) => throw new NotSupportedException();
    }
}
