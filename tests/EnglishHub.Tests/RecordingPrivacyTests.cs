using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Quyền Admin vận hành không mở metadata hoặc retry bản thu riêng. EN: Operational administration does not grant another user's private recording metadata or mutation rights.</summary>
public sealed class RecordingPrivacyTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Admin_media_catalog_contains_editorial_audio_and_own_recordings_only()
    {
        using var admin = await app.Login("admin@englishhub.local"); var bank = await Bank();
        var json = await admin.GetStringAsync("/api/v1/admin/media"); var ids = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(bank.Audio.Id, ids); Assert.Contains(bank.AdminRecording.Id, ids); Assert.DoesNotContain(bank.Private.Id, ids); Assert.DoesNotContain(bank.EditorRecording.Id, ids);
        Assert.DoesNotContain(bank.Private.FileName, json); Assert.DoesNotContain(bank.Private.Attribution, json); Assert.DoesNotContain("stagingKey", json); Assert.DoesNotContain("renditionKey", json);
    }

    [Fact]
    public async Task Editor_catalog_also_hides_other_owners_recordings_and_keeps_own_metadata()
    {
        using var editor = await app.Login("editor@englishhub.local"); var bank = await Bank();
        var rows = await editor.GetFromJsonAsync<JsonElement>("/api/v1/admin/media"); var ids = rows.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(bank.Audio.Id, ids); Assert.Contains(bank.EditorRecording.Id, ids); Assert.DoesNotContain(bank.Private.Id, ids); Assert.DoesNotContain(bank.AdminRecording.Id, ids);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/v1/admin/jobs")).StatusCode);
        using var learner = await app.Login(); Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/v1/admin/media")).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/media")).StatusCode);
    }

    [Fact]
    public async Task Admin_job_catalog_excludes_other_owners_recording_jobs()
    {
        using var admin = await app.Login("admin@englishhub.local"); var bank = await Bank();
        var rows = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/jobs"); var ids = rows.EnumerateArray().Select(x => x.GetProperty("resourceId").GetGuid()).ToArray();
        Assert.Contains(bank.Audio.Id, ids); Assert.Contains(bank.AdminRecording.Id, ids); Assert.DoesNotContain(bank.Private.Id, ids); Assert.DoesNotContain(bank.EditorRecording.Id, ids);
    }

    [Fact]
    public async Task Known_foreign_recording_jobs_and_unknown_jobs_are_404_with_no_retry_side_effects()
    {
        using var admin = await app.Login("admin@englishhub.local"); var bank = await Bank();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var jobs = await db.BackgroundJobs.CountAsync(); var receipts = await db.CommandReceipts.CountAsync(); var audits = await db.AuditEntries.CountAsync();
        var before = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == bank.Private.Id);
        foreach (var job in new[] { bank.PrivateJob, Guid.NewGuid() })
        {
            var response = await admin.PostAsJsonAsync($"/api/v1/admin/jobs/{job}/retry", new { clientRequestId = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("job_not_found", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var after = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == bank.Private.Id); Assert.Equal(before.Generation, after.Generation); Assert.Equal(before.RowVersion, after.RowVersion); Assert.Equal(MediaStatus.Failed, after.Status);
        Assert.Equal(jobs, await db.BackgroundJobs.CountAsync()); Assert.Equal(receipts, await db.CommandReceipts.CountAsync()); Assert.Equal(audits, await db.AuditEntries.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/media/{bank.Private.Id}")).StatusCode);
    }

    [Fact]
    public async Task Own_recording_and_editorial_audio_retries_still_replay_exactly_once_with_csrf_and_roles()
    {
        using var admin = await app.Login("admin@englishhub.local"); var bank = await Bank();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var count = await db.BackgroundJobs.CountAsync();
        foreach (var id in new[] { bank.AdminJob, bank.AudioJob })
        {
            var input = new { clientRequestId = Guid.NewGuid() };
            var first = await admin.PostAsJsonAsync($"/api/v1/admin/jobs/{id}/retry", input); first.EnsureSuccessStatusCode();
            var replay = await admin.PostAsJsonAsync($"/api/v1/admin/jobs/{id}/retry", input); replay.EnsureSuccessStatusCode(); Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        }
        Assert.Equal(count + 2, await db.BackgroundJobs.CountAsync());
        var recording = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == bank.AdminRecording.Id); Assert.Equal(MediaStatus.Uploaded, recording.Status);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/jobs/{bank.AdminJob}/retry", new { clientRequestId = Guid.NewGuid() })).StatusCode);
        using var learner = await app.Login(); Assert.Equal(HttpStatusCode.Forbidden, (await learner.PostAsJsonAsync($"/api/v1/admin/jobs/{bank.PrivateJob}/retry", new { clientRequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(count + 2, await db.BackgroundJobs.CountAsync());
    }

    private sealed record BankData(MediaAsset Private, MediaAsset AdminRecording, MediaAsset EditorRecording, MediaAsset Audio, Guid PrivateJob, Guid AdminJob, Guid AudioJob);
    private async Task<BankData> Bank()
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var learner = await db.Users.Where(x => x.Email == "learner@englishhub.local").Select(x => x.Id).SingleAsync(); var admin = await db.Users.Where(x => x.Email == "admin@englishhub.local").Select(x => x.Id).SingleAsync(); var editor = await db.Users.Where(x => x.Email == "editor@englishhub.local").Select(x => x.Id).SingleAsync();
        MediaAsset Make(Guid owner, string purpose) => new() { OwnerId = owner, Purpose = purpose, FileName = $"private-{Guid.NewGuid():N}.wav", Attribution = $"Private original diary {Guid.NewGuid():N}", Status = MediaStatus.Failed, FailureCode = "invalid_audio", ExpectedBytes = 100, CreatedAt = app.Clock.UtcNow, UploadExpiresAt = app.Clock.UtcNow.AddMinutes(15) };
        var personal = Make(learner, "recording"); var own = Make(admin, "recording"); var editors = Make(editor, "recording"); var audio = Make(editor, "lesson_audio"); var assets = new[] { personal, own, editors, audio }; var jobs = assets.Select(a => new BackgroundJob { ResourceId = a.Id, State = "failed", DedupKey = $"privacy-{a.Id}", DueAt = app.Clock.UtcNow }).ToArray();
        db.MediaAssets.AddRange(assets); db.BackgroundJobs.AddRange(jobs); await db.SaveChangesAsync(); return new(personal, own, editors, audio, jobs[0].Id, jobs[1].Id, jobs[3].Id);
    }
}
