package com.presenta.wear

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.remote.InMemoryPairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import com.presenta.wear.haptics.HapticPattern
import com.presenta.wear.haptics.HapticsCoordinator
import com.presenta.wear.haptics.RecordingHapticsPlayer
import com.presenta.wear.model.AlertKind
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
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/**
 * Every alert kind, from the real server running 100 times faster than real time (Presenta.DevHost --time-scale 100),
 * into the real Kotlin client, with the recording haptics player standing in for the vibrator. A 15-minute session
 * lasts 9 seconds. Opt-in like the other cross-stack test: `gradlew :app:crossStackTest`.
 */
class AlertsCrossStackTest {
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
        val started = ProcessBuilder("dotnet", dll, "--code", CODE, "--grace-seconds", "60", "--time-scale", "$SCALE")
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

    private fun await(what: String, timeoutMs: Long = 30_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) { while (!condition()) delay(10) }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what\nHost output:\n" + output.joinToString("\n"))
        }
    }

    private fun <T> call(block: suspend () -> T): T = runBlocking { withTimeout(10_000) { block() } }

    private fun sleep(ms: Long) = runBlocking { delay(ms) }

    private class Rig(val repo: RemotePresentaRepository, val player: RecordingHapticsPlayer) {
        /** (kind, sequence) of every alert the client has seen, in order, each once. */
        val alerts = CopyOnWriteArrayList<Pair<AlertKind, Int>>()
    }

    private fun rig(): Rig {
        val repo = RemotePresentaRepository(
            scope = scope,
            sockets = OkHttpSocketFactory(),
            store = InMemoryPairingStore(CODE, "ws://127.0.0.1:$port"),
            clock = clock,
        )
        val player = RecordingHapticsPlayer()
        val haptics = HapticsCoordinator(player)
        haptics.start(scope, repo.session)
        val rig = Rig(repo, player)
        scope.launch {
            repo.session.collect { status ->
                val alert = (status as? SessionStatus.Active)?.snapshot?.activeAlert ?: return@collect
                val seen = alert.kind to alert.sequence
                if (rig.alerts.lastOrNull() != seen && seen !in rig.alerts) rig.alerts += seen
            }
        }
        await("connected") { repo.connection.value == ConnectionState.Connected }
        return rig
    }

    @Test
    fun a_standing_still_presenter_gets_every_time_alert_and_each_vibrates_exactly_once() {
        val rig = rig()
        assertEquals(CommandResult.Success, call { rig.repo.startSession("Alerts", 20, 15 * 60_000L) })

        await("overtime") { rig.alerts.any { it.first == AlertKind.Overtime } }
        sleep(600) // anything wrongly repeated would show up now

        // 95 s behind, 630 s running out, 840 s critical, 901 s overtime (docs/manual-test-guide.md, scenario table).
        assertEquals(
            listOf(
                AlertKind.BehindSchedule to 1,
                AlertKind.TimeRunningOut to 2,
                AlertKind.TimeCritical to 3,
                AlertKind.Overtime to 4,
            ),
            rig.alerts.toList(),
        )
        assertEquals(
            listOf(HapticPattern.Warning, HapticPattern.Warning, HapticPattern.Critical, HapticPattern.Critical),
            rig.player.played,
        )
    }

    @Test
    fun rushing_gives_too_fast_and_an_acknowledged_pace_alert_repeats_with_a_new_identity() {
        val rig = rig()
        assertEquals(CommandResult.Success, call { rig.repo.startSession("Rush", 20, 15 * 60_000L) })
        // Past the 60 s warm-up (0.6 s here), jump to the last slide: too fast right away.
        await("warm-up") { ((rig.repo.session.value as? SessionStatus.Active)?.snapshot?.elapsedMs ?: 0) > 61_000 }
        assertEquals(CommandResult.Success, call { rig.repo.setSlide(20) })
        await("the first too-fast alert") { rig.alerts.isNotEmpty() }
        sleep(300)
        assertEquals(listOf(AlertKind.TooFast to 1), rig.alerts.toList())
        assertEquals(listOf(HapticPattern.Warning), rig.player.played)

        // Acknowledging vibrates nothing; the same condition fires again after the 120 s cooldown (1.2 s here).
        assertEquals(CommandResult.Success, call { rig.repo.acknowledgeAlert() })
        sleep(300)
        assertEquals(listOf(HapticPattern.Warning), rig.player.played)
        await("the repeated too-fast alert") { rig.alerts.size >= 2 }
        sleep(300)
        assertEquals(listOf(AlertKind.TooFast to 1, AlertKind.TooFast to 2), rig.alerts.toList())
        assertEquals(listOf(HapticPattern.Warning, HapticPattern.Warning), rig.player.played)
    }

    private companion object {
        const val CODE = "123456"
        const val SCALE = 100
    }
}
