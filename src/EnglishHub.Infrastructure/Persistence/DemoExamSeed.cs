using EnglishHub.Infrastructure.Learning;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Luyện đọc/ngữ pháp nguyên gốc, không phải full mock hay đề ETS. EN: An original reading/grammar starter, not a full mock or an ETS item bank.</summary>
public static class DemoExamSeed
{
    public const string TemplateSlug = "exam-practice-six-study-days";
    public const string Attribution = "EnglishHub original Exam Practice starter v1 · CC0 · not ETS material or a calibrated test";
    private static readonly string[] LessonSlugs = ["exam-word-forms", "exam-linking-logic", "exam-purpose-inference", "exam-cross-document", "exam-exceptions-conditions", "exam-paraphrase-evidence"];

    public static Task Ensure(HubDbContext db) => AuthoredCurriculumSeed.Ensure(db, new(
        TemplateSlug, "exam_practice", "EnglishHub.Seed.Exam", Attribution, LessonSlugs,
        ["exam-practice-context", "exam-practice-documents"], 200, BuildPath));

    private static PathInput BuildPath(IReadOnlyDictionary<string, Guid> content)
    {
        var stages = new[] { new List<PathNodeInput>(), new List<PathNodeInput>(), new List<PathNodeInput>() };
        for (var day = 1; day <= LessonSlugs.Length; day++)
        {
            var stage = (day - 1) / 2;
            string[] prerequisites = day == 1 ? [] : day == 3 ? ["lesson-02", "practice-context"] : [$"lesson-{day - 1:00}"];
            stages[stage].Add(new($"lesson-{day:00}", content[LessonSlugs[day - 1]], day, "lesson_quiz", 100, prerequisites));
            if (day is 2 or 6)
            {
                var key = day == 2 ? "context" : "documents";
                stages[stage].Add(new($"practice-{key}", content[$"exam-practice-{key}"], day, "assessment_all_raw", 60, [$"lesson-{day:00}"]));
            }
        }
        return new("Exam Practice · 6 ngày luyện đọc có chiến lược", "6 bài, 12 câu quiz và 13 câu luyện theo nhóm tài liệu nguyên gốc. Chỉ đọc/ngữ pháp; chưa có audio hoặc full mock. Ngày học là gợi ý, không phải cam kết điểm thi.", Attribution,
            [new("context", "Chọn theo ngữ cảnh", stages[0].ToArray()), new("evidence", "Đối chiếu bằng chứng", stages[1].ToArray()), new("precision", "Đọc đúng điều kiện", stages[2].ToArray())]);
    }
}
