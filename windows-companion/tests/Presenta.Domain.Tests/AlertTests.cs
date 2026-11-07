using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class AlertTests
{
    private readonly FakeTimeProvider _clock = new();

    private static TimeSpan Minutes(double value) => TimeSpan.FromMinutes(value);

    // ---- thresholds ----

    [Theory]
    [InlineData(20, 5, 1)]
    [InlineData(10, 3, 1)]
    [InlineData(2, 0.6, 0.6)]
    public void Time_thresholds_follow_the_documented_formulas(double plannedMinutes, double runningOutMinutes, double criticalMinutes)
    {
        var options = new SessionOptions();
        var planned = Minutes(plannedMinutes);

        Assert.Equal(Minutes(runningOutMinutes), options.GetTimeRunningOutThreshold(planned));
        Assert.Equal(Minutes(criticalMinutes), options.GetTimeCriticalThreshold(planned));
    }

    // ---- time alerts ----

    [Fact]
    public void No_alert_is_raised_before_the_running_out_threshold()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);

        _clock.Advance(Minutes(15) - TimeSpan.FromSeconds(1));

        Assert.Null(session.Tick());
        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    [Fact]
    public void Time_running_out_fires_once_when_remaining_reaches_five_minutes()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);

        _clock.Advance(Minutes(15));
        var alert = session.Tick();

        Assert.NotNull(alert);
        Assert.Equal(AlertKind.TimeRunningOut, alert.Kind);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(300_000, alert.RemainingMs);
        Assert.Equal(900_000, alert.RaisedAtElapsedMs);
        Assert.Equal(alert, session.GetSnapshot().ActiveAlert);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(session.Tick());
    }

    [Fact]
    public void Time_critical_fires_once_at_one_minute_remaining_and_replaces_the_active_alert()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(15));
        session.Tick();

        _clock.Advance(Minutes(4));
        var alert = session.Tick();

        Assert.NotNull(alert);
        Assert.Equal(AlertKind.TimeCritical, alert.Kind);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(60_000, alert.RemainingMs);
        Assert.Equal(AlertKind.TimeCritical, session.GetSnapshot().ActiveAlert?.Kind);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(session.Tick());
    }

    [Fact]
    public void Overtime_fires_once_when_remaining_becomes_negative()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(15));
        session.Tick();
        _clock.Advance(Minutes(4));
        session.Tick();

        _clock.Advance(Minutes(1)); // remaining is exactly zero: not overtime yet
        Assert.Null(session.Tick());

        _clock.Advance(TimeSpan.FromSeconds(1));
        var alert = session.Tick();

        Assert.NotNull(alert);
        Assert.Equal(AlertKind.Overtime, alert.Kind);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(-1_000, alert.RemainingMs);

        _clock.Advance(Minutes(5));
        Assert.Null(session.Tick());
    }

    [Fact]
    public void Jumping_past_several_thresholds_raises_only_the_most_severe_alert_once()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);

        _clock.Advance(Minutes(19.5));
        var alert = session.Tick();

        Assert.Equal(AlertKind.TimeCritical, alert?.Kind);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(session.Tick());
    }

    [Fact]
    public void Jumping_straight_into_overtime_raises_only_overtime()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);

        _clock.Advance(Minutes(25));

        Assert.Equal(AlertKind.Overtime, session.Tick()?.Kind);
        Assert.Null(session.Tick());
    }

    [Fact]
    public void Short_plans_raise_time_critical_when_both_thresholds_coincide()
    {
        var session = TestSessions.Started(_clock, minutes: 2, options: TestSessions.NoPaceAlerts);

        _clock.Advance(TimeSpan.FromSeconds(84)); // 36 s remaining
        var alert = session.Tick();

        Assert.Equal(AlertKind.TimeCritical, alert?.Kind);
    }

    [Fact]
    public void Time_alerts_do_not_fire_while_paused()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(14));
        session.Pause();

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(session.Tick());
        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    // ---- acknowledgment ----

    [Fact]
    public void Acknowledging_clears_the_alert_and_it_is_not_raised_again()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(15));
        session.Tick();

        session.AcknowledgeAlert();

        Assert.Null(session.GetSnapshot().ActiveAlert);
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(session.Tick());
        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    [Fact]
    public void Later_time_alerts_still_fire_after_an_earlier_one_was_acknowledged()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(15));
        session.Tick();
        session.AcknowledgeAlert();

        _clock.Advance(Minutes(4));

        Assert.Equal(AlertKind.TimeCritical, session.Tick()?.Kind);
    }

    [Fact]
    public void Acknowledging_without_an_active_alert_is_harmless()
    {
        var session = TestSessions.Started(_clock);

        session.AcknowledgeAlert();
        session.AcknowledgeAlert();

        Assert.Null(session.GetSnapshot().ActiveAlert);
        Assert.Equal(SessionState.Running, session.State);
    }

    [Fact]
    public void Acknowledging_before_the_session_starts_is_harmless()
    {
        var session = TestSessions.Configured(_clock);

        session.AcknowledgeAlert();

        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    // ---- alerts never block commands ----

    [Fact]
    public void An_active_alert_does_not_block_slide_commands()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(21));
        Assert.NotNull(session.Tick());

        var next = session.Next();
        var previous = session.Previous();
        var set = session.SetSlide(9);

        Assert.True(next.Succeeded && next.CounterChanged);
        Assert.True(previous.Succeeded && previous.CounterChanged);
        Assert.True(set.Succeeded && set.CounterChanged);
        Assert.NotNull(session.GetSnapshot().ActiveAlert);
    }

    [Fact]
    public void Finishing_clears_the_active_alert()
    {
        var session = TestSessions.Started(_clock, options: TestSessions.NoPaceAlerts);
        _clock.Advance(Minutes(21));
        session.Tick();

        session.Finish();

        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    // ---- pace alerts ----

    [Fact]
    public void Behind_schedule_fires_when_the_presenter_falls_behind_after_the_warm_up()
    {
        var session = TestSessions.Started(_clock);

        _clock.Advance(Minutes(2));
        Assert.Null(session.Tick()); // deviation -1.9, within tolerance of 2

        _clock.Advance(Minutes(1));
        var alert = session.Tick(); // expected slide 3.85, deviation -2.85

        Assert.NotNull(alert);
        Assert.Equal(AlertKind.BehindSchedule, alert.Kind);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(-3, alert.SlideDeviation);
        Assert.Equal(19, alert.SlidesRemaining);
    }

    [Fact]
    public void Behind_schedule_fires_once_per_transition()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        Assert.NotNull(session.Tick());

        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(session.Tick());
    }

    [Fact]
    public void Too_fast_fires_when_the_presenter_jumps_ahead_of_schedule()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(2));
        Assert.Null(session.Tick());

        session.SetSlide(10); // expected slide 2.9, deviation +7.1

        var active = session.GetSnapshot().ActiveAlert;
        Assert.NotNull(active);
        Assert.Equal(AlertKind.TooFast, active.Kind);
        Assert.Equal(AlertSeverity.Warning, active.Severity);
        Assert.Equal(7, active.SlideDeviation);
    }

    [Fact]
    public void Pace_alerts_do_not_fire_during_the_warm_up()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(TimeSpan.FromSeconds(30));

        session.SetSlide(15);

        Assert.Null(session.GetSnapshot().ActiveAlert);
        Assert.Null(session.Tick());
    }

    [Fact]
    public void A_new_pace_alert_replaces_the_active_one()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        session.Tick();
        Assert.Equal(AlertKind.BehindSchedule, session.GetSnapshot().ActiveAlert?.Kind);

        session.SetSlide(14);

        Assert.Equal(AlertKind.TooFast, session.GetSnapshot().ActiveAlert?.Kind);
    }

    [Fact]
    public void A_pace_alert_clears_when_the_presenter_recovers()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        session.Tick();

        session.SetSlide(4);

        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    [Fact]
    public void Acknowledged_pace_alert_is_raised_again_only_after_the_cooldown()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        Assert.NotNull(session.Tick());
        _clock.Advance(TimeSpan.FromSeconds(1));
        session.AcknowledgeAlert();

        _clock.Advance(TimeSpan.FromSeconds(118)); // 4:59, one second short of the two minute cooldown
        Assert.Null(session.Tick());

        _clock.Advance(TimeSpan.FromSeconds(1)); // 5:00
        var again = session.Tick();

        Assert.NotNull(again);
        Assert.Equal(AlertKind.BehindSchedule, again.Kind);
    }

    [Fact]
    public void Unacknowledged_pace_alert_is_not_raised_again()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        Assert.NotNull(session.Tick());

        _clock.Advance(Minutes(7));

        Assert.Null(session.Tick());
    }

    [Fact]
    public void Acknowledged_pace_alert_is_not_raised_again_when_the_presenter_is_back_on_pace()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(3));
        session.Tick();
        session.AcknowledgeAlert();
        session.SetSlide(4);

        _clock.Advance(Minutes(2)); // 5:00, expected slide 5.75, slide 4 is within tolerance

        Assert.Null(session.Tick());
    }

    [Fact]
    public void Cooldown_is_configurable()
    {
        var options = new SessionOptions { PaceAlertCooldown = TimeSpan.FromSeconds(30) };
        var session = TestSessions.Started(_clock, options: options);
        _clock.Advance(Minutes(3));
        session.Tick();
        session.AcknowledgeAlert();

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(AlertKind.BehindSchedule, session.Tick()?.Kind);
    }

    [Fact]
    public void Pace_alerts_are_suppressed_once_the_critical_time_threshold_is_reached()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(Minutes(18));
        session.SetSlide(17); // on track at 18:00; first evaluation also raises time running out
        _clock.Advance(Minutes(1) + TimeSpan.FromSeconds(10)); // 19:10, 50 s remaining
        Assert.Equal(AlertKind.TimeCritical, session.Tick()?.Kind);
        session.AcknowledgeAlert();
        session.SetSlide(20); // back on track
        Assert.Null(session.GetSnapshot().ActiveAlert);

        session.SetSlide(15); // far behind, but the critical threshold has been reached

        Assert.Null(session.GetSnapshot().ActiveAlert);
    }

    [Fact]
    public void A_time_alert_raised_in_the_same_evaluation_takes_precedence_over_a_pace_alert()
    {
        var session = TestSessions.Started(_clock);

        _clock.Advance(Minutes(15)); // behind and 5 minutes remaining at the same time
        var alert = session.Tick();

        Assert.Equal(AlertKind.TimeRunningOut, alert?.Kind);
    }
}
