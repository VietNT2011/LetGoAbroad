using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Lịch sử hoạt động phải đọc đúng nguồn/ngày/owner, không tạo tiến độ giả. EN: Verify source counts, local-date boundaries and owner isolation without inventing learner activity.</summary>
public sealed class LearningActivityTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Empty_windows_include_every_date_and_reads_never_create_learning_state()
    {
        SetNow("2026-11-17T12:00:00Z"); using var learner = await Learner(); var user = await User(learner);
        var seven = await Read(learner); Assert.Equal(7, seven.WindowDays); Assert.Equal(7, seven.Days.Length); Assert.Equal(new DateOnly(2026, 11, 11), seven.FromDate); Assert.Equal(new DateOnly(2026, 11, 17), seven.ToDate);
        var thirty = await Read(learner, 30); Assert.Equal(30, thirty.Days.Length); Assert.Equal(new DateOnly(2026, 10, 19), thirty.FromDate); Assert.Equal(0, thirty.ActiveDays);
        Assert.All(thirty.Days, day => Assert.Equal(0, day.TotalActivities)); Assert.Equal(Enumerable.Range(0, 30).Select(x => thirty.FromDate.AddDays(x)), thirty.Days.Select(x => x.Date));
        Assert.Equal(JsonSerializer.Serialize(seven), JsonSerializer.Serialize(await Read(learner)));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.False(await db.Enrollments.AnyAsync(x => x.UserId == user)); Assert.False(await db.DailyTasks.AnyAsync(x => x.UserId == user)); Assert.False(await db.LessonProgress.AnyAsync(x => x.UserId == user)); Assert.False(await db.Attempts.AnyAsync(x => x.UserId == user));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Unsupported_windows_are_rejected_instead_of_querying_unbounded_history(int days)
    {
        using var learner = await Learner(); var response = await learner.GetAsync($"/api/v1/learning/activity?days={days}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode); Assert.Contains("invalid_activity_window", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Only_owned_valid_sources_count_with_full_mock_participation_kept_separate()
    {
        SetNow("2026-11-17T12:00:00Z"); using var owner = await Learner(); using var outsider = await Learner(); var user = await User(owner); var other = await User(outsider);
        var bank = await Bank(ContentKind.Assessment); var lesson = await Bank(ContentKind.Lesson); var fixture = await Bank(ContentKind.Assessment, true); var fixtureLesson = await Bank(ContentKind.Lesson, true);
        foreach (var mode in new[] { "mini", "practice", "placement", "full_mock", "mistake_review", "fixture", "unknown_legacy_mode" }) await Attempt(user, bank, app.Clock.UtcNow, mode);
        await Attempt(user, fixture, app.Clock.UtcNow); await Attempt(user, bank, app.Clock.UtcNow, status: AttemptStatus.InProgress); await Attempt(user, bank, app.Clock.UtcNow, status: AttemptStatus.Abandoned); await Attempt(user, bank, app.Clock.UtcNow, total: 0);
        await Attempt(other, bank, app.Clock.UtcNow); await Lesson(user, lesson, app.Clock.UtcNow); await Lesson(user, fixtureLesson, app.Clock.UtcNow);
        // VI: Nội dung archive vẫn là nguồn lịch sử; xem lời giải không xóa một lượt đã chấm khỏi lịch sử tham gia. EN: Archival and disclosure do not erase genuine participation, which is separate from unseen accuracy.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var version = await db.ContentVersions.SingleAsync(x => x.Id == bank.Id); version.Status = PublicationStatus.Archived;
            var full = await db.Attempts.SingleAsync(x => x.UserId == user && x.Mode == "full_mock"); full.EvidencePolicyVersion = 0; full.FirstDisclosedAt = full.StartedAt; await db.SaveChangesAsync();
        }
        var data = await Read(owner); Assert.Equal(new LearningActivityCounts(1, 3, 1), data.Totals); Assert.Equal(1, data.ActiveDays); Assert.Equal(5, data.Days.Single(x => x.Date == data.ToDate).TotalActivities);
        var foreignQuery = await owner.GetFromJsonAsync<LearningActivitySnapshot>($"/api/v1/learning/activity?userId={other}"); Assert.Equal(data.Totals, foreignQuery!.Totals);
        Assert.Equal(1, (await Read(outsider)).Totals.PracticeAttempts);
        var payload = await owner.GetStringAsync("/api/v1/learning/activity"); Assert.DoesNotContain("correctOptionId", payload); Assert.DoesNotContain("answerText", payload); Assert.DoesNotContain("questionId", payload); Assert.DoesNotContain("accuracy", payload);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/activity")).StatusCode);
    }

    [Theory]
    [InlineData("UTC", "2026-11-17")]
    [InlineData("Pacific/Kiritimati", "2026-11-18")]
    [InlineData("Etc/GMT+12", "2026-11-16")]
    public async Task Local_window_includes_its_first_midnight_and_now_but_excludes_earlier_and_future_sources(string zoneId, string today)
    {
        SetNow("2026-11-17T11:30:00Z"); using var learner = await Learner(zoneId); var user = await User(learner); var bank = await Bank(ContentKind.Assessment);
        var first = DateOnly.Parse(today).AddDays(-6); var boundary = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.FindSystemTimeZoneById(zoneId)));
        await Attempt(user, bank, boundary.AddTicks(-1)); await Attempt(user, bank, boundary); await Attempt(user, bank, app.Clock.UtcNow); await Attempt(user, bank, app.Clock.UtcNow.AddTicks(1));
        var data = await Read(learner); Assert.Equal(DateOnly.Parse(today), data.ToDate); Assert.Equal(first, data.FromDate); Assert.Equal(2, data.Totals.PracticeAttempts); Assert.Equal(1, data.Days[0].PracticeAttempts); Assert.Equal(1, data.Days[^1].PracticeAttempts); Assert.Equal(zoneId, data.TimeZoneId);
    }

    [Theory]
    [InlineData("2026-03-09T12:00:00Z", "2026-03-08T06:59:00Z", "2026-03-08T07:01:00Z", "2026-03-08")]
    [InlineData("2026-11-02T12:00:00Z", "2026-11-01T05:30:00Z", "2026-11-01T06:30:00Z", "2026-11-01")]
    public async Task Dst_skipped_and_repeated_hours_stay_on_their_actual_local_date(string now, string before, string after, string day)
    {
        SetNow(now); using var learner = await Learner("America/New_York"); var user = await User(learner); var bank = await Bank(ContentKind.Assessment);
        await Attempt(user, bank, DateTimeOffset.Parse(before)); await Attempt(user, bank, DateTimeOffset.Parse(after));
        var data = await Read(learner); Assert.Equal(2, data.Days.Single(x => x.Date == DateOnly.Parse(day)).PracticeAttempts); Assert.Equal(1, data.ActiveDays);
    }

    [Fact]
    public async Task A_skipped_calendar_date_does_not_require_constructing_an_invalid_local_midnight()
    {
        SetNow("2011-12-31T12:00:00Z"); using var learner = await Learner("Pacific/Apia"); var user = await User(learner); var bank = await Bank(ContentKind.Assessment);
        await Attempt(user, bank, DateTimeOffset.Parse("2011-12-30T09:59:00Z")); await Attempt(user, bank, DateTimeOffset.Parse("2011-12-30T10:01:00Z"));
        var data = await Read(learner); Assert.Equal(2, data.Totals.PracticeAttempts); Assert.Equal(0, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 30)).TotalActivities);
        Assert.Equal(1, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 29)).PracticeAttempts); Assert.Equal(1, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 31)).PracticeAttempts);
    }

    [Fact]
    public async Task Current_profile_timezone_rebuckets_completion_time_without_rewriting_source_records()
    {
        SetNow("2026-11-17T23:00:00Z"); using var learner = await Learner(); var user = await User(learner); var bank = await Bank(ContentKind.Assessment); var finalized = DateTimeOffset.Parse("2026-11-17T20:00:00Z");
        var id = await Attempt(user, bank, finalized); Assert.Equal(1, (await Read(learner)).Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).PracticeAttempts);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); (await db.Users.SingleAsync(x => x.Id == user)).TimeZoneId = "Asia/Ho_Chi_Minh"; await db.SaveChangesAsync(); }
        var data = await Read(learner); Assert.Equal(new DateOnly(2026, 11, 18), data.ToDate); Assert.Equal(1, data.Days[^1].PracticeAttempts);
        using var check = app.Services.CreateScope(); Assert.Equal(finalized, (await check.ServiceProvider.GetRequiredService<HubDbContext>().AttemptResults.SingleAsync(x => x.AttemptId == id)).FinalizedAt);
    }

    [Fact]
    public async Task Real_lesson_and_submit_receipt_replays_count_each_source_once_at_completion_time()
    {
        SetNow("2026-11-17T23:59:00Z"); using var learner = await Learner();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var lesson = await db.ContentVersions.Where(x => x.Root.Slug == "core-present-perfect" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var questions = await db.Questions.Include(x => x.Options).Where(x => x.Group.ContentVersionId == lesson).ToArrayAsync();
        var lessonInput = new CompleteLessonInput(Guid.NewGuid(), questions.Select(q => new LessonAnswer(q.Id, q.Options.Single(x => x.IsCorrect).Id)).ToArray());
        (await learner.PostAsJsonAsync($"/api/v1/lessons/{lesson}/complete", lessonInput)).EnsureSuccessStatusCode(); (await learner.PostAsJsonAsync($"/api/v1/lessons/{lesson}/complete", lessonInput)).EnsureSuccessStatusCode();
        (await learner.PostAsJsonAsync($"/api/v1/lessons/{lesson}/complete", lessonInput with { ClientRequestId = Guid.NewGuid() })).EnsureSuccessStatusCode();
        var bank = await db.ContentVersions.Where(x => x.Root.Slug == "mini-grammar" && x.Root.CurrentPublishedVersionId == x.Id).Select(x => x.Id).SingleAsync();
        var started = await learner.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(bank, Guid.NewGuid(), "practice", QuestionCount: 1)); started.EnsureSuccessStatusCode(); var attempt = (await started.Content.ReadFromJsonAsync<AttemptView>())!;
        app.Clock.Advance(TimeSpan.FromMinutes(2)); var submit = new SaveAnswers(Guid.NewGuid(), []);
        (await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", submit)).EnsureSuccessStatusCode(); (await learner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", submit)).EnsureSuccessStatusCode();
        var result = await Read(learner); Assert.Equal(new LearningActivityCounts(1, 1, 0), result.Totals); Assert.Equal(2, result.ActiveDays);
        Assert.Equal(1, result.Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).LessonsCompleted); Assert.Equal(1, result.Days.Single(x => x.Date == new DateOnly(2026, 11, 18)).PracticeAttempts);
    }

    private void SetNow(string utc) => app.Clock.Advance(DateTimeOffset.Parse(utc) - app.Clock.UtcNow);
    private static async Task<LearningActivitySnapshot> Read(HttpClient client, int? days = null) => (await client.GetFromJsonAsync<LearningActivitySnapshot>("/api/v1/learning/activity" + (days is null ? "" : $"?days={days}")))!;
    private static async Task<Guid> User(HttpClient client) => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private async Task<HttpClient> Learner(string zone = "UTC")
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"activity-{Guid.NewGuid():N}@example.com";
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Activity fixture", TimeZoneId = zone };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
    private async Task<ContentVersion> Bank(ContentKind kind, bool fixture = false)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var version = new ContentVersion { Title = "Original activity fixture", Status = PublicationStatus.Published, IsFixture = fixture, Groups = [new QuestionGroup { Part = 5, Questions = [new Question { Prompt = "Test only", Skill = "grammar", Options = [new() { Text = "Correct", IsCorrect = true }, new() { Text = "Other" }] }] }] };
        var root = new ContentRoot { Slug = $"activity-{Guid.NewGuid():N}", Kind = kind, Versions = [version] }; db.ContentRoots.Add(root); await db.SaveChangesAsync(); return version;
    }
    private async Task Lesson(Guid user, ContentVersion lesson, DateTimeOffset at)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.LessonProgress.Add(new LessonProgress { UserId = user, ContentVersionId = lesson.Id, CompletedAt = at }); await db.SaveChangesAsync();
    }
    // VI: Các bản ghi tổng hợp giả chỉ nằm trong fixture biệt lập; không ghi lịch sử thử nghiệm vào demo thật. EN: Synthetic history is confined to the isolated fixture, never the persistent demo runtime.
    private async Task<Guid> Attempt(Guid user, ContentVersion bank, DateTimeOffset at, string mode = "practice", AttemptStatus status = AttemptStatus.Graded, int total = 1)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var question = bank.Groups.Single().Questions.Single();
        var attempt = new Attempt { UserId = user, ContentVersionId = bank.Id, Mode = mode, StartedAt = at.AddMinutes(-1), DeadlineAt = at.AddMinutes(10), Status = status,
            Items = [new AttemptItem { QuestionId = question.Id, Prompt = question.Prompt, GradingKey = new GradingKey { CorrectOptionId = question.Options.Single(x => x.IsCorrect).Id, Skill = "grammar", Part = 5 } }] };
        attempt.Result = new AttemptResult { AttemptId = attempt.Id, CorrectCount = 0, TotalCount = total, BlankCount = total, FinalizedAt = at }; db.Attempts.Add(attempt); await db.SaveChangesAsync(); return attempt.Id;
    }
}
