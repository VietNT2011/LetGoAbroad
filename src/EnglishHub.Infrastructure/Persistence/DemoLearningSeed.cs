using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Thêm quiz bằng version mới, giữ nguyên seed cũ đã publish. EN: Upgrade existing demo lessons by publishing a new version, preserving earlier payloads.</summary>
public static class DemoLearningSeed
{
    private static readonly string[] Titles = ["Everyday introductions", "Present simple, real progress", "A conversation at the café", "Reading a workplace email", "Making plans together", "Words that open doors"];
    public static async Task Ensure(HubDbContext db)
    {
        var roots = await db.ContentRoots.Include(x => x.CurrentPublishedVersion).ThenInclude(x => x!.Groups).Where(x => x.Kind == ContentKind.Lesson).ToListAsync();
        foreach (var root in roots)
        {
            var old = root.CurrentPublishedVersion!;
            if (old.Groups.Count > 0 || !old.Attribution.StartsWith("EnglishHub original")) continue;
            var index = Array.IndexOf(Titles, old.Title);
            if (index < 0) continue;
            var quizData = new[] {
                ("Which introduction is correct?", "I'm Linh. I study computer science.", "I Linh. I studying computer science.", "Chào hỏi và giới thiệu dùng I'm + tên; thói quen/ngành học dùng I study."),
                ("Which sentence describes a routine correctly?", "She works at a design studio.", "She work at a design studio.", "She là ngôi thứ ba số ít, cần works."),
                ("What does the customer ask for?", "A coffee with a little milk.", "A sandwich and water.", "Trong hội thoại, người mua gọi coffee và đồng ý thêm a little milk."),
                ("When is the meeting?", "Thursday at 10 a.m.", "Wednesday afternoon.", "Thursday 10 a.m. là giờ họp; Wednesday afternoon là hạn báo vắng."),
                ("Which sentence expresses a decided plan?", "We are going to practise tonight.", "We going practise tonight.", "Kế hoạch dùng be going to + động từ nguyên mẫu."),
                ("What does opportunity mean?", "A chance to do something useful.", "A feeling of being tired.", "Opportunity nghĩa là cơ hội, như định nghĩa trong bài học.") };
            var q = quizData[index];
            var v = new ContentVersion { RootId = root.Id, Version = old.Version + 1, Status = PublicationStatus.Published, Title = old.Title, Summary = old.Summary, Body = old.Body, Skill = old.Skill, Minutes = old.Minutes, CurriculumOrder = index, Attribution = old.Attribution, PublishedAt = DateTimeOffset.UtcNow,
                Groups = [new QuestionGroup { Part = 5, Questions = [new Question { Prompt = q.Item1, Skill = old.Skill, Explanation = q.Item4, Options = [new() { Text = q.Item2, IsCorrect = true }, new() { Text = q.Item3 }, new() { Text = "None of the above." }, new() { Text = "Both statements are correct." }] }] }] };
            db.ContentVersions.Add(v); await db.SaveChangesAsync(); root.CurrentPublishedVersionId = v.Id;
        }
        await db.SaveChangesAsync();
    }
}
