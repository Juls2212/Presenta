using System.Globalization;
using Presenta.Domain.Keyboard;
using Presenta.Persistence;
using Presenta.Server;

// Developer host of the companion server, for automated cross-stack tests and diagnostics.
// It runs the real Presenta.Server but with a keyboard that only prints what it would press.
//
//   dotnet Presenta.DevHost.dll --code 123456 [--port 0] [--database path] [--grace-seconds 300] [--heartbeat-seconds 15]
//
// Output (one line each, flushed): LISTENING port=N, KEY Right|Left, REBOUND port=N, STOPPED.
// Input (stdin): "rebind" restarts the listener on the same port (connections drop, the session survives); "quit" or
// end of input shuts down cleanly.

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int ArgInt(string name, int fallback) =>
    int.TryParse(ArgValue(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

var code = ArgValue("--code");
if (code is null)
{
    Console.Error.WriteLine("Usage: Presenta.DevHost --code <6 digits> [--port N] [--database path] [--grace-seconds N] [--heartbeat-seconds N]");
    return 2;
}

var databasePath = ArgValue("--database") ?? Path.Combine(Path.GetTempPath(), "presenta-devhost-" + Guid.NewGuid().ToString("N") + ".db");
var options = new CompanionServerOptions
{
    Port = ArgInt("--port", 0),
    PairingCode = code,
    DisconnectGracePeriod = TimeSpan.FromSeconds(ArgInt("--grace-seconds", 300)),
    HeartbeatTimeout = TimeSpan.FromSeconds(ArgInt("--heartbeat-seconds", 15)),
};

var history = await SqliteHistoryStore.OpenAsync(databasePath);
await using var host = new CompanionHost(options, new PrintingKeyboard(), history);
await host.StartAsync();
Console.WriteLine($"LISTENING port={host.Port}");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

try
{
    while (!shutdown.IsCancellationRequested)
    {
        var line = await Console.In.ReadLineAsync(shutdown.Token);
        if (line is null || line.Trim() == "quit")
        {
            break;
        }

        if (line.Trim() == "rebind")
        {
            await host.RebindAsync(host.AllowLocalNetwork);
            Console.WriteLine($"REBOUND port={host.Port}");
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C: fall through to a clean shutdown.
}

await host.StopAsync();
Console.WriteLine("STOPPED");
return 0;

/// <summary>Prints the key that would be pressed. Never touches the real keyboard.</summary>
internal sealed class PrintingKeyboard : ISlideKeyboard
{
    public KeyDeliveryResult Send(SlideDirection direction)
    {
        Console.WriteLine("KEY " + (direction == SlideDirection.Next ? "Right" : "Left"));
        return KeyDeliveryResult.Delivered;
    }

    public TargetState GetTargetState() => TargetState.Ready;
}
