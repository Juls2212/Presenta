package com.presenta.wear.data.fake

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.PresentaRepository
import com.presenta.wear.model.Alert
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.Recommendation
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.model.SessionSnapshot
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import java.time.Clock
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Deterministic stand-in for the companion. It keeps a trivial slide counter and timer so the UI flow can be
 * exercised, but it does NOT implement pace or alert rules: pace, alerts and recommendations are scripted by the
 * caller ([scriptPace], [raiseAlert]) exactly as the real companion would report them.
 * All time comes from the injected clocks; nothing sleeps.
 */
class FakePresentaRepository(
    private val monotonic: MonotonicClock,
    private val wallClock: Clock,
    initialConnection: ConnectionState = ConnectionState.Connected,
    initialHistory: List<HistoryItem> = FakeData.history(wallClock),
) : PresentaRepository {

    private val _connection = MutableStateFlow(initialConnection)
    override val connection: StateFlow<ConnectionState> = _connection.asStateFlow()

    private val _session = MutableStateFlow<SessionStatus>(SessionStatus.NoSession)
    override val session: StateFlow<SessionStatus> = _session.asStateFlow()

    private val _summaries = MutableSharedFlow<HistoryItem>(extraBufferCapacity = 8)
    override val summaries: Flow<HistoryItem> = _summaries.asSharedFlow()

    private val _pairing = MutableStateFlow<PairingState>(PairingState.Paired)
    override val pairing: StateFlow<PairingState> = _pairing.asStateFlow()

    /** The last code passed to [connectWithCode], for tests. */
    var lastConnectCode: String? = null
        private set

    private var history: List<HistoryItem> = initialHistory
    private var historyFailure: ErrorCode? = null
    private var nextCommandFailure: ErrorCode? = null

    private var name = ""
    private var totalSlides = 0
    private var plannedMs = 0L
    private var currentSlide = 1
    private var accumulatedMs = 0L
    private var runStartedAtMs = 0L
    private var state = SessionState.Configured
    private var pace = PaceStatus.NotEnoughData
    private var alert: Alert? = null
    private var retryCount = 0

    val retryRequests: Int get() = retryCount

    // ---- scripting hooks (tests and previews) ----

    fun setConnection(value: ConnectionState) {
        _connection.value = value
    }

    fun setPairing(state: PairingState) {
        _pairing.value = state
    }

    fun setHistory(items: List<HistoryItem>) {
        history = items
    }

    fun failHistoryWith(code: ErrorCode?) {
        historyFailure = code
    }

    /** The next command fails with [code], then behavior returns to normal. */
    fun failNextCommandWith(code: ErrorCode?) {
        nextCommandFailure = code
    }

    fun scriptPace(status: PaceStatus) {
        pace = status
        publish()
    }

    fun raiseAlert(value: Alert?) {
        alert = value
        publish()
    }

    /** Re-publishes the current snapshot with the elapsed time at this moment (what a periodic update does). */
    fun refresh() = publish()

    /** Ends the session as if the connection had been lost for too long. */
    fun interruptSession() {
        if (state == SessionState.Running || state == SessionState.Paused) {
            endSession(FinalState.InterruptedByDisconnection)
        }
    }

    // ---- PresentaRepository ----

    override suspend fun startSession(name: String, plannedSlides: Int, plannedDurationMs: Long): CommandResult =
        command {
            if (state == SessionState.Running || state == SessionState.Paused) {
                return@command fail(ErrorCode.InvalidState)
            }
            this.name = name
            totalSlides = plannedSlides
            plannedMs = plannedDurationMs
            currentSlide = 1
            accumulatedMs = 0
            runStartedAtMs = monotonic.nowMs()
            state = SessionState.Running
            pace = PaceStatus.NotEnoughData
            alert = null
            publish()
            CommandResult.Success
        }

    override suspend fun pause(): CommandResult = command {
        requireSession()?.let { return@command it }
        if (state != SessionState.Running) return@command fail(ErrorCode.InvalidState)
        accumulatedMs = elapsedNow()
        state = SessionState.Paused
        publish()
        CommandResult.Success
    }

    override suspend fun resume(): CommandResult = command {
        requireSession()?.let { return@command it }
        if (state != SessionState.Paused) return@command fail(ErrorCode.InvalidState)
        runStartedAtMs = monotonic.nowMs()
        state = SessionState.Running
        publish()
        CommandResult.Success
    }

    override suspend fun finish(): CommandResult = command {
        requireSession()?.let { return@command it }
        endSession(if (state == SessionState.Paused) FinalState.EndedWhilePaused else FinalState.FinishedManually)
        CommandResult.Success
    }

    override suspend fun next(): CommandResult = command { moveTo(currentSlide + 1, clamp = true) }

    override suspend fun previous(): CommandResult = command { moveTo(currentSlide - 1, clamp = true) }

    override suspend fun setSlide(slide: Int): CommandResult = command { moveTo(slide, clamp = false) }

    override suspend fun acknowledgeAlert(): CommandResult = command {
        alert = null
        if (state == SessionState.Running || state == SessionState.Paused) publish()
        CommandResult.Success
    }

    override suspend fun listHistory(): HistoryResult {
        historyFailure?.let { return HistoryResult.Failure(it) }
        return HistoryResult.Success(history.sortedByDescending { it.finishedAtEpochMs })
    }

    override suspend fun savedPairingCode(): String? = lastConnectCode

    override fun connectWithCode(code: String) {
        lastConnectCode = code
        _pairing.value = PairingState.Paired
        _connection.value = ConnectionState.Connected
    }

    override fun retryConnection() {
        retryCount++
        if (_connection.value == ConnectionState.Disconnected) {
            _connection.value = ConnectionState.Reconnecting
        }
    }

    // ---- internals ----

    private inline fun command(block: () -> CommandResult): CommandResult {
        nextCommandFailure?.let {
            nextCommandFailure = null
            return CommandResult.Failure(it)
        }
        if (_connection.value != ConnectionState.Connected) return CommandResult.Failure(ErrorCode.InternalError)
        return block()
    }

    private fun fail(code: ErrorCode) = CommandResult.Failure(code)

    private fun requireSession(): CommandResult? =
        if (state == SessionState.Running || state == SessionState.Paused) null else fail(ErrorCode.NoActiveSession)

    private fun moveTo(target: Int, clamp: Boolean): CommandResult {
        requireSession()?.let { return it }
        val value = if (clamp) target.coerceIn(1, totalSlides) else target
        if (value !in 1..totalSlides) return fail(ErrorCode.SlideOutOfRange)
        currentSlide = value
        publish()
        return CommandResult.Success
    }

    private fun elapsedNow(): Long =
        if (state == SessionState.Running) accumulatedMs + (monotonic.nowMs() - runStartedAtMs) else accumulatedMs

    private fun endSession(finalState: FinalState) {
        val elapsed = elapsedNow()
        val minutes = elapsed / 60_000.0
        val item = HistoryItem(
            name = name,
            finishedAtEpochMs = wallClock.millis(),
            plannedDurationMs = plannedMs,
            actualDurationMs = elapsed,
            plannedSlides = totalSlides,
            registeredSlides = currentSlide,
            averageSlidesPerMinute = if (minutes > 0) currentSlide / minutes else 0.0,
            finalState = finalState,
            endedWithinPlannedTime = elapsed <= plannedMs,
        )
        history = history + item
        state = SessionState.Finished
        alert = null
        _session.value = SessionStatus.NoSession
        _summaries.tryEmit(item)
    }

    private fun publish() {
        if (state != SessionState.Running && state != SessionState.Paused) return
        val elapsed = elapsedNow()
        val remaining = plannedMs - elapsed
        val slidesRemaining = totalSlides - currentSlide
        val snapshot = SessionSnapshot(
            state = state,
            name = name,
            currentSlide = currentSlide,
            totalSlides = totalSlides,
            plannedDurationMs = plannedMs,
            elapsedMs = elapsed,
            remainingMs = remaining,
            running = state == SessionState.Running,
            progressRatio = currentSlide.toFloat() / totalSlides,
            paceStatus = pace,
            slidesRemaining = slidesRemaining,
            averageSlidesPerMinute = 0.0,
            finalState = null,
            activeAlert = alert,
            recommendation = scriptedRecommendation(slidesRemaining, remaining),
        )
        _session.value = SessionStatus.Active(snapshot, monotonic.nowMs())
    }

    private fun scriptedRecommendation(slidesRemaining: Int, remaining: Long): Recommendation? {
        val code = when (pace) {
            PaceStatus.OnTrack -> RecommendationCode.OnTrack
            PaceStatus.Behind -> RecommendationCode.SpeedUpSlightly
            PaceStatus.TooFast -> RecommendationCode.SlowDown
            PaceStatus.NotEnoughData -> return null
        }
        return Recommendation(code, slidesRemaining, remaining)
    }
}
