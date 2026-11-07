package com.presenta.wear.data.remote

import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.PresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.protocol.CommandResultPayload
import com.presenta.wear.protocol.EmptyPayload
import com.presenta.wear.protocol.ErrorPayload
import com.presenta.wear.protocol.HelloAcceptedPayload
import com.presenta.wear.protocol.HelloPayload
import com.presenta.wear.protocol.HelloRejectedPayload
import com.presenta.wear.protocol.HistoryItemPayload
import com.presenta.wear.protocol.HistoryListPayload
import com.presenta.wear.protocol.HistoryListResultPayload
import com.presenta.wear.protocol.MessageTypes
import com.presenta.wear.protocol.PROTOCOL_VERSION
import com.presenta.wear.protocol.ParseOutcome
import com.presenta.wear.protocol.SlideSetPayload
import com.presenta.wear.protocol.StartPayload
import com.presenta.wear.protocol.StatePayload
import com.presenta.wear.protocol.WireCodec
import com.presenta.wear.protocol.WireErrorCode
import com.presenta.wear.protocol.WireMapper.toModel
import com.presenta.wear.protocol.WireMessage
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * All the time constants of the connection. The defaults are justified in ADR-47: with a ping every 1.5 s, a silence
 * timeout of 3 s and a check every 0.5 s, a lost connection is noticed within 3.5 s (the target is 5 s, RNF 04).
 */
data class RemoteTimings(
    val pingIntervalMs: Long = 1_500,
    val silenceTimeoutMs: Long = 3_000,
    val watchdogIntervalMs: Long = 500,
    val connectTimeoutMs: Long = 4_000,
    val helloTimeoutMs: Long = 3_000,
    val commandTimeoutMs: Long = 5_000,
    val backoffInitialMs: Long = 500,
    val backoffMaxMs: Long = 4_000,
    /** After this long without a connection the automatic retries stop and the screen shows "disconnected". */
    val giveUpAfterMs: Long = 30_000,
) {
    /** Longest time between the last frame from the companion and the moment the loss is noticed. */
    val worstCaseLossDetectionMs: Long get() = silenceTimeoutMs + watchdogIntervalMs
}

/**
 * The real [PresentaRepository]: speaks protocol v1 over a WebSocket.
 *
 * Everything runs in one coroutine that processes events one at a time (socket callbacks, commands, ticks), so the
 * connection state needs no locks. Time comes from [clock] and from `delay`, so tests run it on virtual time.
 *
 * Behavior (see ADR-45 to 48):
 * - It connects when a pairing code is stored (or given through [connectWithCode]) and sends `hello` with it.
 * - It sends `ping` periodically and treats the connection as lost after a silence; then it moves to
 *   [ConnectionState.Reconnecting], retries with a capped backoff and re-sends `hello` each time.
 * - Commands are never queued: when not connected they fail at once, and the session UI stays visible.
 * - It re-synchronizes the session on every snapshot; the UI interpolates elapsed time between snapshots.
 */
