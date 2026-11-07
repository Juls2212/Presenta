namespace Presenta.Protocol;

/// <summary>
/// Error codes of protocol v1. The wire form is the lowerCamelCase name. The same list appears in
/// <c>shared-contracts/schemas/common.schema.json</c> and <c>shared-contracts/protocol-v1.md</c>.
/// </summary>
public enum ErrorCode
{
    /// <summary>The pairing code in <c>hello</c> does not match.</summary>
    InvalidPairingCode,

    /// <summary>Too many wrong pairing codes; further attempts are refused until the lockout ends.</summary>
    PairingLockedOut,

    /// <summary>A command was sent before a successful <c>hello</c>.</summary>
    NotPaired,

    /// <summary>The envelope version or the client protocol version is not supported.</summary>
    UnsupportedVersion,

    /// <summary>The message is not valid JSON, violates the envelope or payload rules, or has an unknown type.</summary>
    InvalidMessage,

    /// <summary>The command is not allowed in the current session state.</summary>
    InvalidState,

    /// <summary>The requested slide is outside 1..totalSlides.</summary>
    SlideOutOfRange,

    /// <summary>The command needs an active session and there is none.</summary>
    NoActiveSession,

    /// <summary>
    /// Keys are on, but there is no usable presentation window: none chosen, it cannot be found, several windows match,
    /// or it belongs to an elevated process. Additive in protocol v1.
    /// </summary>
    TargetWindowUnavailable,

    /// <summary>
    /// The presentation window exists but the key could not be delivered (Windows refused to bring it to the front, it did
    /// not become the foreground window in time, or the key was not accepted). Additive in protocol v1.
    /// </summary>
    KeyDeliveryFailed,

    /// <summary>Unexpected failure on the companion.</summary>
    InternalError,
}

public enum SessionState
{
    Configured,
    Running,
    Paused,
    Finished,
}

public enum FinalState
{
    FinishedManually,
    EndedWhilePaused,
    InterruptedByDisconnection,
}

public enum PaceStatus
{
    OnTrack,
    Behind,
    TooFast,
    NotEnoughData,
}

public enum AlertKind
{
    TimeRunningOut,
    TimeCritical,
    Overtime,
    BehindSchedule,
    TooFast,
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public enum RecommendationCode
{
    OnTrack,
    SpeedUpSlightly,
    SpeedUpNow,
    SlowDown,
    WrapUp,
    Overtime,
}
