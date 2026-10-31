namespace EnglishHub.Domain;

/// <summary>VI: Trạng thái ôn theo câu/version, không thay thế đáp án gốc. EN: A question-version recall index never rewrites original assessment evidence.</summary>
public sealed class MistakeItem : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid SourceAttemptItemId { get; set; }
    public AttemptItem SourceItem { get; set; } = null!;
    public int WrongCount { get; set; }
    public DateTimeOffset LastWrongAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public string Status { get; set; } = "learning";
    public DateTimeOffset? NextReviewAt { get; set; }
    public int CorrectReviewDays { get; set; }
    public DateOnly? LastCorrectDate { get; set; }
    public string CycleTimeZoneId { get; set; } = "UTC";
}

public sealed class MistakeReviewTarget
{
    public Guid AttemptItemId { get; set; }
    public AttemptItem Item { get; set; } = null!;
    public Guid MistakeId { get; set; }
    public MistakeItem Mistake { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
}

/// <summary>VI: Import và chấm trực tiếp chỉ tính một lần mỗi attempt. EN: Live grading and historical imports share a durable per-attempt processing marker.</summary>
public sealed class MistakeProcessedAttempt
{
    public Guid AttemptId { get; set; }
}
