using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

public class TimerTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void Elapsed_counts_while_running()
    {
        var session = TestSessions.Started(_clock, minutes: 10);

        _clock.Advance(TimeSpan.FromSeconds(90));

        var snapshot = session.GetSnapshot();
        Assert.Equal(90_000, snapshot.ElapsedMs);
        Assert.Equal(600_000 - 90_000, snapshot.RemainingMs);
        Assert.Equal(600_000, snapshot.PlannedDurationMs);
    }

    [Fact]
    public void Elapsed_does_not_count_before_start()
    {
        var session = TestSessions.Configured(_clock);

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, session.GetSnapshot().ElapsedMs);
    }

    [Fact]
    public void Elapsed_excludes_paused_time()
    {
        var session = TestSessions.Started(_clock, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(2));
        session.Pause();
        _clock.Advance(TimeSpan.FromMinutes(30));
        session.Resume();
        _clock.Advance(TimeSpan.FromMinutes(1));

        var snapshot = session.GetSnapshot();

        Assert.Equal(TimeSpan.FromMinutes(3).TotalMilliseconds, snapshot.ElapsedMs);
        Assert.True(snapshot.IsRunning);
    }

    [Fact]
    public void Snapshot_while_paused_is_frozen_and_not_running()
    {
        var session = TestSessions.Started(_clock, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(2));
        session.Pause();

        var first = session.GetSnapshot();
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = session.GetSnapshot();

        Assert.False(first.IsRunning);
        Assert.Equal(first.ElapsedMs, second.ElapsedMs);
        Assert.Equal(SessionState.Paused, second.State);
    }

    [Fact]
    public void Remaining_is_negative_in_overtime()
    {
        var session = TestSessions.Started(_clock, minutes: 10);

        _clock.Advance(TimeSpan.FromMinutes(12));

        var snapshot = session.GetSnapshot();
        Assert.Equal(-120_000, snapshot.RemainingMs);
    }

    [Fact]
    public void Elapsed_is_frozen_after_finish()
    {
        var session = TestSessions.Started(_clock, minutes: 10);
        _clock.Advance(TimeSpan.FromMinutes(4));
        session.Finish();

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(240_000, session.GetSnapshot().ElapsedMs);
    }

    [Fact]
    public void Snapshot_carries_server_utc_time_from_the_time_provider()
    {
        var session = TestSessions.Started(_clock);
        _clock.Advance(TimeSpan.FromSeconds(30));

        var snapshot = session.GetSnapshot();

        Assert.Equal(_clock.GetUtcNow(), snapshot.ServerTimeUtc);
        Assert.Equal(TimeSpan.Zero, snapshot.ServerTimeUtc.Offset);
    }

    [Fact]
    public void Snapshot_exposes_the_session_name()
    {
        var session = TestSessions.Started(_clock, name: "Demo day");

        Assert.Equal("Demo day", session.GetSnapshot().Name);
    }
}
