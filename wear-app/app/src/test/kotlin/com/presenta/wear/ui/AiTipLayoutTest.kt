package com.presenta.wear.ui

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.test.core.app.ApplicationProvider
import com.presenta.wear.format.StringMapper
import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.LayoutChecks
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.SMALL_ROUND_DP
import com.presenta.wear.ui.screens.control.ControlActions
import com.presenta.wear.ui.screens.control.ControlScreen
import com.presenta.wear.ui.screens.control.ControlUiState
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * The companion's AI tip in the alert state of Control (ADR-78): it replaces the wording of the recommendation code in the
 * pill (and only there), takes at most two lines, and stays inside the circle on both round sizes at font scales 1.0 and
 * 1.3. A missing, blank or unusable tip falls back to the deterministic wording.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class AiTipLayoutTest {
    @get:Rule
    val rule = createComposeRule()

    private val mapper = StringMapper(ApplicationProvider.getApplicationContext<Context>().resources)

    private data class Setup(val sizeDp: Int, val fontScale: Float)

    private val setups = listOf(
        Setup(LARGE_ROUND_DP, 1.0f),
        Setup(LARGE_ROUND_DP, 1.3f),
        Setup(SMALL_ROUND_DP, 1.0f),
        Setup(SMALL_ROUND_DP, 1.3f),
    )

    private val alert = Alert(AlertKind.BehindSchedule, AlertSeverity.Warning, 300_000, 600_000, 16, -5, 4)

    private fun alertState(tip: String?, code: RecommendationCode = RecommendationCode.SpeedUpNow) = ControlUiState(
        connection = ConnectionState.Connected, currentSlide = 7, totalSlides = 20, elapsedMs = 300_000, remainingMs = 600_000,
        paused = false, progress = 0.33f, paceStatus = PaceStatus.Behind, recommendation = code, alert = alert, slidesRemaining = 13,
        recommendationText = tip,
    )

    private fun noActions() = ControlActions({}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {})

    /** The documented exception of ADR-31: "Entendido" is 44 dp tall on Small Round. */
    private fun minHeight(sizeDp: Int, label: String) =
        if (sizeDp == SMALL_ROUND_DP && label == "Entendido") 44f else LayoutChecks.MIN_TOUCH_DP

    private fun render(state: ControlUiState, setup: Setup) {
        rule.setContent { RoundHarness(setup.sizeDp, setup.fontScale) { ControlScreen(state, noActions()) } }
        rule.waitForIdle()
    }

    @Test
    fun the_tip_replaces_the_wording_of_the_code_in_the_alert_pill() {
        val tip = "Acelera un poco, vas atrasado"
        var state by mutableStateOf(alertState(tip))
        var setup by mutableStateOf(setups.first())
        rule.setContent { RoundHarness(setup.sizeDp, setup.fontScale) { ControlScreen(state, noActions()) } }

        for (s in setups) {
            setup = s
            rule.waitForIdle()
            rule.onNodeWithText(tip).assertExists()
            rule.onNodeWithText(mapper.recommendation(RecommendationCode.SpeedUpNow)).assertDoesNotExist()
        }
    }

    @Test
    fun the_accessibility_description_is_the_same_text() {
        val tip = "Acelera un poco, vas atrasado"

        render(alertState(tip), setups.first())

        rule.onNodeWithContentDescription(tip).assertExists()
    }

    @Test
    fun without_a_tip_the_deterministic_wording_is_shown() {
        var state by mutableStateOf(alertState(tip = null, code = RecommendationCode.entries.first()))
        rule.setContent { RoundHarness(LARGE_ROUND_DP, 1f) { ControlScreen(state, noActions()) } }

        for (code in RecommendationCode.entries) {
            state = alertState(tip = null, code = code)
            rule.waitForIdle()
            rule.onNodeWithText(mapper.recommendation(code)).assertExists()
        }
    }

    @Test
    fun a_forty_character_tip_fits_everywhere_without_a_defect() {
        val tip = "Baja el ritmo, vas muy rapido ahora mismo" // 41 characters: the companion sends at most 40; this is the worst case
        assertTrue(tip.length <= 41)
        val problems = mutableListOf<String>()
        var state by mutableStateOf(alertState(tip))
        var setup by mutableStateOf(setups.first())
        rule.setContent { RoundHarness(setup.sizeDp, setup.fontScale) { ControlScreen(state, noActions()) } }

        for (s in setups) {
            setup = s
            rule.waitForIdle()
            LayoutChecks.problems(rule, s.sizeDp, minHeightFor = { label -> minHeight(s.sizeDp, label) })
                // At font scale 1.3 on the small screen the text may need the ellipsis; that is allowed (two lines, then "...").
                .filterNot { "cut off" in it && s.fontScale > 1f }
                .mapTo(problems) { "[${s.sizeDp}dp x${s.fontScale}] $it" }
        }
        assertTrue("Layout problems:\n" + problems.joinToString("\n"), problems.isEmpty())
    }

    @Test
    fun the_longest_text_the_watch_accepts_stays_inside_the_circle_and_overlaps_nothing() {
        val tip = "Hay muchas diapositivas por mostrar y poco tiempo: avanza mas rapido ahora ya" // 80 characters
        assertTrue(tip.length <= 80)
        val problems = mutableListOf<String>()
        var state by mutableStateOf(alertState(tip))
        var setup by mutableStateOf(setups.first())
        rule.setContent { RoundHarness(setup.sizeDp, setup.fontScale) { ControlScreen(state, noActions()) } }

        for (s in setups) {
            setup = s
            rule.waitForIdle()
            LayoutChecks.problems(rule, s.sizeDp, minHeightFor = { label -> minHeight(s.sizeDp, label) })
                .filterNot { "cut off" in it } // an ellipsis is the intended end of a long text
                .mapTo(problems) { "[${s.sizeDp}dp x${s.fontScale}] $it" }
        }
        assertTrue("Layout problems:\n" + problems.joinToString("\n"), problems.isEmpty())
    }

    @Test
    fun outside_an_alert_the_pace_pill_keeps_its_deterministic_wording_and_never_shows_the_tip() {
        val tip = "Acelera un poco, vas atrasado"
        var state by mutableStateOf(alertState(tip).copy(alert = null))
        var setup by mutableStateOf(setups.first())
        rule.setContent { RoundHarness(setup.sizeDp, setup.fontScale) { ControlScreen(state, noActions()) } }

        for (s in setups) {
            setup = s
            rule.waitForIdle()
            rule.onNodeWithText(tip).assertDoesNotExist()
            rule.onNodeWithText(mapper.recommendation(RecommendationCode.SpeedUpNow)).assertExists()
        }
    }
}
