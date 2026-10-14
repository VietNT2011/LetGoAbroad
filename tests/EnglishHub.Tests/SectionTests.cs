using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Infrastructure.Assessment;
public sealed class SectionTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Full_fixture_closes_listening_and_opens_reading_at_the_server_boundary()
    {
        using var client=await app.Login(); var catalog=await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var exam=catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString()=="full-mock-fixture").GetProperty("id").GetGuid();
        var response=await client.PostAsJsonAsync("/api/v1/attempts/",new StartAttempt(exam,Guid.NewGuid(),"fixture")); response.EnsureSuccessStatusCode();
        var attempt=(await response.Content.ReadFromJsonAsync<AttemptView>())!; Assert.Equal(200,attempt.Items.Length); Assert.Equal("listening",attempt.Section);
        Assert.Equal(new[] { 6,25,39,30,30,16,54 },Enumerable.Range(1,7).Select(p => attempt.Items.Count(q => q.Part==p)));
        var reading=attempt.Items.First(x => x.Part>=5); var listening=attempt.Items.First(x => x.Part<=4);
        SaveAnswers Choice(ActiveItem item) => new(Guid.NewGuid(),[new(item.Id,item.Options[0].Id,item.Etag)]);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers",Choice(reading))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit",new SaveAnswers(Guid.NewGuid(),[]))).StatusCode);
        app.Clock.Advance(attempt.ListeningDeadlineAt!.Value-app.Clock.UtcNow);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers",Choice(listening))).StatusCode);
        (await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers",Choice(reading))).EnsureSuccessStatusCode();
        Assert.Equal("reading",(await client.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}"))!.Section);
    }
    [Fact]
    public async Task Practice_preserves_whole_reading_groups_and_reports_actual_question_count()
    {
        using var client=await app.Login(); var catalog=await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var exam=catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString()=="mini-reading").GetProperty("id").GetGuid();
        var input=new StartAttempt(exam,Guid.NewGuid(),"practice",7,2);
        var preview=await client.PostAsJsonAsync("/api/v1/attempts/preview",input); preview.EnsureSuccessStatusCode(); Assert.Equal(4,(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actualQuestionCount").GetInt32());
        var response=await client.PostAsJsonAsync("/api/v1/attempts/",input); response.EnsureSuccessStatusCode();
        var attempt=(await response.Content.ReadFromJsonAsync<AttemptView>())!; Assert.Equal(4,attempt.Items.Length); Assert.Single(attempt.Items.Select(x => x.Stimulus).Distinct());
    }
}
