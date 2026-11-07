using System.Net;
using System.Net.WebSockets;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

public class ConnectionPolicyTests
{
    [Fact]
    public async Task The_session_survives_a_client_disconnect_and_a_reconnecting_client_gets_the_current_state()
    {
        await using var harness = await ServerHarness.StartAsync();
        var first = await harness.ConnectAsync();
        await first.CommandAsync(Msg.Start(slides: 10, name: "Survivor"));
        await first.CommandAsync(Msg.Next());
        await first.CommandAsync(Msg.Next());
        await first.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");

        Assert.NotNull(harness.Service.Status.Session); // still running
        var second = await harness.ConnectAsync(pair: false);
        var accepted = await second.PairAsync(ServerHarness.Code);

        var session = accepted.Payload.State.Session!;
        Assert.Equal("Survivor", session.Name);
        Assert.Equal(3, session.CurrentSlide);
        Assert.Equal(SessionState.Running, session.State);
        Assert.True(session.Running);
    }

    [Fact]
    public async Task A_new_watch_can_control_the_session_a_previous_watch_started()
    {
        await using var harness = await ServerHarness.StartAsync();
        var first = await harness.ConnectAsync();
        await first.CommandAsync(Msg.Start(slides: 10));
        await first.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");

        var second = await harness.ConnectAsync();
        var result = await second.CommandAsync(Msg.Next());

        Assert.True(result.Payload.Ok);
    }

