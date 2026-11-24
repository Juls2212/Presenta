using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Presenta.Ai;
using Presenta.Domain;
using Presenta.Server;
using Presenta.TestSupport;

namespace Presenta.Ai.Tests;

/// <summary>ADR-81: the first request of an alert starts at once, a text that arrives during its alert is shown, texts are reused for a bounded time.</summary>
public class LatencyTests
{
    private const string PrivateName = "Quarterly secret.pptx";
    private const string Answer = "Pick up the pace now";

    private static SessionSnapshot Snapshot(
        int sequence = 1,
        AlertKind? alert = AlertKind.TimeRunningOut,
        RecommendationCode code = RecommendationCode.SpeedUpSlightly,
        int slidesRemaining = 9,
        long remainingMs = 300_000) =>
        new(
            SessionState.Running,
            PrivateName,
            11,
            20,
            900_000,
            900_000 - remainingMs,
            remainingMs,
            true,
            new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero),
            0.55,
            PaceStatus.Behind,
            slidesRemaining,
            1.1,
            null,
            alert is null ? null : new Alert(alert.Value, AlertSeverity.Warning, 600_000, remainingMs, slidesRemaining, -2, sequence),
            new Recommendation(code, slidesRemaining, remainingMs));

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Capture(Lines);

        public void Dispose()
        {
        }

        private sealed class Capture : ILogger
        {
            private readonly ConcurrentQueue<string> _lines;

            public Capture(ConcurrentQueue<string> lines) => _lines = lines;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _lines.Enqueue(formatter(state, exception));
        }
    }

    private sealed record Rig(AiRecommendationEnricher Ai, FakeAiTextClient Client, ManualTimeProvider Time, CapturingLoggerFactory Logs);

    private static Rig Make(Func<AiEnrichmentOptions, AiEnrichmentOptions>? configure = null)
    {
        var options = new AiEnrichmentOptions();
        if (configure is not null)
        {
            options = configure(options);
        }

        var time = new ManualTimeProvider();
        var client = new FakeAiTextClient();
        var logs = new CapturingLoggerFactory();
        var ai = new AiRecommendationEnricher(client, AiAvailability.Available, options, time, logs) { Enabled = true };
        return new Rig(ai, client, time, logs);
    }

    private static async Task<Recommendation?> AskAsync(Rig rig, SessionSnapshot snapshot)
    {
        rig.Ai.Apply(snapshot);
        await rig.Ai.WhenIdleAsync();
        return rig.Ai.Apply(snapshot);
    }

    // ---- the first request of an alert sequence ----

    [Fact]
    public async Task A_new_alert_sequence_asks_at_once_even_when_the_last_request_was_a_second_ago()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Ai.Apply(Snapshot(sequence: 2, slidesRemaining: 4));
        await rig.Ai.WhenIdleAsync();

        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task Later_requests_of_the_same_sequence_still_wait_for_the_spacing()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Ai.Apply(Snapshot(sequence: 1, slidesRemaining: 4));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(1, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromSeconds(14));
        rig.Ai.Apply(Snapshot(sequence: 1, slidesRemaining: 4));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task The_exemption_does_not_lift_the_cap_which_counts_the_first_request()
    {
        var rig = Make(o => o with { MaxCallsPerSession = 2 });

        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));
        await AskAsync(rig, Snapshot(sequence: 2, slidesRemaining: 4));
        await AskAsync(rig, Snapshot(sequence: 3, slidesRemaining: 2));

        Assert.Equal(2, rig.Client.Calls);
        Assert.Equal(2, rig.Ai.CallsThisSession);
    }

    [Fact]
    public async Task The_exemption_does_not_skip_the_breaker()
    {
        var rig = Make(o => o with { FailureThreshold = 1, PauseDuration = TimeSpan.FromSeconds(60) });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.ServerError);

        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));
        Assert.Equal(AiStatus.Paused, rig.Ai.Status);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await AskAsync(rig, Snapshot(sequence: 2, slidesRemaining: 4));
        Assert.Equal(1, rig.Client.Calls); // still paused: the failure of the first request counted

        rig.Time.Advance(TimeSpan.FromSeconds(60));
        await AskAsync(rig, Snapshot(sequence: 3, slidesRemaining: 2));
        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task A_refused_key_still_pauses_the_next_alert_sequence()
    {
        var rig = Make();
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.Unauthorized);

        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));
        await AskAsync(rig, Snapshot(sequence: 2, slidesRemaining: 4));

        Assert.Equal(1, rig.Client.Calls);
        Assert.Equal(AiStatus.InvalidKey, rig.Ai.Status);
    }

    // ---- delivery ----

    [Fact]
    public async Task A_text_that_arrives_during_its_alert_is_announced_at_once_and_shown_even_when_the_figures_drifted()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.FromMinutes(10) });
        rig.Client.Hold = new TaskCompletionSource();
        var ready = 0;
        rig.Ai.TextReady += (_, _) => Interlocked.Increment(ref ready);

        rig.Ai.Apply(Snapshot(sequence: 5, remainingMs: 330_000)); // 5.5 minutes: rounds to 6
        rig.Time.Advance(TimeSpan.FromSeconds(20));
        rig.Ai.Apply(Snapshot(sequence: 5, remainingMs: 310_000)); // the same alert, now 5 minutes: another key
        rig.Client.Hold.SetResult();
        await rig.Ai.WhenIdleAsync();

        Assert.Equal(1, Volatile.Read(ref ready));
        var shown = rig.Ai.Apply(Snapshot(sequence: 5, remainingMs: 309_000));
        Assert.Equal(Answer, shown!.FreeText);
        Assert.Equal(RecommendationCode.SpeedUpSlightly, shown.Code); // the deterministic recommendation is untouched
        Assert.Equal(TimeSpan.FromSeconds(20), rig.Ai.LastLatency);
    }

    [Fact]
    public async Task A_text_that_arrives_after_its_alert_ended_is_cached_and_not_shown_for_another_situation()
    {
        var rig = Make();
        rig.Client.Hold = new TaskCompletionSource();

        rig.Ai.Apply(Snapshot(sequence: 1, slidesRemaining: 9));
        rig.Ai.Apply(Snapshot(alert: null)); // the alert ended
        rig.Client.Hold.SetResult();
        await rig.Ai.WhenIdleAsync();

        // A different situation (another code and alert) in a new sequence is not given the old text.
        var other = rig.Ai.Apply(Snapshot(sequence: 2, alert: AlertKind.TimeCritical, code: RecommendationCode.WrapUp, slidesRemaining: 3, remainingMs: 60_000));
        Assert.Null(other!.FreeText);
        await rig.Ai.WhenIdleAsync();

        // The same situation in a new sequence does get it, from the cache, without a new request for it.
        var callsBefore = rig.Client.Calls;
        var same = rig.Ai.Apply(Snapshot(sequence: 3, slidesRemaining: 9));
        Assert.Equal(Answer, same!.FreeText);
        Assert.Equal(callsBefore, rig.Client.Calls);
    }

    [Fact]
    public async Task A_late_text_is_not_carried_to_the_next_sequence_of_a_different_situation()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot(sequence: 1, slidesRemaining: 9));

        var next = rig.Ai.Apply(Snapshot(sequence: 2, code: RecommendationCode.SlowDown, slidesRemaining: 3, remainingMs: 600_000));

        Assert.Null(next!.FreeText);
    }

    // ---- reuse ----

    [Fact]
    public async Task A_repeated_alert_of_the_same_situation_within_the_time_to_live_uses_the_cache_and_after_it_asks_again()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot(sequence: 1));
        rig.Ai.Apply(Snapshot(alert: null));
        Assert.Equal(1, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromSeconds(119));
        var reused = rig.Ai.Apply(Snapshot(sequence: 2));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(Answer, reused!.FreeText);
        Assert.Equal(1, rig.Client.Calls);

        rig.Ai.Apply(Snapshot(alert: null));
        rig.Time.Advance(TimeSpan.FromSeconds(2)); // 121 s after the text was stored
        var expired = rig.Ai.Apply(Snapshot(sequence: 3));
        Assert.Null(expired!.FreeText);
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task The_cache_does_not_survive_the_end_of_the_session()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot(sequence: 1));

        rig.Ai.ResetSession();
        var afterwards = rig.Ai.Apply(Snapshot(sequence: 1));
        await rig.Ai.WhenIdleAsync();

        Assert.Null(afterwards!.FreeText);
        Assert.Equal(2, rig.Client.Calls);
    }

    // ---- the timing records ----

    [Fact]
    public async Task Each_request_writes_one_timing_record_without_secrets_payload_or_answer()
    {
        var rig = Make();
        rig.Client.Answer = AiTextResult.Success(Answer);
        rig.Ai.Apply(Snapshot(sequence: 4));
        await rig.Ai.WhenIdleAsync();

        var timing = rig.Logs.Lines.Where(l => l.Contains("PresentaTiming", StringComparison.Ordinal)).ToArray();
        var timingOn = AiRecommendationEnricher.TimingDiagnosticsEnabled;
        if (!timingOn)
        {
            Assert.Empty(timing);
            return;
        }

        var line = Assert.Single(timing);
        Assert.Contains("sequence=4", line, StringComparison.Ordinal);
        Assert.Contains("code=SpeedUpSlightly", line, StringComparison.Ordinal);
        Assert.Contains("alertToRequestMs=0", line, StringComparison.Ordinal);
        Assert.Contains("attempts=1", line, StringComparison.Ordinal);
        Assert.Contains("outcome=Accepted", line, StringComparison.Ordinal);
        Assert.Contains("duringAlert=True", line, StringComparison.Ordinal);
        var everything = string.Join('\n', rig.Logs.Lines);
        Assert.DoesNotContain(Answer, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateName, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(AiPromptBuilder.SystemPrompt, everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Situation:", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_that_comes_too_late_is_recorded_as_not_during_the_alert()
    {
        var rig = Make();
        rig.Client.Hold = new TaskCompletionSource();
        rig.Ai.Apply(Snapshot(sequence: 1));
        rig.Ai.Apply(Snapshot(alert: null));
        rig.Client.Hold.SetResult();
        await rig.Ai.WhenIdleAsync();

        var timingOn = AiRecommendationEnricher.TimingDiagnosticsEnabled;
        if (timingOn)
        {
            Assert.Contains(rig.Logs.Lines, l => l.Contains("duringAlert=False", StringComparison.Ordinal));
        }
    }

    // ---- .env.example ----

    [Fact]
    public void The_example_env_file_has_the_two_recommended_lines_and_no_key()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".env.example")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var lines = File.ReadAllLines(Path.Combine(directory!.FullName, ".env.example"));

        Assert.Contains("PRESENTA_AI_REASONING_EFFORT=none", lines);
        Assert.Contains("PRESENTA_AI_MAX_TOKENS=800", lines);
        Assert.Contains("PRESENTA_AI_API_KEY=", lines); // empty: a placeholder only
        Assert.DoesNotContain(lines, l => l.Contains("sk-", StringComparison.Ordinal));
    }
}
