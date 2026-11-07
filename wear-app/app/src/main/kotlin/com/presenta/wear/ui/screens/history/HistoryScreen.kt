package com.presenta.wear.ui.screens.history

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.foundation.rotary.RotaryScrollableDefaults
import androidx.wear.compose.foundation.rotary.rotaryScrollable
import com.presenta.wear.R
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.CappedFontScale
import com.presenta.wear.ui.components.FullRing
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaIcon
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme

/** A row of the history list with its text already resolved. */
data class HistoryRowUi(
    val item: HistoryItem,
    val dayText: String,
    val timeText: String,
    val paceText: String,
)

sealed interface HistoryUiState {
    data object Loading : HistoryUiState

    data object Empty : HistoryUiState

    data class Failed(val message: String) : HistoryUiState

    data class Loaded(val rows: List<HistoryRowUi>) : HistoryUiState
}

/** History list (mockup 06) and its empty variant (06b). Scrolls; rows are at least 48 dp tall. */
@Composable
fun HistoryScreen(state: HistoryUiState, onOpen: (HistoryItem) -> Unit, onBack: () -> Unit) {
    CappedFontScale(maxScale = 1.3f) {
        when (state) {
            is HistoryUiState.Loaded -> HistoryList(state, onOpen, onBack)
            HistoryUiState.Empty -> HistoryMessage(stringResource(R.string.history_empty), IconKind.History, onBack)
            is HistoryUiState.Failed -> HistoryMessage(state.message, IconKind.Warning, onBack)
            HistoryUiState.Loading -> HistoryMessage(stringResource(R.string.history_loading), IconKind.History, onBack)
        }
    }
}

@Composable
private fun HistoryList(state: HistoryUiState.Loaded, onOpen: (HistoryItem) -> Unit, onBack: () -> Unit) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val listState = rememberLazyListState()
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { focusRequester.requestFocus() } }
    ScreenFrame {
        LazyColumn(
            modifier = Modifier
                .fillMaxSize()
                .rotaryScrollable(RotaryScrollableDefaults.behavior(listState), focusRequester),
            state = listState,
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            item {
                Spacer(Modifier.height(if (compact) 14.dp else 16.dp))
                PresentaText(
                    stringResource(R.string.history_title),
                    PresentaText.bold(if (compact) 15.sp else 17.sp),
                    colors.warmWhite,
                    Modifier.semantics { heading() },
                    maxLines = 1,
                )
            }
            items(state.rows) { row -> HistoryRow(row, onOpen, compact) }
            item {
                PresentaButton(
                    onClick = onBack,
                    icon = IconKind.ArrowBack,
                    style = ButtonStyle.Secondary,
                    contentDescription = stringResource(R.string.history_back),
                    minWidth = 72.dp,
                )
                Spacer(Modifier.height(if (compact) 18.dp else 22.dp))
            }
        }
    }
}

@Composable
private fun HistoryRow(row: HistoryRowUi, onOpen: (HistoryItem) -> Unit, compact: Boolean) {
    val colors = PresentaTheme.colors
    val description = stringResource(R.string.history_row_cd, row.dayText, row.timeText, row.paceText)
    Row(
        Modifier
            .width(if (compact) 150.dp else 170.dp)
            .defaultMinSize(minHeight = 52.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(colors.surface)
            .clickable(role = Role.Button, onClick = { onOpen(row.item) })
            .semantics(mergeDescendants = true) { contentDescription = description }
            .padding(start = 12.dp, end = 6.dp, top = 4.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            PresentaText(row.dayText, PresentaText.bold(if (compact) 13.sp else 14.sp), colors.warmWhite, maxLines = 1, align = androidx.compose.ui.text.style.TextAlign.Start)
            // With a large system font the time and the pace no longer fit side by side, so they are stacked.
            val stacked = LocalDensity.current.fontScale > 1.15f
            val detail: @Composable () -> Unit = {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    PresentaIcon(IconKind.Clock, colors.textMuted, size = 11.dp)
                    Spacer(Modifier.width(3.dp))
                    PresentaText(row.timeText, PresentaText.regular(if (compact) 10.sp else 11.sp), colors.textMuted, maxLines = 1)
                }
                PresentaText(
                    stringResource(R.string.history_pace, row.paceText),
                    PresentaText.regular(if (compact) 10.sp else 11.sp),
                    colors.textMuted,
                    Modifier.padding(start = if (stacked) 0.dp else 6.dp),
                    maxLines = 1,
                    align = androidx.compose.ui.text.style.TextAlign.Start,
                )
            }
            if (stacked) {
                Column { detail() }
            } else {
                Row(verticalAlignment = Alignment.CenterVertically) { detail() }
            }
        }
        PresentaIcon(IconKind.Chevron, colors.textMuted, size = 18.dp)
    }
}

@Composable
private fun HistoryMessage(text: String, icon: IconKind, onBack: () -> Unit) = ScreenFontCap {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    ScreenFrame {
        Column(
            Modifier.fillMaxSize().padding(horizontal = 28.dp, vertical = if (compact) 14.dp else 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            PresentaText(
                stringResource(R.string.history_title),
                PresentaText.bold(if (compact) 15.sp else 17.sp),
                colors.warmWhite,
                Modifier.semantics { heading() },
                maxLines = 1,
            )
            Gap(if (compact) 4.dp else 6.dp)
            Box(Modifier.size(if (compact) 40.dp else 52.dp), contentAlignment = Alignment.Center) {
                FullRing(color = colors.navy, dashed = true, strokeWidth = 2.dp, inset = 0.dp)
                PresentaIcon(icon, colors.textMuted, size = if (compact) 20.dp else 26.dp)
            }
            Gap(if (compact) 4.dp else 6.dp)
            PresentaText(text, PresentaText.medium(if (compact) 11.sp else 12.sp), colors.warmWhite, maxLines = 3)
            Gap(if (compact) 4.dp else 6.dp)
            PresentaButton(
                onClick = onBack,
                icon = IconKind.ArrowBack,
                style = ButtonStyle.Secondary,
                contentDescription = stringResource(R.string.history_back),
                minWidth = 72.dp,
            )
        }
    }
}
