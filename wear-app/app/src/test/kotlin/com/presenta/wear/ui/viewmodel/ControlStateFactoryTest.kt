package com.presenta.wear.ui.viewmodel

import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.ui.screens.control.ControlOverlay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ControlStateFactoryTest {
    private val snapshot = FakeData.controlNormal // slide 7 of 20, 08:32 elapsed of 15:00

    private fun build(status: SessionStatus, now: Long) = ControlStateFactory.build(
        ConnectionState.Connected, status, now, ControlOverlay.None, selectorSlide = 7, message = null,
    )

    @Test
    fun no_session_gives_no_state() {
        assertNull(build(SessionStatus.NoSession, now = 0))
    }

    @Test
    fun values_at_the_snapshot_instant_match_the_mockup() {
        val state = build(SessionStatus.Active(snapshot, receivedAtMs = 1_000), now = 1_000)

        assertNotNull(state)
        assertEquals(7, state!!.currentSlide)
        assertEquals(20, state.totalSlides)
        assertEquals(8 * 60_000L + 32_000, state.elapsedMs)
        assertEquals(6 * 60_000L + 28_000, state.remainingMs)
        assertEquals(512_000f / 900_000f, state.progress, 0.0001f) // elapsed / planned, not slide progress
        assertFalse(state.paused)
        assertFalse(state.overtime)
    }

    @Test
    fun a_running_session_advances_with_the_monotonic_clock() {
        val state = build(SessionStatus.Active(snapshot, receivedAtMs = 1_000), now = 1_000 + 5_400)!!

        assertEquals(8 * 60_000L + 37_000, state.elapsedMs)
        assertEquals(6 * 60_000L + 23_000, state.remainingMs)
    }

    @Test
    fun elapsed_and_remaining_always_add_up_to_the_planned_duration() {
        for (extra in listOf(0L, 1L, 499L, 500L, 999L, 1_000L, 1_001L, 12_345L)) {
            val state = build(SessionStatus.Active(snapshot.copy(elapsedMs = 61_999), receivedAtMs = 0), now = extra)!!
            assertEquals(snapshot.plannedDurationMs, state.elapsedMs + state.remainingMs)
            assertEquals(0L, state.elapsedMs % 1000)
        }
    }

    @Test
    fun the_ring_shows_elapsed_time_and_not_the_slide_number() {
        val early = build(SessionStatus.Active(snapshot.copy(currentSlide = 20, elapsedMs = 90_000), receivedAtMs = 0), now = 0)!!
        val late = build(SessionStatus.Active(snapshot.copy(currentSlide = 1, elapsedMs = 450_000), receivedAtMs = 0), now = 0)!!

        assertEquals(0.1f, early.progress, 0.0001f)
        assertEquals(0.5f, late.progress, 0.0001f)
    }

    @Test
    fun the_ring_advances_with_the_interpolated_clock() {
        val state = build(SessionStatus.Active(snapshot.copy(elapsedMs = 0), receivedAtMs = 0), now = 450_000)!!

        assertEquals(0.5f, state.progress, 0.0001f)
    }

    @Test
    fun the_ring_is_clamped_in_overtime_and_safe_without_a_plan() {
        val overtime = build(SessionStatus.Active(FakeData.controlOvertime, receivedAtMs = 0), now = 0)!!
        val noPlan = build(SessionStatus.Active(snapshot.copy(plannedDurationMs = 0), receivedAtMs = 0), now = 0)!!

        assertEquals(1f, overtime.progress, 0f)
        assertEquals(0f, noPlan.progress, 0f)
    }

    @Test
    fun a_paused_session_does_not_advance() {
        val paused = FakeData.controlPaused
        val state = build(SessionStatus.Active(paused, receivedAtMs = 1_000), now = 600_000)!!

        assertTrue(state.paused)
        assertEquals(paused.elapsedMs, state.elapsedMs)
    }

    @Test
    fun a_clock_that_goes_backwards_never_reduces_the_elapsed_time() {
        val state = build(SessionStatus.Active(snapshot, receivedAtMs = 5_000), now = 1_000)!!

        assertEquals(snapshot.elapsedMs, state.elapsedMs)
    }

    @Test
    fun remaining_time_goes_negative_in_overtime() {
        val state = build(SessionStatus.Active(FakeData.controlOvertime, receivedAtMs = 0), now = 0)!!

        assertTrue(state.overtime)
        assertEquals(-70_000L, state.remainingMs)
    }

    @Test
    fun snapshot_fields_are_passed_through_without_new_rules() {
        val state = build(SessionStatus.Active(FakeData.controlAlert, receivedAtMs = 0), now = 0)!!

        assertEquals(FakeData.controlAlert.activeAlert, state.alert)
        assertEquals(FakeData.controlAlert.paceStatus, state.paceStatus)
        assertEquals(FakeData.controlAlert.recommendation?.code, state.recommendation)
        assertEquals(FakeData.controlAlert.slidesRemaining, state.slidesRemaining)
        assertEquals(SessionState.Running, FakeData.controlAlert.state)
    }
}
