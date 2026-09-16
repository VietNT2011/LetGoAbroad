namespace EnglishHub.Domain;

public enum AttemptStatus { InProgress = 0, Graded = 1, Abandoned = 2 }
/// <summary>VI: Snapshot riêng cho từng lần làm bài. EN: Each attempt owns its frozen question and grading snapshots.</summary>
public sealed class Attempt : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public string Mode { get; set; } = "mini";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public AttemptStatus Status { get; set; }
    public List<AttemptItem> Items { get; set; } = [];
    public AttemptResult? Result { get; set; }
}
public sealed class AttemptItem : VersionedEntity
{
    public Guid AttemptId { get; set; }
    public Attempt Attempt { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Order { get; set; }
    public string Prompt { get; set; } = "";
    public string Stimulus { get; set; } = "";
    public string OptionsJson { get; set; } = "[]";
    public Guid? SelectedOptionId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public GradingKey GradingKey { get; set; } = null!;
}
/// <summary>VI: Không serialize entity này ra payload đề. EN: Never serialize this private grading entity into an active attempt DTO.</summary>
public sealed class GradingKey
{
    public Guid AttemptItemId { get; set; }
    public Guid CorrectOptionId { get; set; }
    public string Explanation { get; set; } = "";
    public string Skill { get; set; } = "";
    public int Part { get; set; }
    public int GradingPolicyVersion { get; set; } = 1;
}
public sealed class AttemptResult
{
    public Guid AttemptId { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public int BlankCount { get; set; }
    public int ListeningRaw { get; set; }
    public int ReadingRaw { get; set; }
    public string Reason { get; set; } = "manual_submit";
    public int ScoreCalculationVersion { get; set; } = 1;
    public DateTimeOffset FinalizedAt { get; set; }
}
public sealed class CommandReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Scope { get; set; } = "";
    public Guid CommandId { get; set; }
    public string PayloadHash { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
/// <summary>VI: Sự kiện được lưu cùng transaction nghiệp vụ. EN: Events persist atomically with their business change and can be retried after a crash.</summary>
public sealed class OutboxEvent : VersionedEntity
{
    public string Type { get; set; } = "";
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
