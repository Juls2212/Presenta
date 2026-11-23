using Presenta.Ai;
using Presenta.Domain;
using Presenta.Server;
using Presenta.TestSupport;

namespace Presenta.Ai.Tests;

public class EnricherTests
{
    private const string PrivateName = "Quarterly secret.pptx";

    private static SessionSnapshot Snapshot(
        AlertKind? alert = AlertKind.TimeRunningOut,
        RecommendationCode code = RecommendationCode.SpeedUpSlightly,
        int slidesRemaining = 9,
        long remainingMs = 300_000,
        PaceStatus pace = PaceStatus.Behind) =>
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
            pace,
            slidesRemaining,
            1.1,
            null,
            alert is null ? null : new Alert(alert.Value, AlertSeverity.Warning, 600_000, remainingMs, slidesRemaining, -2, 1),
            new Recommendation(code, slidesRemaining, remainingMs));

    private sealed record Rig(AiRecommendationEnricher Ai, FakeAiTextClient Client, ManualTimeProvider Time);

    private static Rig Make(Func<AiEnrichmentOptions, AiEnrichmentOptions>? configure = null, bool enabled = true)
    {
        var options = new AiEnrichmentOptions();
        if (configure is not null)
        {
            options = configure(options);
        }

        var time = new ManualTimeProvider();
        var client = new FakeAiTextClient();
        var ai = new AiRecommendationEnricher(client, AiAvailability.Available, options, time) { Enabled = enabled };
        return new Rig(ai, client, time);
    }

    private static async Task<Recommendation?> AskAsync(Rig rig, SessionSnapshot snapshot)
    {
        rig.Ai.Apply(snapshot);
        await rig.Ai.WhenIdleAsync();
        return rig.Ai.Apply(snapshot);
    }

    private static SessionSnapshot Distinct(int index) => Snapshot(slidesRemaining: index);

    // ---- off, missing, no alert ----

    [Fact]
    public async Task It_is_off_by_default_and_never_calls_the_provider()
    {
        var rig = Make(enabled: false);

        var result = await AskAsync(rig, Snapshot());

        Assert.Equal(0, rig.Client.Calls);
        Assert.Null(result!.FreeText);
        Assert.Equal(AiStatus.Disabled, rig.Ai.Status);
    }

    [Theory]
    [InlineData(AiAvailability.MissingVariables, AiStatus.NotConfigured)]
    [InlineData(AiAvailability.InvalidAddress, AiStatus.InvalidAddress)]
    [InlineData(AiAvailability.InsecureAddress, AiStatus.InsecureAddress)]
    public async Task Without_valid_settings_the_switch_cannot_be_turned_on_and_nothing_is_called(AiAvailability availability, AiStatus expected)
    {
        var client = new FakeAiTextClient();
        var ai = new AiRecommendationEnricher(client, availability, new AiEnrichmentOptions(), new ManualTimeProvider());

        ai.Enabled = true;
        ai.Apply(Snapshot());
        await ai.WhenIdleAsync();

        Assert.False(ai.Enabled);
        Assert.False(ai.IsAvailable);
        Assert.Equal(expected, ai.Status);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Without_a_client_it_is_not_available_even_if_the_settings_say_so()
    {
        var ai = new AiRecommendationEnricher(null, AiAvailability.Available, new AiEnrichmentOptions(), new ManualTimeProvider()) { Enabled = true };

        ai.Apply(Snapshot());
        await ai.WhenIdleAsync();

        Assert.False(ai.IsAvailable);
        Assert.Equal(AiStatus.NotConfigured, ai.Status);
    }

    [Fact]
    public async Task Without_an_alert_nothing_is_requested_and_without_a_recommendation_nothing_is_returned()
    {
        var rig = Make();

        var noAlert = await AskAsync(rig, Snapshot(alert: null));
        var none = rig.Ai.Apply(Snapshot() with { Recommendation = null });

        Assert.Equal(0, rig.Client.Calls);
        Assert.Null(noAlert!.FreeText);
        Assert.Null(none);
    }

    // ---- the happy path and the cache ----

    [Fact]
    public async Task A_text_is_requested_in_the_background_and_then_filled_in_for_the_same_situation()
    {
        var rig = Make();
        var texts = 0;
        rig.Ai.TextReady += (_, _) => Interlocked.Increment(ref texts);

        var first = rig.Ai.Apply(Snapshot());
        await rig.Ai.WhenIdleAsync();
        var second = rig.Ai.Apply(Snapshot());

        Assert.Null(first!.FreeText); // never waits for the provider
        Assert.Equal("Pick up the pace now", second!.FreeText);
        Assert.Equal(RecommendationCode.SpeedUpSlightly, second.Code); // the deterministic part is untouched
        Assert.Equal(1, rig.Client.Calls);
        Assert.Equal(1, texts);
        Assert.Equal("Pick up the pace now", rig.Ai.LastText);
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
    }

    [Fact]
    public async Task The_same_situation_is_asked_once_and_nearby_numbers_share_the_text()
    {
        var rig = Make();

        await AskAsync(rig, Snapshot(slidesRemaining: 31, remainingMs: 1_800_000));
        var near = rig.Ai.Apply(Snapshot(slidesRemaining: 34, remainingMs: 1_790_000));
        for (var i = 0; i < 20; i++)
        {
            rig.Ai.Apply(Snapshot(slidesRemaining: 31, remainingMs: 1_800_000));
        }

        await rig.Ai.WhenIdleAsync();
        Assert.Equal(1, rig.Client.Calls);
        Assert.Equal("Pick up the pace now", near!.FreeText);
    }

    [Fact]
    public async Task A_text_is_dropped_when_the_situation_changes_and_comes_back_when_it_returns_without_a_new_call()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        var a = Snapshot(code: RecommendationCode.SpeedUpSlightly);
        var b = Snapshot(code: RecommendationCode.WrapUp, alert: AlertKind.TimeCritical, slidesRemaining: 3, remainingMs: 60_000);
        await AskAsync(rig, a);

        var stale = rig.Ai.Apply(b);
        await rig.Ai.WhenIdleAsync();
        var back = rig.Ai.Apply(a);

        Assert.Null(stale!.FreeText); // the text of A must not be shown with B
        Assert.Equal("Pick up the pace now", back!.FreeText);
        Assert.Equal(2, rig.Client.Calls); // A once, B once; coming back to A cost nothing
    }

    [Fact]
    public async Task The_alert_ending_removes_the_text()
    {
        var rig = Make();
        await AskAsync(rig, Snapshot());

        var after = rig.Ai.Apply(Snapshot(alert: null));

        Assert.Null(after!.FreeText);
    }

    [Fact]
    public async Task The_prompt_holds_only_codes_and_coarse_numbers_never_the_session_name()
    {
        var rig = Make();

        await AskAsync(rig, Snapshot());

        var prompt = Assert.Single(rig.Client.Prompts);
        Assert.DoesNotContain("Quarterly", prompt.User + prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", prompt.User + prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pptx", prompt.User + prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Situation: recommendation=SpeedUpSlightly; alert=TimeRunningOut; pace=Behind; slidesRemaining=9; minutesRemaining=5; plannedSlides=20; plannedMinutes=15. Write the tip.",
            prompt.User);
    }

    // ---- limits ----

    [Fact]
    public async Task At_most_one_request_per_interval_and_the_next_one_waits_for_it()
    {
        var rig = Make();
        await AskAsync(rig, Distinct(1));

        rig.Ai.Apply(Distinct(2));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(1, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromSeconds(14));
        rig.Ai.Apply(Distinct(2));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(1, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Ai.Apply(Distinct(2));
        await rig.Ai.WhenIdleAsync();
        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task A_failed_situation_is_not_asked_again_until_the_situation_changes()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero, FailureThreshold = 10 });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.ServerError);

        await AskAsync(rig, Distinct(1));
        rig.Time.Advance(TimeSpan.FromMinutes(5));
        await AskAsync(rig, Distinct(1));

        Assert.Equal(1, rig.Client.Calls);
        await AskAsync(rig, Distinct(2));
        Assert.Equal(2, rig.Client.Calls);
    }

    [Fact]
    public async Task The_per_session_cap_stops_requests_and_a_new_session_starts_counting_again()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero, MaxCallsPerSession = 3 });

        for (var i = 1; i <= 6; i++)
        {
            await AskAsync(rig, Distinct(i));
        }

        Assert.Equal(3, rig.Client.Calls);
        Assert.Equal(3, rig.Ai.CallsThisSession);

        rig.Ai.ResetSession();
        Assert.Equal(0, rig.Ai.CallsThisSession);
        await AskAsync(rig, Distinct(7));
        Assert.Equal(4, rig.Client.Calls);
    }

    [Fact]
    public async Task The_default_limits_are_the_documented_ones()
    {
        var options = new AiEnrichmentOptions();

        Assert.Equal(TimeSpan.FromSeconds(15), options.MinInterval);
        Assert.Equal(30, options.MaxCallsPerSession);
        Assert.Equal(3, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(60), options.PauseDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RateLimitBackoffInitial);
        Assert.Equal(TimeSpan.FromMinutes(5), options.RateLimitBackoffMax);
        await Task.CompletedTask;
    }

    // ---- the circuit breaker ----

    [Theory]
    [InlineData(AiFailureKind.Timeout)]
    [InlineData(AiFailureKind.ServerError)]
    [InlineData(AiFailureKind.Malformed)]
    [InlineData(AiFailureKind.Network)]
    [InlineData(AiFailureKind.Rejected)]
    public async Task Three_failures_in_a_row_pause_the_requests_then_one_trial_decides(AiFailureKind kind)
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Answer = AiTextResult.Fail(kind);
        await AskAsync(rig, Distinct(1));
        await AskAsync(rig, Distinct(2));
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
        await AskAsync(rig, Distinct(3));

        Assert.Equal(AiStatus.Paused, rig.Ai.Status);
        await AskAsync(rig, Distinct(4));
        Assert.Equal(3, rig.Client.Calls); // paused: nothing is sent

        rig.Time.Advance(TimeSpan.FromSeconds(59));
        await AskAsync(rig, Distinct(4));
        Assert.Equal(3, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
        await AskAsync(rig, Distinct(4)); // the trial fails: a single failure opens it again at once
        Assert.Equal(4, rig.Client.Calls);
        Assert.Equal(AiStatus.Paused, rig.Ai.Status);

        rig.Time.Advance(TimeSpan.FromSeconds(60));
        rig.Client.Answer = AiTextResult.Success("Back on track now");
        var result = await AskAsync(rig, Distinct(5));
        Assert.Equal(5, rig.Client.Calls);
        Assert.Equal("Back on track now", result!.FreeText);
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
    }

    [Fact]
    public async Task A_success_resets_the_failure_count()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Script = (n, _) => n is 1 or 2 or 4 or 5 ? AiTextResult.Fail(AiFailureKind.ServerError) : AiTextResult.Success("All good now");

        for (var i = 1; i <= 5; i++)
        {
            await AskAsync(rig, Distinct(i));
        }

        Assert.Equal(5, rig.Client.Calls);
        Assert.Equal(AiStatus.Ready, rig.Ai.Status); // two, a success, two: never three in a row
    }

    [Fact]
    public async Task A_refused_key_shows_invalid_key_and_pauses_at_once_and_turning_it_off_and_on_retries()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.Unauthorized);

        await AskAsync(rig, Distinct(1));

        Assert.Equal(AiStatus.InvalidKey, rig.Ai.Status);
        await AskAsync(rig, Distinct(2));
        Assert.Equal(1, rig.Client.Calls);

        rig.Client.Answer = AiTextResult.Success("Works with a new key");
        rig.Ai.Enabled = false;
        rig.Ai.Enabled = true;
        var result = await AskAsync(rig, Distinct(2));

        Assert.Equal("Works with a new key", result!.FreeText);
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
    }

    [Fact]
    public async Task A_refused_key_is_tried_again_after_the_pause_and_a_success_clears_the_status()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.Unauthorized);
        await AskAsync(rig, Distinct(1));

        rig.Time.Advance(TimeSpan.FromSeconds(61));
        rig.Client.Answer = AiTextResult.Success("Key works again");
        await AskAsync(rig, Distinct(2));

        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
        Assert.Equal(2, rig.Client.Calls);
    }

    // ---- backoff on 429 ----

    [Fact]
    public async Task A_429_without_a_retry_after_backs_off_exponentially_up_to_the_cap()
    {
        var rig = Make(o => o with
        {
            MinInterval = TimeSpan.Zero,
            FailureThreshold = 100,
            RateLimitBackoffInitial = TimeSpan.FromSeconds(5),
            RateLimitBackoffMax = TimeSpan.FromSeconds(12),
        });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.RateLimited);
        var calls = 0;

        async Task ExpectWaitAsync(TimeSpan wait, int index)
        {
            await AskAsync(rig, Distinct(index));
            calls++;
            Assert.Equal(calls, rig.Client.Calls);
            Assert.Equal(AiStatus.Paused, rig.Ai.Status);
            rig.Time.Advance(wait - TimeSpan.FromSeconds(1));
            await AskAsync(rig, Distinct(index + 1));
            Assert.Equal(calls, rig.Client.Calls); // still waiting
            rig.Time.Advance(TimeSpan.FromSeconds(1));
        }

        await ExpectWaitAsync(TimeSpan.FromSeconds(5), 1);
        await ExpectWaitAsync(TimeSpan.FromSeconds(10), 2);
        await ExpectWaitAsync(TimeSpan.FromSeconds(12), 3); // 20 s, capped
        await ExpectWaitAsync(TimeSpan.FromSeconds(12), 4);
    }

    [Fact]
    public async Task A_429_with_a_retry_after_waits_at_least_that_long_but_never_more_than_the_cap()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero, FailureThreshold = 100 });
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.RateLimited, TimeSpan.FromSeconds(30));
        await AskAsync(rig, Distinct(1));

        rig.Time.Advance(TimeSpan.FromSeconds(29));
        await AskAsync(rig, Distinct(2));
        Assert.Equal(1, rig.Client.Calls);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Client.Answer = AiTextResult.Fail(AiFailureKind.RateLimited, TimeSpan.FromHours(3));
        await AskAsync(rig, Distinct(2));
        Assert.Equal(2, rig.Client.Calls);

        rig.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        await AskAsync(rig, Distinct(3));
        Assert.Equal(2, rig.Client.Calls);
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await AskAsync(rig, Distinct(3));
        Assert.Equal(3, rig.Client.Calls); // 3 hours asked, 5 minutes waited
    }

    // ---- time and unusable texts ----

    [Fact]
    public async Task A_provider_that_never_answers_times_out_on_fake_time_and_counts_as_a_failure()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Hold = new TaskCompletionSource();

        rig.Ai.Apply(Distinct(1));
        await WaitUntilAsync(() => rig.Client.Calls == 1);
        Assert.Equal(AiStatus.Working, rig.Ai.Status);
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(AiStatus.Working, rig.Ai.Status); // not yet: the limit is 5 s
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.Ai.WhenIdleAsync();

        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
        rig.Client.Hold = null;
        rig.Client.Answer = AiTextResult.Success("Still alive");
        var result = await AskAsync(rig, Distinct(2));
        Assert.Equal("Still alive", result!.FreeText);
    }

    [Theory]
    [InlineData("see https://example.com now")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Extraordinarily-long-single-word-without-any-spaces")]
    [InlineData("Go faster 😀")]
    public async Task An_unusable_text_leaves_the_deterministic_wording_and_counts_as_a_failure(string raw)
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Answer = AiTextResult.Success(raw);

        var result = await AskAsync(rig, Distinct(1));
        await AskAsync(rig, Distinct(2));
        await AskAsync(rig, Distinct(3));

        Assert.Null(result!.FreeText);
        Assert.Null(rig.Ai.LastText);
        Assert.Equal(AiStatus.Paused, rig.Ai.Status);
    }

    [Fact]
    public async Task A_good_text_is_cleaned_before_it_is_kept()
    {
        var rig = Make();
        rig.Client.Answer = AiTextResult.Success("  \"**Slow down**\n a little\"  ");

        var result = await AskAsync(rig, Snapshot());

        Assert.Equal("Slow down a little", result!.FreeText);
    }

    [Fact]
    public async Task A_client_that_throws_is_a_failure_not_a_crash()
    {
        var rig = Make(o => o with { MinInterval = TimeSpan.Zero });
        rig.Client.Script = (_, _) => throw new InvalidOperationException("boom");

        var result = await AskAsync(rig, Distinct(1));

        Assert.Null(result!.FreeText);
        Assert.Equal(AiStatus.Ready, rig.Ai.Status);
    }

    [Fact]
    public async Task Apply_returns_at_once_while_the_provider_is_still_working_and_the_status_says_so()
    {
        var rig = Make();
        rig.Client.Hold = new TaskCompletionSource();

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = rig.Ai.Apply(Snapshot());
        started.Stop();

        Assert.Null(result!.FreeText);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), "Apply must not wait for the provider");
        await WaitUntilAsync(() => rig.Client.Calls == 1);
        Assert.Equal(AiStatus.Working, rig.Ai.Status);
        rig.Client.Hold.SetResult();
        await rig.Ai.WhenIdleAsync();
        Assert.Equal("Pick up the pace now", rig.Ai.Apply(Snapshot())!.FreeText);
    }

    [Fact]
    public async Task Changes_are_announced_when_the_switch_or_the_status_changes()
    {
        var rig = Make(enabled: false);
        var changes = 0;
        rig.Ai.Changed += (_, _) => Interlocked.Increment(ref changes);

        rig.Ai.Enabled = true;
        Assert.True(changes >= 1);
        var before = changes;
        await AskAsync(rig, Snapshot());

        Assert.True(changes > before);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "Timed out waiting for the condition.");
            await Task.Delay(5);
        }
    }
}
