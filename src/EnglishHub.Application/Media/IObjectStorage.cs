namespace EnglishHub.Application.Media;
public sealed record BlobFacts(long Bytes, string ETag, string ContentType);
public sealed record MediaProbe(long DurationMs, string Codec, long OutputBytes, string Sha256);
/// <summary>VI: Boundary storage không lộ SDK vào nghiệp vụ. EN: Storage boundary keeps provider SDK types out of application contracts.</summary>
public interface IObjectStorage
{
    Task Ensure(CancellationToken ct);
    string UploadGrant(string stagingKey, DateTimeOffset expiresAt);
    string PlaybackGrant(string canonicalKey, DateTimeOffset expiresAt);
    Task<BlobFacts> InspectStaging(string stagingKey, CancellationToken ct);
    Task Freeze(string stagingKey, string expectedETag, string canonicalKey, CancellationToken ct);
    Task Download(string canonicalKey, string destination, CancellationToken ct);
    Task Upload(string canonicalKey, string localFile, CancellationToken ct);
    Task DeleteAsset(Guid assetId, string stagingKey, CancellationToken ct);
    Task Cleanup(Guid assetId, string stagingKey, string? sourceKey, string? renditionKey, DateTimeOffset olderThan, CancellationToken ct);
}
public interface IMediaProcessor
{
    Task<MediaProbe> Process(string inputPath, string outputPath, int maximumSeconds, CancellationToken ct);
}
