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

/// <summary>VI: Ôn sai có nguồn riêng, đủ ngữ cảnh và không tô đẹp điểm gốc. EN: Verify contextual recall, real historical sources and isolated assessment evidence.</summary>
public sealed class MistakeNotebookTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Grading_records_each_wrong_or_blank_once_and_reading_does_not_disclose_keys()
    {
        using var owner = await Learner(); var bank = await Bank(); var attempt = await Start(owner, bank);
        await Finish(owner, attempt, false); var first = await Page(owner); Assert.Equal(3, first.Length);
        Assert.All(first, x => { Assert.Equal(1, x.GetProperty("wrongCount").GetInt32()); Assert.Equal("learning", x.GetProperty("status").GetString()); Assert.Equal(3, x.GetProperty("groupQuestionCount").GetInt32()); });
        (await owner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit", new SaveAnswers(Guid.NewGuid(), []))).EnsureSuccessStatusCode();
        var imported = await Import(owner, new(Guid.NewGuid())); Assert.Equal(0, imported.AttemptsProcessed);
        var json = await owner.GetStringAsync("/api/v1/me/mistakes"); foreach(var secret in new[] {"correctOptionId", "acceptedAnswerVariants", "answerText", "explanation", "selectedOptionId"}) Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Null((await db.Attempts.SingleAsync(x => x.Id == attempt.Id)).FirstDisclosedAt); Assert.Equal(1, await db.MistakeProcessedAttempts.CountAsync(x => x.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task Review_clones_complete_frozen_group_and_only_updates_selected_target_without_inflating_scores()
    {
        using var owner = await Learner(); var bank = await Bank(); var original = await Start(owner, bank); await Finish(owner, original, false); var target = (await Page(owner))[0];
        var stats = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("statistics").GetRawText(); var skills = await owner.GetStringAsync("/api/v1/learning/skill-evidence"); var activity = await owner.GetStringAsync("/api/v1/learning/activity?days=30");
        var command = new MistakeCommand(Guid.NewGuid()); var review = await ReviewStart(owner, target, command);
        Assert.Equal("mistake_review", review.Mode); Assert.Equal(3, review.Items.Length); Assert.Equal(original.Items.Select(x => x.Prompt), review.Items.Select(x => x.Prompt)); Assert.All(review.Items, x => {Assert.Null(x.SelectedOptionId); Assert.Null(x.AnswerText);});
        await Finish(owner, review, true);
        var replay = await ReviewStart(owner, target, command); Assert.Equal(review.Id, replay.Id);
        var after = await Page(owner); Assert.Equal(1, after.Single(x => x.GetProperty("id").GetGuid() == target.GetProperty("id").GetGuid()).GetProperty("correctReviewDays").GetInt32()); Assert.Equal(0, after.Count(x => x.GetProperty("status").GetString() == "mastered")); Assert.Equal(2, after.Count(x => x.GetProperty("correctReviewDays").GetInt32() == 0));
        Assert.Equal(stats, (await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("statistics").GetRawText()); Assert.Equal(skills, await owner.GetStringAsync("/api/v1/learning/skill-evidence")); Assert.Equal(activity, await owner.GetStringAsync("/api/v1/learning/activity?days=30"));
    }

    [Fact]
    public async Task Two_correct_review_days_master_the_question_and_a_later_original_error_reopens_it()
    {
        using var owner = await Learner(); var bank = await Bank(); await Finish(owner, await Start(owner, bank), false); var target = (await Page(owner))[0];
        await Finish(owner, await ReviewStart(owner, target, new(Guid.NewGuid())), true);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(ReviewPath(target), new MistakeCommand(Guid.NewGuid()))).StatusCode);
        using (var scope = app.Services.CreateScope()) {var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var id = await User(owner); (await db.Users.SingleAsync(x => x.Id == id)).TimeZoneId = "Pacific/Kiritimati"; await db.SaveChangesAsync();}
        app.Clock.Advance(TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(1))); Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(ReviewPath(target), new MistakeCommand(Guid.NewGuid()))).StatusCode);
        app.Clock.Advance(TimeSpan.FromTicks(1)); await Finish(owner, await ReviewStart(owner, target, new(Guid.NewGuid())), true);
        var mastered = Assert.Single(await Page(owner, "?status=mastered")); Assert.Equal(2, mastered.GetProperty("correctReviewDays").GetInt32()); Assert.Equal(JsonValueKind.Null, mastered.GetProperty("nextReviewAt").ValueKind); Assert.Equal("UTC", mastered.GetProperty("cycleTimeZoneId").GetString());
        await Finish(owner, await Start(owner, bank), false); var reopened = (await Page(owner)).Single(x => x.GetProperty("id").GetGuid() == mastered.GetProperty("id").GetGuid()); Assert.Equal(0, reopened.GetProperty("correctReviewDays").GetInt32()); Assert.Equal(2, reopened.GetProperty("wrongCount").GetInt32()); Assert.Equal("Pacific/Kiritimati", reopened.GetProperty("cycleTimeZoneId").GetString());
    }

    [Fact]
    public async Task Wrong_review_counts_once_and_retries_after_exact_ten_minutes_with_context_after_archival()
    {
        using var owner = await Learner(); var bank = await Bank(); await Finish(owner, await Start(owner, bank), false); var target = (await Page(owner))[0];
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();(await db.ContentVersions.SingleAsync(x=>x.Id==bank)).Status=PublicationStatus.Archived;await db.SaveChangesAsync();}
        var review = await ReviewStart(owner, target, new(Guid.NewGuid())); await Finish(owner, review, false);
        var changed = (await Page(owner)).Single(x => x.GetProperty("id").GetGuid() == target.GetProperty("id").GetGuid()); Assert.Equal(2, changed.GetProperty("wrongCount").GetInt32()); Assert.Equal(app.Clock.UtcNow.AddMinutes(10), changed.GetProperty("nextReviewAt").GetDateTimeOffset());
        app.Clock.Advance(TimeSpan.FromMinutes(10).Subtract(TimeSpan.FromTicks(1))); Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(ReviewPath(target), new MistakeCommand(Guid.NewGuid()))).StatusCode);
        app.Clock.Advance(TimeSpan.FromTicks(1)); Assert.Equal(3, (await ReviewStart(owner, target, new(Guid.NewGuid()))).Items.Length);
    }

    [Fact]
    public async Task Competing_starts_make_one_attempt_and_same_command_replays_after_completion()
    {
        using var owner = await Learner(); var bank = await Bank(); await Finish(owner, await Start(owner, bank), false); var target = (await Page(owner))[0]; var input = new MistakeCommand(Guid.NewGuid());
        var responses = await Task.WhenAll(owner.PostAsJsonAsync(ReviewPath(target), input), owner.PostAsJsonAsync(ReviewPath(target), input)); Assert.All(responses, x => x.EnsureSuccessStatusCode()); var a=(await responses[0].Content.ReadFromJsonAsync<AttemptView>())!;var b=(await responses[1].Content.ReadFromJsonAsync<AttemptView>())!;Assert.Equal(a.Id,b.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(ReviewPath(target),new MistakeCommand(Guid.NewGuid()))).StatusCode);
        var active=(await Page(owner)).Single(x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid()); Assert.Equal(a.Id,active.GetProperty("activeAttemptId").GetGuid());
        await Finish(owner,a,true);Assert.Equal(a.Id,(await ReviewStart(owner,target,input)).Id);
        var other=(await Page(owner)).First(x=>x.GetProperty("id").GetGuid()!=target.GetProperty("id").GetGuid());Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(ReviewPath(other),input)).StatusCode);
    }

    [Fact]
    public async Task Historical_import_is_bounded_payload_bound_and_idempotent_under_concurrent_retries()
    {
        using var owner=await Learner();var bank=await Bank();await Legacy(owner,bank,53,app.Clock.UtcNow.AddDays(-10));var input=new MistakeCommand(Guid.NewGuid());
        var responses=await Task.WhenAll(owner.PostAsJsonAsync("/api/v1/me/mistakes/import-history",input),owner.PostAsJsonAsync("/api/v1/me/mistakes/import-history",input));Assert.All(responses,x=>x.EnsureSuccessStatusCode());Assert.Equal(await responses[0].Content.ReadAsStringAsync(),await responses[1].Content.ReadAsStringAsync());
        var first=(await responses[0].Content.ReadFromJsonAsync<MistakeImportAck>())!;Assert.Equal(50,first.AttemptsProcessed);Assert.True(first.HasMore);var second=await Import(owner,new(Guid.NewGuid()));Assert.Equal(3,second.AttemptsProcessed);Assert.False(second.HasMore);Assert.Equal(first,await Import(owner,input));Assert.Equal(0,(await Import(owner,new(Guid.NewGuid()))).AttemptsProcessed);
        var page=await Page(owner);Assert.Equal(3,page.Length);Assert.All(page,x=>Assert.Equal(53,x.GetProperty("wrongCount").GetInt32()));
    }

    [Fact]
    public async Task Older_imported_wrong_answers_do_not_reset_a_newer_successful_cycle_or_disclose_answers()
    {
        using var owner=await Learner();var bank=await Bank();await Legacy(owner,bank,1,app.Clock.UtcNow.AddDays(-5));await Finish(owner,await Start(owner,bank),false);var target=(await Page(owner))[0];await Finish(owner,await ReviewStart(owner,target,new(Guid.NewGuid())),true);
        await Import(owner,new(Guid.NewGuid()));var current=(await Page(owner)).Single(x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid());Assert.Equal(2,current.GetProperty("wrongCount").GetInt32());Assert.Equal(1,current.GetProperty("correctReviewDays").GetInt32());Assert.Equal(app.Clock.UtcNow.AddDays(1),current.GetProperty("nextReviewAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Correct_typed_answers_use_pinned_normalization_and_unrelated_neighbor_results_do_not_create_mistakes()
    {
        using var owner=await Learner();var bank=await Bank(typed:true);await Finish(owner,await Start(owner,bank),true);Assert.Empty(await Page(owner));
        var original=await Start(owner,bank);await Finish(owner,original,false);var target=(await Page(owner))[0];
        await Finish(owner,await ReviewStart(owner,target,new(Guid.NewGuid())),true,target.GetProperty("questionId").GetGuid());
        var after=await Page(owner);Assert.Equal(3,after.Length);Assert.All(after,x=>Assert.Equal(1,x.GetProperty("wrongCount").GetInt32()));Assert.Equal(1,after.Single(x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid()).GetProperty("correctReviewDays").GetInt32());
    }

    [Fact]
    public async Task Owner_admin_csrf_validation_and_read_only_boundaries_apply_to_notebook_and_starts()
    {
        using var owner=await Learner();using var other=await app.Login();using var admin=await app.Login("admin@englishhub.local");var bank=await Bank();await Finish(owner,await Start(owner,bank),false);var target=(await Page(owner))[0];
        foreach(var client in new[]{other,admin}){Assert.DoesNotContain(await Page(client),x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid());Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync(ReviewPath(target),new MistakeCommand(Guid.NewGuid()))).StatusCode);}
        foreach(var query in new[]{"?status=nope","?offset=-1","?part=8","?skill="+new string('x',61)})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/me/mistakes"+query)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.PostAsJsonAsync(ReviewPath(target),new MistakeCommand(Guid.Empty))).StatusCode);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var before=await db.CommandReceipts.CountAsync();await Page(owner);Assert.Equal(before,await db.CommandReceipts.CountAsync());}
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(ReviewPath(target),new MistakeCommand(Guid.NewGuid()))).StatusCode);
        using var anonymous=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/me/mistakes")).StatusCode);
    }

    [Fact]
    public async Task Technical_fixtures_and_unbound_review_mode_do_not_create_notebook_evidence()
    {
        using var owner=await Learner();var bank=await Bank(fixture:true);await Finish(owner,await Start(owner,bank,"fixture"),false);var normal=await Bank();await Finish(owner,await Start(owner,normal,"mistake_review"),false);Assert.Empty(await Page(owner));Assert.Equal(0,(await Import(owner,new(Guid.NewGuid()))).AttemptsProcessed);
    }

    private static string ReviewPath(JsonElement item)=>$"/api/v1/me/mistakes/{item.GetProperty("id").GetGuid()}/review";
    [Fact]
    public async Task Partial_legacy_context_is_rejected_without_creating_an_attempt_or_receipt()
    {
        using var owner=await Learner();var bank=await Bank();await Legacy(owner,bank,1,app.Clock.UtcNow.AddDays(-1));
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var user=await User(owner);var items=await db.AttemptItems.Include(x=>x.GradingKey).Where(x=>x.Attempt.UserId==user).OrderBy(x=>x.Order).ToArrayAsync();foreach(var item in items.Skip(1)){db.Remove(item.GradingKey);db.Remove(item);}await db.SaveChangesAsync();}
        await Import(owner,new(Guid.NewGuid()));var target=Assert.Single(await Page(owner));
        using var verification=app.Services.CreateScope();var data=verification.ServiceProvider.GetRequiredService<HubDbContext>();var before=await data.Attempts.CountAsync();var receipts=await data.CommandReceipts.CountAsync();Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(ReviewPath(target),new MistakeCommand(Guid.NewGuid()))).StatusCode);Assert.Equal(before,await data.Attempts.CountAsync());Assert.Equal(receipts,await data.CommandReceipts.CountAsync());
    }

    [Fact]
    public async Task A_25_hour_local_day_cannot_count_twice_even_after_the_24_hour_due_interval()
    {
        using var owner=await Learner();var user=await User(owner);using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();(await db.Users.SingleAsync(x=>x.Id==user)).TimeZoneId="America/New_York";await db.SaveChangesAsync();}
        app.Clock.Advance(DateTimeOffset.Parse("2026-11-01T00:30:00-04:00")-app.Clock.UtcNow);
        var bank=await Bank();await Finish(owner,await Start(owner,bank),false);var target=(await Page(owner))[0];await Finish(owner,await ReviewStart(owner,target,new(Guid.NewGuid())),true);
        app.Clock.Advance(TimeSpan.FromDays(1));await Finish(owner,await ReviewStart(owner,target,new(Guid.NewGuid())),true);
        var sameDay=(await Page(owner)).Single(x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid());Assert.Equal(1,sameDay.GetProperty("correctReviewDays").GetInt32());
        app.Clock.Advance(TimeSpan.FromDays(1));await Finish(owner,await ReviewStart(owner,target,new(Guid.NewGuid())),true);Assert.Single(await Page(owner,"?status=mastered"));
    }

    [Fact]
    public async Task Notebook_pages_and_filters_are_bounded_and_composite_target_fk_rejects_foreign_owners()
    {
        using var owner=await Learner();using var foreign=await Learner();for(var i=0;i<11;i++)await Finish(owner,await Start(owner,await Bank()),false);
        var first=await Page(owner,"?part=7&skill=reading");var second=await Page(owner,"?part=7&skill=reading&offset=30");Assert.Equal(30,first.Length);Assert.Equal(3,second.Length);Assert.Equal(33,first.Concat(second).Select(x=>x.GetProperty("id").GetGuid()).Distinct().Count());Assert.Empty(await Page(owner,"?part=5"));Assert.Empty(await Page(foreign));
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var id=first[0].GetProperty("id").GetGuid();var mistake=await db.MistakeItems.AsNoTracking().SingleAsync(x=>x.Id==id);db.MistakeReviewTargets.Add(new MistakeReviewTarget{AttemptItemId=mistake.SourceAttemptItemId,QuestionId=mistake.QuestionId,MistakeId=id,UserId=await User(foreign)});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());}
    }

    [Fact]
    public async Task Deadline_grading_ignores_late_answers_and_replays_without_counting_the_error_twice()
    {
        using var owner=await Learner();var bank=await Bank();await Finish(owner,await Start(owner,bank),false);var target=(await Page(owner))[0];var review=await ReviewStart(owner,target,new(Guid.NewGuid()));app.Clock.Advance(TimeSpan.FromMinutes(20));await Finish(owner,review,true);
        (await owner.PostAsJsonAsync($"/api/v1/attempts/{review.Id}/submit",new SaveAnswers(Guid.NewGuid(),[]))).EnsureSuccessStatusCode();var changed=(await Page(owner)).Single(x=>x.GetProperty("id").GetGuid()==target.GetProperty("id").GetGuid());Assert.Equal(2,changed.GetProperty("wrongCount").GetInt32());Assert.Equal(0,changed.GetProperty("correctReviewDays").GetInt32());
    }

    private static async Task<JsonElement[]> Page(HttpClient owner,string query="")=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/mistakes"+query)).GetProperty("items").EnumerateArray().ToArray();
    private static async Task<MistakeImportAck> Import(HttpClient owner,MistakeCommand input){var r=await owner.PostAsJsonAsync("/api/v1/me/mistakes/import-history",input);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<MistakeImportAck>())!;}
    private static async Task<AttemptView> ReviewStart(HttpClient owner,JsonElement target,MistakeCommand input){var r=await owner.PostAsJsonAsync(ReviewPath(target),input);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<AttemptView>())!;}
    private static async Task<AttemptView> Start(HttpClient owner,Guid bank,string mode="practice"){var r=await owner.PostAsJsonAsync("/api/v1/attempts/",new StartAttempt(bank,Guid.NewGuid(),mode));r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<AttemptView>())!;}
    private async Task Finish(HttpClient owner,AttemptView attempt,bool correct,Guid? onlyCorrectQuestion=null)
    {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var keys=await db.AttemptItems.Include(x=>x.GradingKey).Where(x=>x.AttemptId==attempt.Id).ToDictionaryAsync(x=>x.Id);
        var changes=attempt.Items.Select(x=>{var right=correct&&(onlyCorrectQuestion is null||keys[x.Id].QuestionId==onlyCorrectQuestion);return new AnswerChange(x.Id,x.Type=="fill_blank"||!right?null:keys[x.Id].GradingKey.CorrectOptionId,x.Etag,x.Type=="fill_blank"?(right?" I  AM ready. ":"I redy"):null);}).ToArray();
        var r=await owner.PostAsJsonAsync($"/api/v1/attempts/{attempt.Id}/submit",new SaveAnswers(Guid.NewGuid(),changes));r.EnsureSuccessStatusCode();
    }
    private async Task<Guid> Bank(bool fixture=false,bool typed=false)
    {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var version=new ContentVersion{Title="Original reading group",Status=PublicationStatus.Published,DurationSeconds=1200,IsFixture=fixture};var root=new ContentRoot{Slug=$"mistakes-{Guid.NewGuid():N}",Kind=ContentKind.Assessment,Versions=[version]};var group=new QuestionGroup{Part=7,Stimulus="Three linked questions share this original passage."};version.Groups.Add(group);
        for(var i=0;i<3;i++)group.Questions.Add(new Question{Order=i,Prompt=$"Original question {i}",Skill="reading",Explanation="Pinned explanation",Type=typed?QuestionType.FillBlank:QuestionType.SingleChoice,AcceptedAnswerVariantsJson=typed?"[\"I am ready\"]":"[]",IgnoreTerminalPunctuation=typed,Options=typed?[]:[new(){Text="Correct",IsCorrect=true},new(){Text="Other"}]});
        db.ContentRoots.Add(root);await db.SaveChangesAsync();return version.Id;
    }
    private async Task Legacy(HttpClient owner,Guid bank,int count,DateTimeOffset at)
    {
        var user=await User(owner);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var questions=await db.Questions.Include(x=>x.Options).Where(x=>x.Group.ContentVersionId==bank).OrderBy(x=>x.Order).ToArrayAsync();
        for(var n=0;n<count;n++){var a=new Attempt{UserId=user,ContentVersionId=bank,Status=AttemptStatus.Graded,StartedAt=at.AddMinutes(-20),DeadlineAt=at,Mode="practice",Result=new AttemptResult{TotalCount=questions.Length,BlankCount=questions.Length,FinalizedAt=at.AddTicks(n)}};foreach(var q in questions)a.Items.Add(new AttemptItem{QuestionId=q.Id,Order=q.Order,Prompt=q.Prompt,Stimulus="Three linked questions share this original passage.",OptionsJson=JsonSerializer.Serialize(q.Options.Select(o=>new PublicOption(o.Id,o.Text))),GradingKey=new GradingKey{CorrectOptionId=q.Options.Single(x=>x.IsCorrect).Id,Part=7,Skill="reading",Explanation=q.Explanation}});db.Attempts.Add(a);}await db.SaveChangesAsync();
    }
    private static async Task<Guid> User(HttpClient owner)=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private async Task<HttpClient> Learner(){using var scope=app.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var email=$"mistakes-{Guid.NewGuid():N}@example.com";var user=new AppUser{Id=Guid.NewGuid(),UserName=email,Email=email,EmailConfirmed=true,DisplayName="Notebook fixture",TimeZoneId="UTC"};Assert.True((await users.CreateAsync(user,DemoSeed.Password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"Learner")).Succeeded);return await app.Login(email);}
}
