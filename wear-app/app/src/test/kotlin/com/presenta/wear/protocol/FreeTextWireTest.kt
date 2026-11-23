package com.presenta.wear.protocol

import com.presenta.wear.protocol.WireMapper.toModel
import com.presenta.wear.support.Contracts
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** The optional `freeText` of a recommendation (Phase 10): the Kotlin client agrees with the shared contracts. */
class FreeTextWireTest {
    private fun example(name: String): String = File(Contracts.root, "examples/$name").readText()

    private fun recommendationOf(json: String): RecommendationPayload {
        val outcome = WireCodec.parse(json)
        assertTrue("should parse: $json", outcome is ParseOutcome.Parsed)
        val state = (outcome as ParseOutcome.Parsed).message.payload as StatePayload
        return state.session!!.recommendation!!
    }

    @Test
    fun a_message_with_a_text_parses_and_the_text_reaches_the_model() {
        val payload = recommendationOf(example("state.update--running-with-ai-text.json"))

        assertEquals("Acelera un poco, vas atrasado", payload.freeText)
        assertEquals("Acelera un poco, vas atrasado", payload.toModel().freeText)
    }

    @Test
    fun a_message_with_null_gives_no_text_and_keeps_the_code() {
        val payload = recommendationOf(example("state.update--running-with-alert.json"))

        assertNull(payload.freeText)
        assertNull(payload.toModel().freeText)
        assertEquals("SpeedUpSlightly", payload.toModel().code.name)
    }

    @Test
    fun the_text_survives_encoding_and_parsing_again() {
        val outcome = WireCodec.parse(example("state.update--running-with-ai-text.json")) as ParseOutcome.Parsed

        val again = WireCodec.parse(WireCodec.encode(outcome.message)) as ParseOutcome.Parsed

        assertEquals(outcome.message, again.message)
    }

    @Test
    fun a_text_that_is_not_a_string_is_rejected_and_a_too_long_one_is_a_schema_matter_only() {
        assertTrue(WireCodec.parse(File(Contracts.root, "examples/invalid/state.update--free-text-not-a-string.json").readText()) is ParseOutcome.Rejected)
        val tooLong = File(Contracts.root, "examples/invalid/state.update--free-text-too-long--schema-only.json").readText()
        val payload = recommendationOf(tooLong)
        assertNotNull(payload.freeText)
        assertEquals(81, payload.freeText!!.length) // parsed; the watch itself refuses to show it (RecommendationTextTest)
    }
}
