package com.presenta.wear.data

import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionStatus
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.StateFlow

/** Monotonic milliseconds, used to interpolate elapsed time between snapshots. */
fun interface MonotonicClock {
    fun nowMs(): Long
}

/**
 * The only dependency of the screens on the outside world. Phase 3 has a fake implementation;
 * the real connection to the companion replaces it later without changing any screen.
 */
interface PresentaRepository {
    val connection: StateFlow<ConnectionState>

    /** Current session, with an explicit "no active session". */
    val session: StateFlow<SessionStatus>

    /** Emits once for every session that ends (manual finish, finish while paused, interruption). */
    val summaries: Flow<HistoryItem>

    /** Pairing progress. The pairing screen is shown while this is [PairingState.NeedsCode] or [PairingState.Rejected]. */
    val pairing: StateFlow<PairingState>

    /** The pairing code used last time (to prefill the pairing screen), or null. */
    suspend fun savedPairingCode(): String?

    /** Stores [code] and connects (or reconnects) with it. The result shows up in [pairing] and [connection]. */
    fun connectWithCode(code: String)

    suspend fun startSession(name: String, plannedSlides: Int, plannedDurationMs: Long): CommandResult

    suspend fun pause(): CommandResult

    suspend fun resume(): CommandResult

    suspend fun finish(): CommandResult

    suspend fun next(): CommandResult

    suspend fun previous(): CommandResult

    suspend fun setSlide(slide: Int): CommandResult

    suspend fun acknowledgeAlert(): CommandResult

    suspend fun listHistory(): HistoryResult

    /** Asks the connection layer to try again now. */
    fun retryConnection()
}
