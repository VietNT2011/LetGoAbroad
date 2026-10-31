using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Lịch 14 ngày học có bài/quiz thật, ghim phiên bản tại lúc seed. EN: An authored fourteen-study-day schedule pins real lessons and quiz versions once.</summary>
public static class DemoPathSeed
{
    public static async Task Ensure(HubDbContext db)
    {
        if (await db.PathTemplates.AnyAsync(x => x.Slug == "foundation-fourteen-days")) return;
        var lessons = await db.ContentVersions.Include(x => x.Root).Where(x => x.Root.Kind == ContentKind.Lesson && x.Root.CurrentPublishedVersionId == x.Id && x.Status == PublicationStatus.Published).OrderBy(x => x.CurriculumOrder).ThenBy(x => x.Root.Slug).Take(14).ToArrayAsync();
        if (lessons.Length != 14) throw new InvalidOperationException("The foundation path requires fourteen authored lessons.");
        var practice = await db.ContentVersions.Include(x => x.Root).Where(x => x.Root.CurrentPublishedVersionId == x.Id && (x.Root.Slug == "mini-grammar" || x.Root.Slug == "mini-reading" || x.Root.Slug == "mini-fill-blank")).ToDictionaryAsync(x => x.Root.Slug);
        var stages = new[] { new List<PathNodeInput>(), new List<PathNodeInput>(), new List<PathNodeInput>() };
        for (var day = 1; day <= 14; day++)
        {
            var stage = day <= 4 ? 0 : day <= 9 ? 1 : 2;
            stages[stage].Add(new($"lesson-{day:00}", lessons[day - 1].Id, day, "lesson_quiz", 100, day == 1 ? [] : [$"lesson-{day - 1:00}"]));
            if (day is 3 or 7 or 10 or 14)
            {
                var slug = day == 3 ? "mini-grammar" : day == 10 ? "mini-fill-blank" : "mini-reading";
                stages[stage].Add(new($"practice-{day:00}", practice[slug].Id, day, "assessment_raw", 50, [$"lesson-{day - 1:00}"]));
            }
        }
        var root = new PathTemplate { Slug = "foundation-fourteen-days", Level = "foundation" };
        var input = new PathInput("14 ngày học xây nền tảng", "14 bài học và 4 mốc luyện tập. Mỗi ngày tối đa ba nhiệm vụ theo ngân sách; có thể cần thêm ngày khi học chậm hoặc bỏ lỡ.", "EnglishHub original schedule v1 · CC0 · not a certified language-level assessment", [new("starting", "Bắt đầu tự tin", stages[0].ToArray()), new("routines", "Kết nối thói quen", stages[1].ToArray()), new("workplace", "Dùng trong công việc", stages[2].ToArray())]);
        var version = PathService.Make(root.Id, 1, input); version.Status = PublicationStatus.Published; version.PublishedAt = DateTimeOffset.UtcNow;
        db.PathTemplates.Add(root); db.PathVersions.Add(version); await db.SaveChangesAsync();
        root.CurrentPublishedVersionId = version.Id; await db.SaveChangesAsync();
    }
}
