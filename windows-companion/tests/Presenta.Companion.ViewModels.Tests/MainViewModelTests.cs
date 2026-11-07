using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Presenta.Companion.ViewModels;
using Presenta.Server;
using Presenta.TestSupport;

namespace Presenta.Companion.ViewModels.Tests;

/// <summary>Records posted actions and runs them on demand, to prove the view model goes through the dispatcher.</summary>
internal sealed class QueueDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    public int PostedCount { get; private set; }

    public void Post(Action action)
    {
        lock (_queue)
        {
            PostedCount++;
            _queue.Enqueue(action);
        }
    }

    public void RunPending()
    {
        while (true)
        {
            Action? next;
            lock (_queue)
            {
                next = _queue.Count > 0 ? _queue.Dequeue() : null;
            }

            if (next is null)
            {
                return;
            }

            next();
        }
    }
}

/// <summary>A minimal watch: opens a WebSocket and sends raw JSON.</summary>
internal sealed class WatchStub : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private int _id;

    public static async Task<WatchStub> ConnectAsync(int port, string code = "123456")
    {
        var watch = new WatchStub();
        await watch._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await watch.SendAsync("hello", $$"""{ "pairingCode": "{{code}}", "protocolVersion": 1, "clientName": "stub" }""");
        await watch.ReceiveAsync();
        return watch;
    }

    public Task SendAsync(string type, string payload = "{}") =>
        _socket.SendAsync(
            Encoding.UTF8.GetBytes($$"""{ "version": 1, "type": "{{type}}", "id": "v-{{Interlocked.Increment(ref _id)}}", "payload": {{payload}} }"""),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

    /// <summary>Sends a command and waits for its command.result, so the server has finished it.</summary>
    public async Task CommandAsync(string type, string payload = "{}")
    {
        await SendAsync(type, payload);
        var expected = $"\"correlationId\":\"v-{_id}\"";
        while (!(await ReceiveAsync()).Contains(expected, StringComparison.Ordinal))
        {
        }
    }

    public async Task<string> ReceiveAsync()
    {
        var buffer = new byte[16 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await _socket.ReceiveAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    public async Task CloseAsync()
    {
        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Abort();
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class MainViewModelTests
{
    private static async Task UntilAsync(Func<bool> condition, string because)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), "Timed out waiting for: " + because);
            await Task.Delay(10);
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public ManualTimeProvider Time { get; } = new();

        public RecordingSlideKeyboard Keyboard { get; } = new();

        public CompanionHost Host { get; }

        public MainViewModel ViewModel { get; }

        public Rig(CompanionServerOptions? options = null, IUiDispatcher? dispatcher = null, int maxEvents = MainViewModel.DefaultMaxEvents)
        {
            Host = new CompanionHost(
                options ?? new CompanionServerOptions { Port = 0, PairingCode = "123456", HeartbeatTimeout = TimeSpan.FromHours(2) },
                Keyboard,
                new InMemoryHistoryStore(),
                Time);
            ViewModel = new MainViewModel(
                Host,
                dispatcher ?? new ImmediateDispatcher(),
                () => new[] { "192.168.1.20" },
                maxEvents,
                TimeZoneInfo.Utc);
        }

        public async Task StartAsync() => await ViewModel.StartAsync();

        public async ValueTask DisposeAsync()
        {
            ViewModel.Dispose();
            await Host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Initial_state_shows_a_stopped_server_a_waiting_watch_and_no_session()
    {
        await using var rig = new Rig();
        var vm = rig.ViewModel;

        Assert.False(vm.IsServerRunning);
        Assert.Equal(UiText.Get("Server_Stopped"), vm.ServerStatusText);
        Assert.Equal(UiText.Get("Server_Start"), vm.ToggleServerText);
        Assert.Equal(UiText.Get("Watch_Waiting"), vm.WatchStatusText);
        Assert.False(vm.WatchConnected);
        Assert.False(vm.HasSession);
        Assert.True(vm.HasNoSession);
        Assert.Equal(rig.Host.Service.PairingCode, vm.PairingCode);
        Assert.Empty(vm.Events);
        Assert.False(vm.HasServerError);
    }

    [Fact]
    public async Task Starting_and_stopping_the_server_updates_the_status_text()
    {
        await using var rig = new Rig();
        var vm = rig.ViewModel;

        vm.ToggleServerCommand.Execute(null);
        await vm.LastOperation!;
        Assert.True(vm.IsServerRunning);
        Assert.Equal(UiText.Get("Server_Running"), vm.ServerStatusText);
        Assert.Equal(UiText.Get("Server_Stop"), vm.ToggleServerText);
        Assert.NotEqual(0, vm.Port);

        vm.ToggleServerCommand.Execute(null);
        await vm.LastOperation!;
        Assert.False(vm.IsServerRunning);
        Assert.Equal(UiText.Get("Server_Stopped"), vm.ServerStatusText);
    }

    [Fact]
    public async Task A_busy_port_shows_an_error_and_leaves_the_server_stopped()
    {
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var busyPort = ((IPEndPoint)blocker.LocalEndpoint).Port;
        await using var rig = new Rig(new CompanionServerOptions { Port = busyPort, PairingCode = "123456" });

        await rig.StartAsync();

        Assert.False(rig.ViewModel.IsServerRunning);
        Assert.True(rig.ViewModel.HasServerError);
        Assert.StartsWith(UiText.Get("Server_Error").Split('{')[0], rig.ViewModel.ServerError);
    }

    [Fact]
    public async Task Regenerating_the_code_changes_the_displayed_code()
    {
        await using var rig = new Rig(new CompanionServerOptions { Port = 0 });
        var before = rig.ViewModel.PairingCode;

        rig.ViewModel.RegenerateCodeCommand.Execute(null);

        Assert.NotEqual(before, rig.ViewModel.PairingCode);
        Assert.Equal(rig.Host.Service.PairingCode, rig.ViewModel.PairingCode);
        Assert.Equal(6, rig.ViewModel.PairingCode.Length);
    }

    [Fact]
    public async Task The_send_keys_toggle_controls_the_service_and_mirrors_it()
    {
        await using var rig = new Rig();
        Assert.True(rig.ViewModel.SendKeys); // on by default

        rig.ViewModel.SendKeys = false;
        Assert.False(rig.Host.Service.SendKeys);

        rig.Host.Service.SetSendKeys(true);
        Assert.True(rig.ViewModel.SendKeys);
    }

    [Fact]
    public async Task The_addresses_list_always_shows_the_emulator_and_localhost_addresses()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var port = rig.Host.Port.ToString();

        Assert.Contains(UiText.Format("Address_Emulator", port), rig.ViewModel.Addresses);
        Assert.Contains(UiText.Format("Address_Localhost", port), rig.ViewModel.Addresses);
        Assert.Contains(UiText.Get("Address_LanOff"), rig.ViewModel.Addresses);
        Assert.DoesNotContain(rig.ViewModel.Addresses, a => a.Contains("192.168.1.20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Allowing_the_local_network_rebinds_the_server_and_lists_the_lan_address()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        var port = rig.Host.Port;

        rig.ViewModel.AllowLocalNetwork = true;
        await rig.ViewModel.LastOperation!;

        Assert.Equal(IPAddress.Any, Assert.Single(rig.Host.BoundEndpoints).Address);
        Assert.Equal(port, rig.Host.Port);
        Assert.Contains(UiText.Format("Address_Lan", "192.168.1.20", port.ToString()), rig.ViewModel.Addresses);
        Assert.DoesNotContain(UiText.Get("Address_LanOff"), rig.ViewModel.Addresses);

        rig.ViewModel.AllowLocalNetwork = false;
        await rig.ViewModel.LastOperation!;
        Assert.Equal(IPAddress.Loopback, Assert.Single(rig.Host.BoundEndpoints).Address);
        Assert.Contains(UiText.Get("Address_LanOff"), rig.ViewModel.Addresses);
    }

    [Fact]
    public async Task The_toggle_can_be_set_before_the_server_starts()
    {
        await using var rig = new Rig();

        rig.ViewModel.AllowLocalNetwork = true;
        await rig.ViewModel.LastOperation!;
        await rig.StartAsync();

        Assert.Equal(IPAddress.Any, Assert.Single(rig.Host.BoundEndpoints).Address);
    }

    [Fact]
    public async Task The_watch_status_follows_the_connection()
    {
        await using var rig = new Rig();
        await rig.StartAsync();

        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await UntilAsync(() => rig.ViewModel.WatchConnected, "the watch to show as connected");
        Assert.Equal(UiText.Get("Watch_Connected"), rig.ViewModel.WatchStatusText);

        await watch.CloseAsync();
        await UntilAsync(() => !rig.ViewModel.WatchConnected, "the watch to show as waiting again");
        Assert.Equal(UiText.Get("Watch_Waiting"), rig.ViewModel.WatchStatusText);
    }

    [Fact]
    public async Task An_active_session_shows_its_name_slide_and_remaining_time()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);

        await watch.CommandAsync("session.start", """{ "plannedSlides": 20, "plannedDurationMs": 900000, "name": "Demo" }""");
        await watch.CommandAsync("slide.next");
        rig.Time.Advance(TimeSpan.FromSeconds(30));
        await UntilAsync(() => rig.ViewModel.SessionRemainingText == UiText.Format("Session_Remaining", "14:30"), "the remaining time to update");

        var vm = rig.ViewModel;
        Assert.True(vm.HasSession);
        Assert.False(vm.HasNoSession);
        Assert.Equal("Demo", vm.SessionName);
        Assert.Equal(UiText.Format("Session_Slide", 2, 20), vm.SessionSlideText);
        Assert.False(vm.SessionPaused);
        Assert.False(vm.SessionOvertime);
    }

    [Fact]
    public async Task A_paused_session_is_flagged()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await watch.CommandAsync("session.start", """{ "plannedSlides": 5, "plannedDurationMs": 60000 }""");

        await watch.CommandAsync("session.pause");

        await UntilAsync(() => rig.ViewModel.SessionPaused, "the session to show as paused");
    }

    [Fact]
    public async Task Overtime_is_shown_with_its_own_text()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await watch.CommandAsync("session.start", """{ "plannedSlides": 5, "plannedDurationMs": 60000 }""");

        rig.Time.Advance(TimeSpan.FromSeconds(75));

        await UntilAsync(() => rig.ViewModel.SessionOvertime, "the session to show overtime");
        Assert.Equal(UiText.Format("Session_Overtime", "00:15"), rig.ViewModel.SessionRemainingText);
    }

    [Fact]
    public async Task A_finished_session_clears_the_session_area()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await watch.CommandAsync("session.start", """{ "plannedSlides": 5, "plannedDurationMs": 60000 }""");
        await UntilAsync(() => rig.ViewModel.HasSession, "the session to appear");

        await watch.CommandAsync("session.finish");

        await UntilAsync(() => !rig.ViewModel.HasSession, "the session area to clear");
        Assert.Equal(string.Empty, rig.ViewModel.SessionName);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(1_999, "00:01")]
    [InlineData(872_000, "14:32")]
    [InlineData(3_600_000, "1:00:00")]
    [InlineData(3_725_000, "1:02:05")]
    public void Remaining_time_is_formatted_as_minutes_and_seconds_or_hours(long milliseconds, string expected)
    {
        Assert.Equal(expected, MainViewModel.FormatClock(milliseconds));
    }

    [Fact]
    public async Task The_activity_list_is_limited_and_newest_first()
    {
        await using var rig = new Rig(maxEvents: 3);
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await watch.CommandAsync("session.start", """{ "plannedSlides": 50, "plannedDurationMs": 600000 }""");
        for (var i = 0; i < 5; i++)
        {
            await watch.CommandAsync("slide.next");
        }

        await UntilAsync(
            () => rig.ViewModel.Events.Count == 3
                && rig.ViewModel.Events.All(e => e.Contains(UiText.Get("Event_NextKeySent"), StringComparison.Ordinal)),
            "the list to hold the last three events");
        Assert.True(rig.ViewModel.HasEvents);
        Assert.Equal(3, rig.ViewModel.Events.Count);
    }

    [Fact]
    public async Task Activity_lines_start_with_the_time_of_the_event()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);

        await UntilAsync(() => rig.ViewModel.Events.Count > 0, "the connection event");

        Assert.StartsWith("14:30:00", rig.ViewModel.Events[0]);
        Assert.Contains(UiText.Get("Event_WatchConnected"), rig.ViewModel.Events[0]);
    }

    [Fact]
    public async Task With_keys_off_the_list_says_no_key_was_sent()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        rig.ViewModel.SendKeys = false;
        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await watch.CommandAsync("session.start", """{ "plannedSlides": 5, "plannedDurationMs": 60000 }""");

        await watch.CommandAsync("slide.next");

        await UntilAsync(
            () => rig.ViewModel.Events.Any(e => e.Contains(UiText.Format("Event_SlideCounterChanged", 2), StringComparison.Ordinal)),
            "the no-key event");
        Assert.Empty(rig.Keyboard.Sent);
    }

    [Fact]
    public async Task Service_events_reach_the_view_model_only_through_the_dispatcher()
    {
        var dispatcher = new QueueDispatcher();
        await using var rig = new Rig(dispatcher: dispatcher);
        await rig.StartAsync();

        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await UntilAsync(() => dispatcher.PostedCount > 0, "the service to post an update");

        Assert.False(rig.ViewModel.WatchConnected); // not applied yet: it is waiting on the "UI thread"
        dispatcher.RunPending();
        Assert.True(rig.ViewModel.WatchConnected);
        Assert.NotEmpty(rig.ViewModel.Events);
    }

    [Fact]
    public void Every_activity_kind_and_every_label_has_a_string_in_the_resource_file()
    {
        foreach (var kind in Enum.GetValues<ActivityKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(UiText.Get("Event_" + kind)), $"Missing text for {kind}.");
        }

        var labels = new UiLabels();
        foreach (var property in typeof(UiLabels).GetProperties())
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(labels)), $"Empty label {property.Name}.");
        }
    }

    [Fact]
    public void A_missing_key_is_reported_clearly()
    {
        Assert.Throws<InvalidOperationException>(() => UiText.Get("No_Such_Key"));
    }

    [Fact]
    public async Task Disposing_the_view_model_stops_it_listening_to_the_service()
    {
        await using var rig = new Rig();
        await rig.StartAsync();
        rig.ViewModel.Dispose();

        await using var watch = await WatchStub.ConnectAsync(rig.Host.Port);
        await Task.Delay(100);

        Assert.False(rig.ViewModel.WatchConnected);
    }
}
