using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

internal sealed record CurriculumDefinition(string TemplateSlug, string Level, string ResourceName, string Attribution,
    string[] LessonSlugs, string[] PracticeSlugs, int OrderOffset, Func<IReadOnlyDictionary<string, Guid>, PathInput> BuildPath);

/// <summary>VI: Cùng một đường publish atomic cho các chương trình demo, không ghi đè dữ liệu đã phát hành. EN: Share atomic, additive publication without taking ownership of existing CMS payloads.</summary>
internal static class AuthoredCurriculumSeed
{
    private sealed record Item(string Kind, string Slug, string Title, string Skill, int Minutes, string Summary, string Body, Group[] Groups);
    private sealed record Group(int Part, string Stimulus, Quiz[] Questions);
    private sealed record Quiz(string Prompt, string Correct, string[] Distractors, string Explanation, string? Skill = null);

    public static Task Ensure(HubDbContext db, CurriculumDefinition definition) => new Transactions(db).Run("path-catalog", async () =>
    {
        // VI: Tôn trọng version/retirement Admin đã chọn; không chạy lại manifest vào nguồn đã ghim. EN: Preserve later CMS versions, retirement and learner pins when a template already exists.
        if (await db.PathTemplates.AnyAsync(x => x.Slug == definition.TemplateSlug)) return false;
        await using var stream = typeof(AuthoredCurriculumSeed).Assembly.GetManifestResourceStream(definition.ResourceName) ?? throw new InvalidOperationException($"Missing curriculum resource {definition.ResourceName}.");
        var items = (await JsonSerializer.DeserializeAsync<Item[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var expected = definition.LessonSlugs.Concat(definition.PracticeSlugs).ToHashSet(StringComparer.Ordinal);
        DomainError.Require(expected.Count == definition.LessonSlugs.Length + definition.PracticeSlugs.Length && items.Length == expected.Count && expected.SetEquals(items.Select(x => x.Slug)) && items.Select(x => x.Slug).Distinct().Count() == items.Length, 422, "invalid_curriculum_manifest", "Manifest thiếu hoặc lặp nội dung.");
        // VI: Trùng slug phải dừng cả transaction, không ghim nhầm bài đã có. EN: Fail the whole publication on a reserved-slug collision instead of adopting unrelated content.
        DomainError.Require(!await db.ContentRoots.AnyAsync(x => expected.Contains(x.Slug)), 409, "curriculum_seed_collision", "Slug chương trình đã tồn tại nhưng chưa có template; cần kiểm tra dữ liệu trước khi seed.");
        var content = new Dictionary<string, ContentVersion>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var (item, order) in items.Select((item, order) => (item, order)))
        {
            var kind = item.Kind switch { "lesson" => ContentKind.Lesson, "assessment" => ContentKind.Assessment, _ => throw new InvalidOperationException("Unsupported curriculum kind.") };
            DomainError.Require((kind == ContentKind.Lesson) == definition.LessonSlugs.Contains(item.Slug), 422, "invalid_curriculum_kind", "Loại nội dung không khớp lịch đã biên soạn.");
            var root = new ContentRoot { Slug = item.Slug, Kind = kind };
            var version = new ContentVersion
            {
                Root = root, RootId = root.Id, Title = item.Title, Summary = item.Summary, Body = item.Body, Skill = item.Skill,
                Minutes = item.Minutes, DurationSeconds = checked(item.Minutes * 60), CurriculumOrder = definition.OrderOffset + order,
                Status = PublicationStatus.Published, PublishedAt = now, Attribution = definition.Attribution,
                Groups = item.Groups.Select((group, index) => new QuestionGroup
                {
                    Order = index, Part = group.Part, Stimulus = group.Stimulus,
                    Questions = group.Questions.Select((quiz, index) => new Question
                    {
                        Order = index, Prompt = quiz.Prompt, Skill = quiz.Skill ?? item.Skill, Explanation = quiz.Explanation,
                        Options = new[] { new QuestionOption { Text = quiz.Correct, IsCorrect = true } }.Concat(quiz.Distractors.Select(text => new QuestionOption { Text = text })).ToList()
                    }).ToList()
                }).ToList()
            };
            // VI: Dùng validator CMS và loại các option trùng chữ trước khi lưu. EN: Reuse CMS publication validation and reject text-identical choices before saving.
            ContentService.Validate(version);
            DomainError.Require(version.Groups.SelectMany(x => x.Questions).All(x => x.Options.Select(o => o.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4), 422, "ambiguous_curriculum_options", "Câu hỏi có lựa chọn trùng nhau.");
            root.Versions.Add(version); db.ContentRoots.Add(root); content.Add(item.Slug, version);
        }
        await db.SaveChangesAsync();
        foreach (var version in content.Values) version.Root.CurrentPublishedVersionId = version.Id;
        await db.SaveChangesAsync();
        var template = new PathTemplate { Slug = definition.TemplateSlug, Level = definition.Level };
        var input = definition.BuildPath(content.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal));
        var path = PathService.Make(template.Id, 1, input);
        var nodes = path.Stages.SelectMany(s => s.Nodes).ToDictionary(n => n.Id);
        DomainError.Require(nodes.Values.All(n => content.Values.Any(c => c.Id == n.ContentVersionId)), 422, "invalid_curriculum_reference", "Lịch seed chỉ ghim nội dung trong manifest này.");
        PathGraph.Validate(nodes.Values.Select(n => new GraphNode(n.Key, n.StudyDay, n.Prerequisites.Select(p => nodes[p.PrerequisiteNodeId].Key).ToArray())).ToArray());
        path.Status = PublicationStatus.Published; path.PublishedAt = now;
        db.PathTemplates.Add(template); db.PathVersions.Add(path); await db.SaveChangesAsync();
        template.CurrentPublishedVersionId = path.Id;
        return true; // VI: Nội dung, graph và con trỏ commit cùng lúc. EN: Commit content, graph and pointers together.
    });
}
