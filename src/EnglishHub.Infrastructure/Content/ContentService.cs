using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Content;

public sealed record OptionInput(string Text, bool IsCorrect);
public sealed record QuestionInput(string Prompt, string Skill, string Explanation, OptionInput[] Options);
public sealed record GroupInput(int Part, string Stimulus, QuestionInput[] Questions);
public sealed record ContentInput(string Title, string Summary, string Body, string Skill, int Minutes, int DurationSeconds, bool IsFixture, string Attribution, GroupInput[] Groups);
public sealed record CreateContentInput(string Slug, ContentKind Kind, ContentInput Content);

/// <summary>VI: CMS chỉ tạo bản mới, không sửa payload đã publish. EN: CMS writes drafts; publishing freezes their question and answer graph.</summary>
public sealed class ContentService(HubDbContext db, Transactions tx, IClock clock)
{
    public IQueryable<ContentVersion> FullVersions => db.ContentVersions.Include(x => x.Root).Include(x => x.Groups).ThenInclude(x => x.Questions).ThenInclude(x => x.Options);

    public Task<ContentVersion> Create(CreateContentInput input, Guid actor) => tx.Run("catalog", async () =>
    {
        DomainError.Require(System.Text.RegularExpressions.Regex.IsMatch(input.Slug, "^[a-z0-9-]{3,120}$"), 422, "invalid_slug", "Slug phải gồm chữ thường, số và dấu gạch.");
        DomainError.Require(!await db.ContentRoots.AnyAsync(x => x.Slug == input.Slug), 409, "slug_exists", "Slug đã tồn tại.");
        var root = new ContentRoot { Slug = input.Slug, Kind = input.Kind };
        var version = MakeVersion(root.Id, 1, input.Content);
        db.ContentRoots.Add(root); db.ContentVersions.Add(version);
        Audit(actor, "content.draft_created", version.Id);
        return version;
    });

    public Task<ContentVersion> NewVersion(Guid rootId, ContentInput input, Guid actor) => tx.Run($"content:{rootId}", async () =>
    {
        DomainError.Require(await db.ContentRoots.AnyAsync(x => x.Id == rootId), 404, "content_not_found", "Không tìm thấy nội dung.");
        var number = await db.ContentVersions.Where(x => x.RootId == rootId).MaxAsync(x => x.Version) + 1;
        var version = MakeVersion(rootId, number, input);
        db.ContentVersions.Add(version); Audit(actor, "content.version_created", version.Id);
        return version;
    });

    public Task<ContentVersion> Publish(Guid id, string? etag, Guid actor) => tx.Run("catalog", async () =>
    {
        var v = await FullVersions.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "content_not_found", "Không tìm thấy nội dung.");
        Etags.Check(v, etag);
        DomainError.Require(v.Status == PublicationStatus.Draft, 409, "immutable_version", "Bản đã publish bất biến; hãy tạo version mới.");
        Validate(v);
        v.Status = PublicationStatus.Published; v.PublishedAt = clock.UtcNow;
        // VI: Lưu version trước con trỏ để thỏa FK vòng. EN: Persist the version before changing the composite publication pointer.
        await db.SaveChangesAsync();
        v.Root.CurrentPublishedVersionId = v.Id;
        Audit(actor, "content.published", id);
        return v;
    });

    public static void Validate(ContentVersion v)
    {
        DomainError.Require(!string.IsNullOrWhiteSpace(v.Title) && v.Title.Length <= 240 && !string.IsNullOrWhiteSpace(v.Attribution), 422, "invalid_content", "Cần tiêu đề và thông tin nguồn.");
        DomainError.Require(v.Minutes is >= 1 and <= 180 && v.DurationSeconds is >= 30 and <= 14400, 422, "invalid_duration", "Thời lượng không hợp lệ.");
        if (v.Root.Kind == ContentKind.Lesson) DomainError.Require(v.Body.Length >= 30, 422, "empty_lesson", "Bài học cần nội dung.");
        else
        {
            DomainError.Require(v.Groups.Count is > 0 and <= 200, 422, "invalid_groups", "Đề cần 1–200 nhóm câu.");
            foreach (var group in v.Groups)
            {
                DomainError.Require(group.Part is >= 1 and <= 7 && group.Questions.Count is > 0 and <= 6, 422, "invalid_group", "Part hoặc kích thước nhóm không hợp lệ.");
                foreach (var q in group.Questions)
                    DomainError.Require(!string.IsNullOrWhiteSpace(q.Prompt) && !string.IsNullOrWhiteSpace(q.Explanation) && q.Options.Count == (group.Part == 2 ? 3 : 4) && q.Options.Count(x => x.IsCorrect) == 1 && q.Options.All(x => !string.IsNullOrWhiteSpace(x.Text)), 422, "invalid_question", "Câu hỏi cần lời giải và đúng một đáp án đúng.");
            }
        }
    }

    private static ContentVersion MakeVersion(Guid rootId, int number, ContentInput input)
    {
        DomainError.Require(input.Groups.Length <= 200 && input.Title.Length <= 240 && input.Body.Length <= 100000, 422, "content_too_large", "Nội dung vượt giới hạn.");
        return new ContentVersion { RootId = rootId, Version = number, Title = input.Title, Summary = input.Summary, Body = input.Body, Skill = input.Skill,
            Minutes = input.Minutes, DurationSeconds = input.DurationSeconds, IsFixture = input.IsFixture, Attribution = input.Attribution,
            Groups = input.Groups.Select((g, i) => new QuestionGroup { Order = i, Part = g.Part, Stimulus = g.Stimulus,
                Questions = g.Questions.Select((q, j) => new Question { Order = j, Prompt = q.Prompt, Skill = q.Skill, Explanation = q.Explanation,
                    Options = q.Options.Select(o => new QuestionOption { Text = o.Text, IsCorrect = o.IsCorrect }).ToList() }).ToList() }).ToList() };
    }

    private void Audit(Guid actor, string action, Guid id) => db.AuditEntries.Add(new AuditEntry { ActorId = actor, Action = action, ResourceId = id, OccurredAt = clock.UtcNow });
}
