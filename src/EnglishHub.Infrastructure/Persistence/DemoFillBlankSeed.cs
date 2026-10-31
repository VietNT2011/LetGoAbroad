using System.Text.Json;
using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Persistence;

/// <summary>VI: Bài gõ đáp án tự biên soạn, seed idempotent và không sửa lịch sử. EN: Seed an original typed-answer exercise once without rewriting published content.</summary>
public static class DemoFillBlankSeed
{
    public static async Task Ensure(HubDbContext db)
    {
        if (await db.ContentRoots.AnyAsync(x => x.Slug == "mini-fill-blank")) return;
        var version = new ContentVersion
        {
            Status = PublicationStatus.Published,
            Title = "Gõ đáp án · Tiếng Anh công sở",
            Summary = "5 câu điền đáp án tự biên soạn; chấm theo biến thể và quy tắc chuẩn hóa đã công bố.",
            Skill = "grammar",
            Minutes = 5,
            DurationSeconds = 600,
            Attribution = "EnglishHub original · CC0 · typed-answer-v1 · not ETS material",
            PublishedAt = DateTimeOffset.UtcNow
        };
        var items = new (string Prompt, string[] Variants, bool Punctuation, string Explanation)[]
        {
            ("I ___ the report every Monday. (send / sends)", ["send"], false, "I đi với động từ nguyên mẫu send ở hiện tại đơn."),
            ("Write the full form of: I'm ready.", ["I am ready"], true, "I'm là dạng rút gọn của I am. Câu này cho phép có hoặc không có dấu chấm cuối câu."),
            ("Complete with don't or doesn't: We ___ work on Sundays.", ["don't", "do not"], false, "We dùng don't hoặc dạng đầy đủ do not. Dấu nháy cong được chuẩn hóa; không bỏ các dấu câu khác."),
            ("The meeting starts ___ 9 a.m. (at / on)", ["at"], false, "Dùng at trước giờ cụ thể: at 9 a.m."),
            ("Write the full form of: They aren't available.", ["They are not available"], true, "Aren't là are not. Hoa/thường và khoảng trắng không làm thay đổi đáp án, nhưng không chấp nhận lỗi chính tả.")
        };
        foreach (var (prompt, variants, punctuation, explanation) in items)
            version.Groups.Add(new QuestionGroup { Order = version.Groups.Count, Part = 5, Stimulus = "Original workplace English · Type your answer", Questions = [new Question { Type = QuestionType.FillBlank, Prompt = prompt, Skill = "grammar", Explanation = explanation, AcceptedAnswerVariantsJson = JsonSerializer.Serialize(variants), IgnoreTerminalPunctuation = punctuation }] });
        var root = new ContentRoot { Slug = "mini-fill-blank", Kind = ContentKind.Assessment, Versions = [version] };
        db.ContentRoots.Add(root); await db.SaveChangesAsync();
        root.CurrentPublishedVersionId = version.Id; await db.SaveChangesAsync();
    }
}
