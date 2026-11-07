package com.presenta.wear.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import com.github.takahirom.roborazzi.captureRoboImage
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.SCREEN_TAG
import com.presenta.wear.support.SMALL_ROUND_DP
import com.presenta.wear.ui.preview.PreviewCatalog
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Renders every screen and state of [PreviewCatalog] on Large Round and Small Round and saves PNGs under
 * docs/ui-previews/. Run with `-Proborazzi.test.record=true` to write the files (see wear-app/README.md).
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class ScreenshotTest {
    @get:Rule
    val rule = createComposeRule()

    @Test
    fun capture_every_screen_on_large_round() = capture(LARGE_ROUND_DP, "large-round")

    @Test
    fun capture_every_screen_on_small_round() = capture(SMALL_ROUND_DP, "small-round")

    private fun capture(sizeDp: Int, folder: String) {
        var index by mutableIntStateOf(0)
        rule.setContent { RoundHarness(sizeDp, fontScale = 1f) { PreviewCatalog.cases[index].content() } }
        PreviewCatalog.cases.forEachIndexed { i, case ->
            index = i
            rule.waitForIdle()
            rule.onNodeWithTag(SCREEN_TAG).captureRoboImage("../../docs/ui-previews/$folder/${case.name}.png")
        }
    }
}
