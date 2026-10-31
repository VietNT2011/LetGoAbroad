namespace EnglishHub.Domain;

/// <summary>VI: Phiên chỉ lưu loại học và đồng hồ server, không lưu văn bản gõ hay URL riêng. EN: Sessions record a study kind and server time, never keystroke contents or private route URLs.</summary>
public sealed class ActivitySession : VersionedEntity
{
    public Guid UserId { get; set; }
    public Guid ClientSessionId { get; set; }
    public string Kind { get; set; } = "lesson";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public int LastSequence { get; set; }
    public bool WasActive { get; set; }
}

public sealed class ActivityHeartbeat
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public int Sequence { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public long CreditedTicks { get; set; }
}

public sealed class ActivityInterval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public long DurationTicks { get; set; }
}

public sealed record StudyRange(DateTimeOffset Start, DateTimeOffset End);

public static class StudyIntervalUnion
{
    /// <summary>VI: Cắt cửa sổ rồi hợp nhất các khoảng UTC; nhiều tab không nhân thời gian. EN: Clip and union elapsed UTC intervals before adding durations across tabs.</summary>
    public static long Ticks(IEnumerable<StudyRange> intervals, DateTimeOffset from, DateTimeOffset to)
    {
        DomainError.Require(from <= to, 422, "invalid_activity_window", "Cửa sổ thời gian không hợp lệ.");
        var ranges = intervals.Select(x => new StudyRange(x.Start < from ? from : x.Start, x.End > to ? to : x.End))
            .Where(x => x.Start < x.End).OrderBy(x => x.Start).ThenBy(x => x.End);
        DateTimeOffset? start = null; var end = from; long total = 0;
        foreach (var range in ranges)
        {
            if (start is null) { start = range.Start; end = range.End; }
            else if (range.Start <= end) { if (range.End > end) end = range.End; }
            else { total += end.UtcTicks - start.Value.UtcTicks; start = range.Start; end = range.End; }
        }
        return total + (start is null ? 0 : end.UtcTicks - start.Value.UtcTicks);
    }
}
