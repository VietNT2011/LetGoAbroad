namespace EnglishHub.Domain;

/// <summary>VI: Snapshot lựa chọn chỉ thêm mới; bảo toàn lý do và placement gốc. EN: Append-only choices retain original selection and placement evidence.</summary>
public sealed class PathChoiceRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public Guid UserId { get; set; }
    public int Number { get; set; }
    public Guid? PathVersionId { get; set; }
    public PathVersion? PathVersion { get; set; }
    public string Level { get; set; } = "foundation";
    public Guid? PlacementAttemptId { get; set; }
    public DateOnly? PathStartedOn { get; set; }
    public string TimeZoneId { get; set; } = "";
    public string StudyDays { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    // VI: Ghi một lần khi được áp dụng; yêu cầu bị thay thế không có mốc này. EN: Record actual activation once so superseded reservations cannot masquerade as a previously used calendar.
    public DateTimeOffset? ActivatedAt { get; set; }
    public string SchedulingTimeZoneId { get; set; } = "";
    public string Action { get; set; } = "";
    public string SelectionReason { get; set; } = "";
    public string UserReason { get; set; } = "";
}
