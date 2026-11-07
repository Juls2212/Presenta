package com.presenta.wear.ui.screens.control

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.activity.compose.BackHandler
import com.presenta.wear.R
import com.presenta.wear.format.DurationFormat
import com.presenta.wear.format.StringMapper
import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.COMPACT_SECONDARY_TARGET
import com.presenta.wear.ui.components.COMPACT_TARGET
import com.presenta.wear.ui.components.MinTouchTarget
import com.presenta.wear.ui.components.CappedFontScale
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaIcon
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.ProgressRing
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ShrinkText
import com.presenta.wear.ui.components.RepeatButton
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.StatusPill
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme

enum class ControlOverlay { None, SlideSelector, FinishConfirmation }

/** Test tag of the progress ring, which shows elapsed time divided by planned time. */
const val ELAPSED_RING_TAG = "elapsed-ring"

/** Everything the Control screen draws. Times are live values (already interpolated by the view model). */
data class ControlUiState(
    val connection: ConnectionState,
    val currentSlide: Int,
    val totalSlides: Int,
    val elapsedMs: Long,
    val remainingMs: Long,
    val paused: Boolean,
    /** 0..1: elapsed time divided by the planned duration, clamped. */
    val progress: Float,
    val paceStatus: PaceStatus,
    val recommendation: RecommendationCode?,
    val alert: Alert?,
    val slidesRemaining: Int,
    val overlay: ControlOverlay = ControlOverlay.None,
    val selectorSlide: Int = currentSlide,
    val message: ErrorCode? = null,
) {
    val overtime: Boolean get() = remainingMs < 0
}

class ControlActions(
    val onNext: () -> Unit,
    val onPrevious: () -> Unit,
    val onTogglePause: () -> Unit,
    val onOpenSelector: () -> Unit,
    val onSelectorDecrease: () -> Unit,
    val onSelectorIncrease: () -> Unit,
    val onSelectorConfirm: () -> Unit,
    val onSelectorCancel: () -> Unit,
    val onOpenFinish: () -> Unit,
    val onFinishCancel: () -> Unit,
    val onFinishConfirm: () -> Unit,
    val onAcknowledge: () -> Unit,
)

/**
 * The key screen. The alert is a state of this screen, never a separate destination: Next and Previous are
 * always present and enabled. Font scale is capped (1.15 on Large Round, 1.0 on Small Round) because the
 * vertical budget of a round screen cannot absorb more (ADR-32).
 */
@Composable
fun ControlScreen(state: ControlUiState, actions: ControlActions) {
    ScreenFontCap {
        when (state.overlay) {
            ControlOverlay.None -> ControlMain(state, actions)
            ControlOverlay.SlideSelector -> SlideSelectorOverlay(state, actions)
            ControlOverlay.FinishConfirmation -> FinishConfirmationOverlay(actions)
        }
    }
}

