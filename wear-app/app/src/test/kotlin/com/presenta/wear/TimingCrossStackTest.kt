package com.presenta.wear

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.remote.InMemoryPairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import com.presenta.wear.diagnostics.TimingLog
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.SessionStatus
import java.io.BufferedReader
import java.io.File
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * Objective timing against the real server (Presenta.DevHost): the round trip of slide commands over loopback and the
 * time the client needs to declare the connection lost after the host dies. Values are printed as `TIMING ...` lines and
 * collected by scripts/verify-all.ps1. Opt-in: `gradlew :app:crossStackTest`.
 *
 * What this does NOT measure: the real key injection into a window and PowerPoint's repaint (the host only prints keys),
 * a real network, or a silent host (a killed process closes its socket at once; the silent case is covered with virtual
 * time in RemotePresentaRepositoryTest and in the 3.5 s bound of ADR-47).
 */
class TimingCrossStackTest {
    private val clock = MonotonicClock { System.nanoTime() / 1_000_000 }
    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private val output = CopyOnWriteArrayList<String>()
    private val timingLines = CopyOnWriteArrayList<String>()
    private var process: Process? = null
    private var stdin: OutputStreamWriter? = null
    private var port = 0

    @Before
    fun startDevHost() {
        val dll = System.getProperty("presenta.devhost.dll")
        assumeTrue("Run through :app:crossStackTest", dll != null && File(dll).isFile)
        val started = ProcessBuilder("dotnet", dll, "--code", CODE, "--grace-seconds", "60")
            .redirectErrorStream(true)
            .start()
        process = started
        stdin = OutputStreamWriter(started.outputStream)
        Thread { BufferedReader(InputStreamReader(started.inputStream)).forEachLine { output += it } }
            .also { it.isDaemon = true }
            .start()
        await("the host to listen") { output.any { it.startsWith("LISTENING port=") } }
        port = output.first { it.startsWith("LISTENING port=") }.substringAfter("port=").toInt()
    }

    @After
    fun stopDevHost() {
        scope.cancel()
        runCatching {
            stdin?.apply { write("quit\n"); flush() }
            process?.waitFor(10, TimeUnit.SECONDS)
        }
        process?.destroyForcibly()
    }

    private fun await(what: String, timeoutMs: Long = 20_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) { while (!condition()) delay(5) }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what\nHost output:\n" + output.joinToString("\n"))
        }
    }

    private fun repository() = RemotePresentaRepository(
        scope = scope,
        sockets = OkHttpSocketFactory(),
        store = InMemoryPairingStore(CODE, "ws://127.0.0.1:$port"),
        clock = clock,
        timing = TimingLog { timingLines += it },
    )

    private fun percentile(sorted: List<Double>, fraction: Double): Double =
        sorted[((sorted.size - 1) * fraction).toInt().coerceIn(0, sorted.size - 1)]

    @Test
    fun slide_command_round_trips_over_loopback() {
        val repo = repository()
        await("connected") { repo.connection.value == ConnectionState.Connected }
        assertEquals(CommandResult.Success, runBlocking { withTimeout(10_000) { repo.startSession("Timing", 100, 3_600_000L) } })
        await("the session") { repo.session.value is SessionStatus.Active }

        // Warm up the connection and the JIT, then measure alternating next and previous commands.
        repeat(10) { runBlocking { repo.next() } }
        val samplesMs = ArrayList<Double>()
        repeat(100) { index ->
            val started = System.nanoTime()
            val result = runBlocking { withTimeout(10_000) { if (index % 2 == 0) repo.next() else repo.previous() } }
            samplesMs += (System.nanoTime() - started) / 1_000_000.0
            assertEquals(CommandResult.Success, result)
        }

        val sorted = samplesMs.sorted()
        val median = percentile(sorted, 0.5)
        val p95 = percentile(sorted, 0.95)
        println(
            String.format(
                java.util.Locale.ROOT,
                "TIMING command-latency samples=%d minMs=%.1f medianMs=%.1f p95Ms=%.1f maxMs=%.1f",
                sorted.size, sorted.first(), median, p95, sorted.last(),
            ),
        )
        println("TIMING watch-log-lines=${timingLines.size} (the debug log of the client saw the same commands)")
        assertTrue("median $median ms", median < 250)
        assertTrue("p95 $p95 ms", p95 < 1_000)
        assertTrue(timingLines.any { it.startsWith("command type=slide.next roundTripMs=") })
    }

    @Test
    fun loss_is_declared_within_five_seconds_after_the_host_dies() {
        val repo = repository()
        await("connected") { repo.connection.value == ConnectionState.Connected }
        assertEquals(CommandResult.Success, runBlocking { withTimeout(10_000) { repo.startSession("Loss", 10, 600_000L) } })
        await("the session") { repo.session.value is SessionStatus.Active }

        val killedAt = System.nanoTime()
        process!!.destroyForcibly()
        await("the loss to be declared", timeoutMs = 10_000) { repo.connection.value != ConnectionState.Connected }
        val lossMs = (System.nanoTime() - killedAt) / 1_000_000.0

        println(String.format(java.util.Locale.ROOT, "TIMING loss-detection afterHostKilledMs=%.0f productionBoundMs=%d", lossMs, 3_500))
        timingLines.filter { it.startsWith("connection-lost") }.forEach { println("TIMING watch-log $it") }
        assertEquals(ConnectionState.Reconnecting, repo.connection.value)
        assertTrue("loss took $lossMs ms", lossMs <= 5_000)
        // The session stays on screen while the client reconnects.
        assertTrue(repo.session.value is SessionStatus.Active)
    }

    private companion object {
        const val CODE = "123456"
    }
}
