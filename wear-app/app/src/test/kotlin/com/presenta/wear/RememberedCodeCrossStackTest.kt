package com.presenta.wear

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.remote.InMemoryPairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
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
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * Phase 8 against the real C# server: the remembered pairing code survives a companion restart (ADR-69) and a session
 * left by a killed companion is recovered exactly once (ADR-67). Opt-in like the other cross-stack tests
 * (`gradlew :app:crossStackTest`). Every companion process is started and stopped by this test.
 */
class RememberedCodeCrossStackTest {
    private val clock = MonotonicClock { System.nanoTime() / 1_000_000 }
    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private val hosts = mutableListOf<Host>()
    private lateinit var folder: File

    private class Host(val process: Process, val stdin: OutputStreamWriter, val output: CopyOnWriteArrayList<String>) {
        val port: Int get() = output.first { it.startsWith("LISTENING port=") }.substringAfter("port=").toInt()

        fun send(line: String) {
            stdin.write(line + "\n")
            stdin.flush()
        }
    }

    @Before
    fun prepare() {
        val dll = System.getProperty("presenta.devhost.dll")
        assumeTrue("Run through :app:crossStackTest", dll != null && File(dll).isFile)
        folder = File(System.getProperty("java.io.tmpdir"), "presenta-phase8-" + System.nanoTime()).also { it.mkdirs() }
    }

    @After
    fun cleanUp() {
        scope.cancel()
        hosts.forEach { host ->
            runCatching {
                host.send("quit")
                host.process.waitFor(10, TimeUnit.SECONDS)
            }
            host.process.destroyForcibly()
        }
        if (this::folder.isInitialized) folder.deleteRecursively()
    }

    private fun startHost(vararg extra: String): Host {
        val dll = System.getProperty("presenta.devhost.dll")
        val started = ProcessBuilder(listOf("dotnet", dll, "--grace-seconds", "120") + extra)
            .redirectErrorStream(true)
            .start()
        val output = CopyOnWriteArrayList<String>()
        Thread { BufferedReader(InputStreamReader(started.inputStream)).forEachLine { output += it } }
            .also { it.isDaemon = true }
            .start()
        val host = Host(started, OutputStreamWriter(started.outputStream), output)
        hosts += host
        await("the host to listen", output) { output.any { it.startsWith("LISTENING port=") } }
        return host
    }

    private fun stopHost(host: Host) {
        host.send("quit")
        host.process.waitFor(10, TimeUnit.SECONDS)
        await("the host to stop", host.output) { host.output.any { it == "STOPPED" } }
    }

    private fun await(what: String, output: List<String> = emptyList(), timeoutMs: Long = 20_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) { while (!condition()) delay(20) }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what\nHost output:\n" + output.joinToString("\n"))
        }
    }

    private fun repository(code: String, port: Int) = RemotePresentaRepository(
        scope = scope,
        sockets = OkHttpSocketFactory(),
        store = InMemoryPairingStore(code, "ws://127.0.0.1:$port"),
        clock = clock,
    )

    private fun <T> call(block: suspend () -> T): T = runBlocking { withTimeout(10_000) { block() } }

    private fun settingsFile() = File(folder, "settings.json")

    private fun storedCode(): String =
        Regex("\"pairingCode\"\\s*:\\s*\"(\\d{6})\"").find(settingsFile().readText())!!.groupValues[1]

    private fun slide(repo: RemotePresentaRepository) = (repo.session.value as? SessionStatus.Active)?.snapshot?.currentSlide

    @Test
    fun a_remembered_code_keeps_the_watch_paired_across_a_companion_restart_and_regenerating_replaces_it() {
        settingsFile().writeText("""{"version":1,"rememberPairingCode":true,"pairingCode":"482915"}""")
        val first = startHost("--settings", settingsFile().path)
        val port = first.port
        val repo = repository("482915", port)
        await("paired with the remembered code", first.output) {
            repo.connection.value == ConnectionState.Connected && repo.pairing.value == PairingState.Paired
        }

        // The companion is closed and opened again: same code, no new pairing on the watch.
        stopHost(first)
        val second = startHost("--settings", settingsFile().path, "--port", port.toString())
        await("the watch to reconnect by itself with the stored code", second.output) {
            repo.connection.value == ConnectionState.Connected && repo.pairing.value == PairingState.Paired
        }
        assertEquals("482915", storedCode())
        assertFalse("the code is never printed", second.output.any { it.contains("482915") })

        // Regenerating replaces the stored code; the old code stops working after the next reconnection.
        second.send("regenerate")
        await("the code to be regenerated", second.output) { second.output.any { it == "REGENERATED" } }
        val fresh = storedCode()
        assertNotEquals("482915", fresh)
        assertFalse("the new code is never printed", second.output.any { it.contains(fresh) })
        second.send("rebind")
        await("the old code to be rejected", second.output) { repo.pairing.value == PairingState.Rejected(ErrorCode.InvalidPairingCode) }

        repo.connectWithCode(fresh)
        await("connected with the regenerated code", second.output) {
            repo.connection.value == ConnectionState.Connected && repo.pairing.value == PairingState.Paired
        }
    }

    @Test
    fun without_remembering_a_restart_gives_a_new_code_and_the_old_one_is_rejected() {
        val first = startHost("--code", "123456")
        val port = first.port
        val repo = repository("123456", port)
        await("paired", first.output) { repo.connection.value == ConnectionState.Connected }

        stopHost(first)
        val second = startHost("--port", port.toString()) // no fixed code, nothing remembered: a random one
        await("the old code to be rejected by the restarted companion", second.output, timeoutMs = 25_000) {
            repo.pairing.value == PairingState.Rejected(ErrorCode.InvalidPairingCode)
        }
    }

    @Test
    fun a_companion_killed_during_a_session_is_recovered_exactly_once_as_interrupted() {
        val database = File(folder, "history.db").path
        val activeSession = File(folder, "active-session.json")
        val common = arrayOf("--code", "123456", "--database", database, "--active-session", activeSession.path)
        val first = startHost(*common)
        val port = first.port
        val repo = repository("123456", port)
        await("paired", first.output) { repo.connection.value == ConnectionState.Connected }
        assertEquals(CommandResult.Success, call { repo.startSession("Crash test", 10, 600_000) })
        await("the session", first.output) { slide(repo) == 1 }
        assertEquals(CommandResult.Success, call { repo.next() })
        await("slide 2", first.output) { slide(repo) == 2 }
        assertTrue("the active session is on disk", activeSession.isFile)

        // A real crash: the process is killed, nothing runs on the way out.
        first.process.destroyForcibly()
        first.process.waitFor(10, TimeUnit.SECONDS)
        assertTrue("the file survives the crash", activeSession.isFile)

        val second = startHost(*common, "--port", port.toString())
        await("the watch to come back", second.output) { repo.connection.value == ConnectionState.Connected }
        assertFalse("the file is removed after recovery", activeSession.exists())
        assertEquals("the session is not resumed", SessionStatus.NoSession, repo.session.value)
        val history = call { repo.listHistory() } as HistoryResult.Success
        val record = history.items.single()
        assertEquals("Crash test", record.name)
        assertEquals(FinalState.InterruptedByDisconnection, record.finalState)
        assertEquals(2, record.registeredSlides)
        assertEquals(10, record.plannedSlides)

        // Another restart adds nothing.
        stopHost(second)
        val third = startHost(*common, "--port", port.toString())
        await("the watch to come back again", third.output) { repo.connection.value == ConnectionState.Connected }
        assertEquals(1, (call { repo.listHistory() } as HistoryResult.Success).items.size)
    }
}
