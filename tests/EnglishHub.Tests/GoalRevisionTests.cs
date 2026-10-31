using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Đổi mục tiêu không sửa lịch hôm nay hay bằng chứng cũ. EN: Goal changes must preserve existing daily assignments and historical evidence.</summary>
public sealed class GoalRevisionTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("2026-11-10T16:59:59Z", "Asia/Ho_Chi_Minh", "2026-11-10T17:00:00Z")]
    [InlineData("2026-11-10T17:00:00Z", "Asia/Ho_Chi_Minh", "2026-11-11T17:00:00Z")]
    [InlineData("2026-11-10T22:03:00Z", "UTC", "2026-11-11T00:00:00Z")]
    public void Effective_boundary_is_next_midnight_in_the_request_timezone(string now, string zone, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected), GoalPlanService.NextMidnight(DateTimeOffset.Parse(now), zone));

    [Fact]
    public async Task Goal_changes_apply_on_the_next_day_and_keep_today_task_ids_and_revision_pins()
    {
        using var learner = await NewLearner(); await Enroll(learner);
        var before = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        var tasksBefore = before.GetProperty("tasks").GetRawText(); var oldRevision = before.GetProperty("tasks")[0].GetProperty("goalRevisionId").GetGuid();
        await Profile(learner, 800, 20);
        var desired = await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"); Assert.Equal(800, desired.GetProperty("targetScore").GetInt32());
        var sameDay = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        Assert.Equal(tasksBefore, sameDay.GetProperty("tasks").GetRawText()); Assert.Equal(650, sameDay.GetProperty("enrollment").GetProperty("targetScore").GetInt32()); Assert.Equal(30, sameDay.GetProperty("enrollment").GetProperty("minutesPerDay").GetInt32());
        var history = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); var scheduled = history.GetProperty("revisions").EnumerateArray().Single(x => x.GetProperty("status").GetString() == "scheduled");
        var effective = scheduled.GetProperty("effectiveAt").GetDateTimeOffset(); Assert.True(effective > app.Clock.UtcNow);
        app.Clock.Advance(effective - app.Clock.UtcNow);
        var tomorrow = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        Assert.Equal(800, tomorrow.GetProperty("enrollment").GetProperty("targetScore").GetInt32()); Assert.Equal(20, tomorrow.GetProperty("enrollment").GetProperty("minutesPerDay").GetInt32());
        var newRevision = scheduled.GetProperty("id").GetGuid(); Assert.All(tomorrow.GetProperty("tasks").EnumerateArray(), task => Assert.Equal(newRevision, task.GetProperty("goalRevisionId").GetGuid()));
        Assert.True(tomorrow.GetProperty("tasks").EnumerateArray().Sum(x => x.GetProperty("estimatedMinutes").GetInt32()) <= 20);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var old = await db.PlanGoalRevisions.SingleAsync(x => x.Id == oldRevision); Assert.Equal(650, old.TargetScore); Assert.Equal(30, old.MinutesPerDay);
    }

    [Fact]
    public async Task Duplicate_profile_writes_do_not_duplicate_revisions_and_later_same_day_choice_supersedes_pending_choice()
    {
        using var learner = await NewLearner(); await Enroll(learner); await learner.GetAsync("/api/v1/learning/dashboard");
        await Task.WhenAll(Profile(learner, 450, 45), Profile(learner, 450, 45));
        var first = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); Assert.Equal(2, first.GetProperty("revisions").GetArrayLength());
        await Profile(learner, 650, 30);
        var next = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); Assert.Equal(3, next.GetProperty("revisions").GetArrayLength());
        Assert.Equal("superseded", next.GetProperty("revisions")[1].GetProperty("status").GetString());
        Assert.Equal("active", next.GetProperty("revisions")[2].GetProperty("status").GetString());
        var scheduled = next.GetProperty("revisions")[0]; app.Clock.Advance(scheduled.GetProperty("effectiveAt").GetDateTimeOffset() - app.Clock.UtcNow);
        var after = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(3, after.GetProperty("enrollment").GetProperty("planRevision").GetInt32()); Assert.Equal(30, after.GetProperty("enrollment").GetProperty("minutesPerDay").GetInt32());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var revision = scheduled.GetProperty("id").GetGuid(); Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == revision && x.Type == "learning.plan_revision_scheduled"));
    }

    [Fact]
    public async Task Owner_history_is_private_and_relational_fk_rejects_another_enrollments_goal_revision()
    {
        using var first = await NewLearner(); using var second = await NewLearner(); await Enroll(first); await Enroll(second);
        var a = await first.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); var b = await second.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        var aRevision = a.GetProperty("tasks")[0].GetProperty("goalRevisionId").GetGuid(); var bRevision = b.GetProperty("tasks")[0].GetProperty("goalRevisionId").GetGuid(); Assert.NotEqual(aRevision, bRevision);
        var others = await second.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); Assert.DoesNotContain(others.GetProperty("revisions").EnumerateArray(), x => x.GetProperty("id").GetGuid() == aRevision);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/plan-revisions")).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var taskId = a.GetProperty("tasks")[0].GetProperty("id").GetGuid(); var source = await db.DailyTasks.SingleAsync(x => x.Id == taskId);
        db.DailyTasks.Add(new DailyTask { UserId = source.UserId, EnrollmentId = source.EnrollmentId, ContentVersionId = source.ContentVersionId, LocalDate = source.LocalDate, SlotKey = 2, Status = "missed", GoalRevisionId = bRevision, CreatedAt = app.Clock.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Legacy_baseline_records_observed_time_and_does_not_invent_or_backfill_old_task_revisions()
    {
        using var learner = await NewLearner(); var me = await learner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"); var userId = me.GetProperty("id").GetGuid();
        var catalog = await learner.GetFromJsonAsync<JsonElement>("/api/v1/catalog"); var lesson = catalog.EnumerateArray().First(x => x.GetProperty("kind").GetString() == "lesson").GetProperty("id").GetGuid(); Guid taskId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var enrollment = new Enrollment { UserId = userId, CreatedAt = app.Clock.UtcNow.AddDays(-10), TargetScore = 650, MinutesPerDay = 30, PlanRevision = 7 };
            var task = new DailyTask { EnrollmentId = enrollment.Id, UserId = userId, ContentVersionId = lesson, CreatedAt = app.Clock.UtcNow, LocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(app.Clock.UtcNow, "Asia/Ho_Chi_Minh").DateTime), EstimatedMinutes = 10 }; taskId = task.Id;
            db.Enrollments.Add(enrollment); db.DailyTasks.Add(task); await db.SaveChangesAsync();
        }
        var history = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); var baseline = history.GetProperty("revisions")[0];
        Assert.Equal(7, baseline.GetProperty("number").GetInt32()); Assert.Equal(app.Clock.UtcNow, baseline.GetProperty("recordedAt").GetDateTimeOffset()); Assert.Equal("observed_baseline", baseline.GetProperty("reason").GetString());
        await Profile(learner, 800, 20);
        var today = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard"); Assert.Equal(taskId, today.GetProperty("tasks")[0].GetProperty("id").GetGuid()); Assert.Equal(JsonValueKind.Null, today.GetProperty("tasks")[0].GetProperty("goalRevisionId").ValueKind);
        var updated = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); Assert.Equal(new[] { 8, 7 }, updated.GetProperty("revisions").EnumerateArray().Select(x => x.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task Preferences_before_enrollment_become_the_initial_active_goal_without_a_future_delay()
    {
        using var learner = await NewLearner(); await Profile(learner, 800, 20);
        Assert.Empty((await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions")).GetProperty("revisions").EnumerateArray());
        await Enroll(learner);
        var history = await learner.GetFromJsonAsync<JsonElement>("/api/v1/learning/plan-revisions"); var initial = history.GetProperty("revisions")[0];
        Assert.Equal(1, initial.GetProperty("number").GetInt32()); Assert.Equal("active", initial.GetProperty("status").GetString()); Assert.Equal("enrollment", initial.GetProperty("reason").GetString()); Assert.Equal(800, initial.GetProperty("targetScore").GetInt32()); Assert.Equal(20, initial.GetProperty("minutesPerDay").GetInt32());
    }

    private static async Task Enroll(HttpClient learner) => (await learner.PostAsJsonAsync("/api/v1/learning/enrollment", new EnrollInput(true))).EnsureSuccessStatusCode();
    private static async Task Profile(HttpClient learner, int target, int minutes) => (await learner.PutAsJsonAsync("/api/v1/profile", new { displayName = "Goal test", timeZoneId = "Asia/Ho_Chi_Minh", targetScore = target, minutesPerDay = minutes, studyDays = new[] { 0, 1, 2, 3, 4, 5, 6 } })).EnsureSuccessStatusCode();
    private async Task<HttpClient> NewLearner()
    {
        var email = $"goal-{Guid.NewGuid():N}@example.com"; using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Goal test", StudyDays = "0,1,2,3,4,5,6" }; Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return await app.Login(email);
    }
}