    [Fact]
    public async Task When_no_watch_returns_within_the_grace_period_the_session_is_interrupted_and_saved()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(60) });
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 10, durationMs: 600_000, name: "Lost"));
        await client.CommandAsync(Msg.Next());
        await client.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");

        harness.Time.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50); // let the queued ticks run; time itself is fake
        Assert.NotNull(harness.Service.Status.Session);
        Assert.Empty(harness.Memory.Records);

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        await Wait.UntilAsync(() => harness.Service.Status.Session is null, "the session to be interrupted");

        var record = Assert.Single(harness.Memory.Records);
        Assert.Equal(Presenta.Domain.FinalState.InterruptedByDisconnection, record.FinalState);
        Assert.Equal("Lost", record.Name);
        Assert.Equal(2, record.RegisteredSlides);
        Assert.True(record.ActualDurationMs >= 60_000, "the recorded duration includes the grace period");
    }

    [Fact]
    public async Task An_interruption_sends_no_message_and_a_later_watch_sees_no_session()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(30) });
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        await client.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");
        harness.Time.Advance(TimeSpan.FromSeconds(31));
        await Wait.UntilAsync(() => harness.Service.Status.Session is null, "the session to be interrupted");

        var later = await harness.ConnectAsync(pair: false);
        var accepted = await later.PairAsync(ServerHarness.Code);
        await later.SendAsync(Msg.Ping());
        var next = await later.ReceiveAsync();

        Assert.Null(accepted.Payload.State.Session);
        Assert.IsType<PongMessage>(next); // no session.summary was queued for the watch
        Assert.Single(harness.Memory.Records);
    }

    [Fact]
    public async Task Reconnecting_before_the_grace_period_ends_keeps_the_session_alive_indefinitely()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(60), HeartbeatTimeout = TimeSpan.FromHours(2) });
        var first = await harness.ConnectAsync();
        await first.CommandAsync(Msg.Start(durationMs: 3_600_000));
        await first.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");
        harness.Time.Advance(TimeSpan.FromSeconds(50));

        var second = await harness.ConnectAsync();
        harness.Time.Advance(TimeSpan.FromSeconds(300));
        await second.SendAsync(Msg.Ping());
        await second.ReceiveUntilAsync<PongMessage>();

        Assert.NotNull(harness.Service.Status.Session);
        Assert.Empty(harness.Memory.Records);
    }

    [Fact]
    public async Task The_grace_period_default_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new CompanionServerOptions().DisconnectGracePeriod);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_disconnect_while_idle_does_not_create_any_record()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(10) });
        var client = await harness.ConnectAsync();
        await client.CloseAsync();
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the disconnect to be noticed");

        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(50);

        Assert.Empty(harness.Memory.Records);
    }

    [Fact]
    public async Task A_second_valid_client_replaces_the_first_and_the_old_socket_is_closed_cleanly()
    {
        await using var harness = await ServerHarness.StartAsync();
        var first = await harness.ConnectAsync();
        await first.CommandAsync(Msg.Start(slides: 10, name: "Shared"));

        var second = await harness.ConnectAsync();

        Assert.Equal(WebSocketCloseStatus.NormalClosure, await first.ExpectCloseAsync());
        var result = await second.CommandAsync(Msg.Next());
        Assert.True(result.Payload.Ok);
        Assert.True(harness.Service.Status.WatchConnected);
        Assert.Equal("Shared", harness.Service.Status.Session!.Name);
    }

    [Fact]
    public async Task Replacing_the_connection_does_not_start_the_grace_period()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { DisconnectGracePeriod = TimeSpan.FromSeconds(10), HeartbeatTimeout = TimeSpan.FromHours(2) });
        var first = await harness.ConnectAsync();
        await first.CommandAsync(Msg.Start(durationMs: 3_600_000));
        var second = await harness.ConnectAsync();
        await first.ExpectCloseAsync();

        harness.Time.Advance(TimeSpan.FromSeconds(30));
        await second.SendAsync(Msg.Ping());
        await second.ReceiveUntilAsync<PongMessage>();

        Assert.NotNull(harness.Service.Status.Session);
        Assert.Empty(harness.Memory.Records);
    }

    [Fact]
    public async Task A_second_connection_with_a_wrong_code_does_not_replace_the_first()
    {
        await using var harness = await ServerHarness.StartAsync();
        var first = await harness.ConnectAsync();
        var intruder = await harness.ConnectAsync(pair: false);

        await intruder.SendAsync(Msg.Hello("000000"));
        await intruder.ReceiveAsync<HelloRejectedMessage>();

        await first.SendAsync(Msg.Ping());
        await first.ReceiveUntilAsync<PongMessage>();
        Assert.True(harness.Service.Status.WatchConnected);
    }

    [Fact]
    public async Task Stopping_the_host_closes_the_connection_and_keeps_the_session()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());

        var closing = client.ExpectCloseAsync(); // a real client is always reading
        await harness.Host.StopAsync();

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await closing);
        Assert.False(harness.Host.IsRunning);
        Assert.NotNull(harness.Service.Status.Session);
    }

    // ---- listening address ----

    [Fact]
    public async Task The_server_binds_loopback_only_by_default()
    {
        await using var harness = await ServerHarness.StartAsync();

        var endpoint = Assert.Single(harness.Host.BoundEndpoints);
        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.NotEqual(0, endpoint.Port);
        Assert.False(harness.Host.AllowLocalNetwork);
    }

    [Fact]
    public async Task The_server_binds_all_interfaces_only_when_the_local_network_is_allowed()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { AllowLocalNetwork = true });

        var endpoint = Assert.Single(harness.Host.BoundEndpoints);
        Assert.Equal(IPAddress.Any, endpoint.Address);
        Assert.True(harness.Host.AllowLocalNetwork);
        var client = await harness.ConnectAsync(); // loopback still works
        await client.SendAsync(Msg.Ping());
        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task Rebinding_changes_the_address_keeps_the_port_and_keeps_the_session()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 10, durationMs: 3_600_000, name: "Rebound"));
        var port = harness.Host.Port;

        await harness.Host.RebindAsync(allowLocalNetwork: true);

        Assert.Equal(IPAddress.Any, Assert.Single(harness.Host.BoundEndpoints).Address);
        Assert.Equal(port, harness.Host.Port);
        var again = await harness.ConnectAsync(pair: false);
        var accepted = await again.PairAsync(ServerHarness.Code);
        Assert.Equal("Rebound", accepted.Payload.State.Session!.Name);

        await harness.Host.RebindAsync(allowLocalNetwork: false);
        Assert.Equal(IPAddress.Loopback, Assert.Single(harness.Host.BoundEndpoints).Address);
    }

    [Fact]
    public async Task A_plain_http_request_is_told_to_use_websocket()
    {
        await using var harness = await ServerHarness.StartAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync($"http://127.0.0.1:{harness.Port}/");

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_default_port_is_8765()
    {
        Assert.Equal(8765, new CompanionServerOptions().Port);
        await Task.CompletedTask;
    }
}
