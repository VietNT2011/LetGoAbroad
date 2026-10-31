using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Giữ lịch Core đã biên soạn và dùng publisher chung, không sửa version đã ghim. EN: Preserve the authored Core schedule while sharing additive publication.</summary>
public static class DemoCoreSeed
{
    public const string TemplateSlug = "core-eight-study-days";
    public const string Attribution = "EnglishHub original Core curriculum v1 · CC0 · not ETS material or a certified level assessment";
    private static readonly string[] LessonSlugs = ["core-present-perfect", "core-past-events", "core-passive-processes", "core-first-conditional", "core-relative-clauses", "core-modals-at-work", "core-reading-revisions", "core-reading-connections"];

    public static Task Ensure(HubDbContext db) => AuthoredCurriculumSeed.Ensure(db, new(
        TemplateSlug, "core", "EnglishHub.Seed.Core", Attribution, LessonSlugs,
        ["core-practice-grammar", "core-practice-reading"], 100, BuildPath));

    private static PathInput BuildPath(IReadOnlyDictionary<string, Guid> content)
    {
        var stages = new[] { new List<PathNodeInput>(), new List<PathNodeInput>(), new List<PathNodeInput>() };
        for (var day = 1; day <= LessonSlugs.Length; day++)
        {
            var stage = day <= 4 ? 0 : day <= 6 ? 1 : 2;
            string[] prerequisites = day == 1 ? [] : day == 7 ? ["lesson-06", "practice-grammar"] : [$"lesson-{day - 1:00}"];
            stages[stage].Add(new($"lesson-{day:00}", content[LessonSlugs[day - 1]], day, "lesson_quiz", 100, prerequisites));
            if (day is 6 or 8)
            {
                var key = day == 6 ? "grammar" : "reading";
                stages[stage].Add(new($"practice-{key}", content[$"core-practice-{key}"], day, "assessment_all_raw", 60, [$"lesson-{day:00}"]));
            }
        }
        return new("Core · 8 ngày học kết nối kiến thức", "8 bài, 2 câu kiểm tra mỗi bài và 2 mốc luyện tập mới. Mốc ngày là gợi ý; prerequisite, ngân sách và việc luyện lại có thể kéo dài lịch. Nội dung đọc/ngữ pháp demo, chưa có audio.", Attribution,
            [new("meaning", "Nối ý với thời gian", stages[0].ToArray()), new("precision", "Diễn đạt chính xác", stages[1].ToArray()), new("reading", "Đọc và kết hợp thông tin", stages[2].ToArray())]);
    }
}
