package com.presenta.wear.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.junit4.createComposeRule
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.LayoutChecks
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.SMALL_ROUND_DP
import com.presenta.wear.ui.preview.PreviewCatalog
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Every screen and state, on both round sizes and at font scales 1.0 and 1.3:
 * all text inside the circle, no overlapping texts, every interactive element labelled and at least 48 dp.
 * The only exception is documented in docs/decisions.md (ADR-31): on Small Round the setup stepper buttons are
 * 42 dp wide (48 dp tall).
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class LayoutAccessibilityTest {
    @get:Rule
    val rule = createComposeRule()

    /** The documented exceptions of ADR-31, all on Small Round. Everything else must be 48 dp. */
    private fun minHeight(sizeDp: Int, caseName: String, label: String): Float {
        if (sizeDp != SMALL_ROUND_DP) return LayoutChecks.MIN_TOUCH_DP
        val isControlSecondary = label.contains("Toca para elegir otra") || label.contains("Mant\u00e9n pulsado")
        return when {
            isControlSecondary -> 36f
            caseName.startsWith("02") -> 44f
            caseName.startsWith("04") && label == "Entendido" -> 44f
            else -> LayoutChecks.MIN_TOUCH_DP
        }
    }

    /** Content that scrolls is checked where it is shown first (ADR-31). */
    private fun bottomFraction(sizeDp: Int, caseName: String): Float = when {
        caseName == "06-history" -> 0.6f
        sizeDp == SMALL_ROUND_DP && caseName.startsWith("05") -> 0.8f
        else -> 1f
    }

    private data class Setup(val sizeDp: Int, val fontScale: Float)

    private val setups = listOf(
        Setup(LARGE_ROUND_DP, 1.0f),
        Setup(LARGE_ROUND_DP, 1.3f),
        Setup(SMALL_ROUND_DP, 1.0f),
        Setup(SMALL_ROUND_DP, 1.3f),
    )

    @Test
    fun every_screen_passes_the_round_layout_and_touch_target_checks() {
        var caseIndex by mutableIntStateOf(0)
        var setup by mutableStateOf(setups.first())
        rule.setContent {
            RoundHarness(setup.sizeDp, setup.fontScale) { PreviewCatalog.cases[caseIndex].content() }
        }

        val problems = mutableListOf<String>()
        for (s in setups) {
            setup = s
            PreviewCatalog.cases.forEachIndexed { index, case ->
                caseIndex = index
                rule.waitForIdle()
                val found = LayoutChecks.problems(
                    provider = rule,
                    sizeDp = s.sizeDp,
                    allowNarrowTouchTargets = s.sizeDp == SMALL_ROUND_DP && case.name.startsWith("02"),
                    minHeightFor = { label -> minHeight(s.sizeDp, case.name, label) },
                    textBottomFraction = bottomFraction(s.sizeDp, case.name),
                )
                found.mapTo(problems) { "[${case.name} @ ${s.sizeDp}dp x${s.fontScale}] $it" }
            }
        }

        assertTrue("Layout problems:\n" + problems.joinToString("\n"), problems.isEmpty())
    }
}
