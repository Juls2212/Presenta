package com.presenta.wear.ui.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onClick
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.material3.Text
import com.presenta.wear.ui.theme.PresentaTheme
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/** Screens up to this height (dp) use the compact layout (Small Round is about 192 dp, Large Round about 227 dp). */
private const val COMPACT_MAX_HEIGHT_DP = 210

/** Smallest touch target used across the app (dp). */
val MinTouchTarget: Dp = 48.dp

/**
 * Documented exception for Small Round (about 192 dp), where the vertical budget cannot hold 48 dp targets:
 * secondary targets are 44 dp tall there (see ADR-31). Primary Next and Previous stay at 48 dp.
 */
val COMPACT_TARGET: Dp = 44.dp

/** Even smaller secondary targets (slide counter and clock) on Small Round Control. See ADR-31. */
val COMPACT_SECONDARY_TARGET: Dp = 36.dp

@Composable
fun isCompactScreen(): Boolean {
    val configuration = LocalConfiguration.current
    return minOf(configuration.screenWidthDp, configuration.screenHeightDp) < COMPACT_MAX_HEIGHT_DP
}

/** Caps the system font scale for layouts that cannot grow on a round screen. Documented per screen. */
@Composable
fun CappedFontScale(maxScale: Float, content: @Composable () -> Unit) {
    val density = LocalDensity.current
    val capped = Density(density.density, fontScale = minOf(density.fontScale, maxScale))
    androidx.compose.runtime.CompositionLocalProvider(LocalDensity provides capped, content = content)
}

/**
 * Font scale cap used by the screens that must fit a round screen without scrolling: 1.15 on Large Round and
 * 1.0 on Small Round (see ADR-32). Lists and content that scrolls keep the full system font scale.
 */
@Composable
fun ScreenFontCap(content: @Composable () -> Unit) {
    CappedFontScale(maxScale = if (isCompactScreen()) 1.0f else 1.15f, content = content)
}

/** Text styles. Numerals use tabular figures so the clock does not jitter. */
object PresentaText {
    fun bold(size: TextUnit, lineHeight: TextUnit = size * 1.1f) = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Bold,
        fontSize = size,
        lineHeight = lineHeight,
        fontFeatureSettings = "tnum",
    )

    fun regular(size: TextUnit, lineHeight: TextUnit = size * 1.2f) = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = size,
        lineHeight = lineHeight,
        fontFeatureSettings = "tnum",
    )

    fun medium(size: TextUnit, lineHeight: TextUnit = size * 1.15f) = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = size,
        lineHeight = lineHeight,
        fontFeatureSettings = "tnum",
    )
}

@Composable
fun PresentaText(
    text: String,
    style: TextStyle,
    color: Color,
    modifier: Modifier = Modifier,
    maxLines: Int = Int.MAX_VALUE,
    align: TextAlign = TextAlign.Center,
) {
    Text(
        text = text,
        style = style,
        color = color,
        modifier = modifier,
        maxLines = maxLines,
        overflow = TextOverflow.Ellipsis,
        textAlign = align,
    )
}

/**
 * Single-line text that shrinks until it fits [maxWidth]. Used for headlines near the top of a round screen,
 * where the available chord is narrow and a translated or scaled string could otherwise leave the circle.
 */
@Composable
fun ShrinkText(
    text: String,
    style: TextStyle,
    color: Color,
    maxWidth: Dp,
    modifier: Modifier = Modifier,
    minSize: TextUnit = 9.sp,
) {
    val fontScale = LocalDensity.current.fontScale
    var size by remember(text, style.fontSize, maxWidth, fontScale) { mutableStateOf(style.fontSize) }
    Text(
        text = text,
        modifier = modifier.widthIn(max = maxWidth),
        color = color,
        style = style.copy(fontSize = size, lineHeight = size * 1.15f),
        maxLines = 1,
        softWrap = false,
        // Clip, not Ellipsis: an ellipsized layout never reports width overflow, so it would not shrink.
        overflow = TextOverflow.Clip,
        textAlign = TextAlign.Center,
        onTextLayout = { result ->
            if (result.didOverflowWidth && size.value > minSize.value) size = (size.value * 0.93f).sp
        },
    )
}

/** Night background that fills the screen. */
@Composable
fun ScreenFrame(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Box(modifier.fillMaxSize().background(PresentaTheme.colors.night)) { content() }
}

enum class ButtonStyle { Primary, Secondary, Light, Outlined }

/**
 * Pill button. Height is at least [minHeight] (48 dp unless a documented exception applies).
 * [accent] colors the outlined style (for example amber).
 */
