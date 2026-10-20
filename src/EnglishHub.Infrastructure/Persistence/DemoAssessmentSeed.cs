using EnglishHub.Domain;
using Microsoft.EntityFrameworkCore;
namespace EnglishHub.Infrastructure.Persistence;
/// <summary>VI: Nâng seed bằng version mới, không sửa đề lịch sử. EN: Publish improved original demo content without changing pinned historical exams.</summary>
public static class DemoAssessmentSeed
{
    private const string Attribution = "EnglishHub original demonstration content · CC0 · question-pool-v2 · not ETS material";
    private static readonly (string Prompt, string Correct, string B, string C, string D, string Explanation)[] Pool = [
        ("She ___ to work every morning.","goes","go","going","gone","She là ngôi thứ ba số ít, dùng goes."),
        ("Please send the report ___ Friday.","by","at","with","into","By chỉ hạn cuối hoàn thành công việc."),
        ("The meeting has been ___ until next week.","postponed","arrived","spoken","grown","Postponed là quá khứ phân từ trong câu bị động."),
        ("We look forward to ___ you.","meeting","meet","met","meets","Look forward to đi với danh từ hoặc V-ing."),
        ("The annual report ___ by our finance team.","was prepared","prepared","preparing","has preparing","Báo cáo là đối tượng nhận hành động, dùng bị động was prepared."),
        ("The new process produces ___ errors.","fewer","less","little","much","Errors là danh từ đếm được số nhiều, dùng fewer."),
        ("They have worked together ___ 2021.","since","for","during","while","Since đi với mốc thời gian; for đi với khoảng thời gian."),
        ("___ the heavy rain, the delivery arrived on time.","Despite","Although","Because","Unless","Despite đi với cụm danh từ the heavy rain."),
        ("Linh is responsible ___ scheduling interviews.","for","to","at","with","Cụm đúng là be responsible for something."),
        ("The team ___ completed the project.","successfully","successful","success","succeed","Trạng từ successfully bổ nghĩa cho động từ completed."),
        ("You can pay either by card ___ in cash.","or","nor","and","but","Cấu trúc tương quan either ... or ..."),
        ("This training course is ___ recommended.","highly","high","height","higher","Highly recommended là kết hợp từ phù hợp; highly là trạng từ."),
        ("If the client arrives early, we ___ the meeting.","will start","started","would started","starting","Điều kiện loại 1: if + hiện tại đơn, will + nguyên mẫu."),
        ("Please contact us for ___ information.","further","far","furthestly","furtherly","Further information nghĩa là thông tin thêm."),
        ("Each participant ___ a certificate.","receives","receive","receiving","have received","Each + danh từ số ít dùng động từ số ít receives."),
        ("The guide contains useful ___.","information","informations","inform","informative","Information là danh từ không đếm được, không thêm -s."),
        ("All applications must ___ before noon.","be submitted","submitted","submitting","have submit","Bị động sau must: must be + quá khứ phân từ."),
        ("According ___ the policy, visitors need an ID.","to","with","for","at","According to là cụm giới từ nghĩa là theo."),
        ("The store offers a wide ___ of office supplies.","range","ranged","ranging","ranges","A wide range of là một phạm vi/loạt đa dạng."),
        ("I have ___ finished the assignment.","already","yet","still not","no longer","Already thường đứng giữa have/has và quá khứ phân từ.")
    ];
    public static async Task Ensure(HubDbContext db)
    {
        foreach (var slug in new[] { "placement-demo", "mini-grammar", "mini-reading", "full-mock-fixture" })
        {
            var root = await db.ContentRoots.Include(x => x.CurrentPublishedVersion).SingleAsync(x => x.Slug == slug);
            var old = root.CurrentPublishedVersion!; if (old.Attribution == Attribution) continue;
            var v = new ContentVersion { RootId = root.Id, Version = old.Version + 1, Status = PublicationStatus.Published, Title = old.Title, Summary = old.Summary, Skill = slug == "mini-reading" ? "reading" : old.Skill, Minutes = old.Minutes, DurationSeconds = old.DurationSeconds, IsFixture = old.IsFixture, Attribution = Attribution, PublishedAt = DateTimeOffset.UtcNow };
            if (slug == "full-mock-fixture")
            {
                var counts = new[] { 6, 25, 39, 30, 30, 16, 54 }; var order = 0;
                for (var part = 1; part <= 7; part++)
                {
                    var size = part is 3 or 4 or 7 ? 3 : part == 6 ? 4 : 1;
                    for (var offset = 0; offset < counts[part - 1]; offset += size)
                    {
                        var group = new QuestionGroup { Part = part, Order = v.Groups.Count, Stimulus = $"Technical format fixture · Part {part} · original grammar items, not simulated ETS content." };
                        for (var j = 0; j < Math.Min(size, counts[part - 1] - offset); j++) group.Questions.Add(MakeQuestion(order++, part, j));
                        v.Groups.Add(group);
                    }
                }
            }
            else if (slug == "mini-reading")
            {
                var passages = new[] { ("Thursday", "10 a.m.", "Room 204", "project updates"), ("Monday", "9 a.m.", "Room 105", "sales figures"), ("Tuesday", "2 p.m.", "the main hall", "training materials"), ("Friday", "11 a.m.", "Room 302", "design sketches"), ("Wednesday", "3 p.m.", "the library", "research notes") };
                foreach (var (day, time, room, bring) in passages)
                {
                    var group = new QuestionGroup { Part = 7, Order = v.Groups.Count, Stimulus = $"Dear team, our meeting is on {day} at {time} in {room}. Please bring your {bring}. If you cannot attend, email the coordinator before the meeting. Thank you." };
                    var questions = new[] { ("On which day is the meeting?", day, "Saturday", "Sunday", "Not stated"), ("Where will the team meet?", room, "At a restaurant", "In the car park", "Online only"), ("What should participants bring?", bring, "Their passports", "Lunch for everyone", "Nothing"), ("What should someone do if unable to attend?", "Email the coordinator", "Call the police", "Ignore the invitation", "Reschedule it alone") };
                    foreach (var (prompt, correct, b, c, d) in questions) group.Questions.Add(new Question { Order = group.Questions.Count, Prompt = prompt, Skill = "reading", Explanation = $"Đọc thông tin cụ thể trong email. Đáp án được nêu trực tiếp: {correct}.", Options = [new() { Text = correct, IsCorrect = true }, new() { Text = b }, new() { Text = c }, new() { Text = d }] });
                    v.Groups.Add(group);
                }
            }
            else for (var i = 0; i < 20; i++) v.Groups.Add(new QuestionGroup { Order = i, Part = 5, Stimulus = "Original EnglishHub workplace English", Questions = [MakeQuestion(i, 5, 0)] });
            db.ContentVersions.Add(v); await db.SaveChangesAsync(); root.CurrentPublishedVersionId = v.Id; await db.SaveChangesAsync();
        }
    }
    private static Question MakeQuestion(int index, int part, int order)
    {
        var q = Pool[index % Pool.Length]; var options = new List<QuestionOption> { new() { Text = q.Correct, IsCorrect = true }, new() { Text = q.B }, new() { Text = q.C } }; if (part != 2) options.Add(new() { Text = q.D });
        return new Question { Order = order, Prompt = q.Prompt, Skill = part <= 4 ? "listening" : "grammar", Explanation = q.Explanation, Options = options };
    }
}
