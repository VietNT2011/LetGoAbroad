namespace EnglishHub.Domain;

/// <summary>VI: Một nhiệm vụ ôn gồm các thẻ đã ghim, không mượn ID nội dung giả. EN: A core review assignment pins real owned cards without fabricating a content version.</summary>
public sealed class DailyReviewBatch : VersionedEntity
{
    public Guid EnrollmentId { get; set; }
    public Guid UserId { get; set; }
    public Guid GoalRevisionId { get; set; }
    public DateOnly LocalDate { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public int Number { get; set; }
    public int EstimatedMinutes { get; set; }
    public int PlanningPolicyVersion { get; set; } = 1;
    public string ReasonCode { get; set; } = "DUE_REVIEW";
    public string ReasonText { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DailyReviewItem> Items { get; set; } = [];
}

public sealed class DailyReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public Guid UserId { get; set; }
    public Guid UserCardId { get; set; }
    public UserCard Card { get; set; } = null!;
    public int Order { get; set; }
    public DateTimeOffset DueAtWhenAssigned { get; set; }
    public Guid? CardReviewId { get; set; }
}
