using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EnglishHub.Infrastructure.Content;

public sealed record OptionInput(string Text, bool IsCorrect);
public sealed record QuestionInput(string Prompt, string Skill, string Explanation, OptionInput[] Options, QuestionType Type = QuestionType.SingleChoice, string[]? AcceptedAnswerVariants = null, int NormalizationPolicyVersion = AnswerNormalization.CurrentVersion, bool IgnoreTerminalPunctuation = false);
public sealed record GroupInput(int Part, string Stimulus, QuestionInput[] Questions);
public sealed record ContentInput(string Title, string Summary, string Body, string Skill, int Minutes, int DurationSeconds, bool IsFixture, string Attribution, GroupInput[] Groups, Guid? MediaAssetId = null);
public sealed record CreateContentInput(string Slug, ContentKind Kind, ContentInput Content);
public sealed record ImportInput(Guid ClientRequestId, CreateContentInput[] Items);

/// <summary>VI: CMS chỉ tạo bản mới, không sửa payload đã publish. EN: CMS writes drafts; publishing freezes their question and answer graph.</summary>
public sealed class ContentService(HubDbContext db, Transactions tx, IClock clock, Receipts receipts)
{
    public IQueryable<ContentVersion> FullVersions => db.ContentVersions.Include(x => x.Root).Include(x => x.Groups).ThenInclude(x => x.Questions).ThenInclude(x => x.Options);

    public Task<ContentVersion> Create(CreateContentInput input, Guid actor) => tx.Run("catalog", async () =>
    {
        CheckKind(input.Kind);
        await CheckMedia(input.Content.MediaAssetId);
        DomainError.Require(System.Text.RegularExpressions.Regex.IsMatch(input.Slug, "^[a-z0-9-]{3,120}$"), 422, "invalid_slug", "Slug phải gồm chữ thường, số và dấu gạch.");
        DomainError.Require(!await db.ContentRoots.AnyAsync(x => x.Slug == input.Slug), 409, "slug_exists", "Slug đã tồn tại.");
        var root = new ContentRoot { Slug = input.Slug, Kind = input.Kind };
        var version = MakeVersion(root.Id, 1, input.Content);
        db.ContentRoots.Add(root); db.ContentVersions.Add(version);
        Audit(actor, "content.draft_created", version.Id);
        return version;
    });

    public Task<ContentVersion> NewVersion(Guid rootId, ContentInput input, Guid actor) => tx.Run("catalog", async () =>
    {
        await CheckMedia(input.MediaAssetId);
        DomainError.Require(await db.ContentRoots.AnyAsync(x => x.Id == rootId), 404, "content_not_found", "Không tìm thấy nội dung.");
        var number = await db.ContentVersions.Where(x => x.RootId == rootId).MaxAsync(x => x.Version) + 1;
        var version = MakeVersion(rootId, number, input);
        db.ContentVersions.Add(version); Audit(actor, "content.version_created", version.Id);
        return version;
    });

