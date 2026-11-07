package com.presenta.wear.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import kotlin.math.pow
import org.junit.Assert.assertTrue
import org.junit.Test

/** WCAG 2.x contrast ratios of every text and icon color pair used by the app (minimum 4.5:1 for text). */
class ContrastTest {
    private val c = PresentaColors()

    private fun channel(value: Float): Double =
        if (value <= 0.03928f) value / 12.92 else ((value + 0.055) / 1.055).pow(2.4)

    private fun luminance(color: Color): Double =
        0.2126 * channel(color.red) + 0.7152 * channel(color.green) + 0.0722 * channel(color.blue)

    private fun ratio(foreground: Color, background: Color): Double {
        val fg = luminance(foreground.compositeOver(background))
        val bg = luminance(background)
        return (maxOf(fg, bg) + 0.05) / (minOf(fg, bg) + 0.05)
    }

    private fun assertAtLeast(minimum: Double, foreground: Color, background: Color, what: String) {
        val value = ratio(foreground, background)
        println("contrast %-46s %.2f:1".format(what, value))
        assertTrue("$what has contrast $value, expected at least $minimum", value >= minimum)
    }

    @Test
    fun text_on_night_background() {
        assertAtLeast(4.5, c.warmWhite, c.night, "warm white on night")
        assertAtLeast(4.5, c.textMuted, c.night, "muted text on night")
        assertAtLeast(4.5, c.cyan, c.night, "cyan on night")
        assertAtLeast(4.5, c.amber, c.night, "amber on night")
        assertAtLeast(4.5, c.critical, c.night, "critical red on night")
    }

    @Test
    fun text_on_buttons() {
        assertAtLeast(4.5, c.night, c.cyan, "night text on cyan button")
        assertAtLeast(4.5, c.night, c.warmWhite, "night text on warm white button")
        assertAtLeast(4.5, c.warmWhite, c.navy, "warm white on navy button")
        assertAtLeast(4.5, c.night, c.amber, "night icon on amber")
        assertAtLeast(4.5, c.night, c.critical, "night text on critical red")
    }

    @Test
    fun text_on_cards_and_pills() {
        assertAtLeast(4.5, c.warmWhite, c.surface, "warm white on card")
        assertAtLeast(4.5, c.textMuted, c.surface, "muted text on card")
        assertAtLeast(4.5, c.cyan, c.cyan.copy(alpha = 0.14f).compositeOver(c.night), "cyan on cyan pill")
        assertAtLeast(4.5, c.amber, c.amber.copy(alpha = 0.14f).compositeOver(c.night), "amber on amber pill")
        assertAtLeast(4.5, c.critical, c.critical.copy(alpha = 0.14f).compositeOver(c.night), "critical red on red pill")
        assertAtLeast(4.5, c.warmWhite, c.surface, "pill on home")
    }

    @Test
    fun critical_red_is_distinct_from_amber_and_cyan() {
        // Color is never the only signal, but the three accents must also differ enough to be told apart.
        assertTrue(ratio(c.critical, c.amber) > 1.2)
        assertTrue(ratio(c.critical, c.cyan) > 1.2)
    }
}
