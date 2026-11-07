package com.presenta.wear.model

/*
 * UI-side models. Names mirror shared-contracts/protocol-v1.md; there is no network or JSON code here.
 * Durations are milliseconds. Remaining time is negative in overtime.
 */

enum class ConnectionState { Connected, Reconnecting, Disconnected }

enum class SessionState { Configured, Running, Paused, Finished }

enum class FinalState { FinishedManually, EndedWhilePaused, InterruptedByDisconnection }

enum class PaceStatus { OnTrack, Behind, TooFast, NotEnoughData }

enum class AlertKind { TimeRunningOut, TimeCritical, Overtime, BehindSchedule, TooFast }

enum class AlertSeverity { Info, Warning, Critical }

enum class RecommendationCode { OnTrack, SpeedUpSlightly, SpeedUpNow, SlowDown, WrapUp, Overtime }

/** Error codes of protocol v1. */
enum class ErrorCode {
    InvalidPairingCode,
    PairingLockedOut,
    NotPaired,
    UnsupportedVersion,
    InvalidMessage,
    InvalidState,
    SlideOutOfRange,
    NoActiveSession,
    TargetWindowUnavailable,
    KeyDeliveryFailed,
    InternalError,
}

data class Alert(
    val kind: AlertKind,
    val severity: AlertSeverity,
    val raisedAtElapsedMs: Long,
    val remainingMs: Long,
    val slidesRemaining: Int,
    val slideDeviation: Int,
)

data class Recommendation(
    val code: RecommendationCode,
    val slidesRemaining: Int,
    val timeRemainingMs: Long,
)

/** Snapshot of one session as the companion reports it. [elapsedMs] is the value at the time of the snapshot. */
data class SessionSnapshot(
    val state: SessionState,
    val name: String,
    val currentSlide: Int,
    val totalSlides: Int,
    val plannedDurationMs: Long,
    val elapsedMs: Long,
    val remainingMs: Long,
    val running: Boolean,
    val progressRatio: Float,
    val paceStatus: PaceStatus,
    val slidesRemaining: Int,
    val averageSlidesPerMinute: Double,
    val finalState: FinalState?,
    val activeAlert: Alert?,
    val recommendation: Recommendation?,
)

/** What the watch knows about the session. [NoSession] is the explicit "no active session". */
sealed interface SessionStatus {
    data object NoSession : SessionStatus

    /** [receivedAtMs] is the monotonic time at which the snapshot arrived; the UI interpolates from it. */
    data class Active(val snapshot: SessionSnapshot, val receivedAtMs: Long) : SessionStatus
}

/** A finished session. Also the content of a session summary. */
data class HistoryItem(
    val name: String,
    val finishedAtEpochMs: Long,
    val plannedDurationMs: Long,
    val actualDurationMs: Long,
    val plannedSlides: Int,
    val registeredSlides: Int,
    val averageSlidesPerMinute: Double,
    val finalState: FinalState,
    val endedWithinPlannedTime: Boolean,
)

/**
 * Where pairing with the companion stands. [NeedsCode] means no code is stored yet; [Rejected] carries the reason the
 * companion gave (wrong code, locked out, unsupported version). The pairing screen is shown for both.
 */
sealed interface PairingState {
    data object NeedsCode : PairingState

    data object Connecting : PairingState

    data object Paired : PairingState

    data class Rejected(val code: ErrorCode) : PairingState
}

sealed interface CommandResult {
    data object Success : CommandResult

    data class Failure(val code: ErrorCode) : CommandResult
}

sealed interface HistoryResult {
    data class Success(val items: List<HistoryItem>) : HistoryResult

    data class Failure(val code: ErrorCode) : HistoryResult
}

/** Limits of the setup screen. Defaults are the ones shown in the mockup. */
object SetupLimits {
    const val MIN_SLIDES = 1
    const val MAX_SLIDES = 200
    const val DEFAULT_SLIDES = 20
    const val MIN_MINUTES = 1
    const val MAX_MINUTES = 180
    const val DEFAULT_MINUTES = 15
}
