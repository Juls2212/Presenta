package com.presenta.wear.format

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import java.time.Clock
import java.time.Instant
import java.time.ZoneId
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

/** The history dates as the mockup writes them: "Hoy", "Ayer" and "08 Oct", from a UTC timestamp and a fixed clock and zone. */
@RunWith(RobolectricTestRunner::class)
class HistoryDatesTest {
    private val mapper = StringMapper(ApplicationProvider.getApplicationContext<Context>().resources)
    private val bogota: ZoneId = ZoneId.of("America/Bogota") // UTC-5, no daylight saving

    private fun formatter(now: String, zone: ZoneId = bogota) = HistoryTimeFormatter(Clock.fixed(Instant.parse(now), zone))

    private fun label(f: HistoryTimeFormatter, utc: String) = mapper.dayLabel(f.dayLabel(Instant.parse(utc).toEpochMilli()))

    @Test
    fun the_mockup_rows_read_hoy_ayer_and_08_oct() {
        val f = formatter("2026-10-31T20:00:00Z") // 15:00 on 31 Oct in Bogota

        assertEquals("Hoy", label(f, "2026-10-31T19:32:00Z"))
        assertEquals("Ayer", label(f, "2026-10-30T17:48:00Z"))
        assertEquals("08 Oct", label(f, "2026-10-08T21:05:00Z"))
    }

    @Test
    fun all_twelve_months_are_capitalized_abbreviations_and_the_day_has_two_digits() {
        val f = formatter("2026-12-31T20:00:00Z")
        val expected = listOf("Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic")

        expected.forEachIndexed { index, month ->
            assertEquals("05 $month", mapper.dayLabel(DayLabel.Date(5, index)))
            assertEquals("27 $month", mapper.dayLabel(DayLabel.Date(27, index)))
        }
        assertEquals("03 Mar", label(f, "2026-03-03T15:00:00Z"))
    }

    @Test
    fun a_utc_timestamp_after_local_midnight_UTC_belongs_to_the_previous_local_day() {
        val f = formatter("2026-10-31T20:00:00Z")

        // 2026-10-31T03:00Z is 22:00 on 30 Oct in Bogota: yesterday, although its UTC date is today.
        assertEquals("Ayer", label(f, "2026-10-31T03:00:00Z"))
        assertEquals("22:00", f.timeOfDay(Instant.parse("2026-10-31T03:00:00Z").toEpochMilli()))
        // 2026-11-01T04:59Z is still 23:59 on 31 Oct in Bogota: today.
        assertEquals("Hoy", label(f, "2026-11-01T04:59:00Z"))
    }

    @Test
    fun the_day_boundaries_around_local_midnight() {
        val now = formatter("2026-10-31T20:00:00Z")

        assertEquals("Hoy", label(now, "2026-10-31T05:00:00Z")) // 00:00 local
        assertEquals("Ayer", label(now, "2026-10-31T04:59:59Z")) // 23:59:59 the day before
        assertEquals("Ayer", label(now, "2026-10-30T05:00:00Z"))
        assertEquals("29 Oct", label(now, "2026-10-30T04:59:59Z"))
    }

    @Test
    fun just_after_midnight_the_same_record_moves_from_hoy_to_ayer() {
        val record = Instant.parse("2026-10-31T19:32:00Z").toEpochMilli()

        assertEquals("Hoy", mapper.dayLabel(formatter("2026-10-31T23:59:00Z").dayLabel(record)))
        assertEquals("Ayer", mapper.dayLabel(formatter("2026-11-01T05:00:00Z").dayLabel(record)))
        assertEquals("31 Oct", mapper.dayLabel(formatter("2026-11-02T05:00:00Z").dayLabel(record)))
    }

    @Test
    fun the_year_change_keeps_ayer_for_the_last_day_of_december_and_a_date_afterwards() {
        val f = formatter("2027-01-01T15:00:00Z") // 10:00 on 1 Jan in Bogota

        assertEquals("Hoy", label(f, "2027-01-01T06:00:00Z"))
        assertEquals("Ayer", label(f, "2026-12-31T20:00:00Z"))
        assertEquals("30 Dic", label(f, "2026-12-30T20:00:00Z"))
        assertEquals("01 Ene", label(formatter("2027-01-05T15:00:00Z"), "2027-01-01T15:00:00Z"))
    }

    @Test
    fun another_time_zone_moves_the_boundary() {
        val madrid = ZoneId.of("Europe/Madrid") // UTC+1 in winter, UTC+2 in summer
        val f = formatter("2026-10-31T20:00:00Z", madrid) // 21:00 on 31 Oct in Madrid

        assertEquals("Hoy", label(f, "2026-10-30T23:30:00Z")) // 00:30 on 31 Oct in Madrid (summer time ended on 25 Oct)
    }

    @Test
    fun pace_keeps_a_dot_as_the_decimal_separator() {
        val previous = java.util.Locale.getDefault()
        try {
            java.util.Locale.setDefault(java.util.Locale("es", "CO"))
            assertEquals("1.37", PaceFormat.slidesPerMinute(1.37))
            assertEquals("1.50", PaceFormat.slidesPerMinute(1.5))
        } finally {
            java.util.Locale.setDefault(previous)
        }
    }
}
