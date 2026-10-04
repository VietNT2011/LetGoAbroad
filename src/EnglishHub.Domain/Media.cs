namespace EnglishHub.Domain;
public enum MediaStatus { PendingUpload = 0, Uploaded = 1, Processing = 2, Ready = 3, Failed = 4, Deleted = 5 }
/// <summary>VI: Blob key do server sinh, không dùng tên file làm đường dẫn. EN: Server-generated storage keys isolate user filenames from storage paths.</summary>
public sealed class MediaAsset : VersionedEntity
{
    public Guid OwnerId { get; set; }
    public Guid Generation { get; set; } = Guid.NewGuid();
    public string Purpose { get; set; } = "lesson_audio";
    public string FileName { get; set; } = "";
    public string DeclaredContentType { get; set; } = "";
    public long ExpectedBytes { get; set; }
    public long ReservedBytes { get; set; }
    public long StoredBytes { get; set; }
    public string StagingKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceETag { get; set; }
    public string? SourceKey { get; set; }
    public string? RenditionKey { get; set; }
    public string? Sha256 { get; set; }
    public long? DurationMs { get; set; }
    public MediaStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public string Attribution { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UploadExpiresAt { get; set; }
}
public sealed class BackgroundJob : VersionedEntity
{
    public string Kind { get; set; } = "process_media";
    public Guid ResourceId { get; set; }
    public string DedupKey { get; set; } = "";
    public string State { get; set; } = "pending";
    public DateTimeOffset DueAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
}
