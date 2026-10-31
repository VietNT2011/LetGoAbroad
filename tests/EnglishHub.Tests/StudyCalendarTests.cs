using System.Net;
using System.Net.Http.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Ngày địa phương không luôn là24 giờ và không được nhân thời gian khi trùng tab. EN: Verify real local-day boundaries after UTC overlap union, including skipped/repeated dates.</summary>
public sealed class StudyCalendarTests(TestApp app) : IClassFixture<TestApp>
{
    [Theory]
    [InlineData("America/New_York", "2026-03-08T05:00:00Z", "2026-03-09T04:00:00Z", "2026-03-08", 23)]
    [InlineData("America/New_York", "2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z", "2026-11-01", 25)]
    [InlineData("UTC", "2026-11-17T00:00:00Z", "2026-11-18T00:00:00Z", "2026-11-17", 24)]
    [InlineData("Pacific/Kwajalein", "1969-09-29T13:00:00Z", "1969-10-01T12:00:00Z", "1969-09-30", 47)]
    public void Full_day_duration_respects_forward_and_backward_DST(string zone, string first, string last, string date, int hours)
    {
        var start = DateTimeOffset.Parse(first); var end = DateTimeOffset.Parse(last); var data = Project(zone, end.AddDays(1), [new(start, end), new(start.AddMinutes(5), end.AddMinutes(-5))]);
        Assert.Equal(hours * 3600m, data.Days.Single(x => x.Date == DateOnly.Parse(date)).EstimatedSeconds); Assert.Equal(hours * 3600m, data.TotalEstimatedSeconds); Assert.Equal(1, data.ActiveDays);
    }

