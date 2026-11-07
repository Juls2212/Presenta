namespace Presenta.Domain;

/// <summary>Lifecycle state of a presentation session.</summary>
public enum SessionState
{
    Configured,
    Running,
    Paused,
    Finished,
}

/// <summary>How a session ended. Only meaningful once the session is finished.</summary>
public enum FinalState
{
    FinishedManually,
    EndedWhilePaused,
    InterruptedByDisconnection,
}

/// <summary>Comparison of the actual slide with the expected slide at the current time.</summary>
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

/// <summary>Failure reasons returned by domain operations. Names map to protocol error codes (see protocol-v1.md).</summary>
public enum SessionErrorCode
{
    /// <summary>The operation is not allowed in the current session state. Maps to <c>invalidState</c>.</summary>
    InvalidState,

    /// <summary>The requested slide is outside 1..totalSlides. Maps to <c>slideOutOfRange</c>.</summary>
    SlideOutOfRange,

    /// <summary>The session configuration violates a limit. Maps to <c>invalidMessage</c>.</summary>
    InvalidConfiguration,
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
