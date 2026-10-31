namespace EnglishHub.Domain;

/// <summary>VI: Snapshot mục tiêu chỉ thêm mới; ngày đang học không bị viết lại. EN: Append-only goal snapshots preserve the plan used for an existing study day.</summary>
public sealed class PlanGoalRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public int Number { get; set; }
    public int TargetScore { get; set; }
    public int MinutesPerDay { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateOnly EffectiveOn { get; set; }
    public string SchedulingTimeZoneId { get; set; } = "";
    public string Reason { get; set; } = "";
}
