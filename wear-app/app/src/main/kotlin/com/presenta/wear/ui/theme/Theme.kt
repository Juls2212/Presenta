package com.presenta.wear.ui.theme

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.wear.compose.material3.ColorScheme
import androidx.wear.compose.material3.MaterialTheme

/** Design tokens from the approved mockups (docs/ui-direction.md). */
@Immutable
data class PresentaColors(
    val night: Color = Color(0xFF0C1424),
    val warmWhite: Color = Color(0xFFF5EFE6),
    val cyan: Color = Color(0xFF22D3EE),
    val amber: Color = Color(0xFFFFB020),
    /** Secondary button and stepper button. */
    val navy: Color = Color(0xFF1E2D4C),
    /** Cards and rows. */
    val surface: Color = Color(0xFF152139),
    val ringTrack: Color = Color(0xFF111725),
    /** Reserved for overtime. */
    val critical: Color = Color(0xFFFF5C5C),
    /** Secondary text on night: warm white at reduced emphasis, still above 4.5:1. */
    val textMuted: Color = Color(0xFFB8B3AF),
)

val LocalPresentaColors = staticCompositionLocalOf { PresentaColors() }

object PresentaTheme {
    val colors: PresentaColors
        @Composable get() = LocalPresentaColors.current
}

@Composable
fun PresentaTheme(content: @Composable () -> Unit) {
    val tokens = PresentaColors()
    val scheme = ColorScheme(
        primary = tokens.cyan,
        onPrimary = tokens.night,
        secondary = tokens.navy,
        onSecondary = tokens.warmWhite,
        background = tokens.night,
        onBackground = tokens.warmWhite,
        surfaceContainer = tokens.surface,
        onSurface = tokens.warmWhite,
        onSurfaceVariant = tokens.textMuted,
        error = tokens.critical,
        onError = tokens.night,
    )
    CompositionLocalProvider(LocalPresentaColors provides tokens) {
        MaterialTheme(colorScheme = scheme, content = content)
    }
}
