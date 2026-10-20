using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
namespace EnglishHub.Infrastructure.Media;

/// <summary>VI: Truyền từng argument, không ghép shell command từ dữ liệu upload. EN: ArgumentList avoids executing filenames or media metadata as shell code.</summary>
public sealed class FfmpegProcessor : IMediaProcessor
{
    public async Task<MediaProbe> Process(string inputPath, string outputPath, int maximumSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var raw = await Run("ffprobe", ["-v", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", "wav,mp3,ogg,matroska,webm,mov", "-show_entries", "format=duration,format_name:stream=codec_type,codec_name", "-of", "json", inputPath], timeout.Token);
        using var document = JsonDocument.Parse(raw);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        DomainError.Require(streams.Any(x => x.GetProperty("codec_type").GetString() == "audio") && streams.All(x => x.GetProperty("codec_type").GetString() != "video"), 422, "invalid_audio", "File không phải audio-only hợp lệ.");
        var duration = double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        DomainError.Require(double.IsFinite(duration) && duration > 0 && duration <= maximumSeconds, 422, "invalid_duration", "Audio vượt thời lượng cho phép.");
        await Run("ffmpeg", ["-nostdin", "-hide_banner", "-v", "error", "-xerror", "-protocol_whitelist", "file,pipe", "-format_whitelist", "wav,mp3,ogg,matroska,webm,mov", "-i", inputPath, "-map", "0:a:0", "-vn", "-map_metadata", "-1", "-ac", "1", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "128k", "-fs", "100000000", "-y", outputPath], timeout.Token);
        var outputDuration = await Run("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", outputPath], timeout.Token);
        DomainError.Require(Math.Abs(double.Parse(outputDuration.Trim(), CultureInfo.InvariantCulture) - duration) < 2, 422, "truncated_audio", "Rendition không đủ thời lượng.");
        await using var output = File.OpenRead(outputPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(output, ct));
        return new((long)(duration * 1000), "mp3", output.Length, hash);
    }

    private static async Task<string> Run(string executable, string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Media processor could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        var text = await stdout; _ = await stderr;
        DomainError.Require(process.ExitCode == 0, 422, "media_decode_failed", "FFmpeg không giải mã được file audio.");
        return text;
    }
}
