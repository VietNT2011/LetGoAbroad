using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Danh sách ghi âm không cấp quyền blob hay quyền Admin trên giọng nói người khác. EN: Metadata lists never issue blob grants or give Admin access to another person's recordings.</summary>
public sealed class RecordingLibraryTests(TestApp app):IClassFixture<TestApp>
{
    [Fact]
    public async Task Library_lists_only_owned_recordings_and_omits_deleted_assets_lesson_audio_and_storage_secrets()
    {
        using var owner=await Learner();using var other=await Learner();using var admin=await app.Login("admin@englishhub.local");await Seed(owner,MediaStatus.Ready);await Seed(owner,MediaStatus.Processing);await Seed(owner,MediaStatus.Deleted);await Seed(owner,MediaStatus.Ready,"lesson_audio");await Seed(other,MediaStatus.Ready);
        var response=await owner.GetStringAsync("/api/v1/me/recordings");var list=JsonDocument.Parse(response).RootElement.GetProperty("items");Assert.Equal(2,list.GetArrayLength());Assert.All(list.EnumerateArray(),x=>Assert.Equal("My private fixture",x.GetProperty("attribution").GetString()));
        foreach(var field in new[]{"uploadUrl","stagingKey","sourceKey","renditionKey","sourceETag","sha256","reservedBytes","ownerId"})Assert.DoesNotContain(field,response,StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await Page(admin)).GetProperty("items").EnumerateArray());Assert.Single((await Page(other)).GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task Stable_pages_are_bounded_and_owner_query_parameters_cannot_change_identity()
    {
        using var owner=await Learner();using var other=await Learner();for(var i=0;i<32;i++)await Seed(owner,MediaStatus.Ready);await Seed(other,MediaStatus.Ready);
        var first=await Page(owner);var second=await Page(owner,"?offset=30");Assert.Equal(30,first.GetProperty("items").GetArrayLength());Assert.Equal(2,second.GetProperty("items").GetArrayLength());Assert.Equal(30,first.GetProperty("nextOffset").GetInt32());Assert.Equal(JsonValueKind.Null,second.GetProperty("nextOffset").ValueKind);Assert.Equal(32,first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x=>x.GetProperty("id").GetGuid()).Distinct().Count());
        var foreign=await User(other);var ignored=await Page(owner,$"?userId={foreign}&ownerId={foreign}");Assert.Equal(first.GetRawText(),ignored.GetRawText());
    }
    [Fact]
    public async Task Metadata_reads_need_auth_and_valid_offsets_but_do_not_initialize_unconfigured_blob_storage()
    {
        using var owner=await Learner();Assert.Empty((await Page(owner)).GetProperty("items").EnumerateArray());foreach(var offset in new[]{-1,100001})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync($"/api/v1/me/recordings?offset={offset}")).StatusCode);
        using var anonymous=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/me/recordings")).StatusCode);
    }
    [Fact]
    public async Task Repeated_library_reads_preserve_media_state_and_all_learning_evidence()
    {
        using var owner=await Learner();var id=await Seed(owner,MediaStatus.DeletePending);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var before=await db.MediaAssets.AsNoTracking().SingleAsync(x=>x.Id==id);var effects=await Counts(db);await Page(owner);await Page(owner);var after=await db.MediaAssets.AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal(before.RowVersion,after.RowVersion);Assert.Equal(MediaStatus.DeletePending,after.Status);Assert.Equal(effects,await Counts(db));
    }
    private static async Task<string> Counts(HubDbContext db)=>JsonSerializer.Serialize(new{attempts=await db.Attempts.CountAsync(),lessons=await db.LessonProgress.CountAsync(),reviews=await db.CardReviews.CountAsync(),sessions=await db.ActivitySessions.CountAsync(),receipts=await db.CommandReceipts.CountAsync(),jobs=await db.BackgroundJobs.CountAsync(),outbox=await db.OutboxEvents.CountAsync()});
    private static async Task<JsonElement> Page(HttpClient owner,string query="")=>await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/recordings"+query);
    private static async Task<Guid> User(HttpClient owner)=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetGuid();
    private async Task<Guid> Seed(HttpClient owner,MediaStatus status,string purpose="recording"){var user=await User(owner);using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<HubDbContext>();var asset=new MediaAsset{OwnerId=user,Purpose=purpose,Status=status,FileName="private-fixture.wav",Attribution="My private fixture",DeclaredContentType="audio/wav",ExpectedBytes=100,ReservedBytes=300,CreatedAt=app.Clock.UtcNow,UploadExpiresAt=app.Clock.UtcNow.AddMinutes(15),StagingKey=Guid.NewGuid().ToString("N"),SourceKey="private-source",RenditionKey="private-output"};db.MediaAssets.Add(asset);await db.SaveChangesAsync();return asset.Id;}
    private async Task<HttpClient> Learner(){using var scope=app.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var email=$"recordings-{Guid.NewGuid():N}@example.com";var user=new AppUser{Id=Guid.NewGuid(),UserName=email,Email=email,EmailConfirmed=true,DisplayName="Recording library fixture"};Assert.True((await users.CreateAsync(user,DemoSeed.Password)).Succeeded);Assert.True((await users.AddToRoleAsync(user,"Learner")).Succeeded);return await app.Login(email);}
}