    /// <summary>VI: Chỉ draft được sửa; xóa graph nháp theo thứ tự FK. EN: Only drafts are editable; replace draft children in foreign-key order.</summary>
    public Task<ContentVersion> UpdateDraft(Guid id, ContentInput input, string? etag, Guid actor) => tx.Run("catalog", async () =>
    {
        var v = await FullVersions.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "content_not_found", "Không tìm thấy bản nháp.");
        Etags.Check(v, etag); DomainError.Require(v.Status == PublicationStatus.Draft, 409, "immutable_version", "Hãy tạo version mới để sửa nội dung đã publish.");
        await CheckMedia(input.MediaAssetId);
        var replacement = MakeVersion(v.RootId, v.Version, input);
        db.QuestionOptions.RemoveRange(v.Groups.SelectMany(g => g.Questions).SelectMany(q => q.Options));
        db.Questions.RemoveRange(v.Groups.SelectMany(g => g.Questions)); db.QuestionGroups.RemoveRange(v.Groups);
        await db.SaveChangesAsync();
        v.Title = replacement.Title; v.Summary = replacement.Summary; v.Body = replacement.Body; v.Skill = replacement.Skill;
        v.Minutes = replacement.Minutes; v.DurationSeconds = replacement.DurationSeconds; v.IsFixture = replacement.IsFixture; v.Attribution = replacement.Attribution;
        v.MediaAssetId = replacement.MediaAssetId;
        v.Groups = replacement.Groups;
        Audit(actor, "content.draft_updated", id); return v;
    });

    public Task Archive(Guid id, string? etag, Guid actor) => tx.Run("catalog", async () =>
    {
        var v = await db.ContentVersions.Include(x => x.Root).SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "content_not_found", "Không tìm thấy nội dung.");
        Etags.Check(v, etag); v.Status = PublicationStatus.Archived;
        if (v.Root.CurrentPublishedVersionId == id) v.Root.CurrentPublishedVersionId = null;
        Audit(actor, "content.archived", id); return true;
    });

    public async Task<object> DryRun(CreateContentInput[] items)
    {
        DomainError.Require(items.Length is >= 1 and <= 20 && items.All(x => x is not null), 422, "invalid_import_size", "Một import gồm 1–20 nội dung hợp lệ.");
        var rows = new List<object>();
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            try
            {
                CheckKind(item.Kind); await CheckMedia(item.Content.MediaAssetId);
                DomainError.Require(System.Text.RegularExpressions.Regex.IsMatch(item.Slug, "^[a-z0-9-]{3,120}$"), 422, "invalid_slug", "Slug không hợp lệ.");
                DomainError.Require(items.Count(x => x.Slug == item.Slug) == 1 && !await db.ContentRoots.AnyAsync(x => x.Slug == item.Slug), 409, "slug_exists", "Slug đã tồn tại hoặc bị lặp trong lô.");
                var version = MakeVersion(Guid.NewGuid(), 1, item.Content); version.Root = new ContentRoot { Kind = item.Kind }; Validate(version);
                rows.Add(new { index, valid = true, code = (string?)null, message = (string?)null });
            }
            catch (DomainError error) { rows.Add(new { index, valid = false, code = error.Code, message = error.Message }); }
        }
        return new { rows };
    }

    public Task<Guid[]> Import(ImportInput input, Guid actor) => tx.Run("catalog", async () =>
    {
        var hash = Receipts.Hash(input); var duplicate = await receipts.Find(actor, "content-import", input.ClientRequestId, hash);
        if (duplicate is not null) return System.Text.Json.JsonSerializer.Deserialize<Guid[]>(duplicate.ResponseJson)!;
        DomainError.Require(input.Items.Length is >= 1 and <= 20 && input.Items.All(x => x is not null) && input.Items.Select(x => x.Slug).Distinct().Count() == input.Items.Length, 422, "invalid_import_size", "Cần 1–20 slug duy nhất.");
        var ids = new List<Guid>();
        foreach (var item in input.Items)
        {
            CheckKind(item.Kind); await CheckMedia(item.Content.MediaAssetId);
            DomainError.Require(System.Text.RegularExpressions.Regex.IsMatch(item.Slug, "^[a-z0-9-]{3,120}$"), 422, "invalid_slug", "Slug không hợp lệ.");
            DomainError.Require(!await db.ContentRoots.AnyAsync(x => x.Slug == item.Slug), 409, "slug_exists", "Import xung đột slug đã tồn tại.");
            var root = new ContentRoot { Kind = item.Kind, Slug = item.Slug }; var version = MakeVersion(root.Id, 1, item.Content); version.Root = root; Validate(version);
            db.ContentRoots.Add(root); db.ContentVersions.Add(version); ids.Add(version.Id); Audit(actor, "content.imported", version.Id);
        }
        receipts.Save(actor, "content-import", input.ClientRequestId, hash, input.ClientRequestId, ids.ToArray()); return ids.ToArray();
    });

    public Task<ContentVersion> Publish(Guid id, string? etag, Guid actor) => tx.Run("catalog", async () =>
    {
        var v = await FullVersions.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "content_not_found", "Không tìm thấy nội dung.");
        Etags.Check(v, etag);
        DomainError.Require(v.Status == PublicationStatus.Draft, 409, "immutable_version", "Bản đã publish bất biến; hãy tạo version mới.");
        Validate(v);
        if (v.MediaAssetId is { } mediaId)
            DomainError.Require(await db.MediaAssets.AnyAsync(x => x.Id == mediaId && x.Status == MediaStatus.Ready && x.Purpose == "lesson_audio"), 422, "media_not_ready", "Chỉ publish lesson audio đã được worker xác minh Ready.");
        v.Status = PublicationStatus.Published; v.PublishedAt = clock.UtcNow;
        // VI: Lưu version trước con trỏ để thỏa FK vòng. EN: Persist the version before changing the composite publication pointer.
        await db.SaveChangesAsync();
        v.Root.CurrentPublishedVersionId = v.Id;
        Audit(actor, "content.published", id);
        return v;
    });

    public static void Validate(ContentVersion v)
    {
        CheckKind(v.Root.Kind);
        DomainError.Require(!string.IsNullOrWhiteSpace(v.Title) && v.Title.Length <= 240 && !string.IsNullOrWhiteSpace(v.Attribution), 422, "invalid_content", "Cần tiêu đề và thông tin nguồn.");
        DomainError.Require(v.Minutes is >= 1 and <= 180 && v.DurationSeconds is >= 30 and <= 14400, 422, "invalid_duration", "Thời lượng không hợp lệ.");
        if (v.Root.Kind == ContentKind.Lesson) DomainError.Require(v.Body.Length >= 30, 422, "empty_lesson", "Bài học cần nội dung.");
        else
        {
            DomainError.Require(v.Groups.Count is > 0 and <= 200, 422, "invalid_groups", "Đề cần 1–200 nhóm câu.");
        }
        foreach (var group in v.Groups)
        {
            DomainError.Require(group.Part is >= 1 and <= 7 && group.Questions.Count is > 0 and <= 6, 422, "invalid_group", "Part hoặc kích thước nhóm không hợp lệ.");
            foreach (var q in group.Questions)
            {
                DomainError.Require(!string.IsNullOrWhiteSpace(q.Prompt) && !string.IsNullOrWhiteSpace(q.Explanation), 422, "invalid_question", "Câu hỏi cần đề bài và lời giải.");
                if (q.Type == QuestionType.SingleChoice)
                    DomainError.Require(q.Options.Count == (group.Part == 2 ? 3 : 4) && q.Options.Count(x => x.IsCorrect) == 1 && q.Options.All(x => !string.IsNullOrWhiteSpace(x.Text)) && q.AcceptedAnswerVariantsJson == "[]" && !q.IgnoreTerminalPunctuation, 422, "invalid_question", "Trắc nghiệm cần đúng một option đúng và không dùng đáp án văn bản.");
                else
                {
                    DomainError.Require(q.Type == QuestionType.FillBlank && q.Options.Count == 0 && q.NormalizationPolicyVersion == AnswerNormalization.CurrentVersion, 422, "invalid_fill_blank", "Điền đáp án không có options và phải dùng policy được hỗ trợ.");
                    var variants = JsonSerializer.Deserialize<string[]>(q.AcceptedAnswerVariantsJson)!;
                    DomainError.Require(variants.Length is >= 1 and <= 20 && variants.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= AnswerNormalization.MaxAnswerLength), 422, "invalid_answer_variants", "Cần 1–20 đáp án biên soạn, mỗi đáp án tối đa 2000 ký tự.");
                    var normalized = variants.Select(x => AnswerNormalization.Normalize(x, q.NormalizationPolicyVersion, q.IgnoreTerminalPunctuation)).ToArray();
                    DomainError.Require(normalized.All(x => x.Length > 0) && normalized.Distinct().Count() == normalized.Length, 422, "invalid_answer_variants", "Đáp án không được rỗng hoặc trùng nhau sau chuẩn hóa.");
                }
            }
        }
    }

    private static ContentVersion MakeVersion(Guid rootId, int number, ContentInput input)
    {
        DomainError.Require(input.Groups.Length <= 200 && input.Title.Length <= 240 && input.Body.Length <= 100000, 422, "content_too_large", "Nội dung vượt giới hạn.");
        DomainError.Require(input.Summary.Length <= 2000 && input.Skill.Length <= 60 && input.Attribution.Length <= 2000 && input.Groups.All(g => g is not null && g.Questions.Length <= 6 && g.Stimulus.Length <= 20000 && g.Questions.All(q => q is not null && q.Prompt.Length <= 4000 && q.Explanation.Length <= 8000 && q.Options.Length <= 4 && q.Options.All(o => o is not null && o.Text.Length <= 2000))), 422, "invalid_content_shape", "Cấu trúc câu hỏi hoặc giới hạn nội dung không hợp lệ.");
        DomainError.Require(input.Groups.SelectMany(g => g.Questions).All(q => q.Type is QuestionType.SingleChoice or QuestionType.FillBlank && q.NormalizationPolicyVersion == AnswerNormalization.CurrentVersion && (q.AcceptedAnswerVariants is null || q.AcceptedAnswerVariants.Length <= 20 && q.AcceptedAnswerVariants.All(x => x is not null && x.Length <= AnswerNormalization.MaxAnswerLength))), 422, "invalid_answer_policy", "Loại câu hỏi, biến thể hoặc policy chuẩn hóa không hợp lệ.");
        return new ContentVersion
        {
            RootId = rootId,
            Version = number,
            Title = input.Title,
            Summary = input.Summary,
            Body = input.Body,
            Skill = input.Skill,
            Minutes = input.Minutes,
            DurationSeconds = input.DurationSeconds,
            IsFixture = input.IsFixture,
            Attribution = input.Attribution,
            MediaAssetId = input.MediaAssetId,
            Groups = input.Groups.Select((g, i) => new QuestionGroup
            {
                Order = i,
                Part = g.Part,
                Stimulus = g.Stimulus,
                Questions = g.Questions.Select((q, j) => new Question
                {
                    Order = j,
                    Prompt = q.Prompt,
                    Skill = q.Skill,
                    Explanation = q.Explanation,
                    Type = q.Type,
                    AcceptedAnswerVariantsJson = JsonSerializer.Serialize(q.AcceptedAnswerVariants ?? []),
                    NormalizationPolicyVersion = q.NormalizationPolicyVersion,
                    IgnoreTerminalPunctuation = q.IgnoreTerminalPunctuation,
                    Options = q.Options.Select(o => new QuestionOption { Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>VI: Báo lỗi tham chiếu trước khi chạm FK SQL. EN: Reject invalid media references as business errors before database foreign-key enforcement.</summary>
    private async Task CheckMedia(Guid? id)
    {
        if (id is { } mediaId) DomainError.Require(await db.MediaAssets.AnyAsync(x => x.Id == mediaId && x.Purpose == "lesson_audio" && x.Status != MediaStatus.Deleted && x.Status != MediaStatus.DeletePending), 422, "invalid_media_reference", "Chỉ gắn lesson audio còn hiệu lực; không gắn recording riêng tư.");
    }
    private static void CheckKind(ContentKind kind) => DomainError.Require(kind is ContentKind.Lesson or ContentKind.Assessment, 422, "invalid_content_kind", "Loại nội dung không được hỗ trợ.");
    private void Audit(Guid actor, string action, Guid id) => db.AuditEntries.Add(new AuditEntry { ActorId = actor, Action = action, ResourceId = id, OccurredAt = clock.UtcNow });
}
