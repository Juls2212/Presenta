package com.presenta.wear.support

import android.content.res.Configuration
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp

const val SCREEN_TAG = "screen"

/** Large Round is about 227 dp (454 px at xhdpi); Small Round is about 192 dp. */
const val LARGE_ROUND_DP = 227
const val SMALL_ROUND_DP = 192

/**
 * Renders [content] inside a round window of [sizeDp] with the given font scale. Density is fixed at 2 (xhdpi)
 * so that 227 dp is exactly the 454 px design baseline. The window itself must be larger (see the test qualifiers).
 */
@Composable
fun RoundHarness(sizeDp: Int, fontScale: Float, content: @Composable () -> Unit) {
    val configuration = Configuration(LocalConfiguration.current).apply {
        screenWidthDp = sizeDp
        screenHeightDp = sizeDp
        screenLayout = (screenLayout and Configuration.SCREENLAYOUT_ROUND_MASK.inv()) or Configuration.SCREENLAYOUT_ROUND_YES
        this.fontScale = fontScale
    }
    CompositionLocalProvider(
        LocalConfiguration provides configuration,
        LocalDensity provides Density(density = 2f, fontScale = fontScale),
    ) {
        Box(
            Modifier
                .size(sizeDp.dp)
                .clip(CircleShape)
                .background(Color.Black)
                .testTag(SCREEN_TAG),
        ) {
            content()
        }
    }
}
