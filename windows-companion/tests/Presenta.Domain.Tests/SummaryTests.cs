using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class SummaryTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void Summary_is_not_available_before_the_session_finishes()
    {
        var session = TestSessions.Started(_clock);

        var result = session.BuildSummary();

        Assert.False(result.Succeeded);
        Assert.Equal(SessionErrorCode.InvalidState, result.Error);
    }

    [Fact]
    public void Summary_of_a_manual_finish_within_the_planned_time()
    {
        var session = TestSessions.Started(_clock, slides: 20, minutes: 15, name: "Demo");
        _clock.Advance(new TimeSpan(0, 14, 32));
        session.SetSlide(20);
        session.Finish();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal("Demo", summary.Name);
        Assert.Equal(872_000, summary.ActualDurationMs);
        Assert.Equal(900_000, summary.PlannedDurationMs);
        Assert.Equal(20, summary.PlannedSlides);
        Assert.Equal(20, summary.RegisteredSlides);
        Assert.Equal(20 / (872.0 / 60), summary.AverageSlidesPerMinute, 10);
        Assert.Equal(FinalState.FinishedManually, summary.FinalState);
        Assert.True(summary.EndedWithinPlannedTime);
        Assert.Equal(_clock.GetUtcNow(), summary.FinishedAtUtc);
    }

    [Fact]
    public void Summary_flags_a_session_that_ran_over_time()
    {
        var session = TestSessions.Started(_clock, slides: 10, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(11));
        session.Finish();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.False(summary.EndedWithinPlannedTime);
        Assert.Equal(660_000, summary.ActualDurationMs);
    }

    [Fact]
    public void Summary_counts_finishing_exactly_at_the_planned_time_as_within_time()
    {
        var session = TestSessions.Started(_clock, slides: 10, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(10));
        session.Finish();

        Assert.True(session.BuildSummary().Value!.EndedWithinPlannedTime);
    }

    [Fact]
    public void Summary_of_a_session_finished_while_paused_excludes_the_paused_time()
    {
        var session = TestSessions.Started(_clock, slides: 10, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(4));
        session.Pause();
        _clock.Advance(TimeSpan.FromMinutes(30));
        session.Finish();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal(FinalState.EndedWhilePaused, summary.FinalState);
        Assert.Equal(240_000, summary.ActualDurationMs);
        Assert.True(summary.EndedWithinPlannedTime);
    }

    [Fact]
    public void Summary_of_an_interrupted_session_reports_the_interruption()
    {
        var session = TestSessions.Started(_clock, slides: 10, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(3));
        session.SetSlide(4);
        session.MarkInterrupted();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal(FinalState.InterruptedByDisconnection, summary.FinalState);
        Assert.Equal(4, summary.RegisteredSlides);
        Assert.Equal(180_000, summary.ActualDurationMs);
    }

    [Fact]
    public void Summary_average_pace_is_zero_for_a_tiny_elapsed_time()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(TimeSpan.FromSeconds(2));
        session.Finish();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal(2_000, summary.ActualDurationMs);
        Assert.Equal(0, summary.AverageSlidesPerMinute);
    }

    [Fact]
    public void Summary_average_pace_is_available_from_five_seconds()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(TimeSpan.FromSeconds(5));
        session.Finish();

        Assert.Equal(12, session.BuildSummary().Value!.AverageSlidesPerMinute, 10);
    }

    [Fact]
    public void Finishing_immediately_gives_a_zero_duration_summary()
    {
        var session = TestSessions.Started(_clock);
        session.Finish();

        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal(0, summary.ActualDurationMs);
        Assert.Equal(0, summary.AverageSlidesPerMinute);
        Assert.Equal(1, summary.RegisteredSlides);
    }

    [Fact]
    public void Snapshot_of_a_finished_session_is_consistent_with_its_summary()
    {
        var session = TestSessions.Started(_clock, slides: 10, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(5));
        session.SetSlide(8);
        session.Finish();

        var snapshot = session.GetSnapshot();
        var summary = session.BuildSummary().Value;

        Assert.NotNull(summary);
        Assert.Equal(snapshot.ElapsedMs, summary.ActualDurationMs);
        Assert.Equal(snapshot.CurrentSlide, summary.RegisteredSlides);
        Assert.Equal(snapshot.FinalState, summary.FinalState);
        Assert.Equal(snapshot.AverageSlidesPerMinute, summary.AverageSlidesPerMinute);
    }
}
