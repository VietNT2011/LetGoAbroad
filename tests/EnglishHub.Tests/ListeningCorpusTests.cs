using System.Security.Cryptography;
using System.Text.Json;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Test thường không tạo Ready audio giả. EN: Ordinary database fixtures verify manifest and failure safety without pretending uploaded media exists.</summary>
public sealed class ListeningCorpusTests
{
    internal static async Task<JsonElement[]> Manifest()
    {
        await using var input = typeof(DemoListeningSeed).Assembly.GetManifestResourceStream(DemoListeningSeed.ManifestResource)!;
        return (await JsonSerializer.DeserializeAsync<JsonElement[]>(input))!;
    }

    [Fact]
    public async Task Embedded_audio_matches_manifest_and_every_bilingual_sentence_has_a_separate_quiz()
    {
        var items = await Manifest(); Assert.Equal(3, items.Length);
        Assert.Equal(9, items.SelectMany(i => new[] { i.GetProperty("rootId").GetGuid(), i.GetProperty("versionId").GetGuid(), i.GetProperty("assetId").GetGuid() }).Distinct().Count());
        foreach (var item in items)
        {
            await using var stream = typeof(DemoListeningSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Audio." + item.GetProperty("fileName").GetString())!;
            using var output = new MemoryStream(); await stream.CopyToAsync(output); var bytes = output.ToArray();
            Assert.Equal(item.GetProperty("bytes").GetInt32(), bytes.Length); Assert.Equal(item.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
            long end = 0; var segments = item.GetProperty("segments").EnumerateArray().ToArray(); Assert.Equal(3, segments.Length);
            foreach (var segment in segments)
            {
                Assert.True(segment.GetProperty("startMs").GetInt64() >= end); end = segment.GetProperty("endMs").GetInt64();
                Assert.True(end > segment.GetProperty("startMs").GetInt64()); Assert.True(end <= item.GetProperty("durationMs").GetInt64());
                Assert.False(string.IsNullOrWhiteSpace(segment.GetProperty("textVi").GetString()));
                Assert.Contains(segment.GetProperty("textEn").GetString(), segment.GetProperty("acceptedAnswers").EnumerateArray().Select(x => x.GetString()));
                Assert.DoesNotContain(segment.GetProperty("textEn").GetString()!, item.GetProperty("body").GetString()!);
            }
            var quiz = item.GetProperty("questions").EnumerateArray().ToArray(); Assert.Equal(2, quiz.Length);
            Assert.All(quiz, q => Assert.Equal(4, q.GetProperty("distractors").EnumerateArray().Select(x => x.GetString()).Append(q.GetProperty("correct").GetString()).Distinct().Count()));
            Assert.Contains("giọng tổng hợp", item.GetProperty("body").GetString());
        }
        Assert.NotNull(typeof(DemoListeningSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Audio.NOTICE.md"));
        Assert.NotNull(typeof(DemoListeningSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Audio.FLITE-NOTICE.txt"));
    }

    [Fact]
    public async Task Default_fixture_does_not_advertise_nonexistent_starter_audio()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.False(await db.ContentVersions.AnyAsync(x => x.Attribution == DemoListeningSeed.Attribution));
        Assert.False(await db.MediaAssets.AnyAsync(x => x.Attribution == DemoListeningSeed.Attribution));
    }

    [Fact]
    public async Task Non_admin_owner_is_rejected_before_any_storage_call()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var owner = await db.Users.Where(x => x.Email == "learner@englishhub.local").Select(x => x.Id).SingleAsync(); var storage = new RejectStorage();
        var error = await Assert.ThrowsAsync<DomainError>(() => DemoListeningSeed.Ensure(db, storage, owner, app.Clock)); Assert.Equal("invalid_seed_owner", error.Code); Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task Reserved_slug_collision_preserves_cms_content_without_uploading()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var root = new ContentRoot { Slug = "listening-office-meeting", Kind = ContentKind.Lesson }; db.ContentRoots.Add(root); await db.SaveChangesAsync(); var count = await db.ContentRoots.CountAsync(); var storage = new RejectStorage();
        var error = await Assert.ThrowsAsync<DomainError>(() => DemoListeningSeed.Ensure(db, storage, Guid.NewGuid(), app.Clock)); Assert.Equal("listening_seed_collision", error.Code);
        Assert.Equal(0, storage.Calls); Assert.Equal(count, await db.ContentRoots.CountAsync()); Assert.Equal(root.Id, (await db.ContentRoots.SingleAsync(x => x.Slug == root.Slug)).Id);
    }

    [Fact]
    public async Task Reserved_asset_id_collision_is_not_adopted_as_seed_audio()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var owner = await db.Users.Where(x => x.Email == "admin@englishhub.local").Select(x => x.Id).SingleAsync(); var asset = new MediaAsset { Id = (await Manifest())[0].GetProperty("assetId").GetGuid(), OwnerId = owner, Status = MediaStatus.PendingUpload, CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow.AddMinutes(15) };
        db.MediaAssets.Add(asset); await db.SaveChangesAsync(); var storage = new RejectStorage();
        var error = await Assert.ThrowsAsync<DomainError>(() => DemoListeningSeed.Ensure(db, storage, owner, app.Clock)); Assert.Equal("listening_seed_collision", error.Code); Assert.Equal(0, storage.Calls); Assert.Equal(MediaStatus.PendingUpload, asset.Status);
    }

    [Fact]
    public async Task A_partial_set_of_reserved_roots_fails_instead_of_silently_skipping_the_corpus()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var item = (await Manifest())[0];
        var root = new ContentRoot { Id = item.GetProperty("rootId").GetGuid(), Slug = item.GetProperty("slug").GetString()!, Kind = ContentKind.Lesson };
        root.Versions.Add(new ContentVersion { Id = item.GetProperty("versionId").GetGuid(), RootId = root.Id, Root = root, Title = "Reserved technical draft" }); db.ContentRoots.Add(root); await db.SaveChangesAsync(); var storage = new RejectStorage();
        var error = await Assert.ThrowsAsync<DomainError>(() => DemoListeningSeed.Ensure(db, storage, Guid.NewGuid(), app.Clock)); Assert.Equal("listening_seed_collision", error.Code); Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task Interrupted_blob_upload_never_commits_a_partial_ready_corpus_or_progress()
    {
        await using var app = new TestApp(); using var client = await app.Login(); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var owner = await db.Users.Where(x => x.Email == "admin@englishhub.local").Select(x => x.Id).SingleAsync(); var before = await db.ContentRoots.CountAsync(); var storage = new RejectStorage(failSecondUpload: true);
        await Assert.ThrowsAsync<IOException>(() => DemoListeningSeed.Ensure(db, storage, owner, app.Clock)); Assert.Equal(3, storage.Calls);
        Assert.Equal(before, await db.ContentRoots.CountAsync()); Assert.False(await db.MediaAssets.AnyAsync(x => x.Attribution == DemoListeningSeed.Attribution)); Assert.False(await db.ListeningSessions.AnyAsync()); Assert.False(await db.SegmentAnswers.AnyAsync());
        Assert.Empty(db.ChangeTracker.Entries<MediaAsset>());
    }

    private sealed class RejectStorage(bool failSecondUpload = false) : IObjectStorage
    {
        public int Calls { get; private set; }
        public Task Ensure(CancellationToken ct) { Calls++; if (!failSecondUpload) throw new InvalidOperationException("Storage must not be used."); return Task.CompletedTask; }
        public Task Upload(string key, string file, CancellationToken ct) { Calls++; Assert.True(File.Exists(file)); Assert.True(new FileInfo(file).Length > 0); if (Calls == 3) throw new IOException("Simulated second blob failure"); return Task.CompletedTask; }
        public string UploadGrant(string key, DateTimeOffset expiry) => throw new NotSupportedException();
        public string PlaybackGrant(string key, DateTimeOffset expiry) => throw new NotSupportedException();
        public Task<BlobFacts> InspectStaging(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task Freeze(string key, string etag, string target, CancellationToken ct) => throw new NotSupportedException();
        public Task Download(string key, string file, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsset(Guid id, string key, CancellationToken ct) => throw new NotSupportedException();
        public Task Cleanup(Guid id, string key, string? source, string? rendition, DateTimeOffset age, CancellationToken ct) => throw new NotSupportedException();
    }
}
