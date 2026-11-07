package com.presenta.wear.protocol

import com.presenta.wear.support.Contracts
import kotlinx.serialization.KSerializer
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The Kotlin client must agree with the shared contracts (schemas and examples) used by the C# server. */
class WireContractTest {
    private fun parsed(text: String): WireMessage = (WireCodec.parse(text) as ParseOutcome.Parsed).message

    private fun rejected(text: String): ParseOutcome.Rejected = WireCodec.parse(text) as ParseOutcome.Rejected

    private fun wireNames(serializer: KSerializer<*>): List<String> =
        (0 until serializer.descriptor.elementsCount).map { serializer.descriptor.getElementName(it) }

    // ---- examples ----

    @Test
    fun every_valid_example_parses_and_serializes_back_to_equivalent_json() {
        val files = Contracts.examples
        assertTrue("no examples found", files.size >= 19)
        for (file in files) {
            val text = file.readText()
            val message = parsed(text)
            assertEquals("${file.name}: message type", Contracts.typeOf(file), message.type)
            val again = WireCodec.encode(message)
            assertTrue(
                "${file.name} does not round-trip:\n$text\n$again",
                Contracts.equivalent(Json.parseToJsonElement(text), Json.parseToJsonElement(again)),
            )
            // The re-serialized text must parse again to the same payload.
            assertEquals("${file.name}: second parse", message, parsed(again))
        }
    }

    @Test
    fun every_invalid_example_the_csharp_parser_rejects_is_rejected_here_too() {
        val files = Contracts.invalidExamples
        assertTrue(files.isNotEmpty())
        for (file in files) {
            if ("schema-only" in file.name) continue // only a schema can reject these (range rules)
            val outcome = WireCodec.parse(file.readText())
            assertTrue("${file.name} should be rejected", outcome is ParseOutcome.Rejected)
        }
    }

    @Test
    fun invalid_examples_include_both_kinds() {
        val names = Contracts.invalidExamples.map { it.name }
        assertTrue(names.any { "schema-only" in it })
        assertTrue(names.any { "schema-only" !in it })
    }

    @Test
    fun every_message_type_has_an_example_and_a_schema() {
        val exampleTypes = Contracts.examples.map(Contracts::typeOf).toSet()
        assertEquals(Contracts.schemaMessageTypes, WireCodec.knownTypes)
        assertEquals(WireCodec.knownTypes, exampleTypes)
        assertEquals(19, WireCodec.knownTypes.size)
    }

    @Test
    fun message_type_constants_match_the_codec() {
        val constants = MessageTypes::class.java.declaredFields
            .filter { it.type == String::class.java && java.lang.reflect.Modifier.isStatic(it.modifiers) }
            .map { it.isAccessible = true; it.get(null) as String }
            .toSet()
        assertEquals(WireCodec.knownTypes, constants)
    }

    // ---- enums and codes equal the schemas ----

    @Test
    fun error_codes_equal_the_schema_exactly() {
        assertEquals(Contracts.schemaEnum("errorCode").sorted(), wireNames(WireErrorCode.serializer()).sorted())
    }

    @Test
    fun error_codes_include_the_pairing_ones() {
        val names = wireNames(WireErrorCode.serializer())
        assertTrue("pairingLockedOut" in names)
        assertTrue("notPaired" in names)
        assertTrue("invalidPairingCode" in names)
    }

    @Test
    fun the_additive_key_delivery_codes_parse_from_their_shared_examples_and_reach_the_ui_model() {
        val expected = mapOf(
            "command.result--target-window-unavailable.json" to WireErrorCode.TargetWindowUnavailable,
            "command.result--key-delivery-failed.json" to WireErrorCode.KeyDeliveryFailed,
        )
        for ((name, code) in expected) {
            val payload = parsed(Contracts.examples.first { it.name == name }.readText()).payload as CommandResultPayload
            assertFalse(payload.ok)
            assertEquals(code, payload.errorCode)
            assertEquals(code.name, com.presenta.wear.model.ErrorCode.valueOf(code.name).name)
        }
        assertTrue("targetWindowUnavailable" in wireNames(WireErrorCode.serializer()))
        assertTrue("keyDeliveryFailed" in wireNames(WireErrorCode.serializer()))
    }

