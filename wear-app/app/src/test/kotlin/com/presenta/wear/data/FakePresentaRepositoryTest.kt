package com.presenta.wear.data

import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.TestClocks
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class FakePresentaRepositoryTest {
    private val clock = FakeMonotonicClock()
    private val repository = FakePresentaRepository(clock, TestClocks.wall)

    private fun active() = (repository.session.value as SessionStatus.Active).snapshot

    private suspend fun start(slides: Int = 10, minutes: Int = 10) =
        repository.startSession("Demo", slides, minutes * 60_000L)

    @Test
    fun there_is_no_active_session_at_first() {
        assertEquals(SessionStatus.NoSession, repository.session.value)
    }

    @Test
    fun start_creates_a_running_session_at_slide_one() = runTest {
        assertEquals(CommandResult.Success, start(slides = 20, minutes = 15))

        val snapshot = active()
        assertEquals(SessionState.Running, snapshot.state)
        assertEquals(1, snapshot.currentSlide)
        assertEquals(20, snapshot.totalSlides)
        assertEquals(15 * 60_000L, snapshot.plannedDurationMs)
        assertEquals("Demo", snapshot.name)
        assertTrue(snapshot.running)
        assertNull(snapshot.activeAlert)
    }

    @Test
    fun starting_twice_fails_with_invalid_state() = runTest {
        start()
        assertEquals(CommandResult.Failure(ErrorCode.InvalidState), start())
    }

    @Test
    fun next_and_previous_move_the_counter_and_stop_at_the_limits() = runTest {
        start(slides = 3)

        repository.previous()
        assertEquals(1, active().currentSlide)
        repository.next()
        repository.next()
        repository.next()
        assertEquals(3, active().currentSlide)
        repository.previous()
        assertEquals(2, active().currentSlide)
    }

    @Test
    fun set_slide_accepts_only_slides_in_range() = runTest {
        start(slides = 10)

        assertEquals(CommandResult.Success, repository.setSlide(7))
        assertEquals(7, active().currentSlide)
        assertEquals(CommandResult.Failure(ErrorCode.SlideOutOfRange), repository.setSlide(11))
        assertEquals(CommandResult.Failure(ErrorCode.SlideOutOfRange), repository.setSlide(0))
        assertEquals(7, active().currentSlide)
    }

    @Test
    fun commands_without_a_session_fail_with_no_active_session() = runTest {
        val expected = CommandResult.Failure(ErrorCode.NoActiveSession)

        assertEquals(expected, repository.next())
        assertEquals(expected, repository.previous())
        assertEquals(expected, repository.setSlide(2))
        assertEquals(expected, repository.pause())
        assertEquals(expected, repository.resume())
        assertEquals(expected, repository.finish())
    }

    @Test
    fun snapshot_elapsed_time_follows_the_injected_clock() = runTest {
        start(minutes = 10)

        clock.advance(90_000)
        repository.refresh()

        val snapshot = active()
        assertEquals(90_000, snapshot.elapsedMs)
        assertEquals(600_000 - 90_000, snapshot.remainingMs)
    }

    @Test
    fun pause_freezes_the_elapsed_time_and_resume_continues() = runTest {
        start(minutes = 10)
        clock.advance(60_000)
        assertEquals(CommandResult.Success, repository.pause())
        assertEquals(SessionState.Paused, active().state)
        assertEquals(false, active().running)

        clock.advance(300_000)
        repository.refresh()
        assertEquals(60_000, active().elapsedMs)

        assertEquals(CommandResult.Success, repository.resume())
        clock.advance(10_000)
        repository.refresh()
        assertEquals(70_000, active().elapsedMs)
    }

    @Test
    fun pause_and_resume_are_rejected_in_the_wrong_state() = runTest {
        start()
        assertEquals(CommandResult.Failure(ErrorCode.InvalidState), repository.resume())
        repository.pause()
        assertEquals(CommandResult.Failure(ErrorCode.InvalidState), repository.pause())
    }

    @Test
    fun slide_commands_work_while_paused() = runTest {
        start(slides = 10)
        repository.pause()

        repository.next()

        assertEquals(2, active().currentSlide)
    }

    @Test
    fun finish_emits_a_summary_and_clears_the_session() = runTest(UnconfinedTestDispatcher()) {
        start(slides = 20, minutes = 15)
        clock.advance(14 * 60_000L + 32_000)
        repository.setSlide(20)
        var summary: HistoryItem? = null
        val job = launch { summary = repository.summaries.first() }

        assertEquals(CommandResult.Success, repository.finish())
        job.join()

        assertEquals(SessionStatus.NoSession, repository.session.value)
        val item = requireNotNull(summary)
        assertEquals(14 * 60_000L + 32_000, item.actualDurationMs)
        assertEquals(20, item.registeredSlides)
        assertEquals(20, item.plannedSlides)
        assertEquals(FinalState.FinishedManually, item.finalState)
        assertTrue(item.endedWithinPlannedTime)
        assertEquals(TestClocks.wall.millis(), item.finishedAtEpochMs)
    }

    @Test
    fun finishing_while_paused_is_recorded_as_ended_while_paused() = runTest(UnconfinedTestDispatcher()) {
        start()
        repository.pause()
        var summary: HistoryItem? = null
        val job = launch { summary = repository.summaries.first() }

        repository.finish()
        job.join()

        assertEquals(FinalState.EndedWhilePaused, summary?.finalState)
    }

    @Test
    fun an_interruption_ends_the_session_with_a_summary() = runTest(UnconfinedTestDispatcher()) {
        start()
        var summary: HistoryItem? = null
        val job = launch { summary = repository.summaries.first() }

        repository.interruptSession()
        job.join()

        assertEquals(FinalState.InterruptedByDisconnection, summary?.finalState)
        assertEquals(SessionStatus.NoSession, repository.session.value)
    }

    @Test
    fun finished_sessions_are_added_to_the_history_newest_first() = runTest {
        start()
        repository.finish()

        val result = repository.listHistory() as HistoryResult.Success

        assertEquals(4, result.items.size)
        assertEquals(result.items.sortedByDescending { it.finishedAtEpochMs }, result.items)
        assertEquals("Demo", result.items.first().name)
    }

    @Test
    fun default_history_matches_the_mockup_rows() = runTest {
        val items = (repository.listHistory() as HistoryResult.Success).items

        assertEquals(listOf(1.37, 1.56, 1.25), items.map { it.averageSlidesPerMinute })
    }

    @Test
    fun history_can_be_empty_or_fail() = runTest {
        repository.setHistory(emptyList())
        assertEquals(HistoryResult.Success(emptyList()), repository.listHistory())

        repository.failHistoryWith(ErrorCode.InternalError)
        assertEquals(HistoryResult.Failure(ErrorCode.InternalError), repository.listHistory())
    }

    @Test
    fun scripted_alert_and_pace_appear_in_the_snapshot_and_acknowledging_clears_the_alert() = runTest {
        start()

        repository.scriptPace(PaceStatus.Behind)
        repository.raiseAlert(FakeData.controlAlert.activeAlert)
        assertEquals(PaceStatus.Behind, active().paceStatus)
        assertEquals(RecommendationCode.SpeedUpSlightly, active().recommendation?.code)
        assertEquals(FakeData.controlAlert.activeAlert, active().activeAlert)

        assertEquals(CommandResult.Success, repository.acknowledgeAlert())
        assertNull(active().activeAlert)
    }

    @Test
    fun an_alert_never_blocks_slide_commands() = runTest {
        start(slides = 10)
        repository.raiseAlert(FakeData.controlOvertimeAlert.activeAlert)

        assertEquals(CommandResult.Success, repository.next())
        assertEquals(CommandResult.Success, repository.previous())
        assertEquals(CommandResult.Success, repository.setSlide(5))
    }

    @Test
    fun commands_fail_when_the_connection_is_down() = runTest {
        repository.setConnection(ConnectionState.Disconnected)

        assertEquals(CommandResult.Failure(ErrorCode.InternalError), start())
    }

    @Test
    fun the_next_command_can_be_made_to_fail_once() = runTest {
        start()
        repository.failNextCommandWith(ErrorCode.InvalidState)

        assertEquals(CommandResult.Failure(ErrorCode.InvalidState), repository.next())
        assertEquals(CommandResult.Success, repository.next())
    }

    @Test
    fun retrying_a_lost_connection_moves_to_reconnecting() {
        repository.setConnection(ConnectionState.Disconnected)

        repository.retryConnection()

        assertEquals(ConnectionState.Reconnecting, repository.connection.value)
        assertEquals(1, repository.retryRequests)
    }
}
