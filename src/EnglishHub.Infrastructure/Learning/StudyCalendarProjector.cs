using EnglishHub.Domain;
using NodaTime;
using NodaTime.TimeZones;

namespace EnglishHub.Infrastructure.Learning;

public sealed record StudyCalendarDay(DateOnly Date, decimal EstimatedSeconds);
public sealed record StudyCalendarSnapshot(int PolicyVersion, int WindowDays, string TimeZoneId, DateOnly FromDate, DateOnly ToDate, decimal TotalEstimatedSeconds, int ActiveDays, StudyCalendarDay[] Days);
public sealed record StudyCalendarWindow(DateTimeZone Zone, string TimeZoneId, DateOnly FromDate, DateOnly ToDate, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Days);

/// <summary>VI: Chia union UTC tại mọi đoạn offset cố định trước khi ghép với ngày địa phương. EN: Intersect disjoint UTC activity with constant-offset zone intervals and local dates, including gaps and repeated days.</summary>
public static class StudyCalendarProjector
{
    public static StudyCalendarWindow Window(TimeZoneInfo timeZone, DateTimeOffset now, int days)
    {
        DomainError.Require(days is 7 or 30, 422, "invalid_activity_window", "Chọn khoảng 7 hoặc 30 ngày.");
        var zone = BclDateTimeZone.FromTimeZoneInfo(timeZone);
        var date = Instant.FromDateTimeOffset(now).InZone(zone).Date;
        var today = new DateOnly(date.Year, date.Month, date.Day); var first = today.AddDays(1 - days);
        // VI: Không chuyển local midnight ngược sang UTC vì nó có thể không tồn tại hoặc xảy ra hai lần. EN: A conservative offset bound avoids resolving a skipped or ambiguous local midnight.
        var lower = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).Subtract(zone.MaxOffset.ToTimeSpan());
        return new(zone, timeZone.Id, first, today, lower, now.ToUniversalTime(), days);
    }

    public static StudyCalendarSnapshot Project(IEnumerable<StudyRange> intervals, StudyCalendarWindow window)
    {
        var merged = StudyIntervalUnion.Merge(intervals, window.FromUtc, window.ToUtc);
        var ticks = new long[window.Days];
        if (merged.Length > 0)
        {
            var from = Instant.FromDateTimeOffset(window.FromUtc); var to = Instant.FromDateTimeOffset(window.ToUtc);
            foreach (var segment in window.Zone.GetZoneIntervals(from, to))
            {
                var segmentStart = segment.HasStart && segment.Start > from ? segment.Start.ToDateTimeOffset().UtcTicks : window.FromUtc.UtcTicks;
                var segmentEnd = segment.HasEnd && segment.End < to ? segment.End.ToDateTimeOffset().UtcTicks : window.ToUtc.UtcTicks;
                var offset = segment.WallOffset.ToTimeSpan().Ticks;
                foreach (var range in merged)
                {
                    var start = Math.Max(segmentStart, range.Start.UtcTicks); var end = Math.Min(segmentEnd, range.End.UtcTicks);
                    if (start >= end) continue;
                    // VI: Trong một đoạn offset cố định, ngày chỉ là giao khoảng tuyến tính; không giả định ngày 24 giờ trong UTC. EN: Within one offset segment local dates are linear intersections, without assuming a 24-hour UTC day.
                    for (var day = 0; day < window.Days; day++)
                    {
                        var date = window.FromDate.AddDays(day);
                        var dayStart = date.ToDateTime(TimeOnly.MinValue).Ticks - offset;
                        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue).Ticks - offset;
                        var first = Math.Max(start, dayStart); var last = Math.Min(end, dayEnd);
                        if (last > first) ticks[day] += last - first;
                    }
                }
            }
        }
        var days = ticks.Select((value, index) => new StudyCalendarDay(window.FromDate.AddDays(index), value / (decimal)TimeSpan.TicksPerSecond)).ToArray();
        return new(1, window.Days, window.TimeZoneId, window.FromDate, window.ToDate, days.Sum(x => x.EstimatedSeconds), days.Count(x => x.EstimatedSeconds > 0), days);
    }
}
