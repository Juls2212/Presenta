using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

public class PairingTests
{
    [Fact]
    public async Task Correct_code_is_accepted_and_the_current_state_is_sent_immediately()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        var accepted = await client.PairAsync(ServerHarness.Code);

        Assert.Equal(1, accepted.Payload.ProtocolVersion);
        Assert.Null(accepted.Payload.State.Session); // explicit "no active session"
        Assert.Equal(harness.Time.GetUtcNow(), accepted.Payload.State.ServerTimeUtc);
        await Wait.UntilAsync(() => harness.Service.Status.WatchConnected, "the watch to be marked as connected");
    }

    [Fact]
    public async Task Wrong_code_is_rejected_with_invalid_pairing_code_and_the_connection_is_closed()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        await client.SendAsync(Msg.Hello("000000"));
        var rejected = await client.ReceiveAsync<HelloRejectedMessage>();

        Assert.Equal(ErrorCode.InvalidPairingCode, rejected.Payload.Reason);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.ExpectCloseAsync());
        Assert.False(harness.Service.Status.WatchConnected);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_out_the_address_until_the_lockout_expires()
    {
        await using var harness = await ServerHarness.StartAsync();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await harness.ConnectAsync(pair: false);
            await wrong.SendAsync(Msg.Hello("111111"));
            var rejected = await wrong.ReceiveAsync<HelloRejectedMessage>();
            Assert.Equal(ErrorCode.InvalidPairingCode, rejected.Payload.Reason);
        }

        var locked = await harness.ConnectAsync(pair: false);
        await locked.SendAsync(Msg.Hello(ServerHarness.Code)); // even the right code is refused while locked out
        var lockedOut = await locked.ReceiveAsync<HelloRejectedMessage>();
        Assert.Equal(ErrorCode.PairingLockedOut, lockedOut.Payload.Reason);
        Assert.InRange(lockedOut.Payload.RetryAfterMs ?? 0, 1, 30_000);

        harness.Time.Advance(TimeSpan.FromSeconds(30));

        var recovered = await harness.ConnectAsync(pair: false);
        var accepted = await recovered.PairAsync(ServerHarness.Code);
        Assert.Equal(1, accepted.Payload.ProtocolVersion);
    }

    [Fact]
    public async Task Lockout_duration_and_threshold_are_configurable()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { LockoutThreshold = 2, LockoutDuration = TimeSpan.FromSeconds(5) });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var wrong = await harness.ConnectAsync(pair: false);
            await wrong.SendAsync(Msg.Hello("111111"));
            await wrong.ReceiveAsync<HelloRejectedMessage>();
        }

        var locked = await harness.ConnectAsync(pair: false);
        await locked.SendAsync(Msg.Hello(ServerHarness.Code));
        Assert.Equal(ErrorCode.PairingLockedOut, (await locked.ReceiveAsync<HelloRejectedMessage>()).Payload.Reason);

        harness.Time.Advance(TimeSpan.FromSeconds(5));
        var recovered = await harness.ConnectAsync(pair: false);
        await recovered.PairAsync(ServerHarness.Code);
    }

    [Fact]
    public async Task A_successful_pairing_resets_the_failure_count()
    {
        await using var harness = await ServerHarness.StartAsync();
        for (var round = 0; round < 3; round++)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var wrong = await harness.ConnectAsync(pair: false);
                await wrong.SendAsync(Msg.Hello("111111"));
                Assert.Equal(ErrorCode.InvalidPairingCode, (await wrong.ReceiveAsync<HelloRejectedMessage>()).Payload.Reason);
            }

            var right = await harness.ConnectAsync(pair: false);
            await right.PairAsync(ServerHarness.Code);
        }
    }

    [Fact]
    public async Task Unsupported_protocol_version_in_hello_is_rejected()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        await client.SendAsync(Msg.Hello(ServerHarness.Code, protocolVersion: 2));

        var rejected = await client.ReceiveAsync<HelloRejectedMessage>();
        Assert.Equal(ErrorCode.UnsupportedVersion, rejected.Payload.Reason);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await client.ExpectCloseAsync());
    }

    [Fact]
    public async Task Unsupported_envelope_version_before_pairing_is_rejected()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        await client.SendRawAsync("""{ "version": 2, "type": "hello", "id": "x-1", "payload": { "pairingCode": "123456", "protocolVersion": 1, "clientName": "w" } }""");

        var rejected = await client.ReceiveAsync<HelloRejectedMessage>();
        Assert.Equal(ErrorCode.UnsupportedVersion, rejected.Payload.Reason);
    }

    [Fact]
    public async Task Command_before_hello_gets_not_paired_and_the_connection_stays_open()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);
        var next = Msg.Next();

        await client.SendAsync(next);
        var error = await client.ReceiveAsync<ErrorMessage>();

        Assert.Equal(ErrorCode.NotPaired, error.Payload.Code);
        Assert.Equal(next.Id, error.Payload.CorrelationId);
        Assert.Empty(harness.Keyboard.Sent);

        var accepted = await client.PairAsync(ServerHarness.Code); // the same connection can still pair
        Assert.Equal(1, accepted.Payload.ProtocolVersion);
    }

    [Fact]
    public async Task A_command_before_hello_never_reaches_the_domain_or_the_keyboard()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync(pair: false);

        await client.SendAsync(Msg.Start());
        await client.ReceiveAsync<ErrorMessage>();
        await client.SendAsync(Msg.Next());
        await client.ReceiveAsync<ErrorMessage>();

        Assert.Null(harness.Service.Status.Session);
        Assert.Empty(harness.Keyboard.Sent);
    }

    [Fact]
    public async Task Pairing_code_is_six_digits_and_regenerating_it_invalidates_the_old_one()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { PairingCode = null });
        var oldCode = harness.Service.PairingCode;
        Assert.Matches(new Regex("^[0-9]{6}$"), oldCode);

        harness.Service.RegeneratePairingCode();
        var newCode = harness.Service.PairingCode;

        Assert.Matches(new Regex("^[0-9]{6}$"), newCode);
        Assert.NotEqual(oldCode, newCode);
        var stale = await harness.ConnectAsync(pair: false);
        await stale.SendAsync(Msg.Hello(oldCode));
        Assert.Equal(ErrorCode.InvalidPairingCode, (await stale.ReceiveAsync<HelloRejectedMessage>()).Payload.Reason);
        var fresh = await harness.ConnectAsync(pair: false);
        await fresh.PairAsync(newCode);
    }

    [Fact]
    public async Task Regenerating_the_code_keeps_an_already_paired_watch_connected()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        harness.Service.RegeneratePairingCode();

        await client.SendAsync(Msg.Ping());
        await client.ReceiveUntilAsync<PongMessage>();
    }

    [Fact]
    public async Task The_pairing_code_never_appears_in_the_logs()
    {
        await using var harness = await ServerHarness.StartAsync();
        var wrong = await harness.ConnectAsync(pair: false);
        await wrong.SendAsync(Msg.Hello("654321"));
        await wrong.ReceiveAsync<HelloRejectedMessage>();
        var right = await harness.ConnectAsync();
        await right.CommandAsync(Msg.Start());
        harness.Service.RegeneratePairingCode();

        var logs = string.Join('\n', harness.Logs.Lines);

        Assert.DoesNotContain(ServerHarness.Code, logs);
        Assert.DoesNotContain("654321", logs);
        Assert.DoesNotContain(harness.Service.PairingCode, logs);
    }

    [Fact]
    public async Task Hello_on_an_already_paired_connection_is_an_error_not_a_second_pairing()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();

        await client.SendAsync(Msg.Hello(ServerHarness.Code));

        var error = await client.ReceiveUntilAsync<ErrorMessage>();
        Assert.Equal(ErrorCode.InvalidState, error.Payload.Code);
    }
}
