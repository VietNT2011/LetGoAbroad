using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Audio bài học phải tồn tại và giải mã được, không chỉ có metadata Ready. EN: Verify actual private MP3 bytes, measured duration, publication and dictation without inventing completion.</summary>
[Trait("Category", "Storage")]
public sealed class ListeningCorpusStorageTests
{
    private static async Task<(Guid Owner, IObjectStorage Storage)> Seed(TestApp app, IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var owner = await db.Users.Where(x => x.Email == "admin@englishhub.local").Select(x => x.Id).SingleAsync(); var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        Assert.True(await DemoListeningSeed.Ensure(db, storage, owner, app.Clock)); return (owner, storage);
    }

    [Fact]
    public async Task Published_corpus_has_real_decodable_range_audio_hidden_transcripts_and_independent_dictation()
    {
        await using var app = new TestApp(); using var learner = await app.Login("learner2@englishhub.local"); using var scope = app.Services.CreateScope(); await Seed(app, scope);
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var beforeAttempts = await db.Attempts.CountAsync(); var beforeProgress = await db.LessonProgress.CountAsync(); var beforeNodes = await db.PathNodes.CountAsync();
        var items = await ListeningCorpusTests.Manifest(); using var blobs = new HttpClient();
        foreach (var item in items)
        {
            var version = item.GetProperty("versionId").GetGuid(); var asset = item.GetProperty("assetId").GetGuid();
            var safe = await learner.GetStringAsync($"/api/v1/lessons/{version}"); Assert.Contains("\"hasListeningSegments\":true", safe); Assert.DoesNotContain("acceptedAnswers", safe); Assert.DoesNotContain("textEn", safe);
            using var redirect = await learner.GetAsync($"/api/v1/media/{asset}/content"); Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            using var range = new HttpRequestMessage(HttpMethod.Get, redirect.Headers.Location); range.Headers.Range = new RangeHeaderValue(0, 63);
            using var partial = await blobs.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode); Assert.Equal(64, (await partial.Content.ReadAsByteArrayAsync()).Length);
            var bytes = await blobs.GetByteArrayAsync(redirect.Headers.Location); Assert.Equal(item.GetProperty("bytes").GetInt64(), bytes.LongLength); Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
            var file = Path.Combine(Path.GetTempPath(), $"englishhub-corpus-probe-{Guid.NewGuid():N}.mp3");
            try
            {
                await File.WriteAllBytesAsync(file, bytes);
                var start = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { "-v", "error", "-show_entries", "stream=codec_name,sample_rate,channels:format=duration", "-of", "json", file }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); Assert.Empty(await error);
                var probe = JsonDocument.Parse(await output).RootElement; var stream = probe.GetProperty("streams")[0]; Assert.Equal("mp3", stream.GetProperty("codec_name").GetString()); Assert.Equal("44100", stream.GetProperty("sample_rate").GetString()); Assert.Equal(1, stream.GetProperty("channels").GetInt32());
                var duration = double.Parse(probe.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) * 1000;
                Assert.InRange(item.GetProperty("durationMs").GetDouble() - duration, 0, 1.001);
                // VI: ffprobe đọc header chưa đủ; giải mã trọn file để phát hiện stream hỏng. EN: Decode the full stream, rather than treating readable headers as successful playback.
                var decode = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
                foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", file, "-f", "null", "-" }) decode.ArgumentList.Add(arg);
                using var decoder = Process.Start(decode)!; var decodeError = decoder.StandardError.ReadToEndAsync(); await decoder.WaitForExitAsync(); Assert.Equal(0, decoder.ExitCode); Assert.Empty(await decodeError);
            }
            finally { File.Delete(file); }
            var started = await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/listening", new StartListening(Guid.NewGuid())); started.EnsureSuccessStatusCode(); var session = (await started.Content.ReadFromJsonAsync<ListeningView>())!;
            Assert.Equal(3, session.Segments.Length); Assert.All(session.Segments, s => { Assert.Null(s.TextEn); Assert.Null(s.TextVi); Assert.False(s.IsDisclosed); });
            var answer = item.GetProperty("segments")[0].GetProperty("textEn").GetString()!;
            var graded = await learner.PostAsJsonAsync($"/api/v1/listening/{session.Id}/segments/{session.Segments[0].Id}/answers", new CheckDictation(Guid.NewGuid(), answer)); graded.EnsureSuccessStatusCode();
            var result = (await graded.Content.ReadFromJsonAsync<DictationResult>())!; Assert.True(result.IsCorrect); Assert.False(result.IsAssisted); Assert.Equal(1, result.Number);
        }
        Assert.Equal(beforeAttempts, await db.Attempts.CountAsync()); Assert.Equal(beforeProgress, await db.LessonProgress.CountAsync()); Assert.Equal(beforeNodes, await db.PathNodes.CountAsync());
        Assert.Equal(3, await db.SegmentAnswers.CountAsync()); Assert.Equal(3, await db.MediaAssets.CountAsync(x => x.Attribution == DemoListeningSeed.Attribution && x.Status == MediaStatus.Ready));
        Assert.Equal(300123, await db.MediaAssets.Where(x => x.Attribution == DemoListeningSeed.Attribution).SumAsync(x => x.ReservedBytes));
        Assert.False(await db.BackgroundJobs.AnyAsync());
    }

    [Fact]
    public async Task Reseed_preserves_archival_deleted_media_and_later_cms_publication_without_storage_calls()
    {
        await using var app = new TestApp(); using var learner = await app.Login(); using var scope = app.Services.CreateScope(); var (owner, _) = await Seed(app, scope); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var versions = await db.ContentVersions.Include(x => x.Root).Where(x => x.Attribution == DemoListeningSeed.Attribution).OrderBy(x => x.Id).ToArrayAsync();
        versions[0].Status = PublicationStatus.Archived; versions[0].Root.CurrentPublishedVersionId = null;
        var asset = await db.MediaAssets.SingleAsync(x => x.Id == versions[0].MediaAssetId); asset.Status = MediaStatus.Deleted;
        versions[1].Status = PublicationStatus.Archived;
        var replacement = new ContentVersion { Root = versions[1].Root, RootId = versions[1].RootId, Version = 2, Title = "Later CMS publication", Body = "A later original editorial revision remains authoritative.", Skill = "listening", Minutes = 5, DurationSeconds = 300, Attribution = "CMS fixture", Status = PublicationStatus.Published, PublishedAt = app.Clock.UtcNow };
        db.ContentVersions.Add(replacement); await db.SaveChangesAsync(); versions[1].Root.CurrentPublishedVersionId = replacement.Id; await db.SaveChangesAsync();
        var oldTokens = versions.Select(v => Convert.ToHexString(v.RowVersion)).ToArray(); var assetToken = Convert.ToHexString(asset.RowVersion);
        Assert.False(await DemoListeningSeed.Ensure(db, new NeverStorage(), owner, app.Clock)); Assert.False(await DemoListeningSeed.Ensure(db, new NeverStorage(), owner, app.Clock));
        Assert.Null(versions[0].Root.CurrentPublishedVersionId); Assert.Equal(replacement.Id, versions[1].Root.CurrentPublishedVersionId); Assert.Equal(MediaStatus.Deleted, asset.Status);
        Assert.Equal(oldTokens, versions.Select(v => Convert.ToHexString(v.RowVersion)).ToArray()); Assert.Equal(assetToken, Convert.ToHexString(asset.RowVersion)); Assert.Equal(9, await db.TranscriptSegments.CountAsync());
    }

    [Fact]
    public async Task Concurrent_first_seeds_upload_same_immutable_bytes_and_commit_exactly_one_corpus()
    {
        await using var app = new TestApp(); using var learner = await app.Login(); using var one = app.Services.CreateScope(); using var two = app.Services.CreateScope();
        var db1 = one.ServiceProvider.GetRequiredService<HubDbContext>(); var db2 = two.ServiceProvider.GetRequiredService<HubDbContext>(); var owner = await db1.Users.Where(x => x.Email == "admin@englishhub.local").Select(x => x.Id).SingleAsync();
        var results = await Task.WhenAll(DemoListeningSeed.Ensure(db1, one.ServiceProvider.GetRequiredService<IObjectStorage>(), owner, app.Clock), DemoListeningSeed.Ensure(db2, two.ServiceProvider.GetRequiredService<IObjectStorage>(), owner, app.Clock));
        Assert.Single(results, x => x); Assert.Equal(3, await db1.ContentVersions.CountAsync(x => x.Attribution == DemoListeningSeed.Attribution)); Assert.Equal(9, await db1.TranscriptSegments.CountAsync()); Assert.Equal(6, await db1.Questions.CountAsync(q => q.Group.ContentVersion.Attribution == DemoListeningSeed.Attribution));
        Assert.Equal(3, await db1.MediaAssets.CountAsync(x => x.Attribution == DemoListeningSeed.Attribution && x.Status == MediaStatus.Ready)); Assert.False(await db1.ListeningSessions.AnyAsync()); Assert.False(await db1.Attempts.AnyAsync()); Assert.False(await db1.LessonProgress.AnyAsync());
    }

    private sealed class NeverStorage : IObjectStorage
    {
        public Task Ensure(CancellationToken ct) => throw new InvalidOperationException("A reseed must preserve storage choices.");
        public Task Upload(string key, string file, CancellationToken ct) => throw new NotSupportedException();
        public string UploadGrant(string key, DateTimeOffset expiry) => throw new NotSupportedException();
        public string PlaybackGrant(string key, DateTimeOffset expiry) => throw new NotSupportedException();
        public Task<BlobFacts> InspectStaging(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task Freeze(string key, string etag, string target, CancellationToken ct) => throw new NotSupportedException();
        public Task Download(string key, string file, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsset(Guid id, string key, CancellationToken ct) => throw new NotSupportedException();
        public Task Cleanup(Guid id, string key, string? source, string? rendition, DateTimeOffset age, CancellationToken ct) => throw new NotSupportedException();
    }
}
