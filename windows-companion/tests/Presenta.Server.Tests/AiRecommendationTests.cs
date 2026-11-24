using Presenta.Ai;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;
using Presenta.TestSupport;

namespace Presenta.Server.Tests;

/// <summary>Phase 10: the optional AI text reaches the watch through the real server, and never gets in the way (ADR-75).</summary>
public class AiRecommendationTests
{
    private sealed record Rig(ServerHarness Harness, FakeAiTextClient Client, AiRecommendationEnricher Ai) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    private static async Task<Rig> StartAsync(bool enabled = true, Action<FakeAiTextClient>? configure = null, AiEnrichmentOptions? options = null)
    {
        var time = new ManualTimeProvider();
        var client = new FakeAiTextClient();
        configure?.Invoke(client);
        var ai = new AiRecommendationEnricher(client, AiAvailability.Available, options ?? new AiEnrichmentOptions(), time) { Enabled = enabled };
        var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) }, time: time, ai: ai);
        return new Rig(harness, client, ai);
    }

    // Moves the fake clock one second at a time until the first alert appears, and stops there, so the situation stays the
    // same while the test looks at it (the pace alert of a presenter who stays on slide 1 comes first, a few seconds in).
    private static async Task<TestClient> StartAlertingSessionAsync(Rig rig)
    {
        var client = await rig.Harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 20, durationMs: 60_000, name: "Secret deck name"));
        for (var second = 0; second < 40 && rig.Harness.Service.Status.Session?.ActiveAlert is null; second++)
        {
            rig.Harness.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(25); // lets the tick callback finish
        }

        await Wait.UntilAsync(() => rig.Harness.Service.Status.Session?.ActiveAlert is not null, "an alert to be raised");
        return client;
    }

    private static bool HasText(StateUpdateMessage update) => update.Payload.Session?.Recommendation?.FreeText is not null;

    [Fact]
    public async Task The_text_reaches_the_watch_in_a_new_state_update_and_the_deterministic_code_is_untouched()
    {
        await using var rig = await StartAsync();
        var client = await StartAlertingSessionAsync(rig);

        var update = await client.ReceiveUntilAsync<StateUpdateMessage>(HasText);

        var recommendation = update.Payload.Session!.Recommendation!;
        Assert.Equal("Pick up the pace now", recommendation.FreeText);
        Assert.NotNull(update.Payload.Session.ActiveAlert);
        Assert.Equal(1, rig.Client.Calls);
        Assert.DoesNotContain("Secret deck name", rig.Client.Prompts[0].User + rig.Client.Prompts[0].System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_that_arrives_mid_alert_is_pushed_at_once_with_the_same_alert_sequence()
    {
        await using var rig = await StartAsync(configure: c => c.Hold = new TaskCompletionSource(), options: new AiEnrichmentOptions { RequestTimeout = TimeSpan.FromMinutes(10) });
        var client = await StartAlertingSessionAsync(rig);
        await Wait.UntilAsync(() => rig.Client.Calls >= 1, "the request to start");
        var sequence = rig.Harness.Service.Status.Session!.ActiveAlert!.Sequence;

        rig.Client.Hold!.SetResult(); // the fake clock does not move: no tick can carry the text

        var update = await client.ReceiveUntilAsync<StateUpdateMessage>(HasText);
        Assert.Equal(sequence, update.Payload.Session!.ActiveAlert!.Sequence);
        Assert.Equal(1, rig.Client.Calls);
    }

    [Fact]
    public async Task A_watch_that_connects_later_gets_the_cached_text_with_the_state()
    {
        await using var rig = await StartAsync();
        var first = await StartAlertingSessionAsync(rig);
        await first.ReceiveUntilAsync<StateUpdateMessage>(HasText);

        var second = await rig.Harness.ConnectAsync(pair: false);
        var accepted = await second.PairAsync(ServerHarness.Code);

        Assert.Equal("Pick up the pace now", accepted.Payload.State.Session!.Recommendation!.FreeText);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_requested_and_no_state_has_a_text()
    {
        await using var rig = await StartAsync(enabled: false);
        var client = await StartAlertingSessionAsync(rig);

        rig.Harness.Time.Advance(TimeSpan.FromSeconds(6));
        var states = new List<StateUpdateMessage>();
        for (var i = 0; i < 3; i++)
        {
            states.Add(await client.ReceiveAsync<StateUpdateMessage>());
        }

        Assert.All(states, s => Assert.Null(s.Payload.Session?.Recommendation?.FreeText));
        Assert.Equal(0, rig.Client.Calls);
    }

    [Fact]
    public async Task A_provider_that_never_answers_blocks_neither_commands_nor_ticks_and_the_text_arrives_when_it_does()
    {
        // A long request limit, so that the 6 s of fake time below do not make the open request time out (that is tested elsewhere).
        await using var rig = await StartAsync(configure: c => c.Hold = new TaskCompletionSource(), options: new AiEnrichmentOptions { RequestTimeout = TimeSpan.FromMinutes(10) });
        var client = await StartAlertingSessionAsync(rig);
        await Wait.UntilAsync(() => rig.Client.Calls >= 1, "the request to start");

        // Commands are answered at once while the request is open.
        var next = await client.CommandAsync(Msg.Next());
        var pause = await client.CommandAsync(Msg.Pause());
        var resume = await client.CommandAsync(Msg.Resume());
        Assert.True(next.Payload.Ok);
        Assert.True(pause.Payload.Ok);
        Assert.True(resume.Payload.Ok);

        // Ticks keep running: the session time still moves and the periodic state still goes out.
        var before = (await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session is not null)).Payload.Session!.ElapsedMs;
        rig.Harness.Time.Advance(TimeSpan.FromSeconds(6)); // past the 5 s periodic state
        var after = (await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session!.ElapsedMs > before)).Payload.Session!.ElapsedMs;
        Assert.True(after > before);

        rig.Client.Hold!.SetResult();
        var update = await client.ReceiveUntilAsync<StateUpdateMessage>(HasText);
        Assert.Equal("Pick up the pace now", update.Payload.Session!.Recommendation!.FreeText);
    }

    [Fact]
    public async Task Every_failure_mode_leaves_the_deterministic_recommendation_and_the_session_working()
    {
        foreach (var failure in new[] { AiFailureKind.Timeout, AiFailureKind.Unauthorized, AiFailureKind.RateLimited, AiFailureKind.ServerError, AiFailureKind.Malformed, AiFailureKind.Network })
        {
            await using var rig = await StartAsync(configure: c => c.Answer = AiTextResult.Fail(failure));
            var client = await StartAlertingSessionAsync(rig);
            await Wait.UntilAsync(() => rig.Client.Calls >= 1, "the request");
            await rig.Ai.WhenIdleAsync();

            var next = await client.CommandAsync(Msg.Next());
            var update = await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session!.CurrentSlide == 2);

            Assert.True(next.Payload.Ok, failure.ToString());
            Assert.Null(update.Payload.Session!.Recommendation?.FreeText);
        }
    }

    [Fact]
    public async Task Unusable_text_from_the_provider_never_reaches_the_watch()
    {
        await using var rig = await StartAsync(configure: c => c.Answer = AiTextResult.Success("Read more at https://example.com/x"));
        var client = await StartAlertingSessionAsync(rig);
        await Wait.UntilAsync(() => rig.Client.Calls >= 1, "the request");
        await rig.Ai.WhenIdleAsync();

        rig.Harness.Time.Advance(TimeSpan.FromSeconds(6));
        var update = await client.ReceiveAsync<StateUpdateMessage>();

        Assert.Null(update.Payload.Session!.Recommendation?.FreeText);
        Assert.Equal(1, rig.Client.Calls);
    }

    [Fact]
    public async Task Acknowledging_the_alert_removes_the_text_because_the_situation_is_over()
    {
        await using var rig = await StartAsync();
        var client = await StartAlertingSessionAsync(rig);
        await client.ReceiveUntilAsync<StateUpdateMessage>(HasText);

        await client.CommandAsync(Msg.Acknowledge());
        var after = await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session?.ActiveAlert is null);

        Assert.Null(after.Payload.Session!.Recommendation!.FreeText);
    }

    [Fact]
    public async Task A_new_presentation_starts_counting_requests_again()
    {
        await using var rig = await StartAsync();
        var client = await StartAlertingSessionAsync(rig);
        await client.ReceiveUntilAsync<StateUpdateMessage>(HasText);
        Assert.Equal(1, rig.Ai.CallsThisSession);

        await client.CommandAsync(Msg.Finish());
        await Wait.UntilAsync(() => rig.Ai.CallsThisSession == 0, "the counter to reset");
        await client.CommandAsync(Msg.Start(slides: 5, durationMs: 60_000));

        Assert.Equal(0, rig.Ai.CallsThisSession);
    }

    [Fact]
    public async Task A_service_without_an_enricher_behaves_exactly_as_before()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) });
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 20, durationMs: 60_000));
        harness.Time.Advance(TimeSpan.FromSeconds(45));

        var update = await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session?.ActiveAlert is not null);

        Assert.Null(update.Payload.Session!.Recommendation!.FreeText);
        Assert.Null(harness.Service.Ai);
    }
}
