package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.protocol.HelloPayload
import com.presenta.wear.protocol.MessageTypes
import com.presenta.wear.protocol.WireErrorCode
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.viewmodel.ControlStateFactory
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Reconnection during a session (ADR-71): while a session is running or paused the repository never gives up, while an
 * idle watch keeps giving up after 30 s. Everything runs on virtual time against a scripted companion.
 */
@OptIn(ExperimentalCoroutinesApi::class)
class SessionReconnectionTest {

    private class Rig(scope: TestScope, code: String? = "123456") {
        val clock = MonotonicClock { scope.testScheduler.currentTime }
        val sockets = FakeSocketFactory(clock)
        val store = InMemoryPairingStore(code)
        val repo = RemotePresentaRepository(scope.backgroundScope, sockets, store, clock)
        val socket get() = sockets.sockets.last()
    }

    private fun TestScope.rig(code: String? = "123456") = Rig(this, code).also { runCurrent() }

    private fun TestScope.goOnline(rig: Rig, session: com.presenta.wear.protocol.SessionPayload? = null) {
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted(session))
        runCurrent()
    }

    /** Lets time pass while every connection attempt fails at once, like a companion that is not there. */
    private fun TestScope.outage(rig: Rig, forMs: Long, stepMs: Long = 100) {
        var seen = rig.sockets.sockets.count { it.failedByTest }
        val until = currentTime + forMs
        while (currentTime < until) {
            for (index in seen until rig.sockets.sockets.size) {
                rig.sockets.sockets[index].failedByTest = true
                rig.sockets.sockets[index].fail()
            }
            seen = rig.sockets.sockets.size
            advanceTimeBy(stepMs)
        }
        runCurrent()
    }

    /** The newest connection attempt that the test has not failed: waits for the next one when the newest is already dead. */
    private fun TestScope.liveAttempt(rig: Rig): FakeSocket {
        var waited = 0L
        while (rig.socket.failedByTest || rig.socket.cancelled) {
            assertTrue("no new connection attempt within 60 s: the watch gave up", waited < 60_000)
            advanceTimeBy(100)
            waited += 100
        }
        return rig.socket
    }

    private fun sessionSnapshot(rig: Rig) = (rig.repo.session.value as SessionStatus.Active).snapshot

    // ---- during a session ----

    @Test
    fun a_long_outage_during_a_running_session_never_reaches_the_give_up_state() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))
        rig.socket.serverClose()
        runCurrent()

        var everDisconnected = false
        repeat(40) { // 40 steps of 15 s = 10 minutes
            outage(rig, 15_000)
            if (rig.repo.connection.value == ConnectionState.Disconnected) everDisconnected = true
        }

        assertFalse("the connection state must never become Disconnected during a session", everDisconnected)
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertTrue("it kept trying", rig.sockets.sockets.size > 100)
        assertEquals(7, sessionSnapshot(rig).currentSlide)
    }

    @Test
    fun the_attempts_during_a_session_keep_the_capped_backoff_and_never_stop() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())
        rig.socket.serverClose()
        runCurrent()

        outage(rig, 120_000)
        val countAtTwoMinutes = rig.sockets.sockets.size
        val lastAttemptAt = rig.sockets.sockets.last().createdAtMs
        outage(rig, 60_000)

        assertTrue("attempts continue after the old 30 s limit", rig.sockets.sockets.size > countAtTwoMinutes)
        val gaps = rig.sockets.sockets.map { it.createdAtMs }.zipWithNext { a, b -> b - a }.takeLast(10)
        assertTrue("gaps stay near the 4 s cap: $gaps", gaps.all { it in 3_900..4_800 })
        assertTrue(rig.sockets.sockets.last().createdAtMs > lastAttemptAt)
    }

    @Test
    fun a_paused_session_also_keeps_retrying() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(running = false))
        assertEquals(SessionState.Paused, sessionSnapshot(rig).state)
        rig.socket.serverClose()
        runCurrent()

        outage(rig, 5 * 60_000)

        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
    }

    @Test
    fun the_screen_keeps_showing_the_session_as_reconnecting_and_the_clock_keeps_running() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(elapsedMs = 10_000, plannedMs = 900_000))
        val acceptedAt = currentTime
        rig.socket.serverClose()
        runCurrent()

        outage(rig, 3 * 60_000)

        val ui = ControlStateFactory.build(
            rig.repo.connection.value, rig.repo.session.value, rig.clock.nowMs(), ControlOverlay.None, 1, null,
        )!!
        assertEquals(ConnectionState.Reconnecting, ui.connection)
        assertEquals(10_000 + (currentTime - acceptedAt), ui.elapsedMs)
    }

    @Test
    fun commands_still_fail_at_once_and_are_never_queued_during_a_long_outage() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())
        rig.socket.serverClose()
        runCurrent()
        outage(rig, 2 * 60_000)
        val before = rig.sockets.sockets.sumOf { it.sent.size }

        val result = async { rig.repo.next() }
        runCurrent()

        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), result.await())
        assertEquals("nothing was sent or queued", before, rig.sockets.sockets.sumOf { it.sent.size })

        // The companion comes back: the old command is not replayed.
        val live = liveAttempt(rig)
        live.open()
        runCurrent()
        live.receive(Companion.accepted(Companion.session(slide = 9)))
        runCurrent()
        assertTrue(live.sentOfType(MessageTypes.SLIDE_NEXT).isEmpty())
    }

    @Test
    fun when_the_companion_comes_back_after_minutes_the_state_is_resynchronized_with_the_stored_code() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7, elapsedMs = 100_000))
        rig.socket.serverClose()
        runCurrent()
        outage(rig, 6 * 60_000)

        val live = liveAttempt(rig)
        live.open()
        runCurrent()
        val hello = live.sentOfType(MessageTypes.HELLO).single().payload as HelloPayload
        live.receive(Companion.accepted(Companion.session(slide = 12, elapsedMs = 460_000)))
        runCurrent()

        assertEquals("123456", hello.pairingCode)
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
        assertEquals(12, sessionSnapshot(rig).currentSlide)
        assertEquals(460_000, sessionSnapshot(rig).elapsedMs)
    }

    @Test
    fun a_companion_that_restarted_without_the_session_clears_it_on_reconnection() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))
        rig.socket.serverClose()
        runCurrent()
        outage(rig, 90_000)

        val live = liveAttempt(rig)
        live.open()
        runCurrent()
        live.receive(Companion.accepted(null)) // the companion restarted and recovered the session as interrupted
        runCurrent()

        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
    }

    @Test
    fun a_stored_code_the_companion_rejects_during_a_session_returns_to_pairing_and_keeps_the_displayed_session() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session(slide = 7))
        rig.socket.serverClose()
        runCurrent()
        outage(rig, 40_000)

        val live = liveAttempt(rig)
        live.open()
        runCurrent()
        live.receive(Companion.rejected(WireErrorCode.InvalidPairingCode))
        runCurrent()

        assertEquals(PairingState.Rejected(ErrorCode.InvalidPairingCode), rig.repo.pairing.value)
        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        assertEquals("the displayed session is not lost", 7, sessionSnapshot(rig).currentSlide)
        val attempts = rig.sockets.sockets.size
        advanceTimeBy(120_000)
        assertEquals("a rejected code is not retried", attempts, rig.sockets.sockets.size)

        // The user types the new code: the session comes back with the companion's state.
        rig.repo.connectWithCode("654321")
        runCurrent()
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted(Companion.session(slide = 8)))
        runCurrent()
        assertEquals(PairingState.Paired, rig.repo.pairing.value)
        assertEquals(8, sessionSnapshot(rig).currentSlide)
        assertEquals("654321", rig.store.code)
    }

    // ---- idle ----

    @Test
    fun an_idle_watch_still_gives_up_after_thirty_seconds_and_retry_reconnects() = runTest {
        val rig = rig()
        goOnline(rig) // no session
        rig.socket.serverClose()
        runCurrent()

        outage(rig, 40_000)

        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        val attempts = rig.sockets.sockets.size
        advanceTimeBy(60_000)
        assertEquals("no automatic retries once given up", attempts, rig.sockets.sockets.size)

        rig.repo.retryConnection()
        runCurrent()
        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        rig.socket.open()
        runCurrent()
        rig.socket.receive(Companion.accepted())
        runCurrent()
        assertEquals(ConnectionState.Connected, rig.repo.connection.value)
    }

    @Test
    fun after_the_session_ends_the_watch_is_idle_again_and_gives_up_as_before() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())
        rig.socket.receive(Companion.state(null)) // the session finished
        runCurrent()
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
        rig.socket.serverClose()
        runCurrent()

        outage(rig, 40_000)

        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
    }

    @Test
    fun a_watch_that_never_connected_gives_up_as_before() = runTest {
        val rig = rig() // never connected: no snapshot at all

        outage(rig, 40_000)

        assertEquals(ConnectionState.Disconnected, rig.repo.connection.value)
        assertEquals(SessionStatus.NoSession, rig.repo.session.value)
    }

    @Test
    fun a_manual_retry_after_the_session_state_returns_is_still_harmless_during_a_session() = runTest {
        val rig = rig()
        goOnline(rig, Companion.session())
        rig.socket.serverClose()
        runCurrent()
        outage(rig, 60_000)
        val before = rig.sockets.sockets.size

        rig.repo.retryConnection()
        runCurrent()

        assertEquals(ConnectionState.Reconnecting, rig.repo.connection.value)
        assertTrue(rig.sockets.sockets.size > before)
    }
}
