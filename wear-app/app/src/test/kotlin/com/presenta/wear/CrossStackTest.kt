package com.presenta.wear

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.remote.InMemoryPairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionState
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
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * The real Kotlin client against the real C# server (Presenta.DevHost, which prints keys instead of pressing them).
 * Opt-in because it needs the .NET SDK: run `gradlew :app:crossStackTest`. It is excluded from `testDebugUnitTest`.
 */
class CrossStackTest {
    private val clock = MonotonicClock { System.nanoTime() / 1_000_000 }
    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private val output = CopyOnWriteArrayList<String>()
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
        Thread {
            BufferedReader(InputStreamReader(started.inputStream)).forEachLine { output += it }
        }.also { it.isDaemon = true }.start()
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

    private fun repository(code: String) = RemotePresentaRepository(
        scope = scope,
        sockets = OkHttpSocketFactory(),
        store = InMemoryPairingStore(code, "ws://127.0.0.1:$port"),
        clock = clock,
    )

    private fun await(what: String, timeoutMs: Long = 15_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) {
                while (!condition()) delay(20)
            }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what\nHost output:\n" + output.joinToString("\n"))
        }
    }

    private fun keys() = output.filter { it.startsWith("KEY ") }

    private fun slide(repo: RemotePresentaRepository) = (repo.session.value as? SessionStatus.Active)?.snapshot?.currentSlide

    private fun <T> call(block: suspend () -> T): T = runBlocking { withTimeout(10_000) { block() } }

    @Test
    fun pair_run_a_whole_presentation_and_survive_a_reconnect() {
        val repo = repository(CODE)
        val summaries = CopyOnWriteArrayList<HistoryItem>()
        scope.launch { repo.summaries.collect { summaries += it } }

        // pair
        await("connected and paired") { repo.connection.value == ConnectionState.Connected && repo.pairing.value == PairingState.Paired }
        assertEquals(SessionStatus.NoSession, repo.session.value)

        // start
        assertEquals(CommandResult.Success, call { repo.startSession("Cross stack", 10, 600_000) })
        await("the session to appear") { slide(repo) == 1 }
        val started = (repo.session.value as SessionStatus.Active).snapshot
        assertEquals("Cross stack", started.name)
        assertEquals(10, started.totalSlides)
        assertEquals(600_000, started.plannedDurationMs)
        assertEquals(SessionState.Running, started.state)

        // next and previous: one key each, Right then Left
        assertEquals(CommandResult.Success, call { repo.next() })
        await("slide 2") { slide(repo) == 2 }
        assertEquals(listOf("KEY Right"), keys())
        assertEquals(CommandResult.Success, call { repo.previous() })
        await("slide 1") { slide(repo) == 1 }
        assertEquals(listOf("KEY Right", "KEY Left"), keys())

        // previous at slide 1 and set slide: no key
        assertEquals(CommandResult.Success, call { repo.previous() })
        assertEquals(CommandResult.Success, call { repo.setSlide(5) })
        await("slide 5") { slide(repo) == 5 }
        assertEquals(CommandResult.Failure(ErrorCode.SlideOutOfRange), call { repo.setSlide(50) })
        assertEquals("no key for set slide, nor at the first slide", listOf("KEY Right", "KEY Left"), keys())

        // pause and resume
        assertEquals(CommandResult.Success, call { repo.pause() })
        await("paused") { (repo.session.value as? SessionStatus.Active)?.snapshot?.state == SessionState.Paused }
        assertEquals(CommandResult.Success, call { repo.resume() })
        await("running") { (repo.session.value as? SessionStatus.Active)?.snapshot?.state == SessionState.Running }

        // the server restarts its listener: the watch reconnects, says hello again and gets the session back
        stdin!!.apply { write("rebind\n"); flush() }
        await("the host to rebind") { output.any { it.startsWith("REBOUND") } }
        await("connected again") { repo.connection.value == ConnectionState.Connected }
        await("the session after the reconnect") { slide(repo) == 5 }
        assertEquals(SessionState.Running, (repo.session.value as SessionStatus.Active).snapshot.state)

        // finish: the summary arrives and the session is gone
        assertEquals(CommandResult.Success, call { repo.finish() })
        await("the summary") { summaries.isNotEmpty() }
        await("no session") { repo.session.value == SessionStatus.NoSession }
        val summary = summaries.single()
        assertEquals("Cross stack", summary.name)
        assertEquals(5, summary.registeredSlides)
        assertEquals(10, summary.plannedSlides)
        assertEquals(FinalState.FinishedManually, summary.finalState)

        // history
        val history = call { repo.listHistory() } as HistoryResult.Success
        assertEquals(listOf("Cross stack"), history.items.map { it.name })
        assertEquals(5, history.items.single().registeredSlides)

        // the keyboard log shows Right and Left only, exactly as expected
        assertEquals(listOf("KEY Right", "KEY Left"), keys())
        assertTrue(keys().all { it == "KEY Right" || it == "KEY Left" })
    }

    @Test
    fun a_wrong_pairing_code_is_rejected_by_the_real_server() {
        val repo = repository("000000")

        await("the rejection") { repo.pairing.value == PairingState.Rejected(ErrorCode.InvalidPairingCode) }

        assertEquals(ConnectionState.Disconnected, repo.connection.value)
        assertEquals(CommandResult.Failure(ErrorCode.InvalidMessage), call { repo.next() })
    }

    @Test
    fun five_wrong_codes_lock_the_address_out_and_the_message_comes_through() {
        repeat(5) {
            val attempt = repository("000000")
            await("rejection ${it + 1}") { attempt.pairing.value == PairingState.Rejected(ErrorCode.InvalidPairingCode) }
        }

        val locked = repository(CODE) // even the right code is refused during the lockout
        await("the lockout") { locked.pairing.value == PairingState.Rejected(ErrorCode.PairingLockedOut) }
    }

    @Test
    fun a_new_code_after_a_rejection_connects() {
        val repo = repository("000000")
        await("the rejection") { repo.pairing.value is PairingState.Rejected }

        repo.connectWithCode(CODE)

        await("connected with the right code") { repo.connection.value == ConnectionState.Connected }
        assertEquals(PairingState.Paired, repo.pairing.value)
    }

    private companion object {
        const val CODE = "123456"
    }
}
