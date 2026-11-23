package com.presenta.wear.haptics

import com.presenta.wear.model.AlertSeverity

/**
 * The only vibration patterns of the app. Each one is a waveform of alternating pauses and pulses in milliseconds,
 * starting with a pause, and is short enough to be felt during a talk without being heard.
 *
 * - [Info]: one short pulse.
 * - [Warning]: two short pulses.
 * - [Critical]: one long pulse followed by one short pulse.
 * - [CommandFailed]: one short pulse (a command did not go through, RF 15).
 */
enum class HapticPattern(val timingsMs: LongArray) {
    Info(longArrayOf(0, SHORT)),
    Warning(longArrayOf(0, SHORT, GAP, SHORT)),
    Critical(longArrayOf(0, LONG, GAP, SHORT)),
    CommandFailed(longArrayOf(0, SHORT)),
    ;

    companion object {
        fun forSeverity(severity: AlertSeverity): HapticPattern = when (severity) {
            AlertSeverity.Info -> Info
            AlertSeverity.Warning -> Warning
            AlertSeverity.Critical -> Critical
        }
    }
}

private const val SHORT = 90L
private const val LONG = 500L
private const val GAP = 140L

/** Plays a [HapticPattern]. The real implementation uses the vibrator; tests use a recording fake. */
fun interface HapticsPlayer {
    fun play(pattern: HapticPattern)
}

/** Used where there is nothing to vibrate (previews, tests that do not care). */
object NoHapticsPlayer : HapticsPlayer {
    override fun play(pattern: HapticPattern) = Unit
}

/** Receives one line per haptic event. Release builds pass [NoHapticLog], so nothing is written. */
fun interface HapticLog {
    fun line(text: String)
}

object NoHapticLog : HapticLog {
    override fun line(text: String) = Unit
}
