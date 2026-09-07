using EnglishHub.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Seed tự biên soạn, chỉ bật trong demo/test. EN: Original illustrative content; never seeded implicitly in production.</summary>
public static class DemoSeed
{
    public const string Password = "EnglishHub@2026";
    public static async Task Run(HubDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles)
    {
        foreach (var role in new[] { "Learner", "ContentEditor", "Admin" })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
        foreach (var (email, name, role) in new[] { ("learner@englishhub.local", "Minh Anh", "Learner"), ("admin@englishhub.local", "Quản trị viên", "Admin"), ("editor@englishhub.local", "Biên tập viên", "ContentEditor"), ("learner2@englishhub.local", "Thanh Việt", "Learner"), ("learner3@englishhub.local", "Thu Hà", "Learner") })
        {
            if (await users.FindByEmailAsync(email) is not null) continue;
            var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, DisplayName = name, EmailConfirmed = true };
            var result = await users.CreateAsync(user, Password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(';', result.Errors.Select(x => x.Description)));
            await users.AddToRoleAsync(user, role);
        }
        if (await db.ContentRoots.AnyAsync()) return;
        var lessons = new[] {
            ("Everyday introductions", "vocabulary", "Introduce yourself with confidence", "Start with a greeting, your name, and one detail about yourself.\n\nHello, I'm Linh. I study computer science in Ho Chi Minh City.\nNice to meet you. What do you do?\n\nGhi nhớ: dùng 'I'm' khi giới thiệu tên hoặc nghề nghiệp; dùng 'I study' cho ngành học. Hãy tự viết ba câu giới thiệu bản thân."),
            ("Present simple, real progress", "grammar", "Thì hiện tại đơn trong công việc hằng ngày", "Use the present simple for routines and facts.\n\nI work from Monday to Friday.\nShe works at a small design studio.\nDo you work on Saturdays?\n\nVới he/she/it, thêm -s hoặc -es trong câu khẳng định. Câu hỏi dùng does và động từ nguyên mẫu. Thực hành: viết lịch hằng ngày của bạn."),
            ("A conversation at the café", "listening", "Nghe ý chính trong hội thoại ngắn", "Before listening, predict the setting and the speakers' purpose.\n\nA: Could I have a coffee, please?\nB: Of course. Would you like milk?\nA: Yes, a little. How much is it?\nB: That will be three dollars.\n\nNội dung hội thoại demo tự biên soạn. Chú ý cụm 'Could I have' để gọi món lịch sự. Xác định người mua muốn gì và giá bao nhiêu."),
            ("Reading a workplace email", "reading", "Đọc email và tìm thông tin cụ thể", "Dear team,\nOur weekly meeting will take place in Room 204 at 10 a.m. on Thursday. Please bring your project updates. If you cannot attend, email me before Wednesday afternoon.\nBest,\nAlex\n\nTìm ba dữ kiện: địa điểm, thời gian, hành động cần làm. Khi đọc lướt, xác định mục đích trước; sau đó quét từ khóa để trả lời từng câu hỏi."),
            ("Making plans together", "grammar", "Diễn đạt kế hoạch bằng be going to", "Use be going to + base verb for plans you have already decided.\n\nWe are going to practise English tonight.\nShe is going to take a mini test tomorrow.\nAre you going to join the study group?\n\nPhân biệt kế hoạch đã quyết định và thói quen. Viết một kế hoạch cho ngày mai, sau đó chuyển thành câu hỏi."),
            ("Words that open doors", "vocabulary", "Từ vựng học tập và công việc", "Opportunity: a chance to do something useful.\nImprove: to become better.\nSchedule: a plan of activities and times.\nConfident: feeling sure about your ability.\n\nA little practice every day can improve your confidence.\n\nTự tạo câu có ngữ cảnh riêng cho mỗi từ. Học nghĩa và cách dùng trong câu thay vì chỉ nhớ bản dịch.") };
        foreach (var (title, skill, summary, body) in lessons)
            db.ContentRoots.Add(MakeRoot(title.ToLowerInvariant().Replace(' ', '-').Replace(",", ""), ContentKind.Lesson, new ContentVersion { Title = title, Skill = skill, Summary = summary, Body = body, Minutes = 10 }));
        foreach (var (slug, title, count) in new[] { ("placement-demo", "Đánh giá đầu vào · 20 câu", 20), ("mini-grammar", "Mini test · Ngữ pháp công sở", 20), ("mini-reading", "Mini test · Đọc hiểu mỗi ngày", 20), ("full-mock-fixture", "Full mock · Dữ liệu demo kỹ thuật", 200) })
        {
            var v = new ContentVersion { Title = title, Summary = count == 200 ? "Fixture kiểm thử timer và chấm điểm, không phải đề thi thật." : "Câu hỏi tự biên soạn để minh họa hành trình học.", Skill = "grammar", Minutes = count == 200 ? 120 : 15, DurationSeconds = count == 200 ? 7200 : 1200, IsFixture = count == 200 };
            for (var i = 0; i < count; i++)
            {
                var examples = new[] {
                    ("She ___ to work every morning.", "goes", "go", "going", "gone", "Chủ ngữ she dùng động từ thêm -s trong hiện tại đơn."),
                    ("Please send the report ___ Friday.", "by", "at", "with", "into", "By chỉ hạn cuối hoàn thành một việc."),
                    ("The meeting has been ___ until next week.", "postponed", "arrived", "spoken", "grown", "Postpone = hoãn; bị động dùng quá khứ phân từ postponed."),
                    ("We look forward to ___ you.", "meeting", "meet", "met", "meets", "Sau look forward to dùng danh từ hoặc V-ing.") };
                var ex = examples[i % examples.Length];
                v.Groups.Add(new QuestionGroup { Order = i, Part = 5, Stimulus = "Original EnglishHub demonstration item", Questions = [new Question { Prompt = ex.Item1, Explanation = ex.Item6, Skill = "grammar", Options = [new() { Text = ex.Item2, IsCorrect = true }, new() { Text = ex.Item3 }, new() { Text = ex.Item4 }, new() { Text = ex.Item5 }] }] });
            }
            db.ContentRoots.Add(MakeRoot(slug, ContentKind.Assessment, v));
        }
        await db.SaveChangesAsync();
        // VI: FK composite chỉ được cập nhật sau khi version tồn tại. EN: Set publication pointers after all version rows exist.
        foreach (var root in db.ContentRoots.Local) root.CurrentPublishedVersionId = root.Versions.Single().Id;
        await db.SaveChangesAsync();
    }

    private static ContentRoot MakeRoot(string slug, ContentKind kind, ContentVersion v)
    {
        v.Status = PublicationStatus.Published; v.PublishedAt = DateTimeOffset.UtcNow;
        v.Attribution = "EnglishHub original demonstration content · CC0 · not ETS material";
        return new ContentRoot { Slug = slug, Kind = kind, Versions = [v] };
    }
}
