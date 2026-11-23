package com.presenta.wear

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.remote.InMemoryPairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.viewmodel.ControlStateFactory
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
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * The optional AI text (Phase 10), from the real server with its canned fake client (`Presenta.DevHost --fake-ai`, which
 * never touches the network) into the real Kotlin client and the Control state. A 15-minute session lasts 9 seconds
 * (`--time-scale 100`). Opt-in like the other cross-stack tests: `gradlew :app:crossStackTest`.
 */
class AiTextCrossStackTest {
    private val clock = MonotonicClock { System.nanoTime() / 1_000_000 }
    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private val processes = mutableListOf<Process>()
    private val writers = mutableListOf<OutputStreamWriter>()

    @Before
    fun check() {
        val dll = System.getProperty("presenta.devhost.dll")
        assumeTrue("Run through :app:crossStackTest", dll != null && File(dll).isFile)
    }

    @After
    fun cleanUp() {
        scope.cancel()
        writers.forEach { runCatching { it.write("quit\n"); it.flush() } }
        processes.forEach { runCatching { it.waitFor(10, TimeUnit.SECONDS) } }
        processes.forEach { it.destroyForcibly() }
    }

    private fun startHost(vararg extra: String): Pair<Int, CopyOnWriteArrayList<String>> {
        val dll = System.getProperty("presenta.devhost.dll")
        val started = ProcessBuilder(listOf("dotnet", dll, "--code", CODE, "--grace-seconds", "60", "--time-scale", "100") + extra)
            .redirectErrorStream(true)
            .start()
        processes += started
        writers += OutputStreamWriter(started.outputStream)
        val output = CopyOnWriteArrayList<String>()
        Thread { BufferedReader(InputStreamReader(started.inputStream)).forEachLine { output += it } }
            .also { it.isDaemon = true }
            .start()
        await("the host to listen", output) { output.any { it.startsWith("LISTENING port=") } }
        return output.first { it.startsWith("LISTENING port=") }.substringAfter("port=").toInt() to output
    }

    private fun await(what: String, output: List<String>, timeoutMs: Long = 30_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) { while (!condition()) delay(10) }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what\nHost output:\n" + output.joinToString("\n"))
        }
    }

    private fun repository(port: Int) = RemotePresentaRepository(
        scope = scope,
        sockets = OkHttpSocketFactory(),
        store = InMemoryPairingStore(CODE, "ws://127.0.0.1:$port"),
        clock = clock,
    )

    private fun <T> call(block: suspend () -> T): T = runBlocking { withTimeout(10_000) { block() } }

    private fun snapshotOf(repo: RemotePresentaRepository) = (repo.session.value as? SessionStatus.Active)?.snapshot

    @Test
    fun the_fake_ai_text_travels_to_the_watch_and_the_control_state_prefers_it() {
        val (port, output) = startHost("--fake-ai")
        val repo = repository(port)
        await("connected", output) { repo.connection.value == ConnectionState.Connected }
        assertEquals(CommandResult.Success, call { repo.startSession("Secret deck name", 20, 15 * 60_000L) })

        await("an alert carrying the AI text", output) {
            snapshotOf(repo)?.let { it.activeAlert != null && it.recommendation?.freeText != null } == true
        }

        val snapshot = snapshotOf(repo)!!
        val recommendation = snapshot.recommendation!!
        assertEquals("Fake AI tip: " + recommendation.code.name, recommendation.freeText)
        val state = ControlStateFactory.build(
            repo.connection.value, repo.session.value, clock.nowMs(), ControlOverlay.None, 1, null,
        )!!
        assertEquals(recommendation.freeText, state.recommendationText)
        assertEquals(recommendation.code, state.recommendation) // the deterministic code is still there
        assertNotNull(state.alert)
        assertEquals("the host prints no text and no name", false, output.any { it.contains("Fake AI tip") || it.contains("Secret deck name") })
    }

    @Test
    fun without_the_fake_ai_no_text_ever_reaches_the_watch() {
        val (port, output) = startHost()
        val repo = repository(port)
        await("connected", output) { repo.connection.value == ConnectionState.Connected }
        assertEquals(CommandResult.Success, call { repo.startSession("No AI", 20, 15 * 60_000L) })

        await("an alert", output) { snapshotOf(repo)?.activeAlert != null }
        runBlocking { delay(1_500) } // several more snapshots and alerts go by

        val snapshot = snapshotOf(repo)!!
        assertNotNull(snapshot.recommendation)
        assertNull(snapshot.recommendation!!.freeText)
        val state = ControlStateFactory.build(
            repo.connection.value, repo.session.value, clock.nowMs(), ControlOverlay.None, 1, null,
        )!!
        assertNull(state.recommendationText)
    }

    private companion object {
        const val CODE = "123456"
    }
}
