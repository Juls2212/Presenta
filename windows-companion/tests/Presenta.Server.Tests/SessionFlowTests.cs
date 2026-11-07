using Presenta.Domain.Keyboard;
using Presenta.Persistence;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

public class SessionFlowTests
{
    [Fact]
    public async Task Full_flow_start_navigate_pause_resume_alert_acknowledge_and_finish()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) });
        var client = await harness.ConnectAsync();

        // start
        var start = await client.CommandAsync(Msg.Start(slides: 20, durationMs: 60_000, name: "Flow"));
        Assert.True(start.Payload.Ok);
        Assert.Null(start.Payload.ErrorCode);
        var started = await client.ReceiveAsync<StateUpdateMessage>();
        Assert.Equal(SessionState.Running, started.Payload.Session!.State);
        Assert.Equal("Flow", started.Payload.Session.Name);
        Assert.Equal(1, started.Payload.Session.CurrentSlide);
        Assert.Equal(60_000, started.Payload.Session.PlannedDurationMs);
        Assert.True(started.Payload.Session.Running);

        // next and previous
        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.Equal(2, (await client.ReceiveAsync<StateUpdateMessage>()).Payload.Session!.CurrentSlide);
        Assert.True((await client.CommandAsync(Msg.Previous())).Payload.Ok);
        Assert.Equal(1, (await client.ReceiveAsync<StateUpdateMessage>()).Payload.Session!.CurrentSlide);

        // pause and resume
        Assert.True((await client.CommandAsync(Msg.Pause())).Payload.Ok);
        var paused = await client.ReceiveAsync<StateUpdateMessage>();
        Assert.Equal(SessionState.Paused, paused.Payload.Session!.State);
        Assert.False(paused.Payload.Session.Running);
        Assert.True((await client.CommandAsync(Msg.Resume())).Payload.Ok);
        Assert.True((await client.ReceiveAsync<StateUpdateMessage>()).Payload.Session!.Running);

        // an alert is broadcast as time passes (fake time)
        harness.Time.Advance(TimeSpan.FromSeconds(45));
        var alerted = await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session?.ActiveAlert is not null);
        Assert.NotNull(alerted.Payload.Session!.ActiveAlert);

        // acknowledge clears the alert
        Assert.True((await client.CommandAsync(Msg.Acknowledge())).Payload.Ok);
        await client.ReceiveUntilAsync<StateUpdateMessage>(u => u.Payload.Session?.ActiveAlert is null);

        // finish: result, then summary, then the "no active session" state
        var finish = Msg.Finish();
        await client.SendAsync(finish);
        var result = await client.ReceiveUntilAsync<CommandResultMessage>(r => r.Payload.CorrelationId == finish.Id);
        Assert.True(result.Payload.Ok);
        var summary = await client.ReceiveAsync<SessionSummaryMessage>();
        Assert.Equal("Flow", summary.Payload.Name);
        Assert.Equal(FinalState.FinishedManually, summary.Payload.FinalState);
        Assert.Equal(20, summary.Payload.PlannedSlides);
        Assert.Equal(1, summary.Payload.RegisteredSlides);
        Assert.Equal(60_000, summary.Payload.PlannedDurationMs);
        Assert.Equal(45_000, summary.Payload.ActualDurationMs);
        var after = await client.ReceiveAsync<StateUpdateMessage>();
        Assert.Null(after.Payload.Session);

        Assert.Equal(new[] { SlideDirection.Next, SlideDirection.Previous }, harness.Keyboard.Sent);
    }

    [Fact]
    public async Task Finishing_while_paused_ends_as_ended_while_paused()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        await client.CommandAsync(Msg.Pause());

        await client.SendAsync(Msg.Finish());
        var summary = await client.ReceiveUntilAsync<SessionSummaryMessage>();

        Assert.Equal(FinalState.EndedWhilePaused, summary.Payload.FinalState);
        Assert.Equal(Presenta.Domain.FinalState.EndedWhilePaused, harness.Memory.Records.Single().FinalState);
    }

    [Fact]
    public async Task A_session_can_be_started_again_after_one_finishes()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        await client.CommandAsync(Msg.Finish());

        var second = await client.CommandAsync(Msg.Start(slides: 3, name: "Second"));

        Assert.True(second.Payload.Ok);
        await Wait.UntilAsync(() => harness.Service.Status.Session?.Name == "Second", "the second session to be active");
    }

    // ---- keyboard rules ----

    [Fact]
    public async Task Next_and_previous_send_arrow_keys_only_when_the_counter_changes()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 3));

        await client.CommandAsync(Msg.Previous()); // slide 1: nothing to go back to
        Assert.Empty(harness.Keyboard.Sent);
        await client.CommandAsync(Msg.Next());     // 1 -> 2
        await client.CommandAsync(Msg.Next());     // 2 -> 3
        var atEnd = await client.CommandAsync(Msg.Next()); // last slide: no key
        Assert.True(atEnd.Payload.Ok);
        await client.CommandAsync(Msg.Previous()); // 3 -> 2
        await client.CommandAsync(Msg.Previous()); // 2 -> 1
        await client.CommandAsync(Msg.Previous()); // slide 1: no key

        Assert.Equal(
            new[] { SlideDirection.Next, SlideDirection.Next, SlideDirection.Previous, SlideDirection.Previous },
            harness.Keyboard.Sent);
        Assert.Equal(1, harness.Service.Status.Session!.CurrentSlide);
    }

    [Fact]
    public async Task Set_slide_never_sends_a_key()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 10));

        Assert.True((await client.CommandAsync(Msg.SetSlide(7))).Payload.Ok);
        Assert.True((await client.CommandAsync(Msg.SetSlide(2))).Payload.Ok);
        Assert.False((await client.CommandAsync(Msg.SetSlide(11))).Payload.Ok);

        Assert.Empty(harness.Keyboard.Sent);
        Assert.Equal(2, harness.Service.Status.Session!.CurrentSlide);
    }

    [Fact]
    public async Task Slide_commands_while_paused_still_follow_the_key_rules()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5));
        await client.CommandAsync(Msg.Pause());

        await client.CommandAsync(Msg.Next());

        Assert.Equal(new[] { SlideDirection.Next }, harness.Keyboard.Sent);
    }

    [Fact]
    public async Task With_the_keys_switch_off_the_counter_moves_but_no_key_is_sent()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5));
        harness.Service.SetSendKeys(false);

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);

        Assert.Empty(harness.Keyboard.Sent);
        Assert.Equal(3, harness.Service.Status.Session!.CurrentSlide);

        harness.Service.SetSendKeys(true);
        await client.CommandAsync(Msg.Previous());
        Assert.Equal(new[] { SlideDirection.Previous }, harness.Keyboard.Sent);
    }

    [Fact]
    public async Task The_keys_switch_can_start_off()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { SendKeys = false });
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());

        await client.CommandAsync(Msg.Next());

        Assert.False(harness.Service.SendKeys);
        Assert.Empty(harness.Keyboard.Sent);
    }

    [Fact]
    public async Task A_keyboard_failure_is_reported_and_the_session_survives()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        harness.Keyboard.FailWith = new InvalidOperationException("injected failure");

        var result = await client.CommandAsync(Msg.Next());

        Assert.False(result.Payload.Ok);
        Assert.Equal(ErrorCode.KeyDeliveryFailed, result.Payload.ErrorCode);
        Assert.Equal(1, harness.Service.Status.Session!.CurrentSlide);
        harness.Keyboard.FailWith = null;
        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
    }

    [Fact]
    public async Task Only_the_next_and_previous_directions_exist()
    {
        await Task.CompletedTask;
        Assert.Equal(new[] { SlideDirection.Next, SlideDirection.Previous }, Enum.GetValues<SlideDirection>());
    }

    // ---- naming ----

    [Fact]
    public async Task A_session_without_a_name_gets_a_neutral_english_fallback_name()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.CommandAsync(Msg.Start(name: null));

        var update = await client.ReceiveAsync<StateUpdateMessage>();
        Assert.Equal("Session 2026-11-02 14:30", update.Payload.Session!.Name);
    }

    [Fact]
    public async Task A_name_from_the_watch_is_used_as_given()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.CommandAsync(Msg.Start(name: "Demo 31 oct"));

        Assert.Equal("Demo 31 oct", (await client.ReceiveAsync<StateUpdateMessage>()).Payload.Session!.Name);
    }

    // ---- history ----

    [Fact]
    public async Task Finishing_saves_exactly_one_record_and_history_list_returns_newest_first()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) });
        var client = await harness.ConnectAsync();

        await client.CommandAsync(Msg.Start(slides: 10, durationMs: 600_000, name: "First"));
        harness.Time.Advance(TimeSpan.FromSeconds(30));
        await client.CommandAsync(Msg.Finish());
        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await client.CommandAsync(Msg.Start(slides: 4, durationMs: 300_000, name: "Second"));
        await client.CommandAsync(Msg.Next());
        harness.Time.Advance(TimeSpan.FromSeconds(10));
        await client.CommandAsync(Msg.Finish());

        Assert.Equal(2, harness.Memory.Records.Count);
        await client.SendAsync(Msg.History());
        var list = await client.ReceiveUntilAsync<HistoryListResultMessage>();

        Assert.Equal(new[] { "Second", "First" }, list.Payload.Items.Select(i => i.Name));
        var second = list.Payload.Items[0];
        Assert.Equal(4, second.PlannedSlides);
        Assert.Equal(2, second.RegisteredSlides);
        Assert.Equal(300_000, second.PlannedDurationMs);
        Assert.Equal(10_000, second.ActualDurationMs);
        Assert.Equal(FinalState.FinishedManually, second.FinalState);
        Assert.True(second.EndedWithinPlannedTime);
        Assert.Equal(harness.Time.GetUtcNow(), second.FinishedAtUtc);
    }

    [Fact]
    public async Task History_list_honours_the_limit()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) });
        var client = await harness.ConnectAsync();
        for (var i = 0; i < 3; i++)
        {
            await client.CommandAsync(Msg.Start(name: "S" + i));
            await client.CommandAsync(Msg.Finish());
            harness.Time.Advance(TimeSpan.FromSeconds(5));
        }

        await client.SendAsync(Msg.History(limit: 2));
        var list = await client.ReceiveUntilAsync<HistoryListResultMessage>();

        Assert.Equal(new[] { "S2", "S1" }, list.Payload.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task An_empty_history_returns_an_empty_list()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendAsync(Msg.History());

        Assert.Empty((await client.ReceiveUntilAsync<HistoryListResultMessage>()).Payload.Items);
    }

    [Fact]
    public async Task A_failing_history_store_gives_an_error_message_and_does_not_end_the_session()
    {
        await using var harness = await ServerHarness.StartAsync(history: new FailingHistoryStore());
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());

        await client.SendAsync(Msg.History());
        var error = await client.ReceiveUntilAsync<ErrorMessage>();

        Assert.Equal(ErrorCode.InternalError, error.Payload.Code);
        Assert.NotNull(harness.Service.Status.Session);
    }

    [Fact]
    public async Task A_history_that_cannot_be_saved_does_not_break_finishing()
    {
        await using var harness = await ServerHarness.StartAsync(history: new FailingHistoryStore());
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());

        await client.SendAsync(Msg.Finish());

        await client.ReceiveUntilAsync<SessionSummaryMessage>();
        Assert.Null(harness.Service.Status.Session);
    }

    [Fact]
    public async Task A_finished_session_survives_a_server_restart_with_the_sqlite_store()
    {
        var path = Path.Combine(Path.GetTempPath(), "presenta-server-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var first = await SqliteHistoryStore.OpenAsync(path);
            await using (var harness = await ServerHarness.StartAsync(history: first))
            {
                var client = await harness.ConnectAsync();
                await client.CommandAsync(Msg.Start(name: "Persisted"));
                await client.CommandAsync(Msg.Finish());
            }

            var reopened = await SqliteHistoryStore.OpenAsync(path);
            await using (var harness = await ServerHarness.StartAsync(history: reopened))
            {
                var client = await harness.ConnectAsync();
                await client.SendAsync(Msg.History());
                var list = await client.ReceiveUntilAsync<HistoryListResultMessage>();
                Assert.Equal("Persisted", Assert.Single(list.Payload.Items).Name);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- concurrency ----

    [Fact]
    public async Task Many_commands_and_timer_ticks_at_once_keep_the_state_consistent()
    {
        // The fake clock races ahead of the server here, so the heartbeat must not fire.
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromHours(2) });
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 200, durationMs: 3_600_000));

        var ticking = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                harness.Time.Advance(TimeSpan.FromSeconds(1));
            }
        });
        var ids = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            var next = Msg.Next();
            ids.Add(next.Id);
            await client.SendAsync(next);
        }

        await ticking;
        var results = new Dictionary<string, CommandResultMessage>();
        while (results.Count < ids.Count)
        {
            var result = await client.ReceiveUntilAsync<CommandResultMessage>();
            results[result.Payload.CorrelationId] = result;
        }

        Assert.All(results.Values, r => Assert.True(r.Payload.Ok));
        Assert.Equal(100, harness.Keyboard.Sent.Count);
        await Wait.UntilAsync(() => harness.Service.Status.Session?.CurrentSlide == 101, "the counter to reach slide 101");
    }

    private sealed class FailingHistoryStore : Presenta.Domain.History.IHistoryStore
    {
        public Task SaveAsync(Presenta.Domain.SessionSummary summary, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("injected save failure");

        public Task<IReadOnlyList<Presenta.Domain.SessionSummary>> ListNewestFirstAsync(int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("injected read failure");
    }
}
