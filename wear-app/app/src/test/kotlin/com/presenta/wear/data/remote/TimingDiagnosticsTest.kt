package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.diagnostics.NoTimingLog
import com.presenta.wear.diagnostics.TimingLog
import com.presenta.wear.diagnostics.TimingLogs
import com.presenta.wear.protocol.MessageTypes
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.async
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.currentTime
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

/** The debug-only measurements for RNF 03 and RNF 04, on virtual time. Release builds get a log that does nothing. */
@OptIn(ExperimentalCoroutinesApi::class)
class TimingDiagnosticsTest {
    private val lines = mutableListOf<String>()

    private fun TestScope.repository(log: TimingLog = TimingLog { lines += it }): Pair<RemotePresentaRepository, FakeSocketFactory> {
        val clock = MonotonicClock { testScheduler.currentTime }
        val sockets = FakeSocketFactory(clock)
        val repo = RemotePresentaRepository(backgroundScope, sockets, InMemoryPairingStore("123456"), clock, timing = log)
        runCurrent()
        sockets.sockets.last().open()
        runCurrent()
        sockets.sockets.last().receive(Companion.accepted())
        runCurrent()
        return repo to sockets
    }

    @Test
    fun a_command_logs_the_milliseconds_between_sending_and_its_result() = runTest {
        val (repo, sockets) = repository()
        val socket = sockets.sockets.last()

        val result = async { repo.next() }
        runCurrent()
        val sent = socket.sentOfType(MessageTypes.SLIDE_NEXT).single()
        advanceTimeBy(137)
        socket.receive(Companion.ok(sent.id))
        runCurrent()
        result.await()

        assertEquals(listOf("command type=${MessageTypes.SLIDE_NEXT} roundTripMs=137 ok=true"), lines)
    }

    @Test
    fun a_silent_server_logs_when_the_loss_was_declared_after_the_last_frame() = runTest {
        val (_, _) = repository()

        advanceTimeBy(8_000) // production constants: silence is declared after 3 s, checked every 0.5 s
        runCurrent()

        val lost = lines.first { it.startsWith("connection-lost") }
        val sinceLastFrame = Regex("sinceLastFrameMs=(\\d+)").find(lost)!!.groupValues[1].toLong()
        assertTrue("declared lost after $sinceLastFrame ms", sinceLastFrame in 3_000..3_500)
        assertTrue(lost.contains("reconnectingAfterMs=0"))
    }

    @Test
    fun a_log_that_does_nothing_records_nothing() = runTest {
        val (repo, sockets) = repository(NoTimingLog)
        val result = async { repo.next() }
        runCurrent()
        sockets.sockets.last().receive(Companion.ok(sockets.sockets.last().sentOfType(MessageTypes.SLIDE_NEXT).single().id))
        runCurrent()
        result.await()

        assertTrue(lines.isEmpty())
    }

    @Test
    fun release_builds_get_the_silent_log_and_debug_builds_get_logcat() {
        assertSame(NoTimingLog, TimingLogs.forBuild(debug = false))
        assertEquals("PresentaTiming", com.presenta.wear.diagnostics.LogcatTimingLog.TAG)
        assertTrue(TimingLogs.forBuild(debug = true) !== NoTimingLog)
    }
}
