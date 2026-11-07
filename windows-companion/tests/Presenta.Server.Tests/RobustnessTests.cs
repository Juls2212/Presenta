using System.Net.WebSockets;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

public class RobustnessTests
{
    [Fact]
    public async Task Invalid_json_after_pairing_returns_an_error_and_keeps_the_connection()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendRawAsync("{ this is not json");
        var error = await client.ReceiveUntilAsync<ErrorMessage>();

        Assert.Equal(ErrorCode.InvalidMessage, error.Payload.Code);
        await client.SendAsync(Msg.Ping());
        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task Invalid_json_before_pairing_returns_an_error_and_keeps_the_connection()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        await client.SendRawAsync("garbage");
        var error = await client.ReceiveAsync<ErrorMessage>();

        Assert.Equal(ErrorCode.InvalidMessage, error.Payload.Code);
        await client.PairAsync(ServerHarness.Code);
    }

    [Fact]
    public async Task Unknown_message_type_returns_an_error_and_keeps_the_connection()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendRawAsync("""{ "version": 1, "type": "slide.teleport", "id": "u-1", "payload": {} }""");
        var error = await client.ReceiveUntilAsync<ErrorMessage>();

        Assert.Equal(ErrorCode.InvalidMessage, error.Payload.Code);
        Assert.Equal("u-1", error.Payload.CorrelationId);
        await client.SendAsync(Msg.Ping());
        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task A_number_sent_as_a_string_is_rejected_by_the_strict_parser()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendRawAsync("""{ "version": 1, "type": "slide.set", "id": "s-9", "payload": { "slide": "4" } }""");

        var error = await client.ReceiveUntilAsync<ErrorMessage>();
        Assert.Equal(ErrorCode.InvalidMessage, error.Payload.Code);
        Assert.Null(harness.Service.Status.Session);
    }

    [Fact]
    public async Task A_message_over_the_frame_limit_closes_the_connection()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendRawAsync(new string('x', 16 * 1024 + 1));

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task A_message_exactly_at_the_frame_limit_is_still_read()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { MaxFrameBytes = 512 });
        var client = await harness.ConnectAsync();
        var padding = new string('x', 512 - """{"version":1,"type":"ping","id":"p","payload":{},"pad":""}""".Length);

        await client.SendRawAsync($$"""{"version":1,"type":"ping","id":"p","payload":{},"pad":"{{padding}}"}""");

        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task The_frame_limit_is_configurable()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { MaxFrameBytes = 100 });
        var client = await harness.ConnectAsync(pair: false);

        await client.SendRawAsync(new string('x', 101));

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task A_binary_frame_closes_the_connection()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendBinaryAsync(new byte[] { 1, 2, 3 });

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task Bad_input_on_one_connection_does_not_stop_the_server()
    {
        await using var harness = await ServerHarness.StartAsync();
        var bad = await harness.ConnectAsync(pair: false);
        await bad.SendBinaryAsync(new byte[] { 0xFF });
        await bad.ExpectCloseAsync();

        var good = await harness.ConnectAsync();
        await good.SendAsync(Msg.Ping());
        await good.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task Ping_gets_a_pong()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendAsync(Msg.Ping());

        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task A_silent_connection_is_dropped_after_the_heartbeat_timeout()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        harness.Time.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.ExpectCloseAsync());
        await Wait.UntilAsync(() => !harness.Service.Status.WatchConnected, "the dropped watch to be marked as disconnected");
    }

    [Fact]
    public async Task Pings_keep_a_connection_alive_past_the_timeout()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        for (var round = 0; round < 4; round++)
        {
            harness.Time.Advance(TimeSpan.FromSeconds(10));
            await client.SendAsync(Msg.Ping());
            await client.ReceiveUntilAsync<PongMessage>();
        }

        Assert.True(harness.Service.Status.WatchConnected);
    }

    [Fact]
    public async Task An_unpaired_silent_connection_is_also_dropped()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        harness.Time.Advance(TimeSpan.FromSeconds(16));

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task The_heartbeat_timeout_is_configurable()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { HeartbeatTimeout = TimeSpan.FromSeconds(3) });
        var client = await harness.ConnectAsync();

        harness.Time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task A_state_update_is_sent_every_five_seconds_while_a_watch_is_connected()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.SendAsync(Msg.Ping());
        await client.ReceiveUntilAsync<PongMessage>();

        harness.Time.Advance(TimeSpan.FromSeconds(5));

        var update = await client.ReceiveUntilAsync<StateUpdateMessage>();
        Assert.Null(update.Payload.Session);
        Assert.Equal(harness.Time.GetUtcNow(), update.Payload.ServerTimeUtc);
    }

    [Fact]
    public async Task Invalid_commands_get_a_command_result_with_the_matching_error_code()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        Assert.Equal(ErrorCode.NoActiveSession, (await client.CommandAsync(Msg.Pause())).Payload.ErrorCode);
        Assert.Equal(ErrorCode.NoActiveSession, (await client.CommandAsync(Msg.Next())).Payload.ErrorCode);
        Assert.Equal(ErrorCode.NoActiveSession, (await client.CommandAsync(Msg.Finish())).Payload.ErrorCode);
        Assert.Equal(ErrorCode.InvalidMessage, (await client.CommandAsync(Msg.Start(slides: 0))).Payload.ErrorCode);
        Assert.True((await client.CommandAsync(Msg.Start(slides: 10))).Payload.Ok);
        Assert.Equal(ErrorCode.InvalidState, (await client.CommandAsync(Msg.Start(slides: 10))).Payload.ErrorCode);
        Assert.Equal(ErrorCode.InvalidState, (await client.CommandAsync(Msg.Resume())).Payload.ErrorCode);
        Assert.Equal(ErrorCode.SlideOutOfRange, (await client.CommandAsync(Msg.SetSlide(99))).Payload.ErrorCode);
        Assert.Empty(harness.Keyboard.Sent);
    }

    [Fact]
    public async Task A_failed_command_echoes_the_correlation_id_and_keeps_the_session()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5));
        var bad = Msg.SetSlide(50);

        var result = await client.CommandAsync(bad);

        Assert.False(result.Payload.Ok);
        Assert.Equal(bad.Id, result.Payload.CorrelationId);
        Assert.Equal(1, harness.Service.Status.Session?.CurrentSlide);
    }
}
