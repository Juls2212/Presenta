package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.protocol.MessageTypes
import com.presenta.wear.protocol.ParseOutcome
import com.presenta.wear.protocol.WireCodec
import com.presenta.wear.protocol.WireErrorCode
import java.util.concurrent.CopyOnWriteArrayList
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * The real OkHttp transport and the real repository against a scripted WebSocket server on the JVM (real time,
 * short timings). It complements the virtual-time tests, which prove the 5 s budget with the production constants.
 */
class OkHttpIntegrationTest {
    private val server = MockWebServer()
    private val scope = CoroutineScope(Dispatchers.Default + Job())
    private val clock = MonotonicClock { System.nanoTime() / 1_000_000 }
    private val fast = RemoteTimings(
        pingIntervalMs = 100,
        silenceTimeoutMs = 500,
        watchdogIntervalMs = 50,
        connectTimeoutMs = 2_000,
        helloTimeoutMs = 1_000,
        commandTimeoutMs = 2_000,
        backoffInitialMs = 100,
        backoffMaxMs = 200,
        giveUpAfterMs = 20_000,
    )

    /** A scripted companion socket. */
    private class Script(
        private val answerPings: Boolean = true,
        private val acceptCode: String = "123456",
        private val closeAfterHello: Boolean = false,
        private val garbageAfterHello: Boolean = false,
    ) : WebSocketListener() {
        val received = CopyOnWriteArrayList<String>()

        fun types(): List<String> = received.mapNotNull { (WireCodec.parse(it) as? ParseOutcome.Parsed)?.message?.type }

        override fun onMessage(webSocket: WebSocket, text: String) {
            received += text
            val message = (WireCodec.parse(text) as? ParseOutcome.Parsed)?.message ?: return
            when (message.type) {
                MessageTypes.HELLO -> {
                    val code = (message.payload as com.presenta.wear.protocol.HelloPayload).pairingCode
                    if (code != acceptCode) {
                        webSocket.send(Companion.rejected(WireErrorCode.InvalidPairingCode))
                        webSocket.close(1008, "pairing rejected")
                        return
                    }
                    webSocket.send(Companion.accepted(Companion.session(slide = 3)))
                    if (garbageAfterHello) {
                        webSocket.send("not json at all")
                        webSocket.send("""{"version":1,"type":"slide.teleport","id":"x","payload":{}}""")
                    }
                    if (closeAfterHello) webSocket.close(1001, "restart")
                }
                MessageTypes.PING -> if (answerPings) webSocket.send(Companion.pong())
                MessageTypes.SLIDE_NEXT -> webSocket.send(Companion.ok(message.id))
            }
        }

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(code, reason)
        }

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = Unit
    }

    @Before
    fun setUp() {
        server.start()
    }

    @After
    fun tearDown() {
        scope.cancel()
        server.shutdown()
    }

    private fun enqueue(script: Script) {
        server.enqueue(MockResponse().withWebSocketUpgrade(script))
    }

    private fun repository(code: String = "123456") = RemotePresentaRepository(
        scope = scope,
        sockets = OkHttpSocketFactory(),
        store = InMemoryPairingStore(code, server.url("/").toString().replaceFirst("http://", "ws://")),
        clock = clock,
        timings = fast,
    )

    private fun await(what: String, timeoutMs: Long = 5_000, condition: () -> Boolean) = runBlocking {
        try {
            withTimeout(timeoutMs) {
                while (!condition()) delay(10)
            }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw AssertionError("Timed out waiting for: $what")
        }
    }

    @Test
    fun pairs_over_a_real_socket_applies_the_state_and_runs_a_command() {
        val script = Script()
        enqueue(script)
        val repo = repository()

        await("connected") { repo.connection.value == ConnectionState.Connected }
        assertEquals(PairingState.Paired, repo.pairing.value)
        assertEquals(3, (repo.session.value as SessionStatus.Active).snapshot.currentSlide)
        val result = runBlocking { withTimeout(5_000) { repo.next() } }

        assertEquals(CommandResult.Success, result)
        assertTrue(MessageTypes.SLIDE_NEXT in script.types())
    }

    @Test
    fun a_wrong_code_is_rejected_over_the_wire_and_not_retried() {
        val script = Script(acceptCode = "999999")
        enqueue(script)
        val repo = repository(code = "123456")

        await("rejection") { repo.pairing.value == PairingState.Rejected(ErrorCode.InvalidPairingCode) }
        runBlocking { delay(600) }

        assertEquals(ConnectionState.Disconnected, repo.connection.value)
        assertEquals("only one connection attempt", 1, server.requestCount)
    }

    @Test
    fun the_repository_reconnects_and_says_hello_again_after_the_server_closes_the_socket() {
        val first = Script(closeAfterHello = true)
        val second = Script()
        enqueue(first)
        enqueue(second)
        val repo = repository()

        await("a second hello on a new connection") { second.types().contains(MessageTypes.HELLO) }
        await("connected again") { repo.connection.value == ConnectionState.Connected }

        assertEquals(1, first.types().count { it == MessageTypes.HELLO })
        assertEquals(1, second.types().count { it == MessageTypes.HELLO })
    }

    @Test
    fun a_server_that_stops_answering_pings_is_detected_as_lost() {
        enqueue(Script(answerPings = false))
        enqueue(Script(answerPings = false))
        val repo = repository()
        await("connected") { repo.connection.value == ConnectionState.Connected }
        val connectedAt = clock.nowMs()

        await("reconnecting", timeoutMs = 3_000) { repo.connection.value == ConnectionState.Reconnecting }

        val detectedAfter = clock.nowMs() - connectedAt
        assertTrue("detected after $detectedAfter ms (silence timeout ${fast.silenceTimeoutMs} ms)", detectedAfter < 1_500)
    }

    @Test
    fun invalid_and_unknown_frames_from_the_server_are_ignored() {
        enqueue(Script(garbageAfterHello = true))
        val repo = repository()

        await("connected") { repo.connection.value == ConnectionState.Connected }
        runBlocking { delay(400) }

        assertEquals(ConnectionState.Connected, repo.connection.value)
        assertEquals(3, (repo.session.value as SessionStatus.Active).snapshot.currentSlide)
    }

    @Test
    fun an_unreachable_server_leaves_the_watch_reconnecting_without_crashing() {
        val repo = RemotePresentaRepository(
            scope = scope,
            sockets = OkHttpSocketFactory(),
            store = InMemoryPairingStore("123456", "ws://127.0.0.1:1/"), // nothing listens on port 1
            clock = clock,
            timings = fast,
        )

        await("reconnecting") { repo.connection.value == ConnectionState.Reconnecting }
        runBlocking { delay(500) }

        assertTrue(repo.connection.value != ConnectionState.Connected)
    }

    @Test
    fun a_malformed_server_address_does_not_crash() {
        val repo = RemotePresentaRepository(
            scope = scope,
            sockets = OkHttpSocketFactory(),
            store = InMemoryPairingStore("123456", "not a url"),
            clock = clock,
            timings = fast,
        )

        runBlocking { delay(500) }

        assertTrue(repo.connection.value != ConnectionState.Connected)
    }
}
