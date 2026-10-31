using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Kiểm quyền mẫu đã archive bằng byte MP3 thật và bản thu tone kỹ thuật thật. EN: Verify archived-sample access with actual MP3 bytes and an explicitly synthetic technical recording.</summary>
[Trait("Category", "Storage")]
public sealed class RecordingContextStorageTests
{
    [Fact]
    public async Task Owned_history_grants_only_its_archived_sample_and_survives_real_recording_deletion()
    {
        await using var app = new TestApp(); using var owner = await app.Login("learner2@englishhub.local"); using var other = await app.Login("learner3@englishhub.local"); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var admin = await db.Users.Where(u => u.Email == "admin@englishhub.local").Select(u => u.Id).SingleAsync();
        await DemoListeningSeed.Ensure(db, scope.ServiceProvider.GetRequiredService<IObjectStorage>(), admin, app.Clock);
        var source = (await ListeningCorpusTests.Manifest())[0]; var versionId = source.GetProperty("versionId").GetGuid(); var sampleMedia = source.GetProperty("assetId").GetGuid();
        using var bytes = new MemoryStream(); using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            const int rate = 16000; writer.Write("RIFF"u8.ToArray()); writer.Write(36 + rate * 2); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8.ToArray()); writer.Write(rate * 2);
            for (var i = 0; i < rate; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 4000));
        }
        app.Clock.Advance(DateTimeOffset.UtcNow - app.Clock.UtcNow);
        var create = await owner.PostAsJsonAsync("/api/v1/media/uploads", new CreateUpload(Guid.NewGuid(), "recording", "context-technical-tone.wav", "audio/wav", bytes.Length, "Original synthetic technical tone, not human speech")); create.EnsureSuccessStatusCode(); var grant = (await create.Content.ReadFromJsonAsync<UploadGrant>())!;
        using var blobs = new HttpClient(); using var upload = new HttpRequestMessage(HttpMethod.Put, grant.UploadUrl) { Content = new ByteArrayContent(bytes.ToArray()) }; upload.Headers.Add("x-ms-blob-type", "BlockBlob"); (await blobs.SendAsync(upload)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/v1/media/{grant.AssetId}/complete", null)).EnsureSuccessStatusCode(); Assert.True(await scope.ServiceProvider.GetRequiredService<MediaPipeline>().RunOne(CancellationToken.None));
        var link = await owner.PutAsJsonAsync($"/api/v1/media/{grant.AssetId}/reflection/source", new SetRecordingSource(Guid.NewGuid(), null, versionId)); link.EnsureSuccessStatusCode();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } }; var linked = (await link.Content.ReadFromJsonAsync<RecordingReflectionView>(options))!;
        (await owner.PutAsJsonAsync($"/api/v1/media/{grant.AssetId}/reflection/source", new SetRecordingSource(Guid.NewGuid(), linked.Etag, null))).EnsureSuccessStatusCode();
        await db.ContentVersions.Where(v => v.Id == versionId).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, PublicationStatus.Archived));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/lessons/{versionId}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/lessons/{versionId}")).StatusCode);
        using var playback = await owner.GetAsync($"/api/v1/media/{sampleMedia}/content"); Assert.Equal(HttpStatusCode.Redirect, playback.StatusCode);
        using var range = new HttpRequestMessage(HttpMethod.Get, playback.Headers.Location); range.Headers.Range = new RangeHeaderValue(0, 63); using var partial = await blobs.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode); Assert.Equal(64, (await partial.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/media/{sampleMedia}/content")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/media/{grant.AssetId}/reflection/history")).StatusCode);
        (await owner.DeleteAsync($"/api/v1/media/{grant.AssetId}")).EnsureSuccessStatusCode(); Assert.True(await scope.ServiceProvider.GetRequiredService<MediaPipeline>().RunOne(CancellationToken.None));
        var journal = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/media/{grant.AssetId}/reflection/history"); Assert.Equal(2, journal.GetProperty("items").GetArrayLength()); Assert.Equal(HttpStatusCode.Redirect, (await owner.GetAsync($"/api/v1/media/{sampleMedia}/content")).StatusCode);
        Assert.Equal(MediaStatus.Deleted, (await db.MediaAssets.AsNoTracking().SingleAsync(a => a.Id == grant.AssetId)).Status); Assert.False(await db.Attempts.AnyAsync()); Assert.False(await db.LessonProgress.AnyAsync());
    }
}
