package com.presenta.wear.support

import com.presenta.wear.data.MonotonicClock
import java.time.Clock
import java.time.Instant
import java.time.ZoneId

/** Monotonic clock that only moves when the test moves it. */
class FakeMonotonicClock(var now: Long = 1_000) : MonotonicClock {
    override fun nowMs(): Long = now

    fun advance(ms: Long) {
        now += ms
    }
}

/** Today is 2026-10-31 (Bogota), so the fake history reads "Hoy", "Ayer" and "08 Oct". */
object TestClocks {
    val zone: ZoneId = ZoneId.of("America/Bogota")

    /** 2026-10-31 15:00 in Bogota. */
    val wall: Clock = Clock.fixed(Instant.parse("2026-10-31T20:00:00Z"), zone)
}
