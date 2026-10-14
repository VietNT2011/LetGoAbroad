using System.Text.Json;
using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;
/// <summary>VI: Nội dung nguyên gốc có quyền CC0, bổ sung đủ 14 bài nền tảng. EN: Original CC0 lessons extend the foundation library to fourteen lessons.</summary>
public static class DemoFoundationSeed
{
    private sealed record Lesson(string Slug,string Title,string Skill,string Body,string Prompt,string Correct,string[] Distractors,string Explanation);
    public static async Task Ensure(HubDbContext db)
    {
        await using var stream=typeof(DemoFoundationSeed).Assembly.GetManifestResourceStream("EnglishHub.Seed.Foundation") ?? throw new InvalidOperationException("Missing foundation seed manifest.");
        var lessons=(await JsonSerializer.DeserializeAsync<Lesson[]>(stream,new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        for(var i=0;i<lessons.Length;i++)
        {
            var lesson=lessons[i]; if(await db.ContentRoots.AnyAsync(x => x.Slug==lesson.Slug)) continue;
            var root=new ContentRoot { Slug=lesson.Slug,Kind=ContentKind.Lesson };
            var version=new ContentVersion { RootId=root.Id,Title=lesson.Title,Summary=lesson.Skill=="reading" ? "Đọc văn bản ngắn và tìm đúng thông tin trong ngữ cảnh." : "Hiểu cách dùng, xem ví dụ và củng cố bằng quiz cuối bài.",Body=lesson.Body,Skill=lesson.Skill,Minutes=10,CurriculumOrder=i+6,Status=PublicationStatus.Published,PublishedAt=DateTimeOffset.UtcNow,Attribution="EnglishHub original foundation curriculum v1 · CC0 · not ETS material",
                Groups=[new QuestionGroup { Part=5,Questions=[new Question { Prompt=lesson.Prompt,Skill=lesson.Skill,Explanation=lesson.Explanation,Options=new[] { new QuestionOption { Text=lesson.Correct,IsCorrect=true } }.Concat(lesson.Distractors.Select(x => new QuestionOption { Text=x })).ToList() }] }] };
            db.ContentRoots.Add(root); db.ContentVersions.Add(version); await db.SaveChangesAsync(); root.CurrentPublishedVersionId=version.Id; await db.SaveChangesAsync();
        }
    }
}
