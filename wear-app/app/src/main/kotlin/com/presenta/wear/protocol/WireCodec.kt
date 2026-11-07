package com.presenta.wear.protocol

import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.descriptors.StructureKind
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.put

/** Wire names of all message types of protocol v1. */
object MessageTypes {
    // Watch to companion
    const val HELLO = "hello"
    const val SESSION_START = "session.start"
    const val SESSION_PAUSE = "session.pause"
    const val SESSION_RESUME = "session.resume"
    const val SESSION_FINISH = "session.finish"
    const val SLIDE_NEXT = "slide.next"
    const val SLIDE_PREVIOUS = "slide.previous"
    const val SLIDE_SET = "slide.set"
    const val ALERT_ACKNOWLEDGE = "alert.acknowledge"
    const val HISTORY_LIST = "history.list"
    const val PING = "ping"

    // Companion to watch
    const val HELLO_ACCEPTED = "hello.accepted"
    const val HELLO_REJECTED = "hello.rejected"
    const val STATE_UPDATE = "state.update"
    const val COMMAND_RESULT = "command.result"
    const val SESSION_SUMMARY = "session.summary"
    const val HISTORY_LIST_RESULT = "history.list.result"
    const val ERROR = "error"
    const val PONG = "pong"
}

/** One message: envelope id and type plus a typed payload (the class depends on [type]). */
data class WireMessage(val id: String, val type: String, val payload: Any)

sealed interface ParseOutcome {
    data class Parsed(val message: WireMessage) : ParseOutcome

    /** The text is not a valid protocol message. It never throws; the caller decides what to do. */
    data class Rejected(val code: WireErrorCode, val detail: String, val correlationId: String?) : ParseOutcome
}

/**
 * Strict reader and writer of protocol v1 messages. Numbers sent as strings, unknown enum values, missing required
 * properties, timestamps without "Z", and bad envelopes are rejected; unknown extra properties are ignored; an unknown
 * message type is reported as rejected without throwing.
 */
object WireCodec {
    val json: Json = Json {
        ignoreUnknownKeys = true
        isLenient = false
        explicitNulls = true
        encodeDefaults = false
    }

    private const val MAX_ID_LENGTH = 64

    @Suppress("UNCHECKED_CAST")
    private val serializers: Map<String, KSerializer<Any>> = mapOf(
        MessageTypes.HELLO to HelloPayload.serializer(),
        MessageTypes.SESSION_START to StartPayload.serializer(),
        MessageTypes.SESSION_PAUSE to EmptyPayload.serializer(),
        MessageTypes.SESSION_RESUME to EmptyPayload.serializer(),
        MessageTypes.SESSION_FINISH to EmptyPayload.serializer(),
        MessageTypes.SLIDE_NEXT to EmptyPayload.serializer(),
        MessageTypes.SLIDE_PREVIOUS to EmptyPayload.serializer(),
        MessageTypes.SLIDE_SET to SlideSetPayload.serializer(),
        MessageTypes.ALERT_ACKNOWLEDGE to EmptyPayload.serializer(),
        MessageTypes.HISTORY_LIST to HistoryListPayload.serializer(),
        MessageTypes.PING to EmptyPayload.serializer(),
        MessageTypes.HELLO_ACCEPTED to HelloAcceptedPayload.serializer(),
        MessageTypes.HELLO_REJECTED to HelloRejectedPayload.serializer(),
        MessageTypes.STATE_UPDATE to StatePayload.serializer(),
        MessageTypes.COMMAND_RESULT to CommandResultPayload.serializer(),
        MessageTypes.SESSION_SUMMARY to HistoryItemPayload.serializer(),
        MessageTypes.HISTORY_LIST_RESULT to HistoryListResultPayload.serializer(),
        MessageTypes.ERROR to ErrorPayload.serializer(),
        MessageTypes.PONG to EmptyPayload.serializer(),
    ) as Map<String, KSerializer<Any>>

    /** Every message type this codec knows. */
    val knownTypes: Set<String> get() = serializers.keys

    fun encode(message: WireMessage): String {
        val serializer = serializers[message.type] ?: throw IllegalArgumentException("Unknown message type '${message.type}'.")
        val payload = json.encodeToJsonElement(serializer, message.payload)
        return buildJsonObject {
            put("version", PROTOCOL_VERSION)
            put("type", message.type)
            put("id", message.id)
            put("payload", payload)
        }.toString()
    }

    fun parse(text: String): ParseOutcome {
        val root = try {
            json.parseToJsonElement(text) as? JsonObject
                ?: return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "The message must be a JSON object.", null)
        } catch (e: SerializationException) {
            return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "Not valid JSON.", null)
        }
        val id = (root["id"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.takeIf { it.length in 1..MAX_ID_LENGTH }

        val version = (root["version"] as? JsonPrimitive)?.takeIf { !it.isString }?.let { runCatching { it.int }.getOrNull() }
            ?: return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "The 'version' property must be an integer.", id)
        if (version != PROTOCOL_VERSION) {
            return ParseOutcome.Rejected(WireErrorCode.UnsupportedVersion, "Protocol version $version is not supported.", id)
        }
        val type = (root["type"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.takeIf { it.isNotEmpty() }
            ?: return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "The 'type' property must be a non-empty string.", id)
        if (id == null) {
            return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "The 'id' property must be a string of 1 to 64 characters.", null)
        }
        val payload = root["payload"] as? JsonObject
            ?: return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "The 'payload' property must be a JSON object.", id)
        val serializer = serializers[type]
            ?: return ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "Unknown message type '${type.take(64)}'.", id)

        return try {
            requireStrictTypes(payload, serializer.descriptor)
            ParseOutcome.Parsed(WireMessage(id, type, json.decodeFromString(serializer, payload.toString())))
        } catch (e: SerializationException) {
            ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "Invalid payload for '$type': ${e.message?.take(120)}", id)
        } catch (e: IllegalArgumentException) {
            ParseOutcome.Rejected(WireErrorCode.InvalidMessage, "Invalid payload for '$type'.", id)
        }
    }

    /**
     * kotlinx.serialization accepts `"4"` for a number and `"true"` for a boolean. The companion's parser does not, so
     * neither does the watch: numbers and booleans must not be JSON strings, and strings must be JSON strings.
     */
    private fun requireStrictTypes(element: JsonElement, descriptor: SerialDescriptor) {
        if (element is JsonNull) return // whether null is allowed is decided by the decoder
        when (descriptor.kind) {
            is PrimitiveKind -> {
                val primitive = element as? JsonPrimitive ?: throw SerializationException("Expected a JSON primitive.")
                val textual = descriptor.kind == PrimitiveKind.STRING || descriptor.kind == PrimitiveKind.CHAR
                if (textual != primitive.isString) throw SerializationException("Wrong JSON type for ${descriptor.serialName}.")
            }
            StructureKind.CLASS, StructureKind.OBJECT -> {
                val obj = element as? JsonObject ?: throw SerializationException("Expected a JSON object.")
                for (i in 0 until descriptor.elementsCount) {
                    obj[descriptor.getElementName(i)]?.let { requireStrictTypes(it, descriptor.getElementDescriptor(i)) }
                }
            }
            StructureKind.LIST -> {
                val array = element as? JsonArray ?: throw SerializationException("Expected a JSON array.")
                array.forEach { requireStrictTypes(it, descriptor.getElementDescriptor(0)) }
            }
            else -> Unit
        }
    }
}
