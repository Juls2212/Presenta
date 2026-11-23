using Presenta.Domain.Tests.Support;

namespace Presenta.Domain.Tests;

/// <summary>
/// The exact second at which each alert fires for example sessions under the default rules, with a one-second tick
/// (what the companion does). These numbers are the ones in the scripted scenario table of the manual test guide:
/// change them here and there together.
/// </summary>
public class AlertTimingTableTests
{
    private readonly FakeTimeProvider _clock = new();

    private sealed record Fired(int Second, AlertKind Kind, AlertSeverity Severity, int Sequence);

    /// <summary>
    /// Runs a session second by second. <paramref name="jumpTo"/> moves to a slide at a given second (after the tick of
    /// that second); <paramref name="acknowledgeAt"/> acknowledges the active alert at a given second.
    /// </summary>
    private List<Fired> Run(
        int slides,
        double minutes,
        int horizonSeconds,
        IReadOnlyDictionary<int, int>? jumpTo = null,
        ISet<int>? acknowledgeAt = null)
    {
        var session = TestSessions.Started(_clock, slides, minutes);
        var fired = new List<Fired>();
        var lastSequence = 0;

        void Observe(int second)
        {
            var alert = session.GetSnapshot().ActiveAlert;
            if (alert is not null && alert.Sequence > lastSequence)
            {
                lastSequence = alert.Sequence;
                fired.Add(new Fired(second, alert.Kind, alert.Severity, alert.Sequence));
            }
        }

        Observe(0);
        for (var second = 1; second <= horizonSeconds; second++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            session.Tick();
            Observe(second);
            if (jumpTo is not null && jumpTo.TryGetValue(second, out var slide))
            {
                Assert.True(session.SetSlide(slide).Succeeded);
                Observe(second);
            }

            if (acknowledgeAt is not null && acknowledgeAt.Contains(second))
            {
                session.AcknowledgeAlert();
            }
        }

        return fired;
    }

    private static void AssertFired(IReadOnlyList<Fired> actual, params (int Second, AlertKind Kind, AlertSeverity Severity)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Second, actual[i].Second);
            Assert.Equal(expected[i].Kind, actual[i].Kind);
            Assert.Equal(expected[i].Severity, actual[i].Severity);
            Assert.Equal(i + 1, actual[i].Sequence); // 1, 2, 3 in order of firing
        }
    }

    // ---- a presenter who stays on slide 1 and never acknowledges ----

    [Fact]
    public void One_minute_five_slides_standing_still()
    {
        // planned 60 s: both time thresholds are 18 s (30% of 60 s), so only the critical alert fires, at 42 s.
        // warm-up 6 s, tolerance 1 slide: behind once the expected slide passes 2.0, which happens at 16 s.
        AssertFired(
            Run(slides: 5, minutes: 1, horizonSeconds: 70),
            (16, AlertKind.BehindSchedule, AlertSeverity.Warning),
            (42, AlertKind.TimeCritical, AlertSeverity.Critical),
            (61, AlertKind.Overtime, AlertSeverity.Critical));
    }

    [Fact]
    public void Two_minutes_ten_slides_standing_still()
    {
        // planned 120 s: both thresholds are 36 s, so critical fires at 84 s. warm-up 12 s, tolerance 1: behind at 14 s.
        AssertFired(
            Run(slides: 10, minutes: 2, horizonSeconds: 130),
            (14, AlertKind.BehindSchedule, AlertSeverity.Warning),
            (84, AlertKind.TimeCritical, AlertSeverity.Critical),
            (121, AlertKind.Overtime, AlertSeverity.Critical));
    }

    [Fact]
    public void Fifteen_minutes_twenty_slides_standing_still()
    {
        // planned 900 s: running out at 270 s left (630 s), critical at 60 s left (840 s), overtime at 901 s.
        // warm-up 60 s, tolerance 2 slides: behind once the expected slide passes 3.0, at 95 s.
        AssertFired(
            Run(slides: 20, minutes: 15, horizonSeconds: 910),
            (95, AlertKind.BehindSchedule, AlertSeverity.Warning),
            (630, AlertKind.TimeRunningOut, AlertSeverity.Warning),
            (840, AlertKind.TimeCritical, AlertSeverity.Critical),
            (901, AlertKind.Overtime, AlertSeverity.Critical));
    }

    // ---- a presenter who rushes ----

    [Fact]
    public void One_minute_five_slides_jumping_to_the_last_slide_at_ten_seconds()
    {
        AssertFired(
            Run(slides: 5, minutes: 1, horizonSeconds: 20, jumpTo: new Dictionary<int, int> { [10] = 5 }),
            (10, AlertKind.TooFast, AlertSeverity.Warning));
    }

    [Fact]
    public void Two_minutes_ten_slides_jumping_to_the_last_slide_at_thirteen_seconds()
    {
        // 13 s is after the 12 s warm-up and one second before "behind" would have fired for a presenter standing still.
        AssertFired(
            Run(slides: 10, minutes: 2, horizonSeconds: 30, jumpTo: new Dictionary<int, int> { [13] = 10 }),
            (13, AlertKind.TooFast, AlertSeverity.Warning));
    }

    [Fact]
    public void Fifteen_minutes_twenty_slides_jumping_to_the_last_slide_at_one_minute()
    {
        AssertFired(
            Run(slides: 20, minutes: 15, horizonSeconds: 70, jumpTo: new Dictionary<int, int> { [60] = 20 }),
            (60, AlertKind.TooFast, AlertSeverity.Warning));
    }

    // ---- acknowledging and the pace cooldown ----

    [Fact]
    public void An_acknowledged_pace_alert_fires_again_after_the_two_minute_cooldown_with_a_new_sequence()
    {
        // 15 min / 20 slides standing still; "Entendido" the moment each alert appears: it repeats 120 s later (215 s, 335 s).
        var fired = Run(slides: 20, minutes: 15, horizonSeconds: 340, acknowledgeAt: new HashSet<int> { 95, 215, 335 });

        Assert.Equal(
            new[] { (95, AlertKind.BehindSchedule, 1), (215, AlertKind.BehindSchedule, 2), (335, AlertKind.BehindSchedule, 3) },
            fired.Take(3).Select(f => (f.Second, f.Kind, f.Sequence)).ToArray());
    }

    [Fact]
    public void An_unacknowledged_pace_alert_does_not_repeat()
    {
        var fired = Run(slides: 20, minutes: 15, horizonSeconds: 600);

        Assert.Single(fired);
        Assert.Equal(95, fired[0].Second);
    }

    [Fact]
    public void Sequences_start_at_one_for_every_session()
    {
        var first = Run(slides: 5, minutes: 1, horizonSeconds: 20);
        var second = Run(slides: 5, minutes: 1, horizonSeconds: 20);

        Assert.Equal(1, first[0].Sequence);
        Assert.Equal(1, second[0].Sequence);
    }
}
