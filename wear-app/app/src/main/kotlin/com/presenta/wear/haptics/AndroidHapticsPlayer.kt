package com.presenta.wear.haptics

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.util.Log

/** Real vibration through the system vibrator. It does nothing on a device without one. */
class AndroidHapticsPlayer(context: Context) : HapticsPlayer {
    private val vibrator: Vibrator? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        context.getSystemService(VibratorManager::class.java)?.defaultVibrator
    } else {
        context.getSystemService(Vibrator::class.java)
    }

    override fun play(pattern: HapticPattern) {
        val target = vibrator ?: return
        if (!target.hasVibrator()) return
        target.vibrate(VibrationEffect.createWaveform(pattern.timingsMs, NO_REPEAT))
    }

    private companion object {
        const val NO_REPEAT = -1
    }
}

/** Debug builds only: one Logcat line per haptic event under the tag [TAG]. */
class LogcatHapticLog : HapticLog {
    override fun line(text: String) {
        Log.d(TAG, text)
    }

    companion object {
        const val TAG = "PresentaHaptics"
    }
}
