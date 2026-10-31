using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Assessment;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class FillBlankTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("  I\tam\u00a0 READY \n", "I am ready", false, true)]
    [InlineData("DON’T", "don't", false, true)]
    [InlineData("cafe\u0301", "café", false, true)]
    [InlineData("cafe", "café", false, false)]
    [InlineData("ready!", "ready", false, false)]
    [InlineData("ready!?", "ready.", true, true)]
    [InlineData("re.ady", "ready", true, false)]
    [InlineData("I ready am", "I am ready", true, false)]
    [InlineData("redy", "ready", true, false)]
    [InlineData("  ?!  ", "ready", true, false)]
    public void Policy_v1_matches_only_explicit_normalized_variants(string answer, string variant, bool punctuation, bool expected)
        => Assert.Equal(expected, AnswerNormalization.Matches(answer, [variant], 1, punctuation));

    [Fact]
    public void Unknown_policy_is_rejected_instead_of_silently_using_current_rules()
        => Assert.Equal("unsupported_normalization_policy", Assert.Throws<DomainError>(() => AnswerNormalization.Normalize("ready", 99, false)).Code);

    [Fact]
    public async Task Typed_answers_are_private_atomic_replayable_and_graded_with_correct_blank_counts()
    {
        using var client = await app.Login();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var versionId = catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-fill-blank").GetProperty("id").GetGuid();
        var attempt = await Start(client, versionId);
        Assert.All(attempt.Items, item => { Assert.Equal("fill_blank", item.Type); Assert.Empty(item.Options); });
        var active = await client.GetStringAsync($"/api/v1/attempts/{attempt.Id}");
        Assert.DoesNotContain("acceptedAnswerVariants", active); Assert.DoesNotContain("explanation", active); Assert.DoesNotContain("gradingKey", active);
        var items = attempt.Items;
        var invalid = new SaveAnswers(Guid.NewGuid(), [new(items[0].Id, null, items[0].Etag, "send"), new(items[1].Id, Guid.NewGuid(), items[1].Etag, "I am ready")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers", invalid)).StatusCode);
        var unchanged = await client.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}");
        Assert.All(unchanged!.Items, item => { Assert.Null(item.AnswerText); Assert.Equal(items.Single(x => x.Id == item.Id).Etag, item.Etag); });
        var texts = new[] { " SEND ", " I\tAM ready. ", "don’t", " \t", "They are not availble" };
        var save = new SaveAnswers(Guid.NewGuid(), items.Select((q, i) => new AnswerChange(q.Id, null, q.Etag, texts[i])).ToArray());
        var saved = await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers", save); saved.EnsureSuccessStatusCode();
        var acknowledgement = await saved.Content.ReadAsStringAsync();
        var replay = await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers", save);
        Assert.Equal(acknowledgement, await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/attempts/{attempt.Id}/answers", save with { Answers = [save.Answers[0] with { AnswerText = "different" }] })).StatusCode);
        var refreshed = await client.GetFromJsonAsync<AttemptView>($"/api/v1/attempts/{attempt.Id}");
        Assert.Equal(texts, refreshed!.Items.Select(x => x.AnswerText));
        var submit = await client.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), [])); submit.EnsureSuccessStatusCode();
        var result = await submit.Content.ReadFromJsonAsync<AttemptResult>();
        Assert.Equal(3, result!.CorrectCount); Assert.Equal(5, result.TotalCount); Assert.Equal(1, result.BlankCount); Assert.Equal(3, result.ReadingRaw); Assert.Equal(0, result.ListeningRaw);
        var review = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attempts/{attempt.Id}/result");
        Assert.Equal("send", review.GetProperty("items")[0].GetProperty("acceptedAnswerVariants")[0].GetString());
        using var other = await app.Login("learner2@englishhub.local");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/attempts/{attempt.Id}/result")).StatusCode);
    }

    [Fact]
    public async Task Answer_shapes_and_length_are_checked_and_expiry_never_accepts_new_text()
    {
        using var client = await app.Login();
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog");
        var blank = await Start(client, catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-fill-blank").GetProperty("id").GetGuid());
        var item = blank.Items[0];
        var tooLong = new SaveAnswers(Guid.NewGuid(), [new(item.Id, null, item.Etag, new string('a', 2001))]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/v1/attempts/{blank.Id}/answers", tooLong)).StatusCode);
        var choice = await Start(client, catalog.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mini-grammar").GetProperty("id").GetGuid());
        var wrongShape = new SaveAnswers(Guid.NewGuid(), [new(choice.Items[0].Id, choice.Items[0].Options[0].Id, choice.Items[0].Etag, "goes")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/v1/attempts/{choice.Id}/answers", wrongShape)).StatusCode);
        app.Clock.Advance(blank.DeadlineAt - app.Clock.UtcNow);
        var late = new SaveAnswers(Guid.NewGuid(), [new(item.Id, null, item.Etag, "send")]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/attempts/{blank.Id}/answers", late)).StatusCode);
        var submitted = await client.PostAsJsonAsync($"/api/v1/attempts/{blank.Id}/submit", late); submitted.EnsureSuccessStatusCode();
        var result = await submitted.Content.ReadFromJsonAsync<AttemptResult>();
        Assert.Equal(0, result!.CorrectCount); Assert.Equal(5, result.BlankCount); Assert.Equal("deadline", result.Reason);
    }

    [Fact]
    public async Task Cms_rejects_ambiguous_variants_unsupported_policy_and_mixed_question_shapes()
    {
        using var admin = await app.Login("admin@englishhub.local");
        var q = Blank("Write ready.", ["ready"]);
        var cases = new[]
        {
            (q with { AcceptedAnswerVariants = ["Ready", " ready "] }, "invalid_answer_variants"),
            (q with { AcceptedAnswerVariants = ["?!"], IgnoreTerminalPunctuation = true }, "invalid_answer_variants"),
            (q with { AcceptedAnswerVariants = [] }, "invalid_answer_variants"),
            (q with { AcceptedAnswerVariants = [null!] }, "invalid_answer_policy"),
            (q with { NormalizationPolicyVersion = 2 }, "invalid_answer_policy"),
            (q with { Type = (QuestionType)99 }, "invalid_answer_policy"),
            (q with { Options = [new("ready", true)] }, "invalid_fill_blank")
        };
        foreach (var (question, expectedCode) in cases)
        {
            var response = await admin.PostAsJsonAsync("/api/v1/admin/imports/dry-run", new[] { Input(question) }); response.EnsureSuccessStatusCode();
            var row = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows")[0];
            Assert.False(row.GetProperty("valid").GetBoolean()); Assert.Equal(expectedCode, row.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Assessment_pins_variants_and_policy_when_a_new_content_version_is_published()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await app.Login();
        var firstInput = Input(Blank("Type the authored greeting.", ["Hello"]));
        var first = await Publish(admin, firstInput);
        var oldAttempt = await Start(learner, first);
        var old = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{first}");
        var nextContent = firstInput.Content with { Groups = [new(5, "Original greeting", [Blank("Type the updated greeting.", ["Good morning"])])] };
        var created = await admin.PostAsJsonAsync($"/api/v1/admin/content/{old.GetProperty("rootId").GetGuid()}/versions", nextContent); created.EnsureSuccessStatusCode();
        var next = await created.Content.ReadFromJsonAsync<JsonElement>(); await PublishDraft(admin, next);
        var newAttempt = await Start(learner, next.GetProperty("id").GetGuid());
        foreach (var a in new[] { oldAttempt, newAttempt })
        {
            var item = a.Items.Single();
            var response = await learner.PostAsJsonAsync($"/api/v1/attempts/{a.Id}/submit", new SaveAnswers(Guid.NewGuid(), [new(item.Id, null, item.Etag, "HELLO")])); response.EnsureSuccessStatusCode();
            Assert.Equal(a.Id == oldAttempt.Id ? 1 : 0, (await response.Content.ReadFromJsonAsync<AttemptResult>())!.CorrectCount);
        }
    }

    [Fact]
    public async Task Lesson_quiz_uses_normalization_and_writes_completion_only_once_after_a_correct_answer()
    {
        using var admin = await app.Login("admin@englishhub.local"); using var learner = await app.Login();
        var input = Input(Blank("Expand: I'm ready.", ["I am ready"]) with { IgnoreTerminalPunctuation = true }) with { Kind = ContentKind.Lesson };
        var version = await Publish(admin, input);
        var lesson = await learner.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{version}");
        var q = lesson.GetProperty("quiz")[0]; Assert.Equal("fill_blank", q.GetProperty("type").GetString());
        Assert.False(q.TryGetProperty("acceptedAnswerVariants", out _));
        var questionId = q.GetProperty("id").GetGuid();
        var wrong = await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", new CompleteLessonInput(Guid.NewGuid(), [new(questionId, null, "I ready")])); wrong.EnsureSuccessStatusCode();
        Assert.False((await wrong.Content.ReadFromJsonAsync<LessonCheck>())!.Completed);
        var command = new CompleteLessonInput(Guid.NewGuid(), [new(questionId, null, " I  am\tREADY! ")]);
        var correct = await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", command); correct.EnsureSuccessStatusCode();
        Assert.True((await correct.Content.ReadFromJsonAsync<LessonCheck>())!.Completed);
        (await learner.PostAsJsonAsync($"/api/v1/lessons/{version}/complete", command)).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        Assert.Equal(1, await db.LessonProgress.CountAsync(x => x.ContentVersionId == version));
    }

    private static QuestionInput Blank(string prompt, string[] variants) => new(prompt, "grammar", "Lời giải tự biên soạn cho ví dụ này.", [], QuestionType.FillBlank, variants);
    private static CreateContentInput Input(QuestionInput q) => new($"fill-{Guid.NewGuid():N}", ContentKind.Assessment, new("Original typed exercise", "Original demo", "An original lesson body containing enough context for a meaningful quiz.", "grammar", 5, 600, false, "EnglishHub original · CC0", [new(5, "Original context", [q])]));
    private static async Task<AttemptView> Start(HttpClient client, Guid version)
    {
        var response = await client.PostAsJsonAsync("/api/v1/attempts/", new StartAttempt(version, Guid.NewGuid())); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttemptView>())!;
    }
    private static async Task<Guid> Publish(HttpClient admin, CreateContentInput input)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/content", input); response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>(); await PublishDraft(admin, draft); return draft.GetProperty("id").GetGuid();
    }
    private static async Task PublishDraft(HttpClient admin, JsonElement draft)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/versions/{draft.GetProperty("id").GetGuid()}/publish");
        request.Headers.TryAddWithoutValidation("If-Match", draft.GetProperty("etag").GetString());
        (await admin.SendAsync(request)).EnsureSuccessStatusCode();
    }
}
