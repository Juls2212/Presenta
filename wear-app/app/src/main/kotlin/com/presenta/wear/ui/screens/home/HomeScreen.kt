package com.presenta.wear.ui.screens.home

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.presenta.wear.R
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.FullRing
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.IconBadge
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaIcon
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme

data class HomeUiState(val connection: ConnectionState)

/** Home when connected (mockup 01); otherwise the disconnected / reconnecting screen (mockup 07). */
@Composable
fun HomeScreen(
    state: HomeUiState,
    onNewPresentation: () -> Unit,
    onHistory: () -> Unit,
    onRetry: () -> Unit,
) {
    ScreenFontCap {
        if (state.connection == ConnectionState.Connected) {
            ConnectedHome(onNewPresentation, onHistory)
        } else {
            DisconnectedHome(state.connection, onRetry)
        }
    }
}

@Composable
private fun ConnectedHome(onNewPresentation: () -> Unit, onHistory: () -> Unit) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    ScreenFrame {
        Column(
            Modifier.fillMaxSize().padding(top = 14.dp, bottom = if (compact) 14.dp else 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            if (!compact) {
                PresentaIcon(IconKind.Logo, colors.cyan, size = 30.dp)
                Gap(2.dp)
            }
            PresentaText(
                stringResource(R.string.home_title),
                com.presenta.wear.ui.components.PresentaText.bold(if (compact) 20.sp else 24.sp),
                colors.warmWhite,
                Modifier.semantics { heading() },
                maxLines = 1,
            )
            Gap(4.dp)
            ConnectedPill()
            Gap(if (compact) 6.dp else 8.dp)
            PresentaButton(
                onClick = onNewPresentation,
                label = stringResource(R.string.home_new_presentation),
                icon = IconKind.Plus,
                style = ButtonStyle.Primary,
                textSize = if (compact) 13.sp else 14.sp,
            )
            Gap(4.dp)
            PresentaButton(
                onClick = onHistory,
                label = stringResource(R.string.home_history),
                icon = IconKind.History,
                style = ButtonStyle.Outlined,
                textSize = if (compact) 13.sp else 14.sp,
            )
        }
    }
}

@Composable
private fun ConnectedPill() {
    val colors = PresentaTheme.colors
    Row(
        Modifier
            .clip(CircleShape)
            .background(colors.surface)
            .border(1.dp, colors.navy, CircleShape)
            .padding(horizontal = 10.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(colors.cyan))
        Spacer(Modifier.width(5.dp))
        PresentaIcon(IconKind.Monitor, colors.warmWhite, size = 12.dp)
        Spacer(Modifier.width(4.dp))
        PresentaText(
            stringResource(R.string.home_pill_connected),
            com.presenta.wear.ui.components.PresentaText.medium(11.sp),
            colors.warmWhite,
            maxLines = 1,
        )
    }
}

@Composable
private fun DisconnectedHome(connection: ConnectionState, onRetry: () -> Unit) {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val reconnecting = connection == ConnectionState.Reconnecting
    val accent = if (reconnecting) colors.amber else colors.warmWhite.copy(alpha = 0.75f)
    ScreenFrame {
        FullRing(color = if (reconnecting) colors.amber else colors.navy.copy(alpha = 0.95f), dashed = true)
        Column(
            Modifier.fillMaxSize().padding(horizontal = if (compact) 26.dp else 30.dp, vertical = if (compact) 18.dp else 22.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            IconBadge(
                icon = IconKind.MonitorOff,
                tint = accent,
                background = colors.navy,
                size = if (compact) 32.dp else 38.dp,
                iconSize = if (compact) 18.dp else 22.dp,
            )
            Gap(4.dp)
            PresentaText(
                stringResource(if (reconnecting) R.string.reconnecting_title else R.string.disconnected_title),
                com.presenta.wear.ui.components.PresentaText.bold(if (compact) 15.sp else 17.sp),
                if (reconnecting) colors.amber else colors.warmWhite,
                Modifier.semantics { heading() },
                maxLines = 2,
            )
            Gap(2.dp)
            PresentaText(
                stringResource(if (reconnecting) R.string.reconnecting_hint else R.string.disconnected_hint),
                com.presenta.wear.ui.components.PresentaText.regular(if (compact) 10.sp else 11.sp),
                colors.textMuted,
                maxLines = 3,
            )
            Gap(if (compact) 4.dp else 6.dp)
            PresentaButton(
                onClick = onRetry,
                label = stringResource(R.string.disconnected_retry),
                icon = IconKind.Refresh,
                style = ButtonStyle.Light,
                textSize = if (compact) 13.sp else 14.sp,
            )
        }
    }
}
