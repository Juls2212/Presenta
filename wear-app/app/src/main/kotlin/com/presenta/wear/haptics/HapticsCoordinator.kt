package com.presenta.wear.haptics

import com.presenta.wear.model.Alert
import com.presenta.wear.model.SessionStatus
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch

/**
 * Decides when the watch vibrates. The rule (ADR-61): one pattern for each NEW alert instance, identified by the
 * `sequence` the companion puts on every alert.
 *
 * - The first snapshot of a real session that this object ever sees is a baseline: an alert that is already active then
 *   (the app was just opened or restarted during a session) is remembered but not played, because the person is
 *   looking at it. The "no session" the repository starts with does not count.
 * - After that, an alert whose sequence differs from the last one handled plays once. The same alert repeated in later
 *   snapshots (including the ones after a reconnection) and the moment it is acknowledged do nothing.
 * - Seeing "no session" forgets the sequence, because the next session counts from 1 again.
 * - An alert that was raised long before the snapshot (for example it fired while the watch was disconnected) is
 *   remembered but not played: a late buzz would be confusing.
 *
 * It holds no timers and no Android types, so tests drive it by calling [onSession].
 */
class HapticsCoordinator(
    private val player: HapticsPlayer,
    private val log: HapticLog = NoHapticLog,
) {
    private var seenFirstSnapshot = false
    private var lastHandledSequence: Int? = null

    /** Starts collecting [sessions] in [scope]. */
    fun start(scope: CoroutineScope, sessions: Flow<SessionStatus>) {
        scope.launch { sessions.collect { onSession(it) } }
    }

    fun onSession(status: SessionStatus) {
        when (status) {
            SessionStatus.NoSession -> lastHandledSequence = null
            is SessionStatus.Active -> {
                val baseline = !seenFirstSnapshot
                seenFirstSnapshot = true
                val alert = status.snapshot.activeAlert ?: return
                if (alert.sequence == lastHandledSequence) return
                lastHandledSequence = alert.sequence
                val age = status.snapshot.elapsedMs - alert.raisedAtElapsedMs
                when {
                    baseline -> log.line("skip alert ${describe(alert)} reason=initial-snapshot")
                    age > MAX_ALERT_AGE_MS -> log.line("skip alert ${describe(alert)} reason=stale ageMs=$age")
                    else -> {
                        val pattern = HapticPattern.forSeverity(alert.severity)
                        log.line("play ${pattern.name} reason=new-alert ${describe(alert)}")
                        player.play(pattern)
                    }
                }
            }
        }
    }

    /** A command did not go through: one short pulse. */
    fun commandFailed() {
        log.line("play ${HapticPattern.CommandFailed.name} reason=command-failed")
        player.play(HapticPattern.CommandFailed)
    }

    private fun describe(alert: Alert) = "kind=${alert.kind} severity=${alert.severity} sequence=${alert.sequence}"

    companion object {
        /** Older than this when the snapshot arrives, an alert is no longer worth a vibration. */
        const val MAX_ALERT_AGE_MS = 10_000L
    }
}
