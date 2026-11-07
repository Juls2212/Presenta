package com.presenta.wear.ui.screens.pairing

import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.rotary.onRotaryScrollEvent
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.presenta.wear.R
import com.presenta.wear.format.StringMapper
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.ui.components.ButtonStyle
import com.presenta.wear.ui.components.Gap
import com.presenta.wear.ui.components.IconKind
import com.presenta.wear.ui.components.PresentaButton
import com.presenta.wear.ui.components.PresentaText
import com.presenta.wear.ui.components.RepeatButton
import com.presenta.wear.ui.components.ScreenFontCap
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.components.ShrinkText
import com.presenta.wear.ui.components.isCompactScreen
import com.presenta.wear.ui.theme.PresentaTheme
import kotlin.math.abs

const val PAIRING_CODE_LENGTH = 6

/** State of the pairing screen: the six digits being entered, the selected one, and the last outcome. */
data class PairingUiState(
    val digits: List<Int> = List(PAIRING_CODE_LENGTH) { 0 },
    val index: Int = 0,
    val connecting: Boolean = false,
    val error: ErrorCode? = null,
) {
    val isLastDigit: Boolean get() = index == PAIRING_CODE_LENGTH - 1
    val code: String get() = digits.joinToString("")
}

class PairingActions(
    val onIncrease: () -> Unit,
    val onDecrease: () -> Unit,
    val onPreviousDigit: () -> Unit,
    /** "Siguiente" on digits 1 to 5; "Conectar" on the last one. */
    val onNextOrConnect: () -> Unit,
)

/**
 * Pairing (derived screen). A numeric keypad cannot have 48 dp keys on a round screen, so the code is entered one digit
 * at a time: − and + change the selected digit (the crown does too), "Siguiente" moves on, and the last step connects.
 * The focus order is: title, message, digits summary, −, digit, +, previous digit, next.
 */
@Composable
fun PairingScreen(state: PairingUiState, actions: PairingActions) = ScreenFontCap {
    val colors = PresentaTheme.colors
    val compact = isCompactScreen()
    val mapper = StringMapper(LocalResources.current)
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { focusRequester.requestFocus() } }
    val rotaryAccumulator = remember { floatArrayOf(0f) }
    val message = state.error?.let(mapper::error)
        ?: stringResource(if (state.connecting) R.string.pairing_connecting else R.string.pairing_hint)
    val selectedDigit = state.digits[state.index]
    val summary = stringResource(R.string.pairing_code_cd, state.digits.joinToString(" "), state.index + 1)
    val digitDescription = stringResource(R.string.pairing_digit_cd, state.index + 1, selectedDigit)

    ScreenFrame(
        Modifier
            .onRotaryScrollEvent { event ->
                rotaryAccumulator[0] += event.verticalScrollPixels
                if (abs(rotaryAccumulator[0]) >= ROTARY_STEP_PX) {
                    if (rotaryAccumulator[0] > 0) actions.onIncrease() else actions.onDecrease()
                    rotaryAccumulator[0] = 0f
                }
                true
            }
            .focusRequester(focusRequester)
            .focusable(),
    ) {
        Column(
            Modifier.fillMaxSize().padding(top = if (compact) 12.dp else 14.dp, bottom = if (compact) 22.dp else 18.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            ShrinkText(
                stringResource(R.string.pairing_title),
                PresentaText.bold(if (compact) 14.sp else 16.sp),
                colors.warmWhite,
                maxWidth = 110.dp,
                modifier = Modifier.semantics { heading() },
            )
            // Long messages (for example the lockout) wrap onto a second line instead of being cut off.
            PresentaText(
                message,
                PresentaText.regular(if (compact) 9.sp else 11.sp),
                if (state.error != null) colors.amber else colors.textMuted,
                Modifier.widthIn(max = if (compact) 126.dp else 160.dp),
                maxLines = 2,
            )
            Gap(if (compact) 0.dp else 2.dp)
            Row(
                Modifier.semantics(mergeDescendants = true) { contentDescription = summary },
                horizontalArrangement = Arrangement.spacedBy(if (compact) 2.dp else 3.dp),
            ) {
                state.digits.forEachIndexed { position, digit ->
                    val selected = position == state.index
                    Box(
                        Modifier
                            .width(if (compact) 20.dp else 23.dp)
                            .height(if (compact) 22.dp else 26.dp)
                            .clip(RoundedCornerShape(8.dp))
                            .background(if (selected) colors.cyan else colors.surface),
                        contentAlignment = Alignment.Center,
                    ) {
                        PresentaText(
                            digit.toString(),
                            PresentaText.bold(if (compact) 12.sp else 14.sp),
                            if (selected) colors.night else colors.warmWhite,
                            maxLines = 1,
                        )
                    }
                }
            }
            Gap(if (compact) 0.dp else 2.dp)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                RepeatButton(actions.onDecrease, IconKind.Minus, stringResource(R.string.pairing_decrease), minWidth = 48.dp)
                Box(
                    Modifier.width(if (compact) 44.dp else 56.dp).semantics(mergeDescendants = true) { contentDescription = digitDescription },
                    contentAlignment = Alignment.Center,
                ) {
                    PresentaText(selectedDigit.toString(), PresentaText.bold(if (compact) 34.sp else 42.sp), colors.cyan, maxLines = 1)
                }
                RepeatButton(actions.onIncrease, IconKind.Plus, stringResource(R.string.pairing_increase), minWidth = 48.dp)
            }
            Gap(if (compact) 0.dp else 2.dp)
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                PresentaButton(
                    onClick = actions.onPreviousDigit,
                    icon = IconKind.ArrowBack,
                    style = ButtonStyle.Secondary,
                    contentDescription = stringResource(R.string.pairing_previous_digit),
                    minWidth = 48.dp,
                    enabled = state.index > 0,
                    modifier = Modifier.alpha(if (state.index > 0) 1f else 0.4f),
                )
                PresentaButton(
                    onClick = actions.onNextOrConnect,
                    label = stringResource(if (state.isLastDigit) R.string.pairing_connect else R.string.pairing_next),
                    icon = if (state.isLastDigit) IconKind.Check else IconKind.Chevron,
                    style = ButtonStyle.Primary,
                    minWidth = if (compact) 76.dp else 96.dp,
                    textSize = if (compact) 12.sp else 13.sp,
                    enabled = !state.connecting,
                )
            }
        }
    }
}

private const val ROTARY_STEP_PX = 40f