@Composable
fun PresentaButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    label: String? = null,
    icon: IconKind? = null,
    style: ButtonStyle = ButtonStyle.Primary,
    accent: Color? = null,
    contentDescription: String? = null,
    minHeight: Dp = MinTouchTarget,
    minWidth: Dp = MinTouchTarget,
    textSize: TextUnit = 15.sp,
    enabled: Boolean = true,
    maxWidth: Dp = Dp.Unspecified,
    maxLines: Int = 1,
) {
    val colors = PresentaTheme.colors
    val (background, content) = when (style) {
        ButtonStyle.Primary -> colors.cyan to colors.night
        ButtonStyle.Secondary -> colors.navy to colors.warmWhite
        ButtonStyle.Light -> colors.warmWhite to colors.night
        ButtonStyle.Outlined -> Color.Transparent to (accent ?: colors.warmWhite)
    }
    val shape = RoundedCornerShape(50)
    var base = modifier
        .widthIn(max = maxWidth)
        .defaultMinSize(minWidth = minWidth, minHeight = minHeight)
        .clip(shape)
        .background(background)
    if (style == ButtonStyle.Outlined) {
        base = base.border(1.5.dp, (accent ?: colors.warmWhite).copy(alpha = 0.8f), shape)
    }
    Row(
        modifier = base
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick)
            .then(if (contentDescription != null) Modifier.semantics { this.contentDescription = contentDescription } else Modifier)
            .padding(horizontal = 14.dp),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            PresentaIcon(icon, content, size = if (label == null) 26.dp else 18.dp)
        }
        if (icon != null && label != null) Spacer(Modifier.width(6.dp))
        if (label != null) {
            PresentaText(label, PresentaText.bold(textSize), content, maxLines = maxLines)
        }
    }
}

/**
 * Button that fires once on press and keeps repeating while held (used by the steppers).
 * Exposes a normal click action to accessibility services.
 */
@Composable
fun RepeatButton(
    onClick: () -> Unit,
    icon: IconKind,
    contentDescription: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    minWidth: Dp = MinTouchTarget,
    minHeight: Dp = MinTouchTarget,
) {
    val colors = PresentaTheme.colors
    val currentClick by rememberUpdatedState(onClick)
    val scope = rememberCoroutineScope()
    Box(
        modifier = modifier
            .defaultMinSize(minWidth = minWidth, minHeight = minHeight)
            .clip(RoundedCornerShape(50))
            .background(colors.navy)
            .semantics(mergeDescendants = true) {
                this.contentDescription = contentDescription
                role = Role.Button
                onClick(label = null) {
                    if (enabled) currentClick()
                    enabled
                }
            }
            .pointerInput(enabled) {
                detectTapGestures(
                    onPress = {
                        if (enabled) {
                            currentClick()
                            val job = scope.launch {
                                delay(450)
                                while (true) {
                                    currentClick()
                                    delay(90)
                                }
                            }
                            tryAwaitRelease()
                            job.cancel()
                        }
                    },
                )
            },
        contentAlignment = Alignment.Center,
    ) {
        PresentaIcon(icon, if (enabled) colors.warmWhite else colors.warmWhite.copy(alpha = 0.35f), size = 22.dp)
    }
}

/** Pill with an optional icon, used for connection status and pace. Not interactive. */
@Composable
fun StatusPill(
    text: String,
    accent: Color,
    modifier: Modifier = Modifier,
    icon: IconKind? = null,
    textSize: TextUnit = 12.sp,
    height: Dp = 24.dp,
) {
    val shape = RoundedCornerShape(50)
    Row(
        modifier = modifier
            .height(height)
            .clip(shape)
            .background(accent.copy(alpha = 0.14f))
            .border(1.dp, accent.copy(alpha = 0.55f), shape)
            .padding(horizontal = 10.dp),
        horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            PresentaIcon(icon, accent, size = 13.dp)
            Spacer(Modifier.width(4.dp))
        }
        PresentaText(text, PresentaText.medium(textSize), accent, maxLines = 1)
    }
}

/** Dark track with a colored arc starting at 12 o'clock. [progress] is 0..1. */
@Composable
fun ProgressRing(progress: Float, color: Color, modifier: Modifier = Modifier, strokeWidth: Dp = 4.dp, inset: Dp = 6.dp) {
    val track = PresentaTheme.colors.ringTrack
    Canvas(modifier.fillMaxSize()) {
        val stroke = strokeWidth.toPx()
        val pad = inset.toPx() + stroke / 2
        val arcSize = Size(size.width - 2 * pad, size.height - 2 * pad)
        val topLeft = Offset(pad, pad)
        drawArc(track, 0f, 360f, false, topLeft, arcSize, style = Stroke(stroke))
        val sweep = 360f * progress.coerceIn(0f, 1f)
        if (sweep > 0f) {
            drawArc(color, -90f, sweep, false, topLeft, arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
        }
    }
}

/** Full ring without progress. [dashed] draws the disconnected look. */
@Composable
fun FullRing(color: Color, modifier: Modifier = Modifier, dashed: Boolean = false, strokeWidth: Dp = 3.dp, inset: Dp = 6.dp) {
    Canvas(modifier.fillMaxSize()) {
        val stroke = strokeWidth.toPx()
        val pad = inset.toPx() + stroke / 2
        val effect = if (dashed) PathEffect.dashPathEffect(floatArrayOf(stroke * 3f, stroke * 3f)) else null
        drawArc(
            color,
            0f,
            360f,
            false,
            Offset(pad, pad),
            Size(size.width - 2 * pad, size.height - 2 * pad),
            style = Stroke(stroke, pathEffect = effect),
        )
    }
}

/** Circle with an icon, used as the header badge of several screens. */
@Composable
fun IconBadge(icon: IconKind, tint: Color, background: Color, modifier: Modifier = Modifier, size: Dp = 28.dp, iconSize: Dp = 16.dp) {
    Box(modifier.size(size).clip(CircleShape).background(background), contentAlignment = Alignment.Center) {
        PresentaIcon(icon, tint, size = iconSize)
    }
}

/** Vertical spacing helper that avoids magic numbers in screens. */
@Composable
fun Gap(height: Dp) = Spacer(Modifier.height(height))
