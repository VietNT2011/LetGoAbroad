using System.Security.Cryptography;
using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Seed audio đã biên dịch, kiểm hash và upload thật trước khi công bố. EN: Publish prebuilt, hash-verified audio only after actual immutable blob upload succeeds.</summary>
public static class DemoListeningSeed
{
    public const string Attribution = "EnglishHub original text/exercises · CC0; synthetic US English voice: CMU Flite slt · see seed/audio/NOTICE.md · not ETS material";
    public const string ManifestResource = "EnglishHub.Seed.Listening";
    private static readonly string[] Slugs = ["listening-office-meeting", "listening-hotel-room", "listening-train-platform"];
    private sealed record Sentence(long StartMs, long EndMs, string TextEn, string TextVi, string[] AcceptedAnswers);
    private sealed record Quiz(string Prompt, string Correct, string[] Distractors, string Explanation);
    private sealed record Lesson(string Slug, Guid RootId, Guid VersionId, Guid AssetId, string Title, string Summary, string Body,
        string FileName, long Bytes, string Sha256, long DurationMs, Sentence[] Segments, Quiz[] Questions);

    public static async Task<bool> Ensure(HubDbContext db, IObjectStorage storage, Guid owner, IClock clock, CancellationToken ct = default)
    {
        await using var source = typeof(DemoListeningSeed).Assembly.GetManifestResourceStream(ManifestResource)
            ?? throw new InvalidOperationException("Missing listening manifest.");
        var items = await JsonSerializer.DeserializeAsync<Lesson[]>(source, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct)
            ?? throw new InvalidOperationException("Empty listening manifest.");
        DomainError.Require(items.Length == 3 && items.Select(x => x.Slug).ToHashSet(StringComparer.Ordinal).SetEquals(Slugs)
            && items.SelectMany(x => new[] { x.RootId, x.VersionId, x.AssetId }).All(x => x != Guid.Empty)
            && items.SelectMany(x => new[] { x.RootId, x.VersionId, x.AssetId }).Distinct().Count() == 9,
            422, "invalid_listening_manifest", "Manifest nghe không hợp lệ.");
        if (await Existing(db, items, ct)) return false;
        DomainError.Require(await (from userRole in db.UserRoles join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == owner && role.NormalizedName == "ADMIN" select userRole).AnyAsync(ct),
            422, "invalid_seed_owner", "Audio mẫu cần tài khoản Admin hiện có.");

        // VI: Kiểm toàn bộ tài nguyên trước khi ghi blob; không giữ transaction SQL qua I/O storage.
        // EN: Validate all embedded bytes before blob I/O, and never hold a SQL transaction across storage operations.
        var prepared = new List<(Lesson Item, ContentVersion Version, byte[] Bytes)>();
        foreach (var item in items)
        {
            DomainError.Require(item.FileName == item.Slug + ".mp3" && item.DurationMs is > 0 and <= 180000
                && item.Bytes is > 0 and <= 4000000 && item.Segments.Length == 3 && item.Questions.Length == 2,
                422, "invalid_listening_manifest", "Audio mẫu vượt giới hạn.");
            await using var audio = typeof(DemoListeningSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Audio." + item.FileName)
                ?? throw new InvalidOperationException($"Missing embedded audio {item.FileName}.");
            using var buffer = new MemoryStream(); await audio.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
            DomainError.Require(bytes.LongLength == item.Bytes && Convert.ToHexString(SHA256.HashData(bytes)) == item.Sha256,
                422, "listening_audio_hash_mismatch", "Audio không khớp bản biên soạn.");
            var version = Make(item); ContentService.Validate(version);
            DomainError.Require(version.Segments.All(x => x.EndMs <= item.DurationMs)
                && version.Groups.SelectMany(x => x.Questions).All(q => q.Options.Count == 4 && q.Options.Select(o => o.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4),
                422, "invalid_listening_manifest", "Transcript hoặc quiz không hợp lệ.");
            prepared.Add((item, version, bytes));
        }
        var folder = Path.Combine(Path.GetTempPath(), "englishhub-listening-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await storage.Ensure(ct);
            foreach (var (item, _, bytes) in prepared)
            {
                var file = Path.Combine(folder, item.FileName); await File.WriteAllBytesAsync(file, bytes, ct);
                // VI: Key cố định theo asset/hash; Upload dùng If-None-Match và kiểm hash khi retry.
                // EN: Asset/hash keys make retries safe; Upload conditionally creates or verifies matching existing bytes.
                await storage.Upload(Key(item), file, ct);
            }
        }
        finally { Directory.Delete(folder, true); }

        return await new Transactions(db).Run("catalog", async () =>
        {
            // VI: Kiểm lại sau I/O để không chiếm slug do CMS/seed khác vừa tạo. EN: Recheck under the catalog lock after I/O to reject concurrent CMS collisions.
            if (await Existing(db, items, ct)) return false;
            foreach (var (item, version, _) in prepared)
            {
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = item.AssetId, OwnerId = owner, Purpose = "lesson_audio", FileName = item.FileName,
                    DeclaredContentType = "audio/mpeg", ExpectedBytes = item.Bytes, StoredBytes = item.Bytes, ReservedBytes = item.Bytes,
                    StagingKey = $"seed/{item.AssetId:N}", RenditionKey = Key(item), Sha256 = item.Sha256,
                    DurationMs = item.DurationMs, Status = MediaStatus.Ready, Attribution = Attribution,
                    CreatedAt = clock.UtcNow, UploadExpiresAt = clock.UtcNow
                });
                version.Status = PublicationStatus.Published; version.PublishedAt = clock.UtcNow;
                db.ContentRoots.Add(version.Root);
            }
            await db.SaveChangesAsync(ct);
            foreach (var (_, version, _) in prepared) version.Root.CurrentPublishedVersionId = version.Id;
            return true;
        }, ct);
    }

