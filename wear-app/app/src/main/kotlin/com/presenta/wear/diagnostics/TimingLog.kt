package com.presenta.wear.diagnostics

import android.util.Log

/**
 * Receives one line per measured event (command round trips, connection loss). Debug builds send the lines to Logcat under
 * [LogcatTimingLog.TAG]; release builds use [NoTimingLog], which does nothing, so no timing is ever written there.
 */
fun interface TimingLog {
    fun line(text: String)
}

object NoTimingLog : TimingLog {
    override fun line(text: String) = Unit
}

class LogcatTimingLog : TimingLog {
    override fun line(text: String) {
        Log.d(TAG, text)
    }

    companion object {
        const val TAG = "PresentaTiming"
    }
}

object TimingLogs {
    /** The log for a build type: Logcat in debug builds, nothing in release builds. */
    fun forBuild(debug: Boolean): TimingLog = if (debug) LogcatTimingLog() else NoTimingLog
}
