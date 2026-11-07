package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.protocol.EmptyPayload
import com.presenta.wear.protocol.HelloPayload
import com.presenta.wear.protocol.MessageTypes
import com.presenta.wear.protocol.SlideSetPayload
import com.presenta.wear.protocol.StartPayload
import com.presenta.wear.protocol.WireErrorCode
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.viewmodel.ControlStateFactory
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Behavior of the real repository against a scripted companion, on virtual time: no real waiting, no network.
 * The clock the repository sees is the test scheduler's clock, so `advanceTimeBy` moves both `delay` and `nowMs()`.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class RemotePresentaRepositoryTest {

    private class Rig(
        scope: TestScope,
        code: String? = "123456",
        val timings: RemoteTimings = RemoteTimings(),
    ) {
        val clock = MonotonicClock { scope.testScheduler.currentTime }
        val sockets = FakeSocketFactory(clock)
        val store = InMemoryPairingStore(code)
        val repo = RemotePresentaRepository(scope.backgroundScope, sockets, store, clock, timings)
        val socket get() = sockets.sockets.last()
    }

    private fun TestScope.rig(code: String? = "123456", timings: RemoteTimings = RemoteTimings()) = Rig(this, code, timings).also { runCurrent() }

    /** Opens the newest socket and answers hello with `hello.accepted`. */
    private fun TestScope.goOnline(rig: Rig, session: com.presenta.wear.protocol.SessionPayload? = null) {
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted(session))
        runCurrent()
    }

    private fun session(rig: Rig) = (rig.repo.session.value as SessionStatus.Active).snapshot

    // ---- starting up and pairing ----

    @Test
    fun without_a_stored_code_it_waits_for_pairing_and_opens_no_socket() = runTest {
        val rig = rig(code = null)

        assertEquals(PairingState.NeedsCode, rig.repo.pairing.value)
        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        assertTrue(rig.sockets.sockets.isEmpty())
    }

    @Test
    fun with_a_stored_code_it_connects_and_sends_hello_with_that_code() = runTest {
        val rig = rig()
        assertEquals(1, rig.sockets.sockets.size)
        assertEquals("ws://test.invalid:8765", rig.socket.url)
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertEquals(PairingState.Connecting, rig.repo.pairing.value)

        rig.socket.open()
        runCurrent()

        val hello = rig.socket.sentOfType(MessageTypes.HELLO).single()
        assertEquals(HelloPayload("123456", 1, "Wear OS watch"), hello.payload)
    }

    @Test
    fun hello_accepted_marks_the_watch_connected_and_paired() = runTest {
        val rig = rig()

        goOnline(rig)

        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
    }

    @Test
    fun hello_accepted_carries_the_current_session_so_a_reconnect_restores_it() = runTest {
        val rig = rig()

        goOnline(rig, Companion.session(slide = 7, elapsedMs = 512_000))

        val snapshot = session(rig)
        assertEquals("Demo", snapshot.name)
        assertEquals(7, snapshot.currentSlide)
        assertEquals(20, snapshot.totalSlides)
        assertEquals(512_000, snapshot.elapsedMs)
        assertTrue(snapshot.running)
        assertEquals(SessionState.Running, snapshot.state)
    }

    @Test
    fun a_wrong_code_returns_to_the_pairing_state_and_stops_retrying() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()

        rig.socket.receive(Companion.rejected(WireErrorCode.InvalidPairingCode))
        runCurrent()

        assertEquals(PairingState.Rejected(ErrorCode.InvalidPairingCode), rig.repo.pairing.value)
        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        advanceTimeBy(120_000)
        assertEquals("no automatic retry with a rejected code", 1, rig.sockets.sockets.size)
    }

    @Test
    fun a_lockout_is_reported_as_such() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()

        rig.socket.receive(Companion.rejected(WireErrorCode.PairingLockedOut, retryAfterMs = 30_000))
        runCurrent()

        assertEquals(PairingState.Rejected(ErrorCode.PairingLockedOut), rig.repo.pairing.value)
    }

    @Test
    fun an_unsupported_version_is_reported() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()

        rig.socket.receive(Companion.rejected(WireErrorCode.UnsupportedVersion))
        runCurrent()

        assertEquals(PairingState.Rejected(ErrorCode.UnsupportedVersion), rig.repo.pairing.value)
    }

    @Test
    fun a_new_code_is_saved_and_used_to_connect() = runTest {
        val rig = rig(code = null)

        rig.repo.connectWithCode("654321")
        runCurrent()

        assertEquals("654321", rig.store.code)
        assertEquals(PairingState.Connecting, rig.repo.pairing.value)
        rig.socket.open()
        runCurrent()
        assertEquals("654321", (rig.socket.sentOfType(MessageTypes.HELLO).single().payload as HelloPayload).pairingCode)
        rig.socket.receive(Companion.accepted())
        runCurrent()
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
    }

    @Test
    fun after_a_rejection_a_new_code_connects_again() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.rejected(WireErrorCode.InvalidPairingCode))
        runCurrent()

        rig.repo.connectWithCode("111111")
        runCurrent()

        assertEquals(2, rig.sockets.sockets.size)
        assertEquals(PairingState.Connecting, rig.repo.pairing.value)
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted())
        runCurrent()
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
    }

    @Test
    fun the_saved_code_is_available_to_prefill_the_pairing_screen() = runTest {
        val rig = rig(code = "482913")

        assertEquals("482913", rig.repo.savedPairingCode())
    }

    // ---- state, summaries, history ----

    @Test
    fun state_updates_replace_the_session() = runTest {
        val rig = rig()
        goOnline(rig)

        rig.socket.receive(Companion.state(Companion.session(slide = 3)))
        runCurrent()
        assertEquals(3, session(rig).currentSlide)

        rig.socket.receive(Companion.state(Companion.session(slide = 4, alert = Companion.timeAlert)))
        runCurrent()
        assertEquals(4, session(rig).currentSlide)
        assertEquals(AlertKind.TimeRunningOut, session(rig).activeAlert?.kind)

        rig.socket.receive(Companion.state(null))
        runCurrent()
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
    }

    @Test
    fun a_session_summary_is_emitted_once() = runTest {
        val rig = rig()
        goOnline(rig)
        val received = mutableListOf<HistoryItem>()
        backgroundScope.launch { rig.repo.summaries.collect { received += it } }
        runCurrent()

        rig.socket.receive(Companion.summary(registered = 12))
        runCurrent()

        assertEquals(1, received.size)
        assertEquals(12, received.single().registeredSlides)
        assertEquals(872_000, received.single().actualDurationMs)
        assertTrue(received.single().endedWithinPlannedTime)
    }

    @Test
    fun history_list_sends_a_request_and_returns_the_items_in_order() = runTest {
        val rig = rig()
        goOnline(rig)

        val result = async { rig.repo.listHistory() }
        runCurrent()
        assertEquals(1, rig.socket.sentOfType(MessageTypes.HISTORY_LIST).size)
        rig.socket.receive(Companion.history(Companion.historyItem("A"), Companion.historyItem("B")))
        runCurrent()

        val items = (result.await() as HistoryResult.Success).items
        assertEquals(listOf("A", "B"), items.map { it.name })
    }

    @Test
    fun an_empty_history_is_a_success() = runTest {
        val rig = rig()
        goOnline(rig)

        val result = async { rig.repo.listHistory() }
        runCurrent()
        rig.socket.receive(Companion.history())
        runCurrent()

        assertEquals(HistoryResult.Success(emptyList()), result.await())
    }

    @Test
    fun a_history_error_is_mapped() = runTest {
        val rig = rig()
        goOnline(rig)

        val result = async { rig.repo.listHistory() }
        runCurrent()
        val id = rig.socket.sentOfType(MessageTypes.HISTORY_LIST).single().id
        rig.socket.receive(Companion.error(WireErrorCode.InternalError, id))
        runCurrent()

        assertEquals(HistoryResult.Failure(ErrorCode.InternalError), result.await())
    }

    @Test
    fun history_fails_at_once_when_not_connected() = runTest {
        val rig = rig()

        val result = async { rig.repo.listHistory() }
        runCurrent()

        assertEquals(HistoryResult.Failure(ErrorCode.InvalidMessage), result.await())
    }

    // ---- commands ----

    @Test
    fun a_command_is_sent_with_a_correlation_id_and_succeeds_on_an_ok_result() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())

        val result = async { rig.repo.next() }
        runCurrent()
        val sent = rig.socket.sentOfType(MessageTypes.SLIDE_NEXT).single()
        assertEquals(EmptyPayload, sent.payload)
        rig.socket.receive(Companion.ok(sent.id))
        runCurrent()

        assertEquals(CommandResult.Success, result.await())
    }

    @Test
    fun every_command_sends_its_own_message_type() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())
        val calls = mapOf<String, suspend () -> CommandResult>(
            MessageTypes.SESSION_START to { rig.repo.startSession("Demo", 20, 900_000) },
            MessageTypes.SESSION_PAUSE to { rig.repo.pause() },
            MessageTypes.SESSION_RESUME to { rig.repo.resume() },
            MessageTypes.SESSION_FINISH to { rig.repo.finish() },
            MessageTypes.SLIDE_NEXT to { rig.repo.next() },
            MessageTypes.SLIDE_PREVIOUS to { rig.repo.previous() },
            MessageTypes.SLIDE_SET to { rig.repo.setSlide(9) },
            MessageTypes.ALERT_ACKNOWLEDGE to { rig.repo.acknowledgeAlert() },
        )

        for ((type, call) in calls) {
            val result = async { call() }
            runCurrent()
            val sent = rig.socket.sentOfType(type).single()
            rig.socket.receive(Companion.ok(sent.id))
            runCurrent()
            assertEquals(type, CommandResult.Success, result.await())
        }

        assertEquals(StartPayload(20, 900_000, "Demo"), rig.socket.sentOfType(MessageTypes.SESSION_START).single().payload)
        assertEquals(SlideSetPayload(9), rig.socket.sentOfType(MessageTypes.SLIDE_SET).single().payload)
    }

    @Test
    fun a_failed_result_becomes_the_matching_ui_error_and_the_session_stays() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))

        val result = async { rig.repo.setSlide(99) }
        runCurrent()
        val id = rig.socket.sentOfType(MessageTypes.SLIDE_SET).single().id
        rig.socket.receive(Companion.failed(id, WireErrorCode.SlideOutOfRange))
        runCurrent()

        assertEquals(CommandResult.Failure(ErrorCode.SlideOutOfRange), result.await())
        assertEquals(7, session(rig).currentSlide)
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
    }

    @Test
    fun a_protocol_error_naming_a_command_fails_that_command() = runTest {
        val rig = rig()
        goOnline(rig)

        val result = async { rig.repo.pause() }
        runCurrent()
        val id = rig.socket.sentOfType(MessageTypes.SESSION_PAUSE).single().id
        rig.socket.receive(Companion.error(WireErrorCode.InvalidState, id))
        runCurrent()

        assertEquals(CommandResult.Failure(ErrorCode.InvalidState), result.await())
    }

    @Test
    fun a_command_without_an_answer_times_out() = runTest {
        val rig = rig()
        goOnline(rig)

        val result = async { rig.repo.next() }
        runCurrent()
        // Keep the connection alive with pongs while the command goes unanswered.
        repeat(8) {
            advanceTimeBy(1_000)
            rig.socket.receive(Companion.pong())
            runCurrent()
        }

        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), result.await())
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
    }

    @Test
    fun commands_fail_at_once_when_not_connected_and_are_never_queued() = runTest {
        val rig = rig()

        val early = async { rig.repo.next() }
        runCurrent()
        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), early.await())

        goOnline(rig)
        assertTrue("nothing was queued for later", rig.socket.sentOfType(MessageTypes.SLIDE_NEXT).isEmpty())
    }

    @Test
    fun commands_fail_at_once_while_reconnecting_and_the_session_stays_visible() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 5))
        rig.socket.serverClose()
        runCurrent()
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)

        val result = async { rig.repo.previous() }
        runCurrent()

        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), result.await())
        assertEquals(5, session(rig).currentSlide)
    }

    @Test
    fun a_command_waiting_when_the_connection_drops_fails_instead_of_hanging() = runTest {
        val rig = rig()
        goOnline(rig)
        val result = async { rig.repo.next() }
        runCurrent()

        rig.socket.serverClose()
        runCurrent()

        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), result.await())
    }

    // ---- heartbeat and loss ----

    @Test
    fun the_watch_pings_regularly() = runTest {
        val rig = rig()
        goOnline(rig)

        repeat(6) {
            advanceTimeBy(500)
            rig.socket.receive(Companion.pong())
            runCurrent()
        }

        assertEquals("a ping every 1.5 seconds", 2, rig.socket.sentOfType(MessageTypes.PING).size)
    }

    @Test
    fun answered_pings_keep_the_connection_alive_indefinitely() = runTest {
        val rig = rig()
        goOnline(rig)
        var answered = 0

        repeat(120) {
            advanceTimeBy(500)
            val pings = rig.socket.sentOfType(MessageTypes.PING).size
            if (pings > answered) {
                answered = pings
                rig.socket.receive(Companion.pong())
                runCurrent()
            }
        }

        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(1, rig.sockets.sockets.size)
    }

    @Test
    fun the_loss_detection_budget_is_under_five_seconds() {
        assertTrue(RemoteTimings().worstCaseLossDetectionMs <= 5_000)
        assertEquals(3_500, RemoteTimings().worstCaseLossDetectionMs)
    }

    @Test
    fun a_silent_server_is_noticed_within_five_seconds() = runTest {
        val rig = rig()
        goOnline(rig)
        val lastFrameAt = currentTime
        var detectedAfter = -1L

        while (currentTime - lastFrameAt <= 6_000) {
            advanceTimeBy(100)
            if (rig.repo.connection.value == ConnectionState.Reconnecting) {
                detectedAfter = currentTime - lastFrameAt
                break
            }
        }

        assertTrue("loss was never detected", detectedAfter >= 0)
        assertTrue("loss detected after $detectedAfter ms, over the 5 s budget", detectedAfter <= 5_000)
        assertTrue("loss detected too early (after $detectedAfter ms)", detectedAfter > 3_000)
    }

    @Test
    fun the_worst_case_alignment_of_the_last_frame_is_also_inside_the_budget() = runTest {
        val rig = rig()
        goOnline(rig)
        // Receive a frame just after a watchdog tick, so the next check is as late as possible.
        advanceTimeBy(510)
        rig.socket.receive(Companion.pong())
        runCurrent()
        val lastFrameAt = currentTime

        while (rig.repo.connection.value == ConnectionState.Connected && currentTime - lastFrameAt < 10_000) {
            advanceTimeBy(50)
        }

        assertTrue("detected after ${currentTime - lastFrameAt} ms", currentTime - lastFrameAt <= 5_000)
    }

    @Test
    fun the_session_keeps_ticking_from_the_last_snapshot_while_the_connection_is_lost() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(elapsedMs = 10_000, plannedMs = 900_000))
        val atAccept = currentTime

        advanceTimeBy(8_000) // silence is detected, the connection goes to reconnecting
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        val ui = ControlStateFactory.build(
            rig.repo.connection.value, rig.repo.session.value, rig.clock.nowMs(), ControlOverlay.None, 1, null,
        )!!

        assertTrue(ui.elapsedMs >= 10_000 + (currentTime - atAccept) - 1_000)
        assertEquals(ConnectionState.Reconnecting, ui.connection)
        assertTrue(rig.repo.session.value is SessionStatus.Active)
    }

    @Test
    fun a_closed_socket_triggers_a_reconnect_with_a_new_hello_and_restores_the_state() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))

        rig.socket.serverClose()
        runCurrent()
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertEquals("the first retry waits for the backoff", 1, rig.sockets.sockets.size)
        advanceTimeBy(600)

        assertEquals(2, rig.sockets.sockets.size)
        rig.socket.open()
        runCurrent()
        assertEquals("123456", (rig.socket.sentOfType(MessageTypes.HELLO).single().payload as HelloPayload).pairingCode)
        rig.socket.receive(Companion.accepted(Companion.session(slide = 9)))
        runCurrent()
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(9, session(rig).currentSlide)
    }

    @Test
    fun a_failed_connection_is_retried() = runTest {
        val rig = rig()
        rig.socket.fail()
        runCurrent()

        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        advanceTimeBy(600)
        assertEquals(2, rig.sockets.sockets.size)
    }

    @Test
    fun the_backoff_grows_and_is_capped() = runTest {
        val rig = rig()
        val attemptTimes = mutableListOf<Long>()
        var seen = 0
        repeat(400) {
            while (seen < rig.sockets.sockets.size) {
                attemptTimes += rig.sockets.sockets[seen].createdAtMs
                rig.sockets.sockets[seen].fail()
                seen++
            }
            advanceTimeBy(100)
        }

        val gaps = attemptTimes.zipWithNext { a, b -> b - a }.take(6)
        assertTrue("gaps were $gaps", gaps.size >= 5)
        assertTrue("gaps must not shrink: $gaps", gaps.zipWithNext().all { (a, b) -> b >= a })
        assertTrue("the first gap is about 0.5 s: $gaps", gaps.first() in 500..1_000)
        assertTrue("gaps are capped near 4 s: $gaps", gaps.all { it <= 4_500 })
        assertTrue("the cap is reached: $gaps", gaps.last() >= 4_000)
    }

    @Test
    fun it_gives_up_after_the_limit_and_a_manual_retry_connects_again() = runTest {
        val rig = rig()
        var seen = 0
        repeat(400) {
            while (seen < rig.sockets.sockets.size) rig.sockets.sockets[seen++].fail()
            advanceTimeBy(100)
        }
        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        val attempts = rig.sockets.sockets.size
        advanceTimeBy(30_000)
        assertEquals("no more automatic retries", attempts, rig.sockets.sockets.size)

        rig.repo.retryConnection()
        runCurrent()

        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertEquals(attempts + 1, rig.sockets.sockets.size)
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted())
        runCurrent()
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
    }

    @Test
    fun a_connection_that_never_opens_times_out() = runTest {
        val rig = rig()

        advanceTimeBy(5_600)

        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertTrue("a second attempt was made", rig.sockets.sockets.size >= 2)
        assertTrue(rig.sockets.sockets.first().cancelled)
    }

    @Test
    fun a_server_that_never_answers_hello_times_out() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()

        advanceTimeBy(5_000)

        assertTrue("a second attempt was made", rig.sockets.sockets.size >= 2)
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
    }

    @Test
    fun events_from_a_replaced_socket_are_ignored() = runTest {
        val rig = rig()
        goOnline(rig)
        val old = rig.socket
        old.serverClose()
        runCurrent()
        advanceTimeBy(600)
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted())
        runCurrent()

        old.fail()
        old.receive(Companion.state(Companion.session(slide = 15)))
        runCurrent()

        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
    }

    // ---- bad input ----

    @Test
    fun invalid_unknown_and_unexpected_messages_are_ignored() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))

        rig.socket.receive("garbage")
        rig.socket.receive("""{"version":1,"type":"slide.teleport","id":"x","payload":{}}""")
        rig.socket.receive("""{"version":1,"type":"state.update","id":"x","payload":{"serverTimeUtc":"nope","session":null}}""")
        rig.socket.receive(Companion.ok("nobody-asked"))
        rig.socket.receive(Companion.error(WireErrorCode.InvalidMessage, null))
        runCurrent()

        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(7, session(rig).currentSlide)
    }

    @Test
    fun a_state_update_before_hello_is_accepted_is_ignored() = runTest {
        val rig = rig()
        rig.socket.open()
        runCurrent()

        rig.socket.receive(Companion.state(Companion.session()))
        runCurrent()

        assertNull((rig.repo.session.value as? SessionStatus.Active))
        assertFalse(rig.repo.connection.value == ConnectionState.Connected)
    }

    // ---- time interpolation ----

    @Test
    fun elapsed_time_advances_from_the_snapshot_and_resynchronizes_on_every_new_one() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(elapsedMs = 10_000))

        fun elapsed() = ControlStateFactory.build(
            rig.repo.connection.value, rig.repo.session.value, rig.clock.nowMs(), ControlOverlay.None, 1, null,
        )!!.elapsedMs

        fun TestScope.passTime(ms: Long) {
            var left = ms
            while (left > 0) { // pongs keep the connection alive while time passes
                val step = minOf(left, 500)
                advanceTimeBy(step)
                rig.socket.receive(Companion.pong())
                runCurrent()
                left -= step
            }
        }

        assertEquals(10_000, elapsed())
        passTime(5_500)
        assertEquals(15_000, elapsed())

        rig.socket.receive(Companion.state(Companion.session(elapsedMs = 14_000))) // the server says 14 s
        runCurrent()
        assertEquals(14_000, elapsed())
        passTime(2_000)
        assertEquals(16_000, elapsed())
    }

    @Test
    fun a_paused_session_does_not_advance() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(elapsedMs = 30_000, running = false))

        advanceTimeBy(2_000)
        rig.socket.receive(Companion.pong())
        runCurrent()

        val ui = ControlStateFactory.build(rig.repo.connection.value, rig.repo.session.value, rig.clock.nowMs(), ControlOverlay.None, 1, null)!!
        assertEquals(30_000, ui.elapsedMs)
        assertTrue(ui.paused)
    }
}
