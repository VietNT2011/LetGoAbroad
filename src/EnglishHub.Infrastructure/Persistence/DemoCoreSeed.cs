using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Nội dung Core nguyên gốc, chỉ thêm mới trong demo; không sửa bản đã publish. EN: Add an original demo Core curriculum without rewriting published versions or enrollments.</summary>
public static class DemoCoreSeed
{
    public const string TemplateSlug = "core-eight-study-days";
    public const string Attribution = "EnglishHub original Core curriculum v1 · CC0 · not ETS material or a certified level assessment";
    private sealed record Item(string Kind, string Slug, string Title, string Skill, int Minutes, string Summary, string Body, Group[] Groups);
    private sealed record Group(int Part, string Stimulus, Quiz[] Questions);
    private sealed record Quiz(string Prompt, string Correct, string[] Distractors, string Explanation);
    private static readonly string[] LessonSlugs = ["core-present-perfect", "core-past-events", "core-passive-processes", "core-first-conditional", "core-relative-clauses", "core-modals-at-work", "core-reading-revisions", "core-reading-connections"];

    public static Task Ensure(HubDbContext db) => new Transactions(db).Run("path-catalog", async () =>
    {
        // VI: Seed lại không lấy lại quyền điều khiển mẫu mà Admin đã sửa/ngừng nhận đăng ký. EN: Reseeding never takes control back from subsequent CMS versions or retirement decisions.
        if (await db.PathTemplates.AnyAsync(x => x.Slug == TemplateSlug)) return false;
        await using var stream = typeof(DemoCoreSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Core") ?? throw new InvalidOperationException("Missing Core curriculum manifest.");
        var items = (await JsonSerializer.DeserializeAsync<Item[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var expected = LessonSlugs.Concat(["core-practice-grammar", "core-practice-reading"]).ToHashSet(StringComparer.Ordinal);
        DomainError.Require(items.Length == expected.Count && expected.SetEquals(items.Select(x => x.Slug)) && items.Select(x => x.Slug).Distinct().Count() == items.Length, 422, "invalid_core_manifest", "Core manifest thiếu hoặc lặp nội dung.");
        // VI: Trùng slug với nội dung khác phải dừng, không ghim nhầm hay ghi đè payload. EN: Abort on reserved-slug collisions instead of pinning or overwriting somebody else's content.
        DomainError.Require(!await db.ContentRoots.AnyAsync(x => expected.Contains(x.Slug)), 409, "core_seed_collision", "Slug Core đã tồn tại nhưng chưa có template; cần kiểm tra dữ liệu trước khi seed.");
        var content = new Dictionary<string, ContentVersion>(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var (item, order) in items.Select((item, order) => (item, order)))
        {
            var kind = item.Kind switch { "lesson" => ContentKind.Lesson, "assessment" => ContentKind.Assessment, _ => throw new InvalidOperationException("Unsupported Core manifest kind.") };
            DomainError.Require((kind == ContentKind.Lesson) == LessonSlugs.Contains(item.Slug), 422, "invalid_core_kind", "Loại nội dung Core không khớp lịch đã biên soạn.");
            var root = new ContentRoot { Slug = item.Slug, Kind = kind };
            var version = new ContentVersion
            {
                Root = root, RootId = root.Id, Title = item.Title, Summary = item.Summary, Body = item.Body, Skill = item.Skill,
                Minutes = item.Minutes, DurationSeconds = 600, CurriculumOrder = 100 + order,
                Status = PublicationStatus.Published, PublishedAt = now, Attribution = Attribution,
                Groups = item.Groups.Select((group, index) => new QuestionGroup
                {
                    Order = index, Part = group.Part, Stimulus = group.Stimulus,
                    Questions = group.Questions.Select((quiz, index) => new Question
                    {
                        Order = index, Prompt = quiz.Prompt, Skill = item.Skill, Explanation = quiz.Explanation,
                        Options = new[] { new QuestionOption { Text = quiz.Correct, IsCorrect = true } }.Concat(quiz.Distractors.Select(text => new QuestionOption { Text = text })).ToList()
                    }).ToList()
                }).ToList()
            };
            // VI: Seed chịu cùng validator với publish CMS; đáp án trùng chữ không được qua. EN: Apply the CMS publication validator and reject text-identical options too.
            ContentService.Validate(version);
            DomainError.Require(version.Groups.SelectMany(x => x.Questions).All(x => x.Options.Select(o => o.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4), 422, "ambiguous_core_options", "Câu Core có lựa chọn trùng nhau.");
            root.Versions.Add(version); db.ContentRoots.Add(root); content.Add(item.Slug, version);
        }
        await db.SaveChangesAsync();
        foreach (var version in content.Values) version.Root.CurrentPublishedVersionId = version.Id;
        await db.SaveChangesAsync();

        var stages = new[] { new List<PathNodeInput>(), new List<PathNodeInput>(), new List<PathNodeInput>() };
        for (var day = 1; day <= LessonSlugs.Length; day++)
        {
            var stage = day <= 4 ? 0 : day <= 6 ? 1 : 2;
            string[] prerequisites = day == 1 ? [] : day == 7 ? ["lesson-06", "practice-grammar"] : [$"lesson-{day - 1:00}"];
            stages[stage].Add(new($"lesson-{day:00}", content[LessonSlugs[day - 1]].Id, day, "lesson_quiz", 100, prerequisites));
            if (day is 6 or 8)
            {
                var key = day == 6 ? "grammar" : "reading";
                stages[stage].Add(new($"practice-{key}", content[$"core-practice-{key}"].Id, day, "assessment_all_raw", 60, [$"lesson-{day:00}"]));
            }
        }
        var template = new PathTemplate { Slug = TemplateSlug, Level = "core" };
        var input = new PathInput("Core · 8 ngày học kết nối kiến thức", "8 bài, 2 câu kiểm tra mỗi bài và 2 mốc luyện tập mới. Mốc ngày là gợi ý; prerequisite, ngân sách và việc luyện lại có thể kéo dài lịch. Nội dung đọc/ngữ pháp demo, chưa có audio.", Attribution,
            [new("meaning", "Nối ý với thời gian", stages[0].ToArray()), new("precision", "Diễn đạt chính xác", stages[1].ToArray()), new("reading", "Đọc và kết hợp thông tin", stages[2].ToArray())]);
        var path = PathService.Make(template.Id, 1, input);
        var nodes = path.Stages.SelectMany(s => s.Nodes).ToDictionary(n => n.Id);
        PathGraph.Validate(nodes.Values.Select(n => new GraphNode(n.Key, n.StudyDay, n.Prerequisites.Select(p => nodes[p.PrerequisiteNodeId].Key).ToArray())).ToArray());
        path.Status = PublicationStatus.Published; path.PublishedAt = now;
        db.PathTemplates.Add(template); db.PathVersions.Add(path); await db.SaveChangesAsync();
        template.CurrentPublishedVersionId = path.Id;
        return true; // VI: Transaction lưu toàn bộ nội dung, graph và con trỏ cùng lúc. EN: Commit content, graph and publication pointers atomically.
    });
}
