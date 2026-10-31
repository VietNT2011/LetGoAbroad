namespace EnglishHub.Domain;

/// <summary>VI: Ghi chú riêng ghim vào câu/version qua snapshot nguồn. EN: Private annotations pin a question version through an immutable source attempt item.</summary>
public sealed class QuestionAnnotation : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid SourceAttemptItemId { get; set; }
    public AttemptItem SourceItem { get; set; } = null!;
    public bool IsBookmarked { get; set; }
    public string NoteText { get; set; } = "";
    public string AnchorBlock { get; set; } = "prompt";
    public int AnchorStart { get; set; }
    public int AnchorEnd { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