    @Test
    fun other_enums_equal_their_schema_definitions() {
        assertEquals(Contracts.schemaEnum("sessionState").sorted(), wireNames(WireSessionState.serializer()).sorted())
        assertEquals(Contracts.schemaEnum("finalState").sorted(), wireNames(WireFinalState.serializer()).sorted())
        assertEquals(Contracts.schemaEnum("paceStatus").sorted(), wireNames(WirePaceStatus.serializer()).sorted())
        assertEquals(Contracts.schemaEnum("alertKind").sorted(), wireNames(WireAlertKind.serializer()).sorted())
        assertEquals(Contracts.schemaEnum("alertSeverity").sorted(), wireNames(WireAlertSeverity.serializer()).sorted())
        assertEquals(Contracts.schemaEnum("recommendationCode").sorted(), wireNames(WireRecommendationCode.serializer()).sorted())
    }

    @Test
    fun wire_enum_names_match_the_ui_model_enum_names() {
        fun names(entries: Iterable<Enum<*>>) = entries.map { it.name }
        assertEquals(names(com.presenta.wear.model.ErrorCode.entries), names(WireErrorCode.entries))
        assertEquals(names(com.presenta.wear.model.SessionState.entries), names(WireSessionState.entries))
        assertEquals(names(com.presenta.wear.model.FinalState.entries), names(WireFinalState.entries))
        assertEquals(names(com.presenta.wear.model.PaceStatus.entries), names(WirePaceStatus.entries))
        assertEquals(names(com.presenta.wear.model.AlertKind.entries), names(WireAlertKind.entries))
        assertEquals(names(com.presenta.wear.model.AlertSeverity.entries), names(WireAlertSeverity.entries))
        assertEquals(names(com.presenta.wear.model.RecommendationCode.entries), names(WireRecommendationCode.entries))
    }

    @Test
    fun the_error_code_table_in_protocol_v1_md_matches() {
        val lines = java.io.File(Contracts.root, "protocol-v1.md").readLines()
        val start = lines.indexOf("## Error codes")
        val documented = lines.drop(start + 1).takeWhile { !it.startsWith("## ") }
            .filter { it.startsWith("| `") }
            .map { it.substring(3, it.indexOf('`', 3)) }
        assertEquals(documented.sorted(), wireNames(WireErrorCode.serializer()).sorted())
    }

    // ---- tolerance and strictness ----

    @Test
    fun unknown_extra_properties_are_ignored() {
        val message = parsed("""{"version":1,"type":"slide.set","id":"a","future":[1],"payload":{"slide":4,"x":{"y":1}}}""")
        assertEquals(SlideSetPayload(4), message.payload)
    }

    @Test
    fun an_unknown_message_type_is_rejected_without_throwing() {
        val outcome = rejected("""{"version":1,"type":"slide.teleport","id":"u-1","payload":{}}""")
        assertEquals(WireErrorCode.InvalidMessage, outcome.code)
        assertEquals("u-1", outcome.correlationId)
    }

    @Test
    fun garbage_is_rejected_without_throwing() {
        for (text in listOf("", "   ", "not json", "{", "[]", "42", "null", "\"text\"")) {
            assertEquals(text, WireErrorCode.InvalidMessage, rejected(text).code)
        }
    }

    @Test
    fun an_unsupported_version_has_its_own_code() {
        assertEquals(WireErrorCode.UnsupportedVersion, rejected("""{"version":2,"type":"ping","id":"a","payload":{}}""").code)
    }

    @Test
    fun a_version_sent_as_a_string_is_rejected() {
        assertEquals(WireErrorCode.InvalidMessage, rejected("""{"version":"1","type":"ping","id":"a","payload":{}}""").code)
    }

    @Test
    fun a_number_sent_as_a_string_is_rejected() {
        assertEquals(WireErrorCode.InvalidMessage, rejected("""{"version":1,"type":"slide.set","id":"a","payload":{"slide":"4"}}""").code)
    }

    @Test
    fun a_non_integer_is_rejected_for_an_integer_property() {
        assertEquals(WireErrorCode.InvalidMessage, rejected("""{"version":1,"type":"slide.set","id":"a","payload":{"slide":4.5}}""").code)
    }

