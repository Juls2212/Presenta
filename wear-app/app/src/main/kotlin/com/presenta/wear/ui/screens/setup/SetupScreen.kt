package com.presenta.wear.ui.screens.setup

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.presenta.wear.R
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.SetupLimits
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.COMPACT_TARGET
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.MinTouchTarget
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.ProgressRing
import com.presenta.wear.ui.components.RepeatButton
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.ShrinkText
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme

data class SetupUiState(
    val slides: Int = SetupLimits.DEFAULT_SLIDES,
    val minutes: Int = SetupLimits.DEFAULT_MINUTES,
    val starting: Boolean = false,
    val error: ErrorCode? = null,
) {
    val canDecreaseSlides get() = slides > SetupLimits.MIN_SLIDES
    val canIncreaseSlides get() = slides < SetupLimits.MAX_SLIDES
    val canDecreaseMinutes get() = minutes > SetupLimits.MIN_MINUTES
    val canIncreaseMinutes get() = minutes < SetupLimits.MAX_MINUTES
}

class SetupActions(
    val onSlidesDecrease: () -> Unit,
    val onSlidesIncrease: () -> Unit,
    val onMinutesDecrease: () -> Unit,
    val onMinutesIncrease: () -> Unit,
    val onStart: () -> Unit,
)

/** Setup (mockup 02): two circular steppers and the start button. */
@Composable
fun SetupScreen(state: SetupUiState, errorText: String?, actions: SetupActions) = ScreenFontCap {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    ScreenFrame {
        Column(
            Modifier.fillMaxSize().padding(top = if (compact) 15.dp else 22.dp, bottom = if (compact) 13.dp else 11.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            ShrinkText(
                stringResource(R.string.setup_title),
                PresentaText.bold(if (compact) 12.sp else 14.sp),
                colors.warmWhite,
                maxWidth = if (compact) 96.dp else 124.dp,
                modifier = Modifier.semantics { heading() },
            )
            PresentaText(
                errorText ?: stringResource(R.string.setup_subtitle),
                PresentaText.regular(if (compact) 9.sp else 10.sp),
                if (errorText != null) colors.amber else colors.textMuted,
                maxLines = 1,
            )
            Gap(2.dp)
            Row(horizontalArrangement = Arrangement.spacedBy(if (compact) 4.dp else 8.dp)) {
                Stepper(
                    value = state.slides,
                    unit = stringResource(R.string.setup_slides_unit),
                    progress = state.slides.toFloat() / SetupLimits.MAX_SLIDES,
                    valueDescription = pluralStringResource(R.plurals.setup_slides_value_cd, state.slides, state.slides),
                    decreaseDescription = stringResource(R.string.setup_slides_decrease),
                    increaseDescription = stringResource(R.string.setup_slides_increase),
                    canDecrease = state.canDecreaseSlides,
                    canIncrease = state.canIncreaseSlides,
                    onDecrease = actions.onSlidesDecrease,
                    onIncrease = actions.onSlidesIncrease,
                    compact = compact,
                )
                Stepper(
                    value = state.minutes,
                    unit = stringResource(R.string.setup_minutes_unit),
                    progress = state.minutes.toFloat() / SetupLimits.MAX_MINUTES,
                    valueDescription = pluralStringResource(R.plurals.setup_minutes_value_cd, state.minutes, state.minutes),
                    decreaseDescription = stringResource(R.string.setup_minutes_decrease),
                    increaseDescription = stringResource(R.string.setup_minutes_increase),
                    canDecrease = state.canDecreaseMinutes,
                    canIncrease = state.canIncreaseMinutes,
                    onDecrease = actions.onMinutesDecrease,
                    onIncrease = actions.onMinutesIncrease,
                    compact = compact,
                )
            }
            Gap(2.dp)
            PresentaButton(
                onClick = actions.onStart,
                label = stringResource(R.string.setup_start),
                icon = IconKind.Play,
                style = ButtonStyle.Primary,
                enabled = !state.starting,
                minWidth = if (compact) 96.dp else 104.dp,
                minHeight = if (compact) COMPACT_TARGET else MinTouchTarget,
                textSize = if (compact) 13.sp else 15.sp,
            )
        }
    }
}

@Composable
private fun Stepper(
    value: Int,
    unit: String,
    progress: Float,
    valueDescription: String,
    decreaseDescription: String,
    increaseDescription: String,
    canDecrease: Boolean,
    canIncrease: Boolean,
    onDecrease: () -> Unit,
    onIncrease: () -> Unit,
    compact: Boolean,
) {
    val colors = PresentaTheme.colors
    val circle: Dp = if (compact) 40.dp else 54.dp
    val buttonWidth: Dp = if (compact) 42.dp else 48.dp
    val buttonHeight: Dp = if (compact) COMPACT_TARGET else MinTouchTarget
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier.size(circle).semantics(mergeDescendants = true) { contentDescription = valueDescription },
            contentAlignment = Alignment.Center,
        ) {
            ProgressRing(progress = progress, color = colors.cyan, strokeWidth = 3.dp, inset = 0.dp)
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                PresentaText(value.toString(), PresentaText.bold(if (compact) 17.sp else 23.sp), colors.warmWhite, maxLines = 1)
                PresentaText(unit, PresentaText.regular(if (compact) 7.sp else 8.sp), colors.textMuted, maxLines = 1)
            }
        }
        Gap(2.dp)
        Row(horizontalArrangement = Arrangement.spacedBy(2.dp)) {
            RepeatButton(onDecrease, IconKind.Minus, decreaseDescription, enabled = canDecrease, minWidth = buttonWidth, minHeight = buttonHeight)
            RepeatButton(onIncrease, IconKind.Plus, increaseDescription, enabled = canIncrease, minWidth = buttonWidth, minHeight = buttonHeight)
        }
    }
}
