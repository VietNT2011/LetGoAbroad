using System.Text.RegularExpressions;
using EnglishHub.Application;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record PathNodeInput(string Key, Guid ContentVersionId, int StudyDay, string CompletionRule, int MinimumCorrectPercent, string[] Prerequisites);
public sealed record PathStageInput(string Key, string Title, PathNodeInput[] Nodes);
public sealed record PathInput(string Title, string Summary, string Attribution, PathStageInput[] Stages);
public sealed record CreatePathInput(string Slug, string Level, PathInput Content);
public sealed record AdoptPathInput(Guid PathVersionId, Guid ClientRequestId);

/// <summary>VI: Chỉ sửa draft; publish kiểm tra DAG và bằng chứng completion. EN: Only drafts are editable; publication validates the DAG and evidence-based rules.</summary>
public sealed class PathService(HubDbContext db, Transactions tx, IClock clock)
{
    public IQueryable<PathVersion> Full => db.PathVersions.Include(x => x.Template).Include(x => x.Stages).ThenInclude(x => x.Nodes).ThenInclude(x => x.Prerequisites).Include(x => x.Stages).ThenInclude(x => x.Nodes).ThenInclude(x => x.ContentVersion).ThenInclude(x => x.Root).AsSplitQuery();

    public Task<PathVersion> Create(CreatePathInput input, Guid actor) => tx.Run("path-catalog", async () =>
    {
        DomainError.Require(Regex.IsMatch(input.Slug, "^[a-z0-9-]{3,120}$") && input.Level is "foundation" or "core" or "exam_practice", 422, "invalid_path_template", "Slug hoặc level không hợp lệ.");
        DomainError.Require(!await db.PathTemplates.AnyAsync(x => x.Slug == input.Slug), 409, "slug_exists", "Slug lộ trình đã tồn tại.");
        var root = new PathTemplate { Slug = input.Slug, Level = input.Level };
        var version = Make(root.Id, 1, input.Content); await CheckReferences(version);
        db.PathTemplates.Add(root); db.PathVersions.Add(version); Audit(actor, "path.draft_created", version.Id); return version;
    });

    public Task<PathVersion> NewVersion(Guid templateId, PathInput input, Guid actor) => tx.Run("path-catalog", async () =>
    {
        DomainError.Require(await db.PathTemplates.AnyAsync(x => x.Id == templateId), 404, "path_not_found", "Không tìm thấy template.");
        var number = await db.PathVersions.Where(x => x.TemplateId == templateId).MaxAsync(x => x.Version) + 1;
        var version = Make(templateId, number, input); await CheckReferences(version);
        db.PathVersions.Add(version); Audit(actor, "path.version_created", version.Id); return version;
    });

