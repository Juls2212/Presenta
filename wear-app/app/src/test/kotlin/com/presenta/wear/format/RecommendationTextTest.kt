package com.presenta.wear.format

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class RecommendationTextTest {
    @Test
    fun a_plain_short_line_is_kept() {
        assertEquals("Acelera un poco", RecommendationText.clean("Acelera un poco"))
        assertEquals("¿Vas bien de tiempo?", RecommendationText.clean("¿Vas bien de tiempo?"))
    }

    @Test
    fun spaces_and_line_breaks_become_a_single_line() {
        assertEquals("Acelera un poco", RecommendationText.clean("  Acelera\n un\t poco \r\n"))
    }

    @Test
    fun nothing_is_shown_when_there_is_no_text_or_only_blanks() {
        assertNull(RecommendationText.clean(null))
        assertNull(RecommendationText.clean(""))
        assertNull(RecommendationText.clean("   \n\t "))
    }

    @Test
    fun the_limit_is_eighty_characters_like_the_schema() {
        assertEquals(80, RecommendationText.MAX_LENGTH)
        assertEquals("a".repeat(80), RecommendationText.clean("a".repeat(80)))
        assertNull(RecommendationText.clean("a".repeat(81)))
    }

    @Test
    fun control_characters_make_the_whole_text_unusable() {
        assertNull(RecommendationText.clean("Acelera\u0000 un poco"))
        assertNull(RecommendationText.clean("Acelera \u001B[31m un poco"))
        assertNull(RecommendationText.clean("Acelera\u0007"))
    }
}
