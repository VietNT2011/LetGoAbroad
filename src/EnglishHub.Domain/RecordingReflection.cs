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
}
