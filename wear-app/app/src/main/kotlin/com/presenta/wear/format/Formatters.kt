package com.presenta.wear.format

import java.time.Clock
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.util.Locale
import kotlin.math.abs

/** Time format: `mm:ss`, and `h:mm:ss` from one hour. The sign is ignored; callers pick the wording. */
object DurationFormat {
    fun clock(ms: Long): String {
        val totalSeconds = abs(ms) / 1000
        val hours = totalSeconds / 3600
        val minutes = (totalSeconds % 3600) / 60
        val seconds = totalSeconds % 60
        return if (hours > 0) {
            String.format(Locale.ROOT, "%d:%02d:%02d", hours, minutes, seconds)
        } else {
            String.format(Locale.ROOT, "%02d:%02d", minutes, seconds)
        }
    }
}

/**
 * Pace is written with a dot as the decimal separator and two decimals (for example `1.37`), exactly as in the
 * mockup, independent of the device locale (Locale.ROOT).
 */
object PaceFormat {
    fun slidesPerMinute(value: Double): String = String.format(Locale.ROOT, "%.2f", value)
}

/** Day label of a history row, before it is turned into Spanish text. */
sealed interface DayLabel {
    data object Today : DayLabel

    data object Yesterday : DayLabel

    /** [monthIndex] is 0 for January. */
    data class Date(val day: Int, val monthIndex: Int) : DayLabel
}

/** Day labels and clock times for history rows, relative to an injectable [clock]. */
class HistoryTimeFormatter(private val clock: Clock) {
    fun dayLabel(epochMs: Long): DayLabel {
        val date = localDate(epochMs)
        val today = LocalDate.now(clock)
        return when (date) {
            today -> DayLabel.Today
            today.minusDays(1) -> DayLabel.Yesterday
            else -> DayLabel.Date(date.dayOfMonth, date.monthValue - 1)
        }
    }

    /** 24-hour `HH:mm`, as in the mockup (`14:32`, `16:05`). */
    fun timeOfDay(epochMs: Long): String {
        val time = localDateTime(epochMs)
        return String.format(Locale.ROOT, "%02d:%02d", time.hour, time.minute)
    }

    fun today(): LocalDate = LocalDate.now(clock)

    private fun localDateTime(epochMs: Long): LocalDateTime =
        LocalDateTime.ofInstant(Instant.ofEpochMilli(epochMs), clock.zone)

    private fun localDate(epochMs: Long): LocalDate = localDateTime(epochMs).toLocalDate()
}
