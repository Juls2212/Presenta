using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class SessionLifecycleTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void New_session_is_configured_and_not_running()
    {
        var session = TestSessions.Configured(_clock);

        var snapshot = session.GetSnapshot();
        Assert.Equal(SessionState.Configured, snapshot.State);
        Assert.False(snapshot.IsRunning);
        Assert.Equal(0, snapshot.ElapsedMs);
        Assert.Equal(1, snapshot.CurrentSlide);
        Assert.Null(snapshot.Recommendation);
        Assert.Null(snapshot.ActiveAlert);
        Assert.Null(snapshot.FinalState);
    }

    [Fact]
    public void Start_moves_configured_to_running()
    {
        var session = TestSessions.Configured(_clock);

        Assert.True(session.Start().Succeeded);

        Assert.Equal(SessionState.Running, session.State);
        Assert.True(session.GetSnapshot().IsRunning);
    }

    [Fact]
    public void Start_twice_fails_with_invalid_state()
    {
        var session = TestSessions.Started(_clock);

        var result = session.Start();

        Assert.False(result.Succeeded);
        Assert.Equal(SessionErrorCode.InvalidState, result.Error);
    }

    [Fact]
    public void Pause_requires_running_state()
    {
        var session = TestSessions.Configured(_clock);

        Assert.Equal(SessionErrorCode.InvalidState, session.Pause().Error);
        session.Start();
        Assert.True(session.Pause().Succeeded);
        Assert.Equal(SessionState.Paused, session.State);
        Assert.Equal(SessionErrorCode.InvalidState, session.Pause().Error);
    }

    [Fact]
    public void Resume_requires_paused_state()
    {
        var session = TestSessions.Started(_clock);

        Assert.Equal(SessionErrorCode.InvalidState, session.Resume().Error);
        session.Pause();
        Assert.True(session.Resume().Succeeded);
        Assert.Equal(SessionState.Running, session.State);
    }

    [Fact]
    public void Finish_while_running_ends_as_finished_manually()
    {
        var session = TestSessions.Started(_clock);

        Assert.True(session.Finish().Succeeded);

        var snapshot = session.GetSnapshot();
        Assert.Equal(SessionState.Finished, snapshot.State);
        Assert.Equal(FinalState.FinishedManually, snapshot.FinalState);
        Assert.False(snapshot.IsRunning);
    }

    [Fact]
    public void Finish_while_paused_ends_as_ended_while_paused()
    {
        var session = TestSessions.Started(_clock);
        session.Pause();

        Assert.True(session.Finish().Succeeded);

        Assert.Equal(FinalState.EndedWhilePaused, session.GetSnapshot().FinalState);
    }

    [Fact]
    public void Finish_before_start_or_twice_fails_with_invalid_state()
    {
        var session = TestSessions.Configured(_clock);
        Assert.Equal(SessionErrorCode.InvalidState, session.Finish().Error);

        session.Start();
        session.Finish();
        Assert.Equal(SessionErrorCode.InvalidState, session.Finish().Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarkInterrupted_ends_running_or_paused_session_as_interrupted(bool paused)
    {
        var session = TestSessions.Started(_clock);
        if (paused)
        {
            session.Pause();
        }

        Assert.True(session.MarkInterrupted().Succeeded);

        Assert.Equal(SessionState.Finished, session.State);
        Assert.Equal(FinalState.InterruptedByDisconnection, session.GetSnapshot().FinalState);
    }

    [Fact]
    public void MarkInterrupted_fails_when_configured_or_already_finished()
    {
        var session = TestSessions.Configured(_clock);
        Assert.Equal(SessionErrorCode.InvalidState, session.MarkInterrupted().Error);

        session.Start();
        session.Finish();
        Assert.Equal(SessionErrorCode.InvalidState, session.MarkInterrupted().Error);
    }

    [Fact]
    public void Operations_after_finish_fail_with_invalid_state()
    {
        var session = TestSessions.Started(_clock);
        session.Finish();

        Assert.Equal(SessionErrorCode.InvalidState, session.Start().Error);
        Assert.Equal(SessionErrorCode.InvalidState, session.Pause().Error);
        Assert.Equal(SessionErrorCode.InvalidState, session.Resume().Error);
        Assert.Equal(SessionErrorCode.InvalidState, session.Next().Error);
        Assert.Equal(SessionErrorCode.InvalidState, session.Previous().Error);
        Assert.Equal(SessionErrorCode.InvalidState, session.SetSlide(2).Error);
    }

    [Fact]
    public void Tick_does_nothing_unless_running()
    {
        var session = TestSessions.Configured(_clock);
        Assert.Null(session.Tick());

        session.Start();
        session.Pause();
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(session.Tick());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1000, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 0.0001)]
    [InlineData(10, 721)]
    public void Configuration_rejects_values_outside_limits(int slides, double minutes)
    {
        var result = SessionConfiguration.Create(slides, TimeSpan.FromMinutes(minutes));

        Assert.False(result.Succeeded);
        Assert.Equal(SessionErrorCode.InvalidConfiguration, result.Error);
    }

    [Theory]
    [InlineData(1, 0.0167)]
    [InlineData(999, 720)]
    public void Configuration_accepts_values_at_limits(int slides, double minutes)
    {
        var result = SessionConfiguration.Create(slides, TimeSpan.FromMinutes(minutes));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configuration_uses_neutral_fallback_name_when_name_is_missing(string? name)
    {
        var result = SessionConfiguration.Create(10, TimeSpan.FromMinutes(10), name);

        Assert.Equal(SessionLimits.DefaultSessionName, result.Value!.Name);
    }

    [Fact]
    public void Configuration_trims_name_and_rejects_names_that_are_too_long()
    {
        var trimmed = SessionConfiguration.Create(10, TimeSpan.FromMinutes(10), "  Demo  ");
        Assert.Equal("Demo", trimmed.Value!.Name);

        var tooLong = SessionConfiguration.Create(10, TimeSpan.FromMinutes(10), new string('x', SessionLimits.MaxNameLength + 1));
        Assert.Equal(SessionErrorCode.InvalidConfiguration, tooLong.Error);

        var atLimit = SessionConfiguration.Create(10, TimeSpan.FromMinutes(10), new string('x', SessionLimits.MaxNameLength));
        Assert.True(atLimit.Succeeded);
    }
}
