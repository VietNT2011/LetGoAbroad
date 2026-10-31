namespace EnglishHub.Domain;

/// <summary>VI: Tự đánh giá là metadata riêng của asset, không phải điểm phát âm. EN: A reflection inherits its recording's ownership and never represents an objective speech score.</summary>
public sealed class RecordingReflection : VersionedEntity
{
    public Guid MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
    public string Notes { get; set; } = "";
    public int? Clarity { get; set; }
    public int? Rhythm { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Revision { get; set; }
    public Guid? SourceContentVersionId { get; set; }
    public Guid? SourceSegmentId { get; set; }
}

/// <summary>VI: Chỉ thêm lịch sử; snapshot cũ được ghi nhãn riêng khi bắt đầu theo dõi. EN: Append history; a previously unversioned value is explicitly labelled as a captured legacy baseline.</summary>
public sealed class RecordingReflectionRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReflectionId { get; set; }
    public RecordingReflection Reflection { get; set; } = null!;
    public int Number { get; set; }
    public string Kind { get; set; } = "edit";
    public string Notes { get; set; } = "";
    public int? Clarity { get; set; }
    public int? Rhythm { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public Guid? SourceContentVersionId { get; set; }
    public Guid? SourceSegmentId { get; set; }
}
