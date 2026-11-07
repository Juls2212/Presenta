package com.presenta.wear.data

import android.os.SystemClock

object SystemMonotonicClock : MonotonicClock {
    override fun nowMs(): Long = SystemClock.elapsedRealtime()
}
