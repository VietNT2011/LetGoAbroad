namespace EnglishHub.Domain;

/// <summary>VI: Enrollment duy nhất cho mỗi người trong bản đầu. EN: One active learning enrollment per user in the initial release.</summary>
public sealed class Enrollment : VersionedEntity
{
    public Guid UserId { get; set; }
    public string Level { get; set; } = "foundation";
    public int TargetScore { get; set; }
    public int MinutesPerDay { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int RuleVersion { get; set; } = 1;
    public int PlanRevision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? PathVersionId { get; set; }
    public PathVersion? PathVersion { get; set; }
    public Guid? PlacementAttemptId { get; set; }
    public string SelectionReason { get; set; } = "legacy";
    public DateOnly? PathStartedOn { get; set; }
    public string? PathTimeZoneId { get; set; }
    public string? PathStudyDays { get; set; }
}
public sealed class DailyTask : VersionedEntity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public Guid UserId { get; set; }
    public DateOnly LocalDate { get; set; }
    public int SlotKey { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public int EstimatedMinutes { get; set; }
    public string ReasonCode { get; set; } = "FOUNDATION_NEXT";
    public string ReasonText { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? PathVersionId { get; set; }
    public Guid? PathNodeId { get; set; }
    public PathNode? PathNode { get; set; }
    public Guid? SourceTaskId { get; set; }
    public string? TimeZoneId { get; set; }
    public Guid? GoalRevisionId { get; set; }
    public PlanGoalRevision? GoalRevision { get; set; }
}
public sealed class LessonProgress : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ContentVersionId { get; set; }
    public ContentVersion ContentVersion { get; set; } = null!;
    public DateTimeOffset CompletedAt { get; set; }
}
public sealed class TaskCompletion
{
    public Guid DailyTaskId { get; set; }
    public DailyTask DailyTask { get; set; } = null!;
    public Guid? LessonProgressId { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public DateOnly LocalDate { get; set; }
    public string TimeZoneId { get; set; } = "";
}
