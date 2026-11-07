package com.presenta.wear.ui.screens.summary

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.presenta.wear.R
import com.presenta.wear.format.DurationFormat
import com.presenta.wear.format.PaceFormat
import com.presenta.wear.format.StringMapper
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.FullRing
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.IconBadge
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme
import kotlin.math.abs

data class SummaryUiState(val item: HistoryItem, val readOnly: Boolean)

/**
 * Summary (mockup 05) and its variants: over time, interrupted, and read-only (from history).
 * The primary button only navigates; the companion has already saved the record.
 * Font scale is capped (ADR-32). On Small Round the content scrolls, because the button cannot fit above the
 * curve of the screen together with the other lines (ADR-31).
 */
@Composable
fun SummaryScreen(state: SummaryUiState, onDone: () -> Unit) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val item = state.item
    val mapper = StringMapper(LocalResources.current)
    val interrupted = item.finalState == FinalState.InterruptedByDisconnection
    val overBy = abs(item.actualDurationMs - item.plannedDurationMs)
    val (message, messageColor) = when {
        interrupted -> stringResource(R.string.summary_interrupted) to colors.amber
        item.endedWithinPlannedTime -> stringResource(R.string.summary_within_time) to colors.cyan
        else -> stringResource(R.string.summary_over_time, DurationFormat.clock(overBy)) to colors.amber
    }
    val ringColor = if (interrupted || !item.endedWithinPlannedTime) colors.amber else colors.cyan
    ScreenFontCap {
        ScreenFrame {
            FullRing(color = ringColor, strokeWidth = 3.dp, inset = 5.dp)
            Column(
                Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(horizontal = if (compact) 24.dp else 26.dp)
                    .padding(top = if (compact) 16.dp else 15.dp, bottom = if (compact) 18.dp else 20.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                IconBadge(
                    icon = if (interrupted) IconKind.Warning else IconKind.Check,
                    tint = colors.night,
                    background = ringColor,
                    size = if (compact) 22.dp else 26.dp,
                    iconSize = if (compact) 13.dp else 15.dp,
                )
                PresentaText(
                    stringResource(R.string.summary_title),
                    PresentaText.bold(if (compact) 13.sp else 14.sp),
                    colors.warmWhite,
                    Modifier.semantics { heading() },
                    maxLines = 2,
                )
                PresentaText(
                    stringResource(R.string.summary_duration_label),
                    PresentaText.medium(if (compact) 8.sp else 9.sp),
                    colors.textMuted,
                    maxLines = 1,
                )
                PresentaText(
                    DurationFormat.clock(item.actualDurationMs),
                    PresentaText.bold(if (compact) 28.sp else 34.sp),
                    colors.warmWhite,
                    maxLines = 1,
                )
                PresentaText(
                    mapper.slidesCount(item.registeredSlides),
                    PresentaText.bold(if (compact) 11.sp else 12.sp),
                    colors.warmWhite,
                    maxLines = 1,
                )
                PresentaText(
                    stringResource(R.string.summary_average_pace, PaceFormat.slidesPerMinute(item.averageSlidesPerMinute)),
                    PresentaText.regular(if (compact) 10.sp else 11.sp),
                    colors.textMuted,
                    maxLines = 1,
                )
                PresentaText(
                    message,
                    PresentaText.medium(if (compact) 10.sp else 11.sp),
                    messageColor,
                    maxLines = 2,
                )
                Gap(3.dp)
                PresentaButton(
                    onClick = onDone,
                    label = stringResource(if (state.readOnly) R.string.summary_back else R.string.summary_save_and_home),
                    style = ButtonStyle.Light,
                    minWidth = 96.dp,
                    maxWidth = if (compact) 112.dp else 124.dp,
                    maxLines = 2,
                    textSize = if (compact) 11.sp else 12.sp,
                )
            }
        }
    }
}
