using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Test thật với Azurite, ffprobe và FFmpeg; chạy -Storage. EN: Real blob and transcoding integration; opt in with scripts/test.ps1 -Storage.</summary>
[Trait("Category","Storage")]
public sealed class MediaIntegrationTests(TestApp app) : IClassFixture<TestApp>
{
    // VI: Tone kỹ thuật do test sinh, không giả là audio bài học. EN: An authored technical tone fixture, never presented as learning speech.
    private static byte[] Wave()
    {
        const int rate=16000; using var buffer=new MemoryStream(); using var writer=new BinaryWriter(buffer);
        writer.Write("RIFF"u8.ToArray()); writer.Write(36+rate*2); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8.ToArray()); writer.Write(rate*2);
        for(var i=0;i<rate;i++) writer.Write((short)(Math.Sin(2*Math.PI*440*i/rate)*5000));
        return buffer.ToArray();
    }
    private async Task<(HttpClient Client,UploadGrant Grant,byte[] Bytes)> Upload(byte[]? bytes=null)
    {
        var client=await app.Login("admin@englishhub.local"); bytes ??= Wave();
        var response=await client.PostAsJsonAsync("/api/v1/media/uploads",new CreateUpload(Guid.NewGuid(),"lesson_audio","tone.wav","audio/wav",bytes.Length,"Original synthetic tone · technical integration fixture")); response.EnsureSuccessStatusCode();
        var grant=(await response.Content.ReadFromJsonAsync<UploadGrant>())!;
        await Put(grant.UploadUrl,bytes);
        return (client,grant,bytes);
    }
    private static async Task Put(string url,byte[] bytes)
    {
        using var http=new HttpClient(); using var content=new ByteArrayContent(bytes); content.Headers.ContentType=new MediaTypeHeaderValue("audio/wav");
        using var request=new HttpRequestMessage(HttpMethod.Put,url) { Content=content }; request.Headers.Add("x-ms-blob-type","BlockBlob");
        (await http.SendAsync(request)).EnsureSuccessStatusCode();
    }
    private async Task Process()
    {
        await using var scope=app.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<MediaPipeline>().RunOne(CancellationToken.None));
    }
    [Fact]
    public async Task Uploaded_audio_is_processed_then_private_playback_supports_real_byte_ranges()
    {
        var (client,grant,_)=await Upload(); using var owner=client;
        (await owner.PostAsync($"/api/v1/media/{grant.AssetId}/complete",null)).EnsureSuccessStatusCode(); await Process();
        var state=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/media/{grant.AssetId}"); Assert.Equal("ready",state.GetProperty("status").GetString());
        using var other=await app.Login("learner2@englishhub.local");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/media/{grant.AssetId}/content")).StatusCode);
        var noRedirect=app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect=false }); await TestApp.Csrf(noRedirect);
        (await noRedirect.PostAsJsonAsync("/api/v1/auth/login",new { email="admin@englishhub.local",password="EnglishHub@2026" })).EnsureSuccessStatusCode();
        using var playback=await noRedirect.GetAsync($"/api/v1/media/{grant.AssetId}/content"); Assert.Equal(HttpStatusCode.Redirect,playback.StatusCode);
        using var blob=new HttpClient(); using var range=new HttpRequestMessage(HttpMethod.Get,playback.Headers.Location); range.Headers.Range=new RangeHeaderValue(0,99);
        using var partial=await blob.SendAsync(range); Assert.Equal(HttpStatusCode.PartialContent,partial.StatusCode); Assert.Equal(100,(await partial.Content.ReadAsByteArrayAsync()).Length);
    }
    [Fact]
    public async Task Rewriting_staging_after_complete_is_detected_and_never_becomes_ready()
    {
        var (client,grant,bytes)=await Upload(); using var owner=client;
        (await owner.PostAsync($"/api/v1/media/{grant.AssetId}/complete",null)).EnsureSuccessStatusCode();
        bytes[^1]^=1; await Put(grant.UploadUrl,bytes); await Process();
        var state=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/media/{grant.AssetId}"); Assert.Equal("failed",state.GetProperty("status").GetString()); Assert.Equal("staging_changed",state.GetProperty("failureCode").GetString());
    }
    [Fact]
    public async Task A_text_file_declared_as_wav_fails_real_decoding()
    {
        var (client,grant,_)=await Upload("This is not an audio file."u8.ToArray()); using var owner=client;
        (await owner.PostAsync($"/api/v1/media/{grant.AssetId}/complete",null)).EnsureSuccessStatusCode(); await Process();
        var state=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/media/{grant.AssetId}"); Assert.Equal("failed",state.GetProperty("status").GetString()); Assert.Equal("media_decode_failed",state.GetProperty("failureCode").GetString());
    }
}
