package com.presenta.wear.haptics

import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.SessionSnapshot
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Records what would have vibrated. */
class RecordingHapticsPlayer : HapticsPlayer {
    val played = mutableListOf<HapticPattern>()

    override fun play(pattern: HapticPattern) {
        played += pattern
    }
}

class HapticsCoordinatorTest {
    private val player = RecordingHapticsPlayer()
    private val lines = mutableListOf<String>()
    private val coordinator = HapticsCoordinator(player) { lines += it }

    private fun alert(kind: AlertKind, severity: AlertSeverity, sequence: Int, raisedAt: Long = 100_000) =
        Alert(kind, severity, raisedAt, 0, 5, 0, sequence)

    private fun status(alert: Alert?, elapsedMs: Long = 100_000, state: SessionState = SessionState.Running) = SessionStatus.Active(
        SessionSnapshot(
            state = state, name = "s", currentSlide = 3, totalSlides = 10, plannedDurationMs = 600_000, elapsedMs = elapsedMs,
            remainingMs = 600_000 - elapsedMs, running = state == SessionState.Running, progressRatio = 0.3f,
            paceStatus = PaceStatus.OnTrack, slidesRemaining = 7, averageSlidesPerMinute = 1.0, finalState = null,
            activeAlert = alert, recommendation = null,
        ),
        receivedAtMs = 0,
    )

    private val running = status(null)
    private val warning = alert(AlertKind.TimeRunningOut, AlertSeverity.Warning, 1)

    @Test
    fun patterns_are_distinct_and_follow_the_severity() {
        assertEquals(HapticPattern.Info, HapticPattern.forSeverity(AlertSeverity.Info))
        assertEquals(HapticPattern.Warning, HapticPattern.forSeverity(AlertSeverity.Warning))
        assertEquals(HapticPattern.Critical, HapticPattern.forSeverity(AlertSeverity.Critical))
        // pause, pulse(, pause, pulse): one pulse, two pulses, and a long pulse followed by a short one
        assertEquals(2, HapticPattern.Info.timingsMs.size)
        assertEquals(4, HapticPattern.Warning.timingsMs.size)
        assertEquals(4, HapticPattern.Critical.timingsMs.size)
        assertTrue(HapticPattern.Critical.timingsMs[1] > HapticPattern.Critical.timingsMs[3])
        assertEquals(HapticPattern.Warning.timingsMs[1], HapticPattern.Warning.timingsMs[3])
        assertTrue(HapticPattern.entries.all { p -> p.timingsMs.sum() < 1_000 })
    }