@Composable
private fun accentFor(state: ControlUiState): Color {
    val colors = PresentaTheme.colors
    return when {
        state.alert?.kind == AlertKind.Overtime || (state.overtime && state.alert == null) -> colors.critical
        state.alert != null -> colors.amber
        else -> colors.cyan
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ControlMain(state: ControlUiState, actions: ControlActions) {
    val accent = accentFor(state)
    var dragTotal by remember { mutableFloatStateOf(0f) }
    val swipeThreshold = with(LocalDensity.current) { 48.dp.toPx() }
    ScreenFrame(
        Modifier.pointerInput(Unit) {
            detectVerticalDragGestures(
                onDragStart = { dragTotal = 0f },
                onDragEnd = { if (dragTotal < -swipeThreshold) actions.onOpenFinish() },
                onDragCancel = { dragTotal = 0f },
                onVerticalDrag = { _, delta -> dragTotal += delta },
            )
        },
    ) {
        ProgressRing(
            progress = state.progress,
            color = accent,
            modifier = Modifier
                .testTag(ELAPSED_RING_TAG)
                .semantics { progressBarRangeInfo = ProgressBarRangeInfo(state.progress, 0f..1f) },
        )
        if (state.alert != null) {
            AlertContent(state, actions, accent)
        } else {
            NormalContent(state, actions, accent)
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun NormalContent(state: ControlUiState, actions: ControlActions, accent: Color) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val mapper = StringMapper(LocalResources.current)
    val elapsedText = DurationFormat.clock(state.elapsedMs)
    val remainingText = if (state.overtime) {
        stringResource(R.string.control_overtime, DurationFormat.clock(state.remainingMs))
    } else {
        stringResource(R.string.control_remaining, DurationFormat.clock(state.remainingMs))
    }
    val clockDescription = stringResource(
        if (state.paused) R.string.control_clock_cd_paused else R.string.control_clock_cd_running,
        elapsedText,
    ) + ". " + remainingText
    val counterDescription = stringResource(R.string.control_slide_counter_cd, state.currentSlide, state.totalSlides)
    val toggleLabel = stringResource(if (state.paused) R.string.control_resume_action else R.string.control_pause_action)

    Column(
        Modifier.fillMaxSize().padding(top = 14.dp, bottom = if (compact) 20.dp else 26.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        StatusLine(state, mapper)
        Box(
            Modifier
                .defaultMinSize(minHeight = if (compact) COMPACT_SECONDARY_TARGET else MinTouchTarget, minWidth = MinTouchTarget)
                .clickable(role = Role.Button, onClickLabel = stringResource(R.string.control_choose_slide), onClick = actions.onOpenSelector)
                .semantics { contentDescription = counterDescription },
            contentAlignment = Alignment.Center,
        ) {
            Row(verticalAlignment = Alignment.Bottom) {
                PresentaText(
                    String.format(java.util.Locale.ROOT, "%02d", state.currentSlide),
                    PresentaText.bold(if (compact) 32.sp else 40.sp),
                    colors.warmWhite,
                    maxLines = 1,
                )
                PresentaText(
                    " / ${state.totalSlides}",
                    PresentaText.medium(if (compact) 16.sp else 20.sp),
                    colors.textMuted,
                    Modifier.padding(bottom = if (compact) 3.dp else 4.dp),
                    maxLines = 1,
                )
            }
        }
        Column(
            Modifier
                .defaultMinSize(minHeight = if (compact) COMPACT_SECONDARY_TARGET else MinTouchTarget)
                .combinedClickable(
                    role = Role.Button,
                    onClickLabel = null,
                    onLongClickLabel = toggleLabel,
                    onLongClick = actions.onTogglePause,
                    onClick = {},
                )
                .semantics(mergeDescendants = true) { contentDescription = clockDescription }
                .padding(horizontal = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            Row(Modifier.alpha(if (state.paused) 0.55f else 1f), verticalAlignment = Alignment.CenterVertically) {
                PresentaIcon(IconKind.Clock, colors.warmWhite, size = if (compact) 14.dp else 16.dp)
                Spacer(Modifier.width(4.dp))
                PresentaText(elapsedText, PresentaText.bold(if (compact) 19.sp else 24.sp), colors.warmWhite, maxLines = 1)
            }
            PresentaText(
                remainingText,
                PresentaText.regular(if (compact) 11.sp else 13.sp),
                if (state.overtime) colors.critical else colors.textMuted,
                maxLines = 1,
            )
        }
        Gap(2.dp)
        PacePill(state, mapper, accent, compact)
        Gap(2.dp)
        NavigationRow(actions, accent, compact)
    }
}

@Composable
private fun StatusLine(state: ControlUiState, mapper: StringMapper) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val text: String
    val tint: Color
    val icon: IconKind
    when {
        state.connection != ConnectionState.Connected -> {
            text = mapper.connectionStatus(state.connection)
            tint = colors.amber
            icon = IconKind.MonitorOff
        }
        state.paused -> {
            text = stringResource(R.string.control_paused)
            tint = colors.amber
            icon = IconKind.Pause
        }
        else -> {
            text = mapper.connectionStatus(ConnectionState.Connected)
            tint = colors.textMuted
            icon = IconKind.Monitor
        }
    }
    Row(
        Modifier.semantics(mergeDescendants = true) {
            contentDescription = text
        },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (state.connection == ConnectionState.Connected && !state.paused) {
            Box(Modifier.size(5.dp).clip(CircleShape).background(colors.cyan))
            Spacer(Modifier.width(4.dp))
        }
        PresentaIcon(icon, tint, size = if (compact) 10.dp else 11.dp)
        Spacer(Modifier.width(3.dp))
        PresentaText(text, PresentaText.medium(if (compact) 10.sp else 11.sp), tint, maxLines = 1)
    }
}

@Composable
private fun PacePill(state: ControlUiState, mapper: StringMapper, accent: Color, compact: Boolean) {
    val colors = PresentaTheme.colors
    val height = if (compact) 20.dp else 24.dp
    val size = if (compact) 11.sp else 12.sp
    val message = state.message
    if (message != null) {
        StatusPill(mapper.error(message), colors.amber, icon = IconKind.Warning, height = height, textSize = size)
        return
    }
    val text = mapper.pacePill(state.paceStatus, state.recommendation)
    val (tint, icon) = when {
        state.overtime || state.recommendation == RecommendationCode.Overtime -> colors.critical to IconKind.Warning
        state.recommendation == null && state.paceStatus == PaceStatus.NotEnoughData -> colors.textMuted to IconKind.Clock
        state.recommendation == RecommendationCode.OnTrack || state.recommendation == null -> colors.cyan to IconKind.Check
        else -> colors.amber to IconKind.DoubleChevron
    }
    StatusPill(text, tint, icon = icon, height = height, textSize = size)
}

@Composable
private fun NavigationRow(actions: ControlActions, accent: Color, compact: Boolean) {
    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        PresentaButton(
            onClick = actions.onPrevious,
            icon = IconKind.ArrowBack,
            style = ButtonStyle.Secondary,
            contentDescription = stringResource(R.string.control_previous),
            minWidth = if (compact) 48.dp else 52.dp,
        )
        PresentaButton(
            onClick = actions.onNext,
            icon = IconKind.ArrowForward,
            style = ButtonStyle.Primary,
            contentDescription = stringResource(R.string.control_next),
            minWidth = if (compact) 68.dp else 80.dp,
        )
    }
}

/** Alert state of Control (mockup 04). Next and Previous stay visible and enabled. */
@Composable
private fun AlertContent(state: ControlUiState, actions: ControlActions, accent: Color) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val mapper = StringMapper(LocalResources.current)
    val alert = requireNotNull(state.alert)
    val headline = mapper.alertHeadline(alert)
    val supporting = mapper.alertSupporting(alert, state.remainingMs, state.slidesRemaining)
    val cd = stringResource(R.string.alert_cd_prefix, headline, supporting)
    Column(
        Modifier.fillMaxSize().padding(top = if (compact) 18.dp else 12.dp, bottom = if (compact) 20.dp else 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Column(
            Modifier.semantics(mergeDescendants = true) { contentDescription = cd; heading() },
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            if (!compact) {
                Box(
                    Modifier.size(24.dp).clip(CircleShape).border(1.5.dp, accent.copy(alpha = 0.8f), CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    PresentaIcon(IconKind.Warning, accent, size = 14.dp)
                }
            }
            ShrinkText(headline, PresentaText.bold(if (compact) 19.sp else 22.sp), colors.warmWhite, maxWidth = if (compact) 106.dp else 150.dp)
            PresentaText(supporting, PresentaText.regular(if (compact) 9.sp else 11.sp), colors.textMuted, maxLines = 1)
        }
        Gap(2.dp)
        state.recommendation?.let { code ->
            val tint = if (code == RecommendationCode.Overtime) colors.critical else accent
            StatusPill(
                mapper.recommendation(code),
                tint,
                icon = IconKind.DoubleChevron,
                height = if (compact) 20.dp else 22.dp,
                textSize = if (compact) 11.sp else 12.sp,
            )
            Gap(2.dp)
        }
        PresentaButton(
            onClick = actions.onAcknowledge,
            label = stringResource(R.string.alert_dismiss),
            icon = IconKind.Check,
            style = ButtonStyle.Outlined,
            accent = accent,
            minHeight = if (compact) COMPACT_TARGET else MinTouchTarget,
            textSize = if (compact) 13.sp else 14.sp,
        )
        Gap(2.dp)
        NavigationRow(actions, accent, compact)
    }
}

@Composable
private fun SlideSelectorOverlay(state: ControlUiState, actions: ControlActions) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    BackHandler(onBack = actions.onSelectorCancel)
    val valueDescription = stringResource(R.string.selector_value_cd, state.selectorSlide, state.totalSlides)
    ScreenFrame {
        Column(
            Modifier.fillMaxSize().padding(vertical = if (compact) 16.dp else 22.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            PresentaText(
                stringResource(R.string.selector_title),
                PresentaText.bold(if (compact) 13.sp else 15.sp),
                colors.warmWhite,
                Modifier.semantics { heading() },
                maxLines = 1,
            )
            Box(
                Modifier.semantics(mergeDescendants = true) {
                    contentDescription = valueDescription
                },
            ) {
                Row(verticalAlignment = Alignment.Bottom) {
                    PresentaText(state.selectorSlide.toString(), PresentaText.bold(if (compact) 34.sp else 44.sp), colors.cyan, maxLines = 1)
                    PresentaText(
                        " / ${state.totalSlides}",
                        PresentaText.medium(if (compact) 15.sp else 18.sp),
                        colors.textMuted,
                        Modifier.padding(bottom = 5.dp),
                        maxLines = 1,
                    )
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                RepeatButton(
                    actions.onSelectorDecrease,
                    IconKind.Minus,
                    stringResource(R.string.selector_decrease),
                    enabled = state.selectorSlide > 1,
                    minWidth = 56.dp,
                )
                RepeatButton(
                    actions.onSelectorIncrease,
                    IconKind.Plus,
                    stringResource(R.string.selector_increase),
                    enabled = state.selectorSlide < state.totalSlides,
                    minWidth = 56.dp,
                )
            }
            Gap(4.dp)
            PresentaButton(
                onClick = actions.onSelectorConfirm,
                label = stringResource(R.string.selector_done),
                icon = IconKind.Check,
                style = ButtonStyle.Primary,
                minWidth = 104.dp,
            )
        }
    }
}

@Composable
private fun FinishConfirmationOverlay(actions: ControlActions) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    BackHandler(onBack = actions.onFinishCancel)
    ScreenFrame {
        Column(
            Modifier.fillMaxSize().padding(horizontal = if (compact) 36.dp else 28.dp, vertical = if (compact) 18.dp else 22.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            PresentaText(
                stringResource(R.string.finish_title),
                PresentaText.bold(if (compact) 15.sp else 17.sp),
                colors.warmWhite,
                Modifier.semantics { heading() },
                maxLines = 3,
            )
            Gap(6.dp)
            PresentaButton(
                onClick = actions.onFinishCancel,
                label = stringResource(R.string.finish_cancel),
                style = ButtonStyle.Secondary,
                minWidth = 124.dp,
            )
            Gap(4.dp)
            PresentaButton(
                onClick = actions.onFinishConfirm,
                label = stringResource(R.string.finish_confirm),
                style = ButtonStyle.Primary,
                minWidth = 124.dp,
            )
        }
    }
}
