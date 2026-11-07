package com.presenta.wear.protocol

import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.SerializationException
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.PrimitiveSerialDescriptor
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder

/*
 * Wire models of protocol v1 (shared-contracts/protocol-v1.md and schemas/). They only describe JSON;
 * WireMapper converts them to the UI models. Enum entries carry their lowerCamelCase wire names.
 */

const val PROTOCOL_VERSION = 1

/** UTC instant in ISO 8601 that must end in "Z" (offsets are rejected, as the companion does). */
object UtcInstantSerializer : KSerializer<Instant> {
    private val pattern = Regex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]{1,7})?Z$")
    private val writer = DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'").withZone(ZoneOffset.UTC)

    override val descriptor: SerialDescriptor = PrimitiveSerialDescriptor("UtcInstant", PrimitiveKind.STRING)

    override fun deserialize(decoder: Decoder): Instant {
        val text = decoder.decodeString()
        if (!pattern.matches(text)) throw SerializationException("A UTC timestamp must be ISO 8601 and end in 'Z'.")
        return Instant.parse(text)
    }

    override fun serialize(encoder: Encoder, value: Instant) = encoder.encodeString(writer.format(value))
}

@Serializable
enum class WireErrorCode {
    @SerialName("invalidPairingCode") InvalidPairingCode,
    @SerialName("pairingLockedOut") PairingLockedOut,
    @SerialName("notPaired") NotPaired,
    @SerialName("unsupportedVersion") UnsupportedVersion,
    @SerialName("invalidMessage") InvalidMessage,
    @SerialName("invalidState") InvalidState,
    @SerialName("slideOutOfRange") SlideOutOfRange,
    @SerialName("noActiveSession") NoActiveSession,
    @SerialName("targetWindowUnavailable") TargetWindowUnavailable,
    @SerialName("keyDeliveryFailed") KeyDeliveryFailed,
    @SerialName("internalError") InternalError,
}

@Serializable
enum class WireSessionState {
    @SerialName("configured") Configured,
    @SerialName("running") Running,
    @SerialName("paused") Paused,
    @SerialName("finished") Finished,
}

@Serializable
enum class WireFinalState {
    @SerialName("finishedManually") FinishedManually,
    @SerialName("endedWhilePaused") EndedWhilePaused,
    @SerialName("interruptedByDisconnection") InterruptedByDisconnection,
}

@Serializable
enum class WirePaceStatus {
    @SerialName("onTrack") OnTrack,
    @SerialName("behind") Behind,
    @SerialName("tooFast") TooFast,
    @SerialName("notEnoughData") NotEnoughData,
}

@Serializable
enum class WireAlertKind {
    @SerialName("timeRunningOut") TimeRunningOut,
    @SerialName("timeCritical") TimeCritical,
    @SerialName("overtime") Overtime,
    @SerialName("behindSchedule") BehindSchedule,
    @SerialName("tooFast") TooFast,
}

@Serializable
enum class WireAlertSeverity {
    @SerialName("info") Info,
    @SerialName("warning") Warning,
    @SerialName("critical") Critical,
}

@Serializable
enum class WireRecommendationCode {
    @SerialName("onTrack") OnTrack,
    @SerialName("speedUpSlightly") SpeedUpSlightly,
    @SerialName("speedUpNow") SpeedUpNow,
    @SerialName("slowDown") SlowDown,
    @SerialName("wrapUp") WrapUp,
    @SerialName("overtime") Overtime,
}

/** Payload of messages without data (`session.pause`, `ping`, ...). Extra properties are ignored. */
@Serializable
data object EmptyPayload

@Serializable
data class HelloPayload(val pairingCode: String, val protocolVersion: Int, val clientName: String)

@Serializable
data class HelloAcceptedPayload(val protocolVersion: Int, val state: StatePayload)

@Serializable
data class HelloRejectedPayload(val reason: WireErrorCode, val retryAfterMs: Long? = null)

@Serializable
data class StartPayload(val plannedSlides: Int, val plannedDurationMs: Long, val name: String? = null)

@Serializable
data class SlideSetPayload(val slide: Int)

@Serializable
data class HistoryListPayload(val limit: Int? = null)

/** Full companion state. [session] is null when there is no active session (the explicit "no session" form). */
@Serializable
data class StatePayload(
    @Serializable(with = UtcInstantSerializer::class) val serverTimeUtc: Instant,
    val session: SessionPayload?,
)

@Serializable
data class SessionPayload(
    val state: WireSessionState,
    val name: String,
    val currentSlide: Int,
    val totalSlides: Int,
    val plannedDurationMs: Long,
    val elapsedMs: Long,
    val remainingMs: Long,
    val running: Boolean,
    val progressRatio: Double,
    val paceStatus: WirePaceStatus,
    val slidesRemaining: Int,
    val averageSlidesPerMinute: Double,
    val finalState: WireFinalState?,
    val activeAlert: AlertPayload?,
    val recommendation: RecommendationPayload?,
)

@Serializable
data class AlertPayload(
    val kind: WireAlertKind,
    val severity: WireAlertSeverity,
    val raisedAtElapsedMs: Long,
    val remainingMs: Long,
    val slidesRemaining: Int,
    val slideDeviation: Int,
)

@Serializable
data class RecommendationPayload(
    val code: WireRecommendationCode,
    val slidesRemaining: Int,
    val timeRemainingMs: Long,
    val freeText: String?,
)

@Serializable
data class CommandResultPayload(val correlationId: String, val ok: Boolean, val errorCode: WireErrorCode?)

@Serializable
data class HistoryItemPayload(
    val name: String,
    @Serializable(with = UtcInstantSerializer::class) val finishedAtUtc: Instant,
    val plannedDurationMs: Long,
    val actualDurationMs: Long,
    val plannedSlides: Int,
    val registeredSlides: Int,
    val averageSlidesPerMinute: Double,
    val finalState: WireFinalState,
    val endedWithinPlannedTime: Boolean,
)

@Serializable
data class HistoryListResultPayload(val items: List<HistoryItemPayload>)

@Serializable
data class ErrorPayload(val code: WireErrorCode, val message: String, val correlationId: String? = null)
