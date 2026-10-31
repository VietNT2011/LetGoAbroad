using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Content;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Media;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Chỉ server xác định trợ giúp/đúng sai, session cũ không ghi đè resume mới. EN: Verify owner-bound, assistance-aware dictation and session-fenced playback on both databases.</summary>
public sealed class ListeningTests(TestApp app):IClassFixture<TestApp>
{
    [Theory]
    [InlineData("I am ready", "I am redy", "replace")]
    [InlineData("I am ready", "I ready", "delete")]
    [InlineData("I am ready", "I am very ready", "insert")]
    [InlineData("I’m ready.", "i'm READY!", "equal")]
    [InlineData("", "word", "insert")]
    [InlineData("word", "", "delete")]
    public void Token_feedback_is_bounded_normalized_and_distinguishes_missing_extra_and_different(string expected,string actual,string kind)
    {
        var diff=DictationDiff.Compare(expected,actual,1,true);Assert.Contains(diff,x=>x.Kind==kind);
    }

    [Fact]
    public async Task Read_only_info_and_lesson_payload_hide_transcript_and_do_not_create_practice_or_disclosure()
    {
        using var owner=await Learner();var bank=await Bank();var before=await Effects();
        var info=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{bank.Version}/listening");Assert.Equal(3,info.GetProperty("segmentCount").GetInt32());Assert.Equal(JsonValueKind.Null,info.GetProperty("sessionId").ValueKind);
        var lesson=await owner.GetStringAsync($"/api/v1/lessons/{bank.Version}");Assert.DoesNotContain("I am ready",lesson);Assert.DoesNotContain("acceptedAnswers",lesson);Assert.Equal(before,await Effects());
        using var admin=await app.Login("admin@englishhub.local");var editorial=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{bank.Version}");Assert.Equal(3,editorial.GetProperty("segments").GetArrayLength());
    }