class RemotePresentaRepository(
    private val scope: CoroutineScope,
    private val sockets: SocketFactory,
    private val store: PairingStore,
    private val clock: MonotonicClock,
    private val timings: RemoteTimings = RemoteTimings(),
    private val clientName: String = "Wear OS watch",
) : PresentaRepository {

    private val _connection = MutableStateFlow(ConnectionState.Disconnected)
    override val connection: StateFlow<ConnectionState> = _connection.asStateFlow()

    private val _session = MutableStateFlow<SessionStatus>(SessionStatus.NoSession)
    override val session: StateFlow<SessionStatus> = _session.asStateFlow()

    // Starts as Connecting so the pairing screen does not flash while the stored code is being read.
    private val _pairing = MutableStateFlow<PairingState>(PairingState.Connecting)
    override val pairing: StateFlow<PairingState> = _pairing.asStateFlow()

    private val _summaries = MutableSharedFlow<HistoryItem>(extraBufferCapacity = 8)
    override val summaries: Flow<HistoryItem> = _summaries.asSharedFlow()

    // ---- events processed by the loop ----

    private sealed interface SocketEvent {
        data object Open : SocketEvent

        data class Message(val text: String) : SocketEvent

        data class Closed(val code: Int) : SocketEvent

        data class Failure(val error: Throwable) : SocketEvent
    }

    private sealed interface Event {
        data class FromSocket(val generation: Int, val event: SocketEvent) : Event

        data object Tick : Event

        data object Retry : Event

        data class UseCode(val code: String) : Event

        data class Command(val type: String, val payload: Any, val result: CompletableDeferred<CommandResult>) : Event

        data class History(val result: CompletableDeferred<HistoryResult>) : Event
    }

    private enum class Phase { Idle, Connecting, Handshaking, Online, BackingOff }

    private class PendingCommand(val result: CompletableDeferred<CommandResult>, val deadline: Long)

    private class PendingHistory(val id: String, val result: CompletableDeferred<HistoryResult>, val deadline: Long)

    private val events = Channel<Event>(Channel.UNLIMITED)

    // Loop-confined state.
    private var phase = Phase.Idle
    private var socket: WatchSocket? = null
    private var generation = 0
    private var code: String? = null
    private var serverAddress = ServerDefaults.ADDRESS
    private var attempt = 0
    private var attemptStartedAt = 0L
    private var nextAttemptAt = 0L
    private var tryingSince = 0L
    private var helloSentAt = 0L
    private var lastFrameAt = 0L
    private var lastPingAt = 0L
    private var idCounter = 0
    private val pending = HashMap<String, PendingCommand>()
    private var pendingHistory: PendingHistory? = null

    init {
        scope.launch { runLoop() }
        scope.launch {
            while (true) {
                delay(timings.watchdogIntervalMs)
                events.send(Event.Tick)
            }
        }
    }

    // ---- PresentaRepository ----

    override suspend fun savedPairingCode(): String? = store.load().code

    override fun connectWithCode(code: String) {
        events.trySend(Event.UseCode(code))
    }

    override fun retryConnection() {
        events.trySend(Event.Retry)
    }

    override suspend fun startSession(name: String, plannedSlides: Int, plannedDurationMs: Long): CommandResult =
        command(MessageTypes.SESSION_START, StartPayload(plannedSlides, plannedDurationMs, name))

    override suspend fun pause(): CommandResult = command(MessageTypes.SESSION_PAUSE, EmptyPayload)

    override suspend fun resume(): CommandResult = command(MessageTypes.SESSION_RESUME, EmptyPayload)

    override suspend fun finish(): CommandResult = command(MessageTypes.SESSION_FINISH, EmptyPayload)

    override suspend fun next(): CommandResult = command(MessageTypes.SLIDE_NEXT, EmptyPayload)

    override suspend fun previous(): CommandResult = command(MessageTypes.SLIDE_PREVIOUS, EmptyPayload)

    override suspend fun setSlide(slide: Int): CommandResult = command(MessageTypes.SLIDE_SET, SlideSetPayload(slide))

    override suspend fun acknowledgeAlert(): CommandResult = command(MessageTypes.ALERT_ACKNOWLEDGE, EmptyPayload)

    override suspend fun listHistory(): HistoryResult {
        val result = CompletableDeferred<HistoryResult>()
        events.send(Event.History(result))
        return result.await()
    }

    private suspend fun command(type: String, payload: Any): CommandResult {
        val result = CompletableDeferred<CommandResult>()
        events.send(Event.Command(type, payload, result))
        return result.await()
    }

    // ---- the loop ----

    private suspend fun runLoop() {
        val settings = store.load()
        serverAddress = settings.serverAddress
        code = settings.code
        if (code == null) {
            enterIdle(ConnectionState.Disconnected, PairingState.NeedsCode)
        } else {
            beginTrying()
        }
        for (event in events) {
            when (event) {
                is Event.FromSocket -> if (event.generation == generation) onSocketEvent(event.event)
                Event.Tick -> onTick()
                Event.Retry -> onRetry()
                is Event.UseCode -> onUseCode(event.code)
                is Event.Command -> onCommand(event)
                is Event.History -> onHistory(event)
            }
        }
    }

    private fun now() = clock.nowMs()

    private fun beginTrying() {
        tryingSince = now()
        attempt = 0
        _connection.value = ConnectionState.Reconnecting
        if (_pairing.value !is PairingState.Paired) _pairing.value = PairingState.Connecting
        startAttempt()
    }

    private fun startAttempt() {
        val current = ++generation
        phase = Phase.Connecting
        attemptStartedAt = now()
        socket = try {
            sockets.connect(
                serverAddress,
                object : SocketListener {
                    override fun onOpen() {
                        events.trySend(Event.FromSocket(current, SocketEvent.Open))
                    }

                    override fun onMessage(text: String) {
                        events.trySend(Event.FromSocket(current, SocketEvent.Message(text)))
                    }

                    override fun onClosed(code: Int, reason: String) {
                        events.trySend(Event.FromSocket(current, SocketEvent.Closed(code)))
                    }

                    override fun onFailure(error: Throwable) {
                        events.trySend(Event.FromSocket(current, SocketEvent.Failure(error)))
                    }
                },
            )
        } catch (e: IllegalArgumentException) {
            // A malformed server address: treat it like a failed attempt.
            events.trySend(Event.FromSocket(current, SocketEvent.Failure(e)))
            null
        }
    }

    private fun onSocketEvent(event: SocketEvent) {
        when (event) {
            SocketEvent.Open -> {
                phase = Phase.Handshaking
                helloSentAt = now()
                lastFrameAt = now()
                send(MessageTypes.HELLO, HelloPayload(code.orEmpty(), PROTOCOL_VERSION, clientName))
            }
            is SocketEvent.Message -> {
                lastFrameAt = now()
                when (val outcome = WireCodec.parse(event.text)) {
                    is ParseOutcome.Parsed -> onMessage(outcome.message)
                    is ParseOutcome.Rejected -> Unit // The companion sent something invalid; ignore it, stay connected.
                }
            }
            is SocketEvent.Closed, is SocketEvent.Failure -> onConnectionLost()
        }
    }

    private fun onMessage(message: WireMessage) {
        when (val payload = message.payload) {
            is HelloAcceptedPayload -> if (phase == Phase.Handshaking) onAccepted(payload)
            is HelloRejectedPayload -> if (phase == Phase.Handshaking) onRejected(payload.reason.toModel())
            is StatePayload -> if (phase == Phase.Online) applyState(payload)
            is CommandResultPayload -> {
                val waiting = pending.remove(payload.correlationId) ?: return
                waiting.result.complete(
                    if (payload.ok) CommandResult.Success else CommandResult.Failure(payload.errorCode?.toModel() ?: ErrorCode.InternalError),
                )
            }
            is HistoryItemPayload -> _summaries.tryEmit(payload.toModel())
            is HistoryListResultPayload -> {
                pendingHistory?.result?.complete(HistoryResult.Success(payload.items.map { it.toModel() }))
                pendingHistory = null
            }
            is ErrorPayload -> onProtocolError(payload)
            else -> Unit // pong and anything else only count as a sign of life.
        }
    }

    private fun onAccepted(payload: HelloAcceptedPayload) {
        if (payload.protocolVersion != PROTOCOL_VERSION) {
            onRejected(ErrorCode.UnsupportedVersion)
            return
        }
        phase = Phase.Online
        attempt = 0
        lastFrameAt = now()
        lastPingAt = now()
        _connection.value = ConnectionState.Connected
        _pairing.value = PairingState.Paired
        applyState(payload.state)
    }

    private fun onRejected(reason: ErrorCode) {
        closeSocket()
        failPending(ErrorCode.InvalidMessage)
        enterIdle(ConnectionState.Disconnected, PairingState.Rejected(reason))
    }

    private fun applyState(state: StatePayload) {
        _session.value = state.session?.let { SessionStatus.Active(it.toModel(), now()) } ?: SessionStatus.NoSession
    }

    private fun onProtocolError(error: ErrorPayload) {
        val id = error.correlationId ?: return
        val code = error.code.toModel()
        pending.remove(id)?.result?.complete(CommandResult.Failure(code))
        pendingHistory?.takeIf { it.id == id }?.let {
            it.result.complete(HistoryResult.Failure(code))
            pendingHistory = null
        }
    }

    /** The connection ended or timed out: tell the UI, fail what was waiting, and schedule another attempt. */
    private fun onConnectionLost() {
        if (phase == Phase.Idle || phase == Phase.BackingOff) return
        closeSocket()
        failPending(ErrorCode.InvalidMessage)
        _connection.value = ConnectionState.Reconnecting
        if (phase == Phase.Online) tryingSince = now()
        attempt++
        nextAttemptAt = now() + backoff(attempt)
        phase = Phase.BackingOff
    }

    private fun backoff(attempt: Int): Long {
        var delayMs = timings.backoffInitialMs
        repeat((attempt - 1).coerceAtLeast(0)) { delayMs = minOf(delayMs * 2, timings.backoffMaxMs) }
        return minOf(delayMs, timings.backoffMaxMs)
    }

    private fun onTick() {
        val now = now()
        when (phase) {
            Phase.Connecting -> if (now - attemptStartedAt > timings.connectTimeoutMs) onConnectionLost()
            Phase.Handshaking -> if (now - helloSentAt > timings.helloTimeoutMs) onConnectionLost()
            Phase.Online -> when {
                now - lastFrameAt > timings.silenceTimeoutMs -> onConnectionLost()
                now - lastPingAt >= timings.pingIntervalMs -> {
                    lastPingAt = now
                    send(MessageTypes.PING, EmptyPayload)
                }
            }
            Phase.BackingOff -> when {
                now - tryingSince >= timings.giveUpAfterMs -> enterIdle(ConnectionState.Disconnected, _pairing.value)
                now >= nextAttemptAt -> startAttempt()
            }
            Phase.Idle -> Unit
        }
        expirePending(now)
    }

    private fun onRetry() {
        if (code == null || phase == Phase.Online || phase == Phase.Connecting || phase == Phase.Handshaking) return
        if (_pairing.value is PairingState.Rejected) return // the user has to enter a new code first
        closeSocket()
        beginTrying()
    }

    private fun onUseCode(newCode: String) {
        closeSocket()
        failPending(ErrorCode.InvalidMessage)
        code = newCode
        _session.value = SessionStatus.NoSession
        _pairing.value = PairingState.Connecting
        // Saving is quick and must finish before the next attempt, so the stored code is the one in use.
        scope.launch {
            store.saveCode(newCode)
        }
        beginTrying()
    }

    private fun onCommand(event: Event.Command) {
        if (phase != Phase.Online) {
            event.result.complete(CommandResult.Failure(ErrorCode.InvalidMessage)) // not queued
            return
        }
        val id = nextId()
        if (!send(event.type, event.payload, id)) {
            event.result.complete(CommandResult.Failure(ErrorCode.InvalidMessage))
            return
        }
        pending[id] = PendingCommand(event.result, now() + timings.commandTimeoutMs)
    }

    private fun onHistory(event: Event.History) {
        if (phase != Phase.Online) {
            event.result.complete(HistoryResult.Failure(ErrorCode.InvalidMessage))
            return
        }
        pendingHistory?.result?.complete(HistoryResult.Failure(ErrorCode.InvalidMessage))
        val id = nextId()
        if (!send(MessageTypes.HISTORY_LIST, HistoryListPayload(), id)) {
            event.result.complete(HistoryResult.Failure(ErrorCode.InvalidMessage))
            return
        }
        pendingHistory = PendingHistory(id, event.result, now() + timings.commandTimeoutMs)
    }

    private fun expirePending(now: Long) {
        val expired = pending.filterValues { it.deadline <= now }.keys.toList()
        for (id in expired) pending.remove(id)?.result?.complete(CommandResult.Failure(ErrorCode.InvalidMessage))
        pendingHistory?.takeIf { it.deadline <= now }?.let {
            it.result.complete(HistoryResult.Failure(ErrorCode.InvalidMessage))
            pendingHistory = null
        }
    }

    private fun failPending(code: ErrorCode) {
        pending.values.forEach { it.result.complete(CommandResult.Failure(code)) }
        pending.clear()
        pendingHistory?.result?.complete(HistoryResult.Failure(code))
        pendingHistory = null
    }

    private fun enterIdle(connection: ConnectionState, pairing: PairingState) {
        phase = Phase.Idle
        _connection.value = connection
        _pairing.value = pairing
    }

    private fun closeSocket() {
        socket?.cancel()
        socket = null
        generation++ // anything the old socket still reports is ignored
    }

    private fun nextId(): String = "w-" + (++idCounter)

    private fun send(type: String, payload: Any, id: String = nextId()): Boolean =
        socket?.send(WireCodec.encode(WireMessage(id, type, payload))) ?: false
}