    @Test
    fun a_new_alert_plays_exactly_one_pattern_for_its_severity() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))

        assertEquals(listOf(HapticPattern.Warning), player.played)
    }

    @Test
    fun the_same_alert_in_later_snapshots_does_not_play_again() {
        coordinator.onSession(running)
        repeat(5) { coordinator.onSession(status(warning, elapsedMs = 100_000 + it * 1_000L)) }

        assertEquals(1, player.played.size)
    }

    @Test
    fun a_free_text_that_appears_in_the_middle_of_an_alert_does_not_vibrate_again() {
        // The companion sends a new state with the same alert sequence when the optional text arrives.
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        val withText = status(warning, elapsedMs = 101_000).let { active ->
            active.copy(
                snapshot = active.snapshot.copy(
                    recommendation = com.presenta.wear.model.Recommendation(
                        com.presenta.wear.model.RecommendationCode.SpeedUpSlightly, 7, 500_000, freeText = "Tip",
                    ),
                ),
            )
        }
        coordinator.onSession(withText)
        coordinator.onSession(withText)

        assertEquals(listOf(HapticPattern.Warning), player.played)
    }

    @Test
    fun acknowledging_plays_nothing() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        coordinator.onSession(status(null)) // acknowledged

        assertEquals(1, player.played.size)
    }

    @Test
    fun an_acknowledged_alert_that_reappears_in_a_late_snapshot_does_not_play_again() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        coordinator.onSession(status(null))
        coordinator.onSession(status(warning)) // an old snapshot, same sequence

        assertEquals(1, player.played.size)
    }

    @Test
    fun a_pace_alert_fired_again_after_the_cooldown_plays_again_because_its_sequence_differs() {
        val first = alert(AlertKind.TooFast, AlertSeverity.Warning, 1, raisedAt = 100_000)
        val again = alert(AlertKind.TooFast, AlertSeverity.Warning, 2, raisedAt = 160_000)
        coordinator.onSession(running)
        coordinator.onSession(status(first))
        coordinator.onSession(status(null))
        coordinator.onSession(status(again, elapsedMs = 160_000))

        assertEquals(listOf(HapticPattern.Warning, HapticPattern.Warning), player.played)
    }

    @Test
    fun a_more_severe_alert_replaces_the_active_one_and_plays_its_own_pattern() {
        val critical = alert(AlertKind.TimeCritical, AlertSeverity.Critical, 2)
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        coordinator.onSession(status(critical))

        assertEquals(listOf(HapticPattern.Warning, HapticPattern.Critical), player.played)
    }

    @Test
    fun an_info_alert_plays_the_single_short_pulse() {
        coordinator.onSession(running)
        coordinator.onSession(status(alert(AlertKind.TimeRunningOut, AlertSeverity.Info, 1)))

        assertEquals(listOf(HapticPattern.Info), player.played)
    }

    @Test
    fun a_reconnection_that_repeats_the_same_alert_does_not_replay_it() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        // Reconnecting keeps the last session; the first snapshot after the hello carries the same alert.
        coordinator.onSession(status(warning, elapsedMs = 130_000))

        assertEquals(1, player.played.size)
    }

    @Test
    fun an_alert_already_active_when_the_app_starts_is_a_baseline_and_does_not_play() {
        coordinator.onSession(SessionStatus.NoSession) // the repository's initial value before the first snapshot
        coordinator.onSession(status(warning))

        assertTrue(player.played.isEmpty())
        coordinator.onSession(status(warning, elapsedMs = 105_000))
        assertTrue(player.played.isEmpty())
        coordinator.onSession(status(alert(AlertKind.TimeCritical, AlertSeverity.Critical, 2)))
        assertEquals(listOf(HapticPattern.Critical), player.played)
    }

    @Test
    fun an_alert_raised_long_before_the_snapshot_is_remembered_but_not_played() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning, elapsedMs = 100_000 + HapticsCoordinator.MAX_ALERT_AGE_MS + 1))
        coordinator.onSession(status(warning, elapsedMs = 120_000))

        assertTrue(player.played.isEmpty())
    }

    @Test
    fun a_new_session_counts_sequences_from_one_again() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        coordinator.onSession(SessionStatus.NoSession)
        coordinator.onSession(running)
        coordinator.onSession(status(warning)) // sequence 1 of the next session

        assertEquals(2, player.played.size)
    }

    @Test
    fun a_failed_command_plays_one_short_pulse_and_nothing_else_does() {
        coordinator.onSession(running)
        coordinator.commandFailed()

        assertEquals(listOf(HapticPattern.CommandFailed), player.played)
        assertEquals(HapticPattern.Info.timingsMs.toList(), HapticPattern.CommandFailed.timingsMs.toList())
    }

    @Test
    fun there_is_one_log_line_per_event_with_the_type_and_the_reason() {
        coordinator.onSession(running)
        coordinator.onSession(status(warning))
        coordinator.onSession(status(warning))
        coordinator.commandFailed()

        assertEquals(2, lines.size)
        assertTrue(lines[0], lines[0].startsWith("play Warning reason=new-alert"))
        assertTrue(lines[1], lines[1].startsWith("play CommandFailed reason=command-failed"))
    }

    @Test
    fun without_a_log_nothing_is_written_and_the_player_still_works() {
        val silent = HapticsCoordinator(player)
        silent.onSession(running)
        silent.onSession(status(warning))

        assertEquals(1, player.played.size)
    }
}