    [Fact]
    public async Task Start_is_payload_bound_and_returns_safe_pinned_segments_without_answer_variants()
    {
        using var owner=await Learner();var bank=await Bank();var input=new StartListening(Guid.NewGuid());var first=await Start(owner,bank,input);var replay=await Start(owner,bank,input);
        Assert.Equal(first.Id,replay.Id);Assert.Equal(3,first.Segments.Length);Assert.All(first.Segments,x=>{Assert.Null(x.TextEn);Assert.Null(x.TextVi);Assert.Null(x.Latest);Assert.False(x.IsDisclosed);});Assert.Equal(bank.Media,first.MediaAssetId);Assert.Equal(10000,first.DurationMs);Assert.Equal(app.Clock.UtcNow.AddHours(24),first.ExpiresAt);
        var raw=await owner.GetStringAsync($"/api/v1/listening/{first.Id}");Assert.DoesNotContain("AcceptedAnswers",raw,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("I am ready",raw);
        var other=await Bank();Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync($"/api/v1/lessons/{other.Version}/listening",input)).StatusCode);
    }

    [Fact]
    public async Task First_answer_normalizes_exactly_and_repeated_attempts_cannot_inflate_the_first_attempt_summary()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var answer=await Answer(owner,session,bank.Segments[0],new(Guid.NewGuid()," I  AM ready! "));
        Assert.True(answer.IsCorrect);Assert.False(answer.IsAssisted);Assert.All(answer.Tokens,x=>Assert.Equal("equal",x.Kind));
        var repeated=await Answer(owner,session,bank.Segments[0],new(Guid.NewGuid(),"I redy"));Assert.False(repeated.IsCorrect);Assert.True(repeated.IsAssisted);
        var read=await Read(owner,session);Assert.Equal(1,read.AnsweredSegments);Assert.Equal(1,read.FirstUnaidedCount);Assert.Equal(1,read.FirstCorrectUnaided);Assert.Equal(0,read.FirstAssistedCount);Assert.Equal(2,read.Segments[0].Latest!.Number);Assert.NotNull(read.Segments[0].TextEn);
    }

    [Fact]
    public async Task Reveal_and_prior_sessions_are_server_recorded_assistance_with_separate_denominators()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);
        (await owner.PostAsJsonAsync($"/api/v1/listening/{session.Id}/segments/{bank.Segments[0]}/reveal",new RevealTranscript(Guid.NewGuid()))).EnsureSuccessStatusCode();
        var assisted=await Answer(owner,session,bank.Segments[0],new(Guid.NewGuid(),"I'm ready."));Assert.True(assisted.IsCorrect);Assert.True(assisted.IsAssisted);Assert.All(assisted.Tokens,x=>Assert.Equal("equal",x.Kind));
        await Answer(owner,session,bank.Segments[1],new(Guid.NewGuid(),"The train leaves at 10:30."));var view=await Read(owner,session);Assert.Equal(1,view.FirstAssistedCount);Assert.Equal(1,view.FirstCorrectAssisted);Assert.Equal(1,view.FirstUnaidedCount);
        var newer=await Start(owner,bank,new(Guid.NewGuid(),view.Resume.Etag));Assert.NotNull(newer.Segments[0].TextEn);Assert.True((await Answer(owner,newer,bank.Segments[0],new(Guid.NewGuid(),"I am ready."))).IsAssisted);
    }

    [Fact]
    public async Task Accepted_answer_and_reveal_receipts_replay_after_expiry_and_reject_changed_payload_or_target()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var input=new CheckDictation(Guid.NewGuid(),"I am ready.");var first=await Answer(owner,session,bank.Segments[0],input);var reveal=new RevealTranscript(Guid.NewGuid());var path=$"/api/v1/listening/{session.Id}/segments/{bank.Segments[1]}/reveal";
        var revealed=await owner.PostAsJsonAsync(path,reveal);revealed.EnsureSuccessStatusCode();var json=await revealed.Content.ReadAsStringAsync();app.Clock.Advance(TimeSpan.FromHours(24));Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(await Answer(owner,session,bank.Segments[0],input)));Assert.Equal(json,await (await owner.PostAsJsonAsync(path,reveal)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),input with{AnswerText="changed"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[1]),input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[2]),new CheckDictation(Guid.NewGuid(),"new"))).StatusCode);Assert.Equal(1,(await Read(owner,session)).AnsweredSegments);
    }

    [Fact]
    public async Task Concurrent_same_answer_creates_one_evidence_row_and_distinct_answers_observe_first_disclosure()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var input=new CheckDictation(Guid.NewGuid(),"I am ready.");var responses=await Task.WhenAll(owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),input),owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),input));Assert.All(responses,x=>x.EnsureSuccessStatusCode());Assert.Equal(await responses[0].Content.ReadAsStringAsync(),await responses[1].Content.ReadAsStringAsync());
        var next=await Task.WhenAll(owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[2]),new CheckDictation(Guid.NewGuid(),"Please bring your ticket.")),owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[2]),new CheckDictation(Guid.NewGuid(),"Please bring your ticket.")));var results=await Task.WhenAll(next.Select(async x=>{x.EnsureSuccessStatusCode();return(await x.Content.ReadFromJsonAsync<DictationResult>())!;}));Assert.Single(results,x=>!x.IsAssisted);Assert.Single(results,x=>x.IsAssisted);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var ownerId=await User(owner);Assert.Equal(3,await db.SegmentAnswers.CountAsync(x=>x.SessionId==session.Id));Assert.Equal(2,await db.ListeningDisclosures.CountAsync(x=>x.UserId==ownerId));
    }

    [Fact]
    public async Task Competing_position_writes_require_the_displayed_etag_and_cannot_overwrite_a_new_session()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var first=new SaveListeningPosition(Guid.NewGuid(),session.Resume.Etag,1000);var responses=await Task.WhenAll(owner.PutAsJsonAsync($"/api/v1/listening/{session.Id}/position",first),owner.PutAsJsonAsync($"/api/v1/listening/{session.Id}/position",first with{ClientRequestId=Guid.NewGuid(),PositionMs=2000}));Assert.Single(responses,x=>x.IsSuccessStatusCode);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
        var current=await Read(owner,session);var newer=await Start(owner,bank,new(Guid.NewGuid(),current.Resume.Etag));Assert.Equal(current.Resume.PositionMs,newer.Resume.PositionMs);Assert.Equal(HttpStatusCode.Conflict,(await owner.PutAsJsonAsync($"/api/v1/listening/{session.Id}/position",new SaveListeningPosition(Guid.NewGuid(),newer.Resume.Etag,3000))).StatusCode);
    }

    [Fact]
    public async Task Old_accepted_position_replay_keeps_its_ack_without_replacing_the_new_current_position()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var input=new SaveListeningPosition(Guid.NewGuid(),session.Resume.Etag,1000);var ack=await Position(owner,session,input);var newer=await Start(owner,bank,new(Guid.NewGuid(),ack.Etag));var latest=await Position(owner,newer,new(Guid.NewGuid(),newer.Resume.Etag,5000));Assert.Equal(ack,await Position(owner,session,input));var old=await Read(owner,session);Assert.False(old.IsCurrent);Assert.Equal(latest,old.Resume);
    }

    [Fact]
    public async Task Concurrent_session_claims_have_one_winner_and_same_command_replays_its_session()
    {
        using var owner=await Learner();var bank=await Bank();var input=new StartListening(Guid.NewGuid());var responses=await Task.WhenAll(owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",input),owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",input));Assert.All(responses,x=>x.EnsureSuccessStatusCode());var first=(await responses[0].Content.ReadFromJsonAsync<ListeningView>())!;Assert.Equal(first.Id,(await responses[1].Content.ReadFromJsonAsync<ListeningView>())!.Id);
        var competing=await Task.WhenAll(owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",new StartListening(Guid.NewGuid(),first.Resume.Etag)),owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",new StartListening(Guid.NewGuid(),first.Resume.Etag)));Assert.Single(competing,x=>x.IsSuccessStatusCode);Assert.Single(competing,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Ownership_admin_csrf_and_segment_version_boundaries_apply_to_all_session_commands()
    {
        using var owner=await Learner();using var other=await Learner();using var admin=await app.Login("admin@englishhub.local");var bank=await Bank();var session=await Start(owner,bank);var foreign=await Bank();
        foreach(var client in new[]{other,admin}){Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/listening/{session.Id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),new CheckDictation(Guid.NewGuid(),"test"))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await client.PutAsJsonAsync($"/api/v1/listening/{session.Id}/position",new SaveListeningPosition(Guid.NewGuid(),session.Resume.Etag,1000))).StatusCode);}
        Assert.Equal(HttpStatusCode.NotFound,(await owner.PostAsJsonAsync(AnswerPath(session,foreign.Segments[0]),new CheckDictation(Guid.NewGuid(),"test"))).StatusCode);owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),new CheckDictation(Guid.NewGuid(),"test"))).StatusCode);using var anonymous=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync($"/api/v1/listening/{session.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_lengths_tokens_positions_and_empty_commands_fail_without_partial_answer_or_reveal()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var before=await Effects();foreach(var text in new[]{new string('x',2001),string.Join(' ',Enumerable.Repeat("x",201))})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),new CheckDictation(Guid.NewGuid(),text))).StatusCode);
        foreach(var position in new[]{-1L,10001L})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.PutAsJsonAsync($"/api/v1/listening/{session.Id}/position",new SaveListeningPosition(Guid.NewGuid(),session.Resume.Etag,position))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),new CheckDictation(Guid.Empty,"answer"))).StatusCode);Assert.Equal(before,await Effects());
    }

    [Fact]
    public async Task Answer_history_is_bounded_and_never_creates_assessment_lesson_card_or_activity_completion()
    {
        using var owner=await Learner();var bank=await Bank();var session=await Start(owner,bank);var before=(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("statistics").GetRawText();var activity=await owner.GetStringAsync("/api/v1/learning/activity?days=30");
        for(var i=0;i<20;i++)await Answer(owner,session,bank.Segments[0],new(Guid.NewGuid(),i==0?"":"I am ready."));Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.PostAsJsonAsync(AnswerPath(session,bank.Segments[0]),new CheckDictation(Guid.NewGuid(),"again"))).StatusCode);var read=await Read(owner,session);Assert.Equal(1,read.FirstUnaidedCount);Assert.Equal(0,read.FirstCorrectUnaided);Assert.Equal(20,read.Segments[0].Latest!.Number);Assert.Equal(before,(await owner.GetFromJsonAsync<JsonElement>("/api/v1/learning/dashboard")).GetProperty("statistics").GetRawText());Assert.Equal(activity,await owner.GetStringAsync("/api/v1/learning/activity?days=30"));
    }

    [Fact]
    public async Task Archival_keeps_owned_session_history_and_audio_acl_without_admitting_new_users()
    {
        using var owner=await Learner();using var other=await Learner();var bank=await Bank();var session=await Start(owner,bank);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();(await db.ContentVersions.SingleAsync(x=>x.Id==bank.Version)).Status=PublicationStatus.Archived;await db.SaveChangesAsync();Assert.Equal(session.Id,(await Read(owner,session)).Id);Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync($"/api/v1/lessons/{bank.Version}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/lessons/{bank.Version}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",new StartListening(Guid.NewGuid(),session.Resume.Etag))).StatusCode);
        var service=new MediaService(db,new Transactions(db),new Receipts(db,app.Clock),new GrantOnlyStorage(),app.Clock);Assert.Equal("fixture-playback",await service.Playback(await User(owner),bank.Media,false));var otherId=await User(other);var error=await Assert.ThrowsAsync<DomainError>(()=>service.Playback(otherId,bank.Media,false));Assert.Equal(404,error.Status);
    }

    [Fact]
    public async Task Cms_rejects_overlap_and_out_of_duration_at_publish_and_preserves_segments_during_draft_edit()
    {
        using var admin=await app.Login("admin@englishhub.local");var bank=await Bank();var input=Input(bank.Media,[new(0,2000,"I am ready.","Tôi đã sẵn sàng."),new(1000,3000,"Please wait.","Vui lòng đợi.")]);var created=await admin.PostAsJsonAsync("/api/v1/admin/content",new CreateContentInput($"transcript-{Guid.NewGuid():N}",ContentKind.Lesson,input));created.EnsureSuccessStatusCode();var draft=await created.Content.ReadFromJsonAsync<JsonElement>();var id=draft.GetProperty("id").GetGuid();var etag=draft.GetProperty("etag").GetString()!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Publish(admin,id,etag)).StatusCode);var valid=input with{Segments=[new(0,2000,"I am ready.","Tôi đã sẵn sàng.")]};using var update=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/admin/versions/{id}"){Content=JsonContent.Create(valid)};update.Headers.TryAddWithoutValidation("If-Match",etag);var edited=await admin.SendAsync(update);edited.EnsureSuccessStatusCode();var oldEtag=etag;etag=(await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("etag").GetString()!;Assert.NotEqual(oldEtag,etag);using var stale=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/admin/versions/{id}"){Content=JsonContent.Create(valid)};stale.Headers.TryAddWithoutValidation("If-Match",oldEtag);Assert.Equal(HttpStatusCode.PreconditionFailed,(await admin.SendAsync(stale)).StatusCode);var view=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/versions/{id}");Assert.Single(view.GetProperty("segments").EnumerateArray());(await Publish(admin,id,etag)).EnsureSuccessStatusCode();
        var outside=await admin.PostAsJsonAsync("/api/v1/admin/content",new CreateContentInput($"outside-{Guid.NewGuid():N}",ContentKind.Lesson,input with{Segments=[new(0,10001,"I am ready.","Tôi đã sẵn sàng.")]}));outside.EnsureSuccessStatusCode();var rejected=await outside.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Publish(admin,rejected.GetProperty("id").GetGuid(),rejected.GetProperty("etag").GetString()!)).StatusCode);
    }

    [Fact]
    public void Adding_absent_transcript_does_not_change_legacy_import_payload_hash()
    {
        var content=Input(null,null);var input=new ImportInput(Guid.NewGuid(),[new("legacy-transcript",ContentKind.Lesson,content)]);var legacy=new{input.ClientRequestId,Items=input.Items.Select(x=>new{x.Slug,x.Kind,Content=new{content.Title,content.Summary,content.Body,content.Skill,content.Minutes,content.DurationSeconds,content.IsFixture,content.Attribution,content.Groups,content.MediaAssetId}}).ToArray()};Assert.Equal(Receipts.Hash(legacy),Receipts.Hash(input));
    }

    private static string AnswerPath(ListeningView view,Guid segment)=>$"/api/v1/listening/{view.Id}/segments/{segment}/answers";
    [Fact]
    public async Task Composite_answer_and_resume_keys_reject_cross_owner_evidence_even_if_inserted_directly()
    {
        using var owner=await Learner();using var other=await Learner();var bank=await Bank();var session=await Start(owner,bank);var foreign=await Start(other,bank);var foreignId=await User(other);var ownerId=await User(owner);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();db.SegmentAnswers.Add(new SegmentAnswer{SessionId=session.Id,UserId=foreignId,ContentVersionId=bank.Version,SegmentId=bank.Segments[0],Number=1,AnswerText="foreign"});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());}
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var head=await db.ListeningResumes.SingleAsync(x=>x.UserId==ownerId&&x.ContentVersionId==bank.Version);head.SessionId=foreign.Id;await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());}
        Assert.True((await Read(owner,session)).IsCurrent);
    }

    [Fact]
    public async Task Unavailable_audio_and_technical_fixture_lessons_cannot_create_new_sessions()
    {
        using var owner=await Learner();var bank=await Bank();var before=await Effects();
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();(await db.MediaAssets.SingleAsync(x=>x.Id==bank.Media)).Status=MediaStatus.Processing;await db.SaveChangesAsync();}
        var info=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/lessons/{bank.Version}/listening");Assert.False(info.GetProperty("canStart").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",new StartListening(Guid.NewGuid()))).StatusCode);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();(await db.MediaAssets.SingleAsync(x=>x.Id==bank.Media)).Status=MediaStatus.Ready;(await db.ContentVersions.SingleAsync(x=>x.Id==bank.Version)).IsFixture=true;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.NotFound,(await owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",new StartListening(Guid.NewGuid()))).StatusCode);Assert.Equal(before,await Effects());
    }

    [Fact]
    public async Task Invalid_segment_shape_fails_before_relational_constraints_or_partial_draft_creation()
    {
        using var admin=await app.Login("admin@englishhub.local");var bank=await Bank();using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var before=await db.ContentRoots.CountAsync();
        foreach(var segment in new[]{new TranscriptInput(-1,1000,"Valid English.","Bản dịch."),new TranscriptInput(1000,1000,"Valid English.","Bản dịch."),new TranscriptInput(0,1000,"Valid English.","Bản dịch.",NormalizationPolicyVersion:99)}){var request=new CreateContentInput($"invalid-segment-{Guid.NewGuid():N}",ContentKind.Lesson,Input(bank.Media,[segment]));Assert.Equal(HttpStatusCode.UnprocessableEntity,(await admin.PostAsJsonAsync("/api/v1/admin/content",request)).StatusCode);}
        Assert.Equal(before,await db.ContentRoots.CountAsync());
    }

    private static async Task<ListeningView> Start(HttpClient owner,BankData bank,StartListening? input=null){var r=await owner.PostAsJsonAsync($"/api/v1/lessons/{bank.Version}/listening",input??new(Guid.NewGuid()));r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<ListeningView>())!;}
    private static async Task<DictationResult> Answer(HttpClient owner,ListeningView view,Guid segment,CheckDictation input){var r=await owner.PostAsJsonAsync(AnswerPath(view,segment),input);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<DictationResult>())!;}
    private static async Task<ListeningView> Read(HttpClient owner,ListeningView view)=>(await owner.GetFromJsonAsync<ListeningView>($"/api/v1/listening/{view.Id}"))!;
    private static async Task<ListeningPositionAck> Position(HttpClient owner,ListeningView view,SaveListeningPosition input){var r=await owner.PutAsJsonAsync($"/api/v1/listening/{view.Id}/position",input);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<ListeningPositionAck>())!;}
    private static async Task<HttpResponseMessage> Publish(HttpClient admin,Guid id,string etag){using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/admin/versions/{id}/publish"){Content=JsonContent.Create(new{})};request.Headers.TryAddWithoutValidation("If-Match",etag);return await admin.SendAsync(request);}
    private static ContentInput Input(Guid? media,TranscriptInput[]? segments)=>new("Original listening lesson","Authored bilingual segments","Listen, type and compare an original sentence carefully.","listening",5,1200,false,"Original authored fixture",[],media,segments);
    private sealed record BankData(Guid Version,Guid Media,Guid[] Segments);
    private async Task<BankData> Bank()
    {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var admin=await db.Users.SingleAsync(x=>x.Email=="admin@englishhub.local");var media=new MediaAsset{OwnerId=admin.Id,Purpose="lesson_audio",Status=MediaStatus.Ready,DurationMs=10000,RenditionKey="fixture.mp3",FileName="original-fixture.wav",CreatedAt=app.Clock.UtcNow};db.MediaAssets.Add(media);var version=new ContentVersion{Title="Original listening fixture",Body="Listen and write the original authored sentences carefully.",Status=PublicationStatus.Published,MediaAssetId=media.Id,Attribution="Original fixture",Skill="listening"};var root=new ContentRoot{Slug=$"listening-{Guid.NewGuid():N}",Kind=ContentKind.Lesson,Versions=[version]};var english=new[]{"I am ready.","The train leaves at ten thirty.","Please bring your ticket."};
        for(var i=0;i<3;i++)version.Segments.Add(new TranscriptSegment{Order=i,StartMs=i*2500,EndMs=i*2500+2000,TextEn=english[i],TextVi="Bản dịch biên soạn.",AcceptedAnswersJson=JsonSerializer.Serialize(i==0?new[]{english[i],"I'm ready."}:i==1?new[]{english[i],"The train leaves at 10:30."}:new[]{english[i]})});db.ContentRoots.Add(root);await db.SaveChangesAsync();root.CurrentPublishedVersionId=version.Id;await db.SaveChangesAsync();return new(version.Id,media.Id,version.Segments.Select(x=>x.Id).ToArray());
    }
    private async Task<string> Effects(){using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();return JsonSerializer.Serialize(new{sessions=await db.ListeningSessions.CountAsync(),answers=await db.SegmentAnswers.CountAsync(),disclosures=await db.ListeningDisclosures.CountAsync(),receipts=await db.CommandReceipts.CountAsync(),lessons=await db.LessonProgress.CountAsync(),attempts=await db.Attempts.CountAsync()});}
    private static async Task<Guid> User(HttpClient owner)=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private async Task<HttpClient> Learner(){using var scope=app.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var email=$"listening-{Guid.NewGuid():N}@example.com";var user=new AppUser{Id=Guid.NewGuid(),UserName=email,Email=email,EmailConfirmed=true,DisplayName="Listening fixture"};Assert.True((await users.CreateAsync(user,DemoSeed.Password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"Learner")).Succeeded);return await app.Login(email);}
    private sealed class GrantOnlyStorage:IObjectStorage
    {
        public string PlaybackGrant(string key,DateTimeOffset expiry)=>"fixture-playback";
        public Task Ensure(CancellationToken c)=>throw new NotSupportedException();public string UploadGrant(string k,DateTimeOffset e)=>throw new NotSupportedException();public Task<BlobFacts> InspectStaging(string k,CancellationToken c)=>throw new NotSupportedException();public Task Freeze(string s,string e,string k,CancellationToken c)=>throw new NotSupportedException();public Task Download(string k,string p,CancellationToken c)=>throw new NotSupportedException();public Task Upload(string k,string p,CancellationToken c)=>throw new NotSupportedException();public Task DeleteAsset(Guid i,string k,CancellationToken c)=>throw new NotSupportedException();public Task Cleanup(Guid i,string k,string? s,string? r,DateTimeOffset d,CancellationToken c)=>throw new NotSupportedException();
    }
}
