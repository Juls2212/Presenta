package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.protocol.AlertPayload
import com.presenta.wear.protocol.CommandResultPayload
import com.presenta.wear.protocol.EmptyPayload
import com.presenta.wear.protocol.ErrorPayload
import com.presenta.wear.protocol.HelloAcceptedPayload
import com.presenta.wear.protocol.HelloRejectedPayload
import com.presenta.wear.protocol.HistoryItemPayload
import com.presenta.wear.protocol.HistoryListResultPayload
import com.presenta.wear.protocol.MessageTypes
import com.presenta.wear.protocol.ParseOutcome
import com.presenta.wear.protocol.RecommendationPayload
import com.presenta.wear.protocol.SessionPayload
import com.presenta.wear.protocol.StatePayload
import com.presenta.wear.protocol.WireAlertKind
import com.presenta.wear.protocol.WireAlertSeverity
import com.presenta.wear.protocol.WireCodec
import com.presenta.wear.protocol.WireErrorCode
import com.presenta.wear.protocol.WireFinalState
import com.presenta.wear.protocol.WireMessage
import com.presenta.wear.protocol.WirePaceStatus
import com.presenta.wear.protocol.WireRecommendationCode
import com.presenta.wear.protocol.WireSessionState
import java.io.IOException
import java.time.Instant

/** In-memory [PairingStore]. */
class InMemoryPairingStore(
    var code: String? = "123456",
    var address: String = "ws://test.invalid:8765",
) : PairingStore {
    override suspend fun load() = PairingSettings(code, address)

    override suspend fun saveCode(code: String) {
        this.code = code
    }
}

/** A scripted WebSocket: tests play the companion by calling [open], [receive], [serverClose] and [fail]. */
class FakeSocket(val url: String, private val listener: SocketListener, val createdAtMs: Long) : WatchSocket {
    val sent = mutableListOf<String>()
    var cancelled = false
        private set

    override fun send(text: String): Boolean {
        if (cancelled) return false
        sent += text
        return true
    }

    override fun close(code: Int, reason: String) {
        cancelled = true
    }

    override fun cancel() {
        cancelled = true
    }

    fun open() = listener.onOpen()

    fun receive(text: String) = listener.onMessage(text)

    fun serverClose() = listener.onClosed(1001, "going away")

    fun fail() = listener.onFailure(IOException("connection refused"))

    /** The parsed messages the watch has sent so far. */
    val messages: List<WireMessage>
        get() = sent.map { (WireCodec.parse(it) as ParseOutcome.Parsed).message }

    fun sentOfType(type: String): List<WireMessage> = messages.filter { it.type == type }
}

class FakeSocketFactory(private val clock: MonotonicClock) : SocketFactory {
    val sockets = mutableListOf<FakeSocket>()

    override fun connect(url: String, listener: SocketListener): WatchSocket =
        FakeSocket(url, listener, clock.nowMs()).also { sockets += it }
}

/** JSON the companion would send. */
object Companion {
    fun message(type: String, payload: Any, id: String = "s-1"): String = WireCodec.encode(WireMessage(id, type, payload))

    fun accepted(session: SessionPayload? = null): String =
        message(MessageTypes.HELLO_ACCEPTED, HelloAcceptedPayload(1, StatePayload(Instant.parse("2026-11-02T14:30:00Z"), session)))

    fun rejected(reason: WireErrorCode, retryAfterMs: Long? = null): String =
        message(MessageTypes.HELLO_REJECTED, HelloRejectedPayload(reason, retryAfterMs))

    fun state(session: SessionPayload?): String =
        message(MessageTypes.STATE_UPDATE, StatePayload(Instant.parse("2026-11-02T14:31:00Z"), session))

    fun ok(correlationId: String): String = message(MessageTypes.COMMAND_RESULT, CommandResultPayload(correlationId, true, null))

    fun failed(correlationId: String, code: WireErrorCode): String =
        message(MessageTypes.COMMAND_RESULT, CommandResultPayload(correlationId, false, code))

    fun pong(): String = message(MessageTypes.PONG, EmptyPayload)

    fun error(code: WireErrorCode, correlationId: String?): String =
        message(MessageTypes.ERROR, ErrorPayload(code, "developer message", correlationId))

    fun summary(registered: Int = 12): String = message(MessageTypes.SESSION_SUMMARY, historyItem("Done", registered))

    fun history(vararg items: HistoryItemPayload): String =
        message(MessageTypes.HISTORY_LIST_RESULT, HistoryListResultPayload(items.toList()))

    fun historyItem(name: String, registered: Int = 12) = HistoryItemPayload(
        name = name,
        finishedAtUtc = Instant.parse("2026-11-02T14:45:00Z"),
        plannedDurationMs = 900_000,
        actualDurationMs = 872_000,
        plannedSlides = 20,
        registeredSlides = registered,
        averageSlidesPerMinute = 1.376,
        finalState = WireFinalState.FinishedManually,
        endedWithinPlannedTime = true,
    )

    fun session(
        slide: Int = 7,
        total: Int = 20,
        elapsedMs: Long = 512_000,
        plannedMs: Long = 900_000,
        running: Boolean = true,
        alert: AlertPayload? = null,
    ) = SessionPayload(
        state = if (running) WireSessionState.Running else WireSessionState.Paused,
        name = "Demo",
        currentSlide = slide,
        totalSlides = total,
        plannedDurationMs = plannedMs,
        elapsedMs = elapsedMs,
        remainingMs = plannedMs - elapsedMs,
        running = running,
        progressRatio = slide.toDouble() / total,
        paceStatus = WirePaceStatus.OnTrack,
        slidesRemaining = total - slide,
        averageSlidesPerMinute = 0.82,
        finalState = null,
        activeAlert = alert,
        recommendation = RecommendationPayload(WireRecommendationCode.OnTrack, total - slide, plannedMs - elapsedMs, null),
    )

    val timeAlert = AlertPayload(WireAlertKind.TimeRunningOut, WireAlertSeverity.Warning, 600_000, 300_000, 9, -2)
}