    public Task<PathVersion> Update(Guid id, PathInput input, string? etag, Guid actor) => tx.Run("path-catalog", async () =>
    {
        var version = await Full.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "path_not_found", "Không tìm thấy version.");
        Etags.Check(version, etag); DomainError.Require(version.Status == PublicationStatus.Draft, 409, "immutable_path", "Bản publish bất biến; hãy tạo version mới.");
        var replacement = Make(version.TemplateId, version.Version, input); await CheckReferences(replacement);
        db.PathPrerequisites.RemoveRange(version.Stages.SelectMany(s => s.Nodes).SelectMany(n => n.Prerequisites)); await db.SaveChangesAsync();
        db.PathNodes.RemoveRange(version.Stages.SelectMany(s => s.Nodes)); await db.SaveChangesAsync();
        db.PathStages.RemoveRange(version.Stages); await db.SaveChangesAsync();
        version.Title = replacement.Title; version.Summary = replacement.Summary; version.Attribution = replacement.Attribution;
        foreach (var stage in replacement.Stages)
        {
            stage.PathVersionId = version.Id;
            foreach (var node in stage.Nodes)
            {
                node.PathVersionId = version.Id;
                foreach (var edge in node.Prerequisites) edge.PathVersionId = version.Id;
            }
        }
        version.Stages = replacement.Stages;
        db.PathStages.AddRange(replacement.Stages);
        // VI: Thay graph cũng phải tăng ETag dù tiêu đề không đổi. EN: Graph-only edits must advance the parent ETag even if all scalar text is unchanged.
        db.Entry(version).Property(x => x.Title).IsModified = true;
        Audit(actor, "path.draft_updated", id); return version;
    });

    public Task<PathVersion> Publish(Guid id, string? etag, Guid actor) => tx.Run("path-catalog", async () =>
    {
        var version = await Full.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "path_not_found", "Không tìm thấy version.");
        Etags.Check(version, etag); DomainError.Require(version.Status == PublicationStatus.Draft, 409, "immutable_path", "Bản publish bất biến.");
        await Validate(version); version.Status = PublicationStatus.Published; version.PublishedAt = clock.UtcNow;
        version.Template.CurrentPublishedVersionId = version.Id; Audit(actor, "path.published", id); return version;
    });

    public async Task<string[]> Validate(PathVersion version)
    {
        var nodes = version.Stages.SelectMany(s => s.Nodes).ToArray();
        var keys = nodes.ToDictionary(x => x.Id, x => x.Key);
        var order = PathGraph.Validate(nodes.Select(n => new GraphNode(n.Key, n.StudyDay, n.Prerequisites.Select(p => keys[p.PrerequisiteNodeId]).ToArray())).ToArray());
        var ids = nodes.Select(x => x.ContentVersionId).Distinct().ToArray();
        var content = await db.ContentVersions.AsNoTracking().Include(x => x.Root).Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Status, x.IsFixture, x.Root.Kind, questions = x.Groups.Sum(g => g.Questions.Count) }).ToDictionaryAsync(x => x.Id);
        DomainError.Require(nodes.All(n => content.TryGetValue(n.ContentVersionId, out var c) && c.Status == PublicationStatus.Published && !c.IsFixture && c.questions > 0 && (c.Kind == ContentKind.Lesson ? n.CompletionRule == "lesson_quiz" && n.MinimumCorrectPercent == 100 : n.CompletionRule == "assessment_raw")), 422, "invalid_path_content", "Node cần nội dung đã publish, không phải fixture, có quiz/câu hỏi và quy tắc chấm đúng loại.");
        return order;
    }

    private async Task CheckReferences(PathVersion version)
    {
        var ids = version.Stages.SelectMany(s => s.Nodes).Select(n => n.ContentVersionId).Distinct().ToArray();
        DomainError.Require(await db.ContentVersions.CountAsync(x => ids.Contains(x.Id)) == ids.Length, 422, "invalid_content_reference", "Node tham chiếu ContentVersion không tồn tại.");
    }

    public static PathVersion Make(Guid template, int number, PathInput input)
    {
        DomainError.Require(!string.IsNullOrWhiteSpace(input.Title) && input.Title.Length <= 240 && input.Summary.Length <= 2000 && !string.IsNullOrWhiteSpace(input.Attribution) && input.Attribution.Length <= 2000 && input.Stages.Length is >= 1 and <= 20 && input.Stages.All(s => s is not null && Regex.IsMatch(s.Key, "^[a-z0-9-]{1,80}$") && !string.IsNullOrWhiteSpace(s.Title) && s.Title.Length <= 240 && s.Nodes.Length is >= 1 and <= 200 && s.Nodes.All(n => n is not null && Regex.IsMatch(n.Key, "^[a-z0-9-]{1,80}$") && n.StudyDay is >= 1 and <= 365 && n.MinimumCorrectPercent is >= 1 and <= 100 && n.CompletionRule is "lesson_quiz" or "assessment_raw" && n.Prerequisites.Length <= 20 && n.Prerequisites.All(p => p is not null))), 422, "invalid_path_shape", "Cấu trúc, giới hạn hoặc completion rule không hợp lệ.");
        var inputs = input.Stages.SelectMany(x => x.Nodes).ToArray();
        DomainError.Require(inputs.Length <= 200 && inputs.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == inputs.Length && input.Stages.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == input.Stages.Length, 422, "duplicate_path_key", "Key stage/node phải duy nhất trong version.");
        var version = new PathVersion { TemplateId = template, Version = number, Title = input.Title, Summary = input.Summary, Attribution = input.Attribution };
        version.Stages = input.Stages.Select((s, i) => new PathStage { PathVersionId = version.Id, Key = s.Key, Title = s.Title, Order = i, Nodes = s.Nodes.Select((n, j) => new PathNode { PathVersionId = version.Id, Key = n.Key, Order = j, ContentVersionId = n.ContentVersionId, StudyDay = n.StudyDay, CompletionRule = n.CompletionRule, MinimumCorrectPercent = n.MinimumCorrectPercent }).ToList() }).ToList();
        var byKey = version.Stages.SelectMany(s => s.Nodes).ToDictionary(n => n.Key, StringComparer.Ordinal);
        foreach (var inputNode in inputs)
        {
            DomainError.Require(inputNode.Prerequisites.Distinct(StringComparer.Ordinal).Count() == inputNode.Prerequisites.Length && inputNode.Prerequisites.All(byKey.ContainsKey), 422, "invalid_prerequisite", "Prerequisite bị lặp hoặc không thuộc version này.");
            var node = byKey[inputNode.Key];
            node.Prerequisites = inputNode.Prerequisites.Select(p => new PathPrerequisite { PathVersionId = version.Id, NodeId = node.Id, PrerequisiteNodeId = byKey[p].Id }).ToList();
        }
        return version;
    }

    private void Audit(Guid actor, string action, Guid id) => db.AuditEntries.Add(new AuditEntry { ActorId = actor, Action = action, ResourceId = id, OccurredAt = clock.UtcNow });
}
