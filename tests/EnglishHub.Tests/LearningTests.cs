using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class LearningTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Enrollment_and_daily_generation_are_idempotent_and_completion_requires_quiz_evidence()
    {
        using var client = await app.Login();
        (await client.PutAsJsonAsync("/api/v1/profile", new { displayName = "Minh Anh", timeZoneId = "Asia/Ho_Chi_Minh", targetScore = 650, minutesPerDay = 30, studyDays = new[] { 0,1,2,3,4,5,6 } })).EnsureSuccessStatusCode();
        var enrollments = await Task.WhenAll(client.PostAsJsonAsync("/api/v1/learning/enrollment", new { startFromBasics = true }), client.PostAsJsonAsync("/api/v1/learning/enrollment", new { startFromBasics = true }));
        Assert.All(enrollments, x => x.EnsureSuccessStatusCode());
        Assert.Equal(await enrollments[0].Content.ReadAsStringAsync(), await enrollments[1].Content.ReadAsStringAsync());
        var first = await client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        var second = await client.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard");
        Assert.Equal(first.GetProperty("tasks").GetRawText(), second.GetProperty("tasks").GetRawText());
        var tasks = first.GetProperty("tasks").EnumerateArray().ToArray();
        Assert.InRange(tasks.Length, 1, 3); Assert.True(tasks.Sum(x => x.GetProperty("estimatedMinutes").GetInt32()) <= 30);
        var lessonId = tasks.First(x => x.GetProperty("kind").GetString() == "lesson").GetProperty("contentVersionId").GetGuid();
        var lesson = await client.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{lessonId}");
        var questionId = lesson.GetProperty("quiz")[0].GetProperty("id").GetGuid();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var options = await db.QuestionOptions.AsNoTracking().Where(x => x.QuestionId == questionId).ToListAsync();
        var wrong = new { clientRequestId = Guid.NewGuid(), answers = new[] { new { questionId, selectedOptionId = options.First(x => !x.IsCorrect).Id } } };
        var checkedWrong = await client.PostAsJsonAsync($"/api/v1/lessons/{lessonId}/complete", wrong);
        Assert.False((await checkedWrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completed").GetBoolean());
        var correct = new { clientRequestId = Guid.NewGuid(), answers = new[] { new { questionId, selectedOptionId = options.Single(x => x.IsCorrect).Id } } };
        var checkedCorrect = await client.PostAsJsonAsync($"/api/v1/lessons/{lessonId}/complete", correct); checkedCorrect.EnsureSuccessStatusCode();
        Assert.True((await checkedCorrect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completed").GetBoolean());
        (await client.PostAsJsonAsync($"/api/v1/lessons/{lessonId}/complete", correct)).EnsureSuccessStatusCode();
        await Task.WhenAll(client.GetAsync("/api/v1/learning/dashboard"), client.GetAsync("/api/v1/learning/dashboard"));
        Assert.Equal(1, await db.LessonProgress.CountAsync(x => x.ContentVersionId == lessonId));
        var taskId = tasks.Single(x => x.GetProperty("contentVersionId").GetGuid() == lessonId).GetProperty("id").GetGuid();
        Assert.Equal(1, await db.TaskCompletions.CountAsync(x => x.DailyTaskId == taskId));
        Assert.Equal(1, await db.OutboxEvents.CountAsync(x => x.AggregateId == taskId && x.Type == "learning.task_completed"));
    }

    [Fact]
    public async Task Placement_cannot_be_fabricated_from_an_unknown_attempt()
    {
        using var client = await app.Login("learner3@englishhub.local");
        var response = await client.PostAsJsonAsync("/api/v1/learning/enrollment", new { startFromBasics = false, placementAttemptId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(20)] [InlineData(30)] [InlineData(45)]
    public void Planner_respects_budget_and_never_duplicates_candidates(int minutes)
    {
        var lessons = Enumerable.Range(0,6).Select(i => new PlanCandidate(Guid.NewGuid(),10,"lesson",i)).ToArray();
        var practice = new PlanCandidate(Guid.NewGuid(),15,"practice",0);
        var plan = Planner.Select(lessons,[practice],minutes);
        Assert.InRange(plan.Count,1,3); Assert.True(plan.Sum(x => x.Minutes) <= minutes);
        Assert.Equal(plan.Count,plan.Select(x => x.ContentVersionId).Distinct().Count());
    }
}