    private static string Key(Lesson item) => $"{item.AssetId:N}/seed/{item.Sha256}.mp3";

    private static async Task<bool> Existing(HubDbContext db, Lesson[] items, CancellationToken ct)
    {
        var rootIds = items.Select(x => x.RootId).ToArray(); var versionIds = items.Select(x => x.VersionId).ToArray(); var assetIds = items.Select(x => x.AssetId).ToArray();
        var roots = await db.ContentRoots.AsNoTracking().Where(x => Slugs.Contains(x.Slug) || rootIds.Contains(x.Id)).Select(x => new { x.Id, x.Slug, x.Kind }).ToArrayAsync(ct);
        if (roots.Length == 0)
        {
            DomainError.Require(!await db.ContentVersions.AnyAsync(x => versionIds.Contains(x.Id), ct)
                && !await db.MediaAssets.AnyAsync(x => assetIds.Contains(x.Id), ct), 409, "listening_seed_collision", "ID audio mẫu đã tồn tại; cần kiểm tra trước khi seed.");
            return false;
        }
        var versions = await db.ContentVersions.AsNoTracking().Where(x => versionIds.Contains(x.Id)).Select(x => new { x.Id, x.RootId }).ToArrayAsync(ct);
        DomainError.Require(roots.Length == items.Length && items.All(i => roots.Any(r => r.Id == i.RootId && r.Slug == i.Slug && r.Kind == ContentKind.Lesson)
            && versions.Any(v => v.Id == i.VersionId && v.RootId == i.RootId)), 409, "listening_seed_collision", "Slug hoặc bản nghe mẫu đã bị chiếm; không ghi đè nội dung CMS.");
        // VI: Không phục hồi media đã xóa hoặc con trỏ đã archive/republish. EN: Reseeding never resurrects deleted media or overwrites editorial publication choices.
        return true;
    }

    private static ContentVersion Make(Lesson item)
    {
        var root = new ContentRoot { Id = item.RootId, Slug = item.Slug, Kind = ContentKind.Lesson };
        var version = new ContentVersion
        {
            Id = item.VersionId, Root = root, RootId = root.Id, Version = 1, Title = item.Title, Summary = item.Summary,
            Body = item.Body, Skill = "listening", Minutes = 5, DurationSeconds = 300, CurriculumOrder = 300 + Array.IndexOf(Slugs, item.Slug),
            Attribution = Attribution, MediaAssetId = item.AssetId,
            Segments = item.Segments.Select((s, i) => new TranscriptSegment
            {
                ContentVersionId = item.VersionId, Order = i, StartMs = s.StartMs, EndMs = s.EndMs, TextEn = s.TextEn, TextVi = s.TextVi,
                AcceptedAnswersJson = JsonSerializer.Serialize(s.AcceptedAnswers), NormalizationPolicyVersion = 1, IgnoreTerminalPunctuation = true
            }).ToList(),
            Groups = [new QuestionGroup
            {
                Order = 0, Part = 4, Stimulus = "Nghe audio của bài rồi trả lời hai câu hỏi tự biên soạn.",
                Questions = item.Questions.Select((q, i) => new Question
                {
                    Order = i, Skill = "listening", Prompt = q.Prompt, Explanation = q.Explanation,
                    Options = new[] { new QuestionOption { Text = q.Correct, IsCorrect = true } }.Concat(q.Distractors.Select(text => new QuestionOption { Text = text })).ToList()
                }).ToList()
            }]
        };
        root.Versions.Add(version); return version;
    }
}
