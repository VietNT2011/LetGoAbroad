using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Learning;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>VI: Thời gian phải là union các khoảng thật theo server, không tổng counter của nhiều tab. EN: Verify elapsed server intervals, sequence receipts and multi-tab unions rather than summing tab counters.</summary>
public sealed class StudyActivityTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public void Union_clips_offsets_nested_duplicates_adjacent_ranges_and_gaps_before_rounding()
    {
        var now = DateTimeOffset.Parse("2026-11-18T12:00:00Z");
        StudyRange Range(int first, int last) => new(now.AddSeconds(first), now.AddSeconds(last));
        Assert.Equal(40 * TimeSpan.TicksPerSecond, StudyIntervalUnion.Ticks([Range(-10, 10), Range(0, 10), Range(5, 15), Range(15, 20), Range(30, 60), Range(100, 200), Range(40, 35)], now, now.AddSeconds(50)));
        Assert.Equal(10 * TimeSpan.TicksPerSecond, StudyIntervalUnion.Ticks([new(now.ToOffset(TimeSpan.FromHours(14)), now.AddSeconds(10).ToOffset(TimeSpan.FromHours(-12)))], now, now.AddHours(1)));
        Assert.Equal(10, StudyIntervalUnion.Ticks([new(now, now.AddTicks(5)), new(now.AddTicks(5), now.AddTicks(10))], now, now.AddSeconds(1)));
    }

    [Fact]
    public async Task Session_upsert_is_owner_payload_bound_and_first_active_pulse_does_not_backfill_open_time()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var input = new StartStudySession(Guid.NewGuid(), "lesson"); var session = await Start(learner, input);
        app.Clock.Advance(TimeSpan.FromMinutes(5)); var pulse = await Pulse(learner, session.Id, 1); Assert.Equal(0, pulse.CreditedMilliseconds);
        var current = await Start(learner, input); Assert.Equal(session.Id, current.Id); Assert.Equal(1, current.LastSequence);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync("/api/v1/learning/study-sessions", input with { Kind = "cards" })).StatusCode);
        Assert.Equal(0, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Every_gap_caps_at_thirty_seconds_and_inactive_periods_are_not_backfilled()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1);
        app.Clock.Advance(TimeSpan.FromSeconds(35)); Assert.Equal(30000, (await Pulse(learner, session.Id, 2)).CreditedMilliseconds);
        app.Clock.Advance(TimeSpan.FromSeconds(5)); Assert.Equal(5000, (await Pulse(learner, session.Id, 3, false)).CreditedMilliseconds);
        app.Clock.Advance(TimeSpan.FromHours(2)); Assert.Equal(0, (await Pulse(learner, session.Id, 4)).CreditedMilliseconds);
        app.Clock.Advance(TimeSpan.FromSeconds(15)); Assert.Equal(15000, (await Pulse(learner, session.Id, 5)).CreditedMilliseconds);
        Assert.Equal(50, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Two_tabs_union_their_overlapping_activity_and_never_include_another_owner()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var otherScenario = await Learner(); using var other = otherScenario.Client;
        var first = await Start(learner); var second = await Start(learner); var foreign = await Start(other);
        await Pulse(learner, first.Id, 1); await Pulse(learner, second.Id, 1); await Pulse(other, foreign.Id, 1);
        app.Clock.Advance(TimeSpan.FromSeconds(20)); await Pulse(learner, first.Id, 2); await Pulse(learner, second.Id, 2); await Pulse(other, foreign.Id, 2, false);
        Assert.Equal(20, (await Summary(learner)).EstimatedSeconds);
        app.Clock.Advance(TimeSpan.FromSeconds(10)); await Pulse(learner, first.Id, 3, false); await Pulse(learner, second.Id, 3); Assert.Equal(30, (await Summary(learner)).EstimatedSeconds); Assert.Equal(20, (await Summary(other)).EstimatedSeconds);
        Assert.Equal(30, (await learner.GetFromJsonAsync<StudyTimeSummary>($"/api/v1/learning/study-time?userId={otherScenario.User}"))!.EstimatedSeconds);
    }

    [Fact]
    public async Task Duplicate_and_out_of_order_pulses_do_not_refresh_last_seen_or_credit_twice()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1);
        app.Clock.Advance(TimeSpan.FromSeconds(10)); var second = await Pulse(learner, session.Id, 2);
        app.Clock.Advance(TimeSpan.FromSeconds(5)); Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(4, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(2, false))).StatusCode);
        var replay = await Pulse(learner, session.Id, 2); Assert.Equal(second, replay);
        Assert.Equal(5000, (await Pulse(learner, session.Id, 3)).CreditedMilliseconds); Assert.Equal(15, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Concurrent_exact_retries_have_one_receipt_and_one_interval()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(20));
        var responses = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(2, true)), learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(2, true)));
        Assert.All(responses, response => response.EnsureSuccessStatusCode()); Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(2, await db.ActivityHeartbeats.CountAsync(x => x.SessionId == session.Id)); Assert.Equal(1, await db.ActivityIntervals.CountAsync(x => x.SessionId == session.Id)); Assert.Equal(20, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Concurrent_different_payloads_for_one_sequence_accept_only_one_state()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(20));
        var responses = await Task.WhenAll(learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(2, true)), learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(2, false)));
        Assert.Single(responses, response => response.IsSuccessStatusCode); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); Assert.Equal(20, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Session_expiry_requires_a_new_session_but_old_receipts_still_replay()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var input = new StartStudySession(Guid.NewGuid(), "cards"); var session = await Start(learner, input); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(10)); var previous = await Pulse(learner, session.Id, 2);
        app.Clock.Advance(session.ExpiresAt - app.Clock.UtcNow);
        Assert.Equal(HttpStatusCode.Conflict, (await learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(3, true))).StatusCode); Assert.Equal(previous, await Pulse(learner, session.Id, 2)); Assert.True((await Start(learner, input)).IsExpired);
        var fresh = await Start(learner); Assert.NotEqual(session.Id, fresh.Id); Assert.Equal(0, (await Pulse(learner, fresh.Id, 1)).CreditedMilliseconds);
    }

    [Fact]
    public async Task Backward_clock_never_rewinds_last_seen_or_creates_negative_intervals()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(10)); await Pulse(learner, session.Id, 2);
        app.Clock.Advance(TimeSpan.FromSeconds(-1)); Assert.Equal(0, (await Pulse(learner, session.Id, 3)).CreditedMilliseconds); app.Clock.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(1000, (await Pulse(learner, session.Id, 4)).CreditedMilliseconds); Assert.Equal(11, (await Summary(learner)).EstimatedSeconds);
    }

    [Fact]
    public async Task Rolling_UTC_window_clips_partial_intervals_and_ignores_profile_calendar_changes()
    {
        var now = DateTimeOffset.Parse("2026-11-18T12:00:00Z"); app.Clock.Advance(now.AddDays(-7).AddSeconds(-10) - app.Clock.UtcNow);
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(20)); await Pulse(learner, session.Id, 2, false); app.Clock.Advance(now - app.Clock.UtcNow);
        var seven = await Summary(learner); Assert.Equal(10, seven.EstimatedSeconds); Assert.Equal(now.AddDays(-7), seven.FromUtc); Assert.Equal(20, (await Summary(learner, 30)).EstimatedSeconds);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); var user = await db.Users.SingleAsync(x => x.Id == scenario.User); user.TimeZoneId = "Pacific/Kiritimati"; await db.SaveChangesAsync(); Assert.Equal(seven, await Summary(learner));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000001)]
    public async Task Invalid_sequence_is_rejected_before_state_changes(int sequence)
    {
        var scenario = await Learner(); using var learner = scenario.Client; var session = await Start(learner); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(sequence, true))).StatusCode); Assert.Equal(0, (await learner.GetFromJsonAsync<StudySessionView>($"/api/v1/learning/study-sessions/{session.Id}"))!.LastSequence);
    }

    [Fact]
    public async Task Owner_admin_csrf_and_input_boundaries_apply_to_every_session()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var otherScenario = await Learner(); using var other = otherScenario.Client; using var admin = await app.Login("admin@englishhub.local"); var session = await Start(learner);
        foreach (var outsider in new[] { other, admin }) { Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/learning/study-sessions/{session.Id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(1, true))).StatusCode); }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/learning/study-sessions", new StartStudySession(Guid.Empty, "lesson"))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.PostAsJsonAsync("/api/v1/learning/study-sessions", new StartStudySession(Guid.NewGuid(), "chat"))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await learner.GetAsync("/api/v1/learning/study-time?days=31")).StatusCode);
        learner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await learner.PostAsJsonAsync($"/api/v1/learning/study-sessions/{session.Id}/heartbeat", new StudyPulse(1, true))).StatusCode); using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/learning/study-time")).StatusCode);
    }

    [Fact]
    public async Task Measurement_and_summary_reads_do_not_create_learning_completions_or_awards()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var before = await learner.GetStringAsync("/api/v1/learning/dashboard"); var activity = await learner.GetStringAsync("/api/v1/learning/activity"); var session = await Start(learner); await Pulse(learner, session.Id, 1); app.Clock.Advance(TimeSpan.FromSeconds(10)); await Pulse(learner, session.Id, 2, false);
        var summary = await Summary(learner); Assert.Equal(summary, await Summary(learner)); Assert.Equal(before, await learner.GetStringAsync("/api/v1/learning/dashboard")); Assert.Equal(activity, await learner.GetStringAsync("/api/v1/learning/activity"));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); Assert.Equal(2, await db.ActivityHeartbeats.CountAsync(x => x.UserId == scenario.User)); Assert.Equal(1, await db.ActivityIntervals.CountAsync(x => x.UserId == scenario.User)); Assert.False(await db.OutboxEvents.AnyAsync(x => x.AggregateId == session.Id));
    }

    [Fact]
    public async Task Relational_constraints_reject_overlong_or_foreign_source_intervals()
    {
        var scenario = await Learner(); using var learner = scenario.Client; var other = await Learner(); using var otherClient = other.Client; var session = await Start(learner); await Pulse(learner, session.Id, 1); var now = app.Clock.UtcNow;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.ActivityIntervals.Add(new ActivityInterval { SessionId = session.Id, UserId = scenario.User, Sequence = 1, StartAt = now, EndAt = now.AddSeconds(31), DurationTicks = 31 * TimeSpan.TicksPerSecond }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HubDbContext>(); db.ActivityIntervals.Add(new ActivityInterval { SessionId = session.Id, UserId = other.User, Sequence = 1, StartAt = now, EndAt = now.AddSeconds(1), DurationTicks = TimeSpan.TicksPerSecond }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private sealed record Scenario(HttpClient Client, Guid User);
    private async Task<Scenario> Learner()
    {
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var email = $"study-time-{Guid.NewGuid():N}@example.com"; var user = new AppUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true, DisplayName = "Study-time fixture" };
        Assert.True((await users.CreateAsync(user, DemoSeed.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Learner")).Succeeded); return new(await app.Login(email), user.Id);
    }
    private static async Task<StudySessionView> Start(HttpClient client, StartStudySession? input = null) { var response = await client.PostAsJsonAsync("/api/v1/learning/study-sessions", input ?? new StartStudySession(Guid.NewGuid(), "lesson")); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<StudySessionView>())!; }
    private static async Task<StudyPulseAck> Pulse(HttpClient client, Guid id, int sequence, bool active = true) { var response = await client.PostAsJsonAsync($"/api/v1/learning/study-sessions/{id}/heartbeat", new StudyPulse(sequence, active)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<StudyPulseAck>())!; }
    private static async Task<StudyTimeSummary> Summary(HttpClient client, int days = 7) => (await client.GetFromJsonAsync<StudyTimeSummary>($"/api/v1/learning/study-time?days={days}"))!;
}
