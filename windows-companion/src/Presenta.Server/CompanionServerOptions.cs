using Presenta.Domain;

namespace Presenta.Server;

/// <summary>Settings of the companion server. Every default is the documented V1 value.</summary>
public sealed record CompanionServerOptions
{
    /// <summary>TCP port. 0 asks the operating system for a free port (used by tests).</summary>
    public int Port { get; init; } = 8765;

    /// <summary>False (default): listen on 127.0.0.1 only. True: listen on all interfaces so a physical watch can connect.</summary>
    public bool AllowLocalNetwork { get; init; }

    /// <summary>Fixed pairing code for tests. Null (default): a random 6-digit code is generated at startup.</summary>
    public string? PairingCode { get; init; }

    /// <summary>Largest accepted message in bytes. Larger frames close the connection.</summary>
    public int MaxFrameBytes { get; init; } = 16 * 1024;

    /// <summary>A connection that sends nothing for this long is closed. The watch should send a ping about every 2 seconds.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a running session survives without a watch before it ends as interrupted by disconnection.
    /// The recorded duration then includes this period.
    /// </summary>
    public TimeSpan DisconnectGracePeriod { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Consecutive wrong pairing codes from one address before it is locked out.</summary>
    public int LockoutThreshold { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Period of the internal timer that ticks the session, checks heartbeats and the grace period.</summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>While a watch is connected, a <c>state.update</c> is sent at least this often.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Records returned by <c>history.list</c> when the watch gives no limit.</summary>
    public int DefaultHistoryLimit { get; init; } = 50;

    /// <summary>Initial value of the "send keys to the presentation" switch.</summary>
    public bool SendKeys { get; init; } = true;

    /// <summary>Thresholds of the session rules. Null: domain defaults.</summary>
    public SessionOptions? SessionOptions { get; init; }
}