    @Test
    fun timestamps_must_be_utc_with_a_trailing_z() {
        for (stamp in listOf("2026-10-31T15:30:00", "2026-10-31T15:30:00+00:00", "2026-10-31 15:30:00Z", "2026-10-31", "nope")) {
            val text = """{"version":1,"type":"state.update","id":"s","payload":{"serverTimeUtc":"$stamp","session":null}}"""
            assertEquals(stamp, WireErrorCode.InvalidMessage, rejected(text).code)
        }
        for (stamp in listOf("2026-10-31T15:30:00Z", "2026-10-31T15:30:00.5Z", "2026-10-31T15:30:00.1234567Z")) {
            parsed("""{"version":1,"type":"state.update","id":"s","payload":{"serverTimeUtc":"$stamp","session":null}}""")
        }
    }

    @Test
    fun timestamps_are_written_in_utc_with_milliseconds() {
        val item = parsed(Contracts.examples.first { it.name == "session.summary.json" }.readText())
        assertTrue(WireCodec.encode(item).contains("\"finishedAtUtc\":\"2026-10-31T15:14:32.000Z\""))
    }

    @Test
    fun a_required_nullable_property_must_still_be_present() {
        assertEquals(
            WireErrorCode.InvalidMessage,
            rejected("""{"version":1,"type":"command.result","id":"s","payload":{"correlationId":"m","ok":true}}""").code,
        )
        assertEquals(
            WireErrorCode.InvalidMessage,
            rejected("""{"version":1,"type":"state.update","id":"s","payload":{"serverTimeUtc":"2026-10-31T15:30:00.000Z"}}""").code,
        )
    }

    @Test
    fun envelope_violations_are_rejected() {
        val bad = listOf(
            """{"type":"ping","id":"a","payload":{}}""",
            """{"version":1,"id":"a","payload":{}}""",
            """{"version":1,"type":"ping","payload":{}}""",
            """{"version":1,"type":"ping","id":"a"}""",
            """{"version":1,"type":"","id":"a","payload":{}}""",
            """{"version":1,"type":"ping","id":"","payload":{}}""",
            """{"version":1,"type":"ping","id":"a","payload":null}""",
            """{"version":1,"type":"ping","id":"a","payload":[]}""",
            """{"version":1,"type":5,"id":"a","payload":{}}""",
            """{"version":1,"type":"ping","id":"${"x".repeat(65)}","payload":{}}""",
        )
        for (text in bad) assertEquals(text, WireErrorCode.InvalidMessage, rejected(text).code)
    }

    @Test
    fun optional_properties_are_omitted_when_absent() {
        val start = WireCodec.encode(WireMessage("a", MessageTypes.SESSION_START, StartPayload(10, 600_000)))
        val list = WireCodec.encode(WireMessage("b", MessageTypes.HISTORY_LIST, HistoryListPayload()))
        assertFalse(start.contains("name"))
        assertFalse(list.contains("limit"))
        assertTrue(WireCodec.encode(WireMessage("c", MessageTypes.SESSION_START, StartPayload(10, 600_000, "Demo"))).contains("\"name\":\"Demo\""))
    }

    @Test
    fun the_no_active_session_state_is_an_explicit_null() {
        val text = Contracts.examples.first { it.name == "state.update--no-session.json" }.readText()
        val payload = parsed(text).payload as StatePayload
        assertEquals(null, payload.session)
        assertTrue(WireCodec.encode(parsed(text)).contains("\"session\":null"))
        assertEquals(
            "state.update",
            Json.parseToJsonElement(WireCodec.encode(parsed(text))).jsonObject.getValue("type").jsonPrimitive.content,
        )
    }

    @Test
    fun the_client_message_builders_produce_valid_envelopes() {
        for (type in listOf(MessageTypes.PING, MessageTypes.SLIDE_NEXT, MessageTypes.SESSION_PAUSE, MessageTypes.ALERT_ACKNOWLEDGE)) {
            val text = WireCodec.encode(WireMessage("w-1", type, EmptyPayload))
            assertEquals(type, parsed(text).type)
            assertTrue(text.startsWith("{\"version\":1,\"type\":\"$type\",\"id\":\"w-1\",\"payload\":{}"))
        }
    }
}
