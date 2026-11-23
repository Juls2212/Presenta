package com.presenta.wear.ui.components

import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.ui.platform.LocalView

/**
 * Keeps the screen on while [enabled] is true, using the view flag: no permission, no wake lock, and the system clears
 * it by itself when the window goes away. It is cleared again when [enabled] turns false or this leaves the composition.
 */
@Composable
fun KeepScreenOn(enabled: Boolean) {
    val view = LocalView.current
    DisposableEffect(view, enabled) {
        view.keepScreenOn = enabled
        onDispose { view.keepScreenOn = false }
    }
}
