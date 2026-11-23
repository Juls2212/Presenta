using System.Globalization;
using Presenta.DevHost;
using Presenta.Ai;
using Presenta.Domain.Keyboard;
using Presenta.Persistence;
using Presenta.Server;

// Developer host of the companion server, for automated cross-stack tests and diagnostics.
// It runs the real Presenta.Server but with a keyboard that only prints what it would press.
//
//   dotnet Presenta.DevHost.dll [--code 123456] [--port 0] [--database path] [--grace-seconds 300] [--heartbeat-seconds 15]
//                               [--time-scale 1] [--settings path] [--active-session path] [--fake-ai]
//
// --fake-ai turns the optional AI wording on with a canned client that never touches the network: while an alert is
// active the recommendation carries the text "Fake AI tip: <code>" in its freeText, so the watch's path can be tested.
//
// Without --code the pairing code is random (or the remembered one when --settings points to a file that remembers it).
// --settings uses the same settings file as the companion (ADR-69); --active-session uses the same crash-safe active
// session file (ADR-67), so a killed host is recovered by the next one. The code is never printed.
//
// --time-scale N runs the companion's clock N times faster than real time (N is a whole number from 1 to 1000): a session
// of 15 minutes lasts 15 minutes divided by N. The grace period and the heartbeat timeout are given in real seconds, as
// the watch experiences them, and are multiplied by N so they keep their meaning. This exists only in this tool; the
// shipped companion always uses the system clock.
//
// Output (one line each, flushed): LISTENING port=N, KEY Right|Left, REBOUND port=N, STOPPED.
// Input (stdin): "rebind" restarts the listener on the same port (connections drop, the session survives); "regenerate"
// creates a new pairing code (prints REGENERATED); "quit" or end of input shuts down cleanly.

string? ArgValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int ArgInt(string name, int fallback) =>
    int.TryParse(ArgValue(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

var code = ArgValue("--code");
var settingsPath = ArgValue("--settings");
var activeSessionPath = ArgValue("--active-session");

var timeScale = ArgInt("--time-scale", 1);
if (timeScale is < 1 or > 1000)
{
    Console.Error.WriteLine("--time-scale must be a whole number from 1 to 1000.");
    return 2;
}

var databasePath = ArgValue("--database") ?? Path.Combine(Path.GetTempPath(), "presenta-devhost-" + Guid.NewGuid().ToString("N") + ".db");
var options = new CompanionServerOptions
{
    Port = ArgInt("--port", 0),
    PairingCode = code,
    DisconnectGracePeriod = TimeSpan.FromSeconds(ArgInt("--grace-seconds", 300) * (double)timeScale),
    HeartbeatTimeout = TimeSpan.FromSeconds(ArgInt("--heartbeat-seconds", 15) * (double)timeScale),
};

TimeProvider time = timeScale == 1 ? TimeProvider.System : new ScaledTimeProvider(TimeProvider.System, timeScale);
var history = await SqliteHistoryStore.OpenAsync(databasePath);
ICompanionSettingsStore? settings = settingsPath is null ? null : new JsonCompanionSettingsStore(settingsPath);
IActiveSessionStore? activeSessions = activeSessionPath is null ? null : new JsonActiveSessionStore(activeSessionPath);
AiRecommendationEnricher? ai = null;
if (Array.IndexOf(args, "--fake-ai") >= 0)
{
    ai = new AiRecommendationEnricher(new CannedAiTextClient(), AiAvailability.Available, new AiEnrichmentOptions(), time) { Enabled = true };
}

await using var host = new CompanionHost(options, new PrintingKeyboard(), history, time, activeSessions: activeSessions, settings: settings, ai: ai);
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
        else if (line.Trim() == "regenerate")
        {
            host.Service.RegeneratePairingCode();
            Console.WriteLine("REGENERATED");
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
