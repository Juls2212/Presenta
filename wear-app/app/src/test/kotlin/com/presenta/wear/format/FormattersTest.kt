package com.presenta.wear.format

import com.presenta.wear.support.TestClocks
import java.time.ZonedDateTime
import java.util.Locale
import org.junit.Assert.assertEquals
import org.junit.Test

class FormattersTest {
    @Test
    fun clock_uses_minutes_and_seconds_below_one_hour() {
        assertEquals("00:00", DurationFormat.clock(0))
        assertEquals("08:32", DurationFormat.clock(8 * 60_000L + 32_000))
        assertEquals("59:59", DurationFormat.clock(59 * 60_000L + 59_999))
    }

    @Test
    fun clock_switches_to_hours_from_one_hour() {
        assertEquals("1:00:00", DurationFormat.clock(3_600_000))
        assertEquals("1:05:09", DurationFormat.clock(3_600_000 + 5 * 60_000L + 9_000))
        assertEquals("12:00:01", DurationFormat.clock(12 * 3_600_000L + 1_000))
    }

    @Test
    fun clock_ignores_the_sign() {
        assertEquals("01:10", DurationFormat.clock(-70_000))
    }

    @Test
    fun pace_uses_a_dot_and_two_decimals() {
        assertEquals("1.37", PaceFormat.slidesPerMinute(1.3746))
        assertEquals("0.00", PaceFormat.slidesPerMinute(0.0))
        assertEquals("12.50", PaceFormat.slidesPerMinute(12.5))
    }

    @Test
    fun pace_does_not_depend_on_the_device_locale() {
        val previous = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("es-CO"))
            assertEquals("1.37", PaceFormat.slidesPerMinute(1.37))
            assertEquals("08:32", DurationFormat.clock(512_000))
            Locale.setDefault(Locale.forLanguageTag("ar-EG"))
            assertEquals("1.37", PaceFormat.slidesPerMinute(1.37))
            assertEquals("08:32", DurationFormat.clock(512_000))
        } finally {
            Locale.setDefault(previous)
        }
    }

    private val formatter = HistoryTimeFormatter(TestClocks.wall)

    private fun at(isoLocal: String): Long =
        ZonedDateTime.parse("${isoLocal}-05:00[America/Bogota]").toInstant().toEpochMilli()

    @Test
    fun day_label_is_today_for_the_same_day() {
        assertEquals(DayLabel.Today, formatter.dayLabel(at("2026-10-31T00:05:00")))
        assertEquals(DayLabel.Today, formatter.dayLabel(at("2026-10-31T14:32:00")))
    }

    @Test
    fun day_label_is_yesterday_for_the_previous_day() {
        assertEquals(DayLabel.Yesterday, formatter.dayLabel(at("2026-10-30T23:59:00")))
    }

    @Test
    fun day_label_is_a_date_for_older_days() {
        assertEquals(DayLabel.Date(8, 9), formatter.dayLabel(at("2026-10-08T16:05:00")))
        assertEquals(DayLabel.Date(1, 0), formatter.dayLabel(at("2026-01-01T09:00:00")))
        assertEquals(DayLabel.Date(31, 11), formatter.dayLabel(at("2025-12-31T09:00:00")))
    }

    @Test
    fun day_label_uses_the_clock_time_zone_not_utc() {
        // 2026-11-01T02:00Z is still 31 Oct 21:00 in Bogota.
        val instant = java.time.Instant.parse("2026-11-01T02:00:00Z").toEpochMilli()
        assertEquals(DayLabel.Today, formatter.dayLabel(instant))
    }

    @Test
    fun time_of_day_is_24_hour_with_leading_zeros() {
        assertEquals("14:32", formatter.timeOfDay(at("2026-10-31T14:32:00")))
        assertEquals("16:05", formatter.timeOfDay(at("2026-10-08T16:05:00")))
        assertEquals("00:07", formatter.timeOfDay(at("2026-10-31T00:07:00")))
    }
}
