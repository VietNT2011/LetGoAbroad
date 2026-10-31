using System.Text.Json;
using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;

namespace EnglishHub.Infrastructure.Learning;

public sealed record PathCalendarInput(Guid ClientRequestId, string ExpectedEtag, string TimeZoneId, int[] StudyDays, string Reason, bool CancelPending = false);

public sealed partial class PathChoiceService
{
    /// <summary>VI: Lịch riêng là một lựa chọn có version; giữ prefix ngày học cũ và mọi task đã tạo. EN: A calendar choice preserves the old study-day prefix and all existing task snapshots.</summary>
    public async Task<PathChoiceAck> ScheduleCalendar(Enrollment enrollment, PathCalendarInput input)
    {
        const string scope = "path-calendar"; var hash = Receipts.Hash(input);
        var duplicate = await receipts.Find(enrollment.UserId, scope, input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<PathChoiceAck>(duplicate.ResponseJson)!;
        DomainError.Require(!string.IsNullOrWhiteSpace(input.Reason) && input.Reason.Length <= 500, 422, "invalid_calendar_reason", "Ghi lý do đổi lịch tối đa 500 ký tự.");
        var zone = input.CancelPending ? "" : CanonicalZone(input.TimeZoneId);
        DomainError.Require(input.CancelPending || input.StudyDays is { Length: >= 1 and <= 7 } && input.StudyDays.All(x => x is >= 0 and <= 6), 422, "invalid_calendar_days", "Chọn từ một đến bảy ngày học hợp lệ.");
        var days = input.CancelPending ? "" : string.Join(',', input.StudyDays.Distinct().Order());
        await Initialize(enrollment); var now = clock.UtcNow;
        var active = await ResolveAt(enrollment, now); var latest = await Initialize(enrollment);
        DomainError.Require(active.PathVersionId is not null, 409, "pinned_path_required", "Áp dụng một mẫu trước khi đặt lịch riêng.");
        DomainError.Require(input.ExpectedEtag == Token(enrollment, active, latest), 412, "path_choice_conflict", "Lịch hoặc mẫu đã đổi. Hãy tải lịch sử để so sánh trước khi gửi lại.");
        var pending = latest.EffectiveAt > now;
        DomainError.Require(!pending || latest.PathVersionId == active.PathVersionId, 409, "path_switch_pending", "Hãy giữ mẫu đang học trước khi đặt lịch riêng.");
        PathChoiceRevision revision; bool changed;
        if (input.CancelPending)
        {
            changed = pending && latest.Action == "calendar_change";
            revision = changed ? Snapshot(enrollment, checked(latest.Number + 1), latest.EffectiveAt, "calendar_cancel", input.Reason.Trim(), active.TimeZoneId, active.StudyDays, now) : latest;
        }
        else
        {
            changed = !latest.UsesPathCalendar || latest.TimeZoneId != zone || latest.StudyDays != days;
            var schedulingZone = pending ? latest.SchedulingTimeZoneId : active.TimeZoneId;
            var effective = pending ? latest.EffectiveAt : GoalPlanService.NextMidnight(now, schedulingZone);
            revision = changed ? Snapshot(enrollment, checked(latest.Number + 1), effective, "calendar_change", input.Reason.Trim(), zone, days, now) : latest;
            if (changed)
            {
                // VI: Chỉ phần sau boundary dùng lịch mới; không tính lại quá khứ bằng thứ/ngày mới. EN: Rebase only the future suffix instead of recounting historical weekdays under the new calendar.
                var beforeBoundary = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(effective, active.TimeZoneId).DateTime).AddDays(-1);
                revision.CalendarElapsedDays = checked(active.CalendarElapsedDays + PathScheduler.StudyDay(active.CalendarAnchorOn ?? active.PathStartedOn!.Value, beforeBoundary, active.StudyDays));
                revision.CalendarAnchorOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(effective, zone).DateTime);
                revision.UsesPathCalendar = true;
                revision.SchedulingTimeZoneId = schedulingZone;
            }
        }
        if (changed)
        {
            if (input.CancelPending) revision.SchedulingTimeZoneId = latest.SchedulingTimeZoneId;
            db.PathChoiceRevisions.Add(revision);
            db.OutboxEvents.Add(new OutboxEvent { Type = "learning.path_calendar_scheduled", AggregateId = revision.Id, OccurredAt = now, PayloadJson = JsonSerializer.Serialize(new { userId = enrollment.UserId, revisionId = revision.Id, revision.Action, revision.EffectiveAt, revision.TimeZoneId, revision.StudyDays }) });
            await db.SaveChangesAsync();
        }
        var ack = new PathChoiceAck(revision.Id, revision.PathVersionId, revision.Number, revision.EffectiveAt, changed);
        receipts.Save(enrollment.UserId, scope, input.ClientRequestId, hash, enrollment.Id, ack); return ack;
    }

    private static string CanonicalZone(string? zone)
    {
        DomainError.Require(!string.IsNullOrWhiteSpace(zone) && zone.Length <= 80, 422, "invalid_timezone", "Múi giờ không hợp lệ.");
        try { TimeZoneInfo.FindSystemTimeZoneById(zone!); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new DomainError(422, "invalid_timezone", "Múi giờ không hợp lệ."); }
        return zone == "UTC" ? "UTC" : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone!, out var canonical) ? canonical : zone!;
    }
}
