using Presenta.Domain.Keyboard;
using Presenta.Protocol;
using Presenta.Server.Tests.Support;

namespace Presenta.Server.Tests;

/// <summary>
/// Keys must reach the chosen presentation window or nothing at all, and the slide counter must never run ahead of the
/// presentation. These tests use the real WebSocket server with a fake keyboard that can report any delivery result.
/// </summary>
public class KeyTargetingTests
{
    [Theory]
    [InlineData(TargetState.NoneSelected)]
    [InlineData(TargetState.NotFound)]
    [InlineData(TargetState.Ambiguous)]
    [InlineData(TargetState.Elevated)]
    public async Task Start_is_rejected_when_keys_are_on_and_the_target_is_not_usable(TargetState state)
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        harness.Keyboard.State = state;

        var result = await client.CommandAsync(Msg.Start());

        Assert.False(result.Payload.Ok);
        Assert.Equal(ErrorCode.TargetWindowUnavailable, result.Payload.ErrorCode);
        Assert.Null(harness.Service.Status.Session);
    }

    [Theory]
    [InlineData(TargetState.Ready)]
    [InlineData(TargetState.Minimized)]
    public async Task Start_is_allowed_when_the_target_is_ready_or_only_minimized(TargetState state)
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        harness.Keyboard.State = state;

        Assert.True((await client.CommandAsync(Msg.Start())).Payload.Ok);
        Assert.NotNull(harness.Service.Status.Session);
    }

    [Fact]
    public async Task Start_needs_no_target_when_keys_are_off()
    {
        await using var harness = await ServerHarness.StartAsync(o => o with { SendKeys = false });
        var client = await harness.ConnectAsync();
        harness.Keyboard.State = TargetState.NoneSelected;

        Assert.True((await client.CommandAsync(Msg.Start())).Payload.Ok);

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.Equal(2, harness.Service.Status.Session!.CurrentSlide);
        Assert.Equal(0, harness.Keyboard.Attempts);
    }

    [Fact]
    public async Task A_rejected_start_can_be_retried_after_the_target_becomes_ready()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        harness.Keyboard.State = TargetState.NoneSelected;
        Assert.False((await client.CommandAsync(Msg.Start())).Payload.Ok);

        harness.Keyboard.State = TargetState.Ready;

        Assert.True((await client.CommandAsync(Msg.Start())).Payload.Ok);
    }

    [Fact]
    public async Task Next_and_previous_deliver_one_key_each_and_move_the_counter()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.True((await client.CommandAsync(Msg.Previous())).Payload.Ok);

        Assert.Equal(new[] { SlideDirection.Next, SlideDirection.Previous }, harness.Keyboard.Sent);
        Assert.Equal(1, harness.Service.Status.Session!.CurrentSlide);
    }

    [Theory]
    [InlineData(KeyDeliveryResult.NoTargetSelected, ErrorCode.TargetWindowUnavailable)]
    [InlineData(KeyDeliveryResult.TargetNotFound, ErrorCode.TargetWindowUnavailable)]
    [InlineData(KeyDeliveryResult.TargetAmbiguous, ErrorCode.TargetWindowUnavailable)]
    [InlineData(KeyDeliveryResult.TargetElevated, ErrorCode.TargetWindowUnavailable)]
    [InlineData(KeyDeliveryResult.ActivationFailed, ErrorCode.KeyDeliveryFailed)]
    [InlineData(KeyDeliveryResult.NotConfirmedForeground, ErrorCode.KeyDeliveryFailed)]
    [InlineData(KeyDeliveryResult.SendFailed, ErrorCode.KeyDeliveryFailed)]
    public async Task A_failed_delivery_keeps_the_counter_reports_the_code_and_the_session_continues(KeyDeliveryResult delivery, ErrorCode expected)
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 5));
        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        harness.Keyboard.Result = delivery;

        var failedNext = await client.CommandAsync(Msg.Next());
        var failedPrevious = await client.CommandAsync(Msg.Previous());

        Assert.False(failedNext.Payload.Ok);
        Assert.Equal(expected, failedNext.Payload.ErrorCode);
        Assert.Equal(expected, failedPrevious.Payload.ErrorCode);
        Assert.Equal(2, harness.Service.Status.Session!.CurrentSlide);
        Assert.Single(harness.Keyboard.Sent);

        harness.Keyboard.Result = KeyDeliveryResult.Delivered;
        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.Equal(3, harness.Service.Status.Session!.CurrentSlide);
    }

    [Fact]
    public async Task The_state_the_watch_receives_after_a_failed_key_still_shows_the_previous_slide()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        await client.ReceiveAsync<StateUpdateMessage>();
        harness.Keyboard.Result = KeyDeliveryResult.NotConfirmedForeground;

        await client.CommandAsync(Msg.Next());
        var update = await client.ReceiveAsync<StateUpdateMessage>();

        Assert.Equal(1, update.Payload.Session!.CurrentSlide);
    }

    [Fact]
    public async Task No_key_is_attempted_when_the_counter_would_not_change()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 2));

        Assert.True((await client.CommandAsync(Msg.Previous())).Payload.Ok); // already at slide 1
        Assert.Equal(0, harness.Keyboard.Attempts);

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        harness.Keyboard.Result = KeyDeliveryResult.TargetNotFound; // would fail if it were attempted
        var attemptsBefore = harness.Keyboard.Attempts;

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok); // already at the last slide
        Assert.Equal(attemptsBefore, harness.Keyboard.Attempts);
        Assert.Equal(2, harness.Service.Status.Session!.CurrentSlide);
    }

    [Fact]
    public async Task Slide_set_never_sends_a_key_and_never_needs_the_target()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start(slides: 10));
        harness.Keyboard.Result = KeyDeliveryResult.TargetNotFound;

        Assert.True((await client.CommandAsync(Msg.SetSlide(7))).Payload.Ok);

        Assert.Equal(7, harness.Service.Status.Session!.CurrentSlide);
        Assert.Equal(0, harness.Keyboard.Attempts);
    }

    [Fact]
    public async Task A_keyboard_that_throws_is_a_delivery_failure_not_a_moved_counter()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        harness.Keyboard.FailWith = new InvalidOperationException("injected failure");

        var result = await client.CommandAsync(Msg.Next());

        Assert.Equal(ErrorCode.KeyDeliveryFailed, result.Payload.ErrorCode);
        Assert.Equal(1, harness.Service.Status.Session!.CurrentSlide);
    }

    [Fact]
    public async Task Turning_keys_off_while_the_target_is_missing_lets_the_counter_move_without_a_key()
    {
        await using var harness = await ServerHarness.StartAsync();
        var client = await harness.ConnectAsync();
        await client.CommandAsync(Msg.Start());
        harness.Keyboard.Result = KeyDeliveryResult.TargetNotFound;
        Assert.False((await client.CommandAsync(Msg.Next())).Payload.Ok);

        harness.Service.SetSendKeys(false);

        Assert.True((await client.CommandAsync(Msg.Next())).Payload.Ok);
        Assert.Equal(2, harness.Service.Status.Session!.CurrentSlide);
        Assert.Equal(1, harness.Keyboard.Attempts);
    }
}
