using Presenta.Domain;

namespace Presenta.Server;

/// <summary>What the desktop window needs to show. Immutable; replaced on every change.</summary>
/// <param name="WatchConnected">True while a paired watch is connected.</param>
/// <param name="PairingCode">The current pairing code.</param>
/// <param name="SendKeys">Whether Next and Previous press keys on this computer.</param>
/// <param name="Session">The active session, or null when there is none.</param>
public sealed record CompanionStatus(bool WatchConnected, string PairingCode, bool SendKeys, SessionSnapshot? Session);

/// <summary>Kinds of events shown in the activity list of the window. Mapped to text by the UI only.</summary>
public enum ActivityKind
{
    WatchConnected,
    WatchDisconnected,
    WatchReplaced,
    PairingRejected,
    PairingLockedOut,
    SessionStarted,
    SessionPaused,
    SessionResumed,
    SessionFinished,
    SessionInterrupted,
    NextKeySent,
    PreviousKeySent,
    SlideCounterChanged,
    SlideSet,
    AlertRaised,
    KeyFailed,
    StartRejectedNoTarget,
}

/// <summary>One entry of the activity list.</summary>
/// <param name="At">UTC time of the event.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Slide">The slide counter after the event, when relevant.</param>
public sealed record CompanionActivity(DateTimeOffset At, ActivityKind Kind, int? Slide = null);