    [Fact]
    public void Fractional_midnight_splits_preserve_ticks_and_total_without_per_row_rounding()
    {
        var midnight = DateTimeOffset.Parse("2026-11-17T17:00:00Z"); var data = Project("Asia/Ho_Chi_Minh", midnight.AddHours(1), [new(midnight.AddMilliseconds(-600), midnight.AddMilliseconds(600)), new(midnight.AddMilliseconds(-400), midnight.AddMilliseconds(400))]);
        Assert.Equal(.6m, data.Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).EstimatedSeconds); Assert.Equal(.6m, data.Days.Single(x => x.Date == new DateOnly(2026, 11, 18)).EstimatedSeconds); Assert.Equal(1.2m, data.TotalEstimatedSeconds);
        var ticks = Project("Asia/Ho_Chi_Minh", midnight.AddHours(1), [new(midnight.AddTicks(-1), midnight.AddTicks(1))]); Assert.Equal(.0000002m, ticks.TotalEstimatedSeconds); Assert.Equal(2, ticks.ActiveDays);
    }

    [Fact]
    public void A_day_with_a_skipped_midnight_still_receives_all_of_its_actual_elapsed_time()
    {
        var begin = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 8);
        var finish = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2025, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), begin, finish);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/SkippedMidnight", TimeSpan.Zero, "Test only", "Standard", "Daylight", [rule]);
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 8, 0, 0, 0)));
        var data = StudyCalendarProjector.Project([new(DateTimeOffset.Parse("2026-03-08T00:00:00Z"), DateTimeOffset.Parse("2026-03-08T23:00:00Z"))], StudyCalendarProjector.Window(zone, DateTimeOffset.Parse("2026-03-09T12:00:00Z"), 7));
        Assert.Equal(82800m, data.Days.Single(x => x.Date == new DateOnly(2026, 3, 8)).EstimatedSeconds); Assert.Equal(82800m, data.TotalEstimatedSeconds);
    }

    [Fact]
    public void Samoa_skipped_date_is_present_with_zero_without_losing_surrounding_elapsed_time()
    {
        var data = Project("Pacific/Apia", DateTimeOffset.Parse("2012-01-01T12:00:00Z"), [new(DateTimeOffset.Parse("2011-12-29T10:00:00Z"), DateTimeOffset.Parse("2011-12-31T10:00:00Z"))]);
        Assert.Equal(86400m, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 29)).EstimatedSeconds); Assert.Equal(0m, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 30)).EstimatedSeconds); Assert.Equal(86400m, data.Days.Single(x => x.Date == new DateOnly(2011, 12, 31)).EstimatedSeconds); Assert.Equal(172800m, data.TotalEstimatedSeconds);
    }

    [Fact]
    public void A_subminute_return_to_the_previous_date_cannot_hide_a_brief_new_day()
    {
        var startRule = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 1);
        var endRule = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 20), 1, 2);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2025, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), startRule, endRule);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/BriefNewDay", TimeSpan.Zero, "Test only", "Standard", "Daylight", [rule]);
        var now = DateTimeOffset.Parse("2026-01-02T01:00:00Z"); var window = StudyCalendarProjector.Window(zone, now, 7);
        var data = StudyCalendarProjector.Project([new(DateTimeOffset.Parse("2026-01-01T22:59:50Z"), DateTimeOffset.Parse("2026-01-01T23:00:15Z")), new(DateTimeOffset.Parse("2026-01-01T23:00:10Z"), DateTimeOffset.Parse("2026-01-01T23:00:30Z"))], window);
        Assert.Equal(20m, data.Days.Single(x => x.Date == new DateOnly(2026, 1, 1)).EstimatedSeconds); Assert.Equal(20m, data.Days.Single(x => x.Date == new DateOnly(2026, 1, 2)).EstimatedSeconds); Assert.Equal(40m, data.TotalEstimatedSeconds);
    }

    [Fact]
    public void Installed_system_zones_can_partition_a_recent_interval_without_losing_ticks()
    {
        var now = DateTimeOffset.Parse("2026-11-18T12:00:00Z");
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var data = StudyCalendarProjector.Project([new(now.AddSeconds(-30), now)], StudyCalendarProjector.Window(zone, now, 7));
            Assert.True(data.TotalEstimatedSeconds == 30m, $"Unexpected partition in {zone.Id}: {data.TotalEstimatedSeconds}.");
        }
    }

    [Theory]
    [InlineData("Pacific/Kiritimati", "2026-11-18")]
    [InlineData("Etc/GMT+12", "2026-11-16")]
    public void Date_window_clips_the_first_local_midnight_and_future_intervals(string zoneId, string today)
    {
        var now = DateTimeOffset.Parse("2026-11-17T11:30:00Z"); var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); var first = DateOnly.Parse(today).AddDays(-6); var midnight = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), zone));
        var data = Project(zoneId, now, [new(midnight.AddSeconds(-10), midnight.AddSeconds(10)), new(now.AddSeconds(-5), now.AddSeconds(5))]);
        Assert.Equal(DateOnly.Parse(today), data.ToDate); Assert.Equal(first, data.FromDate); Assert.Equal(10m, data.Days[0].EstimatedSeconds); Assert.Equal(5m, data.Days[^1].EstimatedSeconds); Assert.Equal(15m, data.TotalEstimatedSeconds);
    }

    [Fact]
    public async Task Empty_default_and_thirty_day_reads_fill_dates_and_have_no_write_effects()
    {
        var s = await Learner(); using var learner = s.Client; var first = await Read(learner); var longer = await Read(learner, 30); Assert.Equal(7, first.Days.Length); Assert.Equal(30, longer.Days.Length); Assert.Equal(0m, first.TotalEstimatedSeconds); Assert.Equal(0, first.ActiveDays); Assert.Equal(Enumerable.Range(0, 30).Select(x => longer.FromDate.AddDays(x)), longer.Days.Select(x => x.Date));
        Assert.Equal(first, (await Read(learner)) with { Days = first.Days });
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.False(await db.ActivitySessions.AnyAsync(x => x.UserId == s.User)); Assert.False(await db.ActivityIntervals.AnyAsync(x => x.UserId == s.User));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.GetAsync("/api/v1/learning/study-time/calendar?days=31")).StatusCode);
    }

    [Fact]
    public async Task Profile_timezone_rebuckets_real_accepted_intervals_without_rewriting_sources()
    {
        SetNow("2026-11-17T16:59:50Z"); var s = await Learner(); using var learner = s.Client; var session = await Start(learner); await Pulse(learner, session, 1); app.Clock.Advance(TimeSpan.FromSeconds(20)); await Pulse(learner, session, 2, false);
        var utc = await Read(learner); Assert.Equal(20m, utc.Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).EstimatedSeconds);
        Guid interval; DateTimeOffset beforeStart; DateTimeOffset beforeEnd;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var row = await db.ActivityIntervals.SingleAsync(x => x.UserId == s.User); interval = row.Id; beforeStart = row.StartAt; beforeEnd = row.EndAt; var user = await db.Users.SingleAsync(x => x.Id == s.User); user.TimeZoneId = "Asia/Ho_Chi_Minh"; await db.SaveChangesAsync();
        }
        var local = await Read(learner); Assert.Equal("Asia/Ho_Chi_Minh", local.TimeZoneId); Assert.Equal(10m, local.Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).EstimatedSeconds); Assert.Equal(10m, local.Days.Single(x => x.Date == new DateOnly(2026, 11, 18)).EstimatedSeconds); Assert.Equal(20m, local.TotalEstimatedSeconds);
        using var final = app.Services.CreateScope(); var saved = await final.ServiceProvider.GetRequiredService<HubDbContext>().ActivityIntervals.SingleAsync(x => x.Id == interval); Assert.Equal(beforeStart, saved.StartAt); Assert.Equal(beforeEnd, saved.EndAt);
    }

    [Fact]
    public async Task Overlapping_tabs_are_unioned_before_daily_split_and_owner_queries_do_not_leak()
    {
        SetNow("2026-11-17T23:59:50Z"); var s = await Learner(); using var learner = s.Client; var first = await Start(learner); var second = await Start(learner); await Pulse(learner, first, 1); await Pulse(learner, second, 1); app.Clock.Advance(TimeSpan.FromSeconds(20)); await Pulse(learner, first, 2, false); await Pulse(learner, second, 2, false);
        var data = await Read(learner); Assert.Equal(20m, data.TotalEstimatedSeconds); Assert.Equal(2, data.ActiveDays); Assert.Equal(10m, data.Days.Single(x => x.Date == new DateOnly(2026, 11, 17)).EstimatedSeconds); Assert.Equal(data.TotalEstimatedSeconds, data.Days.Sum(x => x.EstimatedSeconds));
        var other = await Learner(); using var outsider = other.Client; using var admin = await app.Login("admin@englishhub.local");
        foreach (var client in new[] { outsider, admin }) Assert.Equal(0m, (await client.GetFromJsonAsync<StudyCalendarSnapshot>($"/api/v1/learning/study-time/calendar?userId={s.User}"))!.TotalEstimatedSeconds);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/study-time/calendar")).StatusCode);
        var rolling = await learner.GetFromJsonAsync<StudyTimeSummary>("/api/v1/learning/study-time"); Assert.Equal(20, rolling!.EstimatedSeconds);
    }

    private static StudyCalendarSnapshot Project(string zone, DateTimeOffset now, StudyRange[] ranges) => StudyCalendarProjector.Project(ranges, StudyCalendarProjector.Window(TimeZoneInfo.FindSystemTimeZoneById(zone), now, 7));
    private void SetNow(string now) => app.Clock.Advance(DateTimeOffset.Parse(now) - app.Clock.UtcNow);
    private static async Task<StudyCalendarSnapshot> Read(HttpClient client, int? days = null) => (await client.GetFromJsonAsync<StudyCalendarSnapshot>("/api/v1/learning/study-time/calendar" + (days is null ? "" : $"?days={days}")))!;
    private static async Task<Guid> Start(HttpClient client) { var response = await client.PostAsJsonAsync("/api/v1/learning/study-sessions", new StartStudySession(Guid.NewGuid(), "cards")); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<StudySessionView>())!.Id; }
    private static async Task Pulse(HttpClient client, Guid id, int sequence, bool active = true) => (await client.PostAsJsonAsync($"/api/v1/learning/study-sessions/{id}/heartbeat", new StudyPulse(sequence, active))).EnsureSuccessStatusCode();
    private sealed record Scenario(HttpClient Client, Guid User);
    private async Task<Scenario> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"calendar-time-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Calendar time fixture", TimeZoneId = "UTC" };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return new(await app.Login(email), user.Id);
    }
}
