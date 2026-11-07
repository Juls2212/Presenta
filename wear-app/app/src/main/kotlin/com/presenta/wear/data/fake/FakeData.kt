package com.presenta.wear.data.fake

import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.Recommendation
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.model.SessionSnapshot
import com.presenta.wear.model.SessionState
import java.time.Clock
import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZonedDateTime

/** Canned data resembling the approved mockups. Used by the fake repository, previews and screenshot tests. */
object FakeData {
    private const val MINUTE = 60_000L

    /** Three history rows as in the mockup: today 14:32, yesterday 12:48, and 23 days earlier at 16:05 (08 Oct when today is 31 Oct). */
    fun history(clock: Clock): List<HistoryItem> {
        val zone: ZoneId = clock.zone
        val today = LocalDate.now(clock)
        fun at(date: LocalDate, time: LocalTime) = ZonedDateTime.of(date, time, zone).toInstant().toEpochMilli()
        return listOf(
            item("Fake A", at(today, LocalTime.of(14, 32)), 20, 20, 14 * MINUTE + 32_000, 15 * MINUTE, 1.37),
            item("Fake B", at(today.minusDays(1), LocalTime.of(12, 48)), 18, 18, 11 * MINUTE + 30_000, 12 * MINUTE, 1.56),
            item("Fake C", at(today.minusDays(23), LocalTime.of(16, 5)), 15, 15, 12 * MINUTE, 10 * MINUTE, 1.25),
        )
    }

    fun item(
        name: String,
        finishedAt: Long,
        plannedSlides: Int,
        registered: Int,
        actualMs: Long,
        plannedMs: Long,
        pace: Double,
        finalState: FinalState = FinalState.FinishedManually,
    ) = HistoryItem(
        name = name,
        finishedAtEpochMs = finishedAt,
        plannedDurationMs = plannedMs,
        actualDurationMs = actualMs,
        plannedSlides = plannedSlides,
        registeredSlides = registered,
        averageSlidesPerMinute = pace,
        finalState = finalState,
        endedWithinPlannedTime = actualMs <= plannedMs,
    )

    /** Mockup 03: slide 7 of 20, 08:32 elapsed, 06:28 remaining, good pace. */
    val controlNormal = snapshot(
        slide = 7,
        elapsedMs = 8 * MINUTE + 32_000,
        plannedMs = 15 * MINUTE,
        pace = PaceStatus.OnTrack,
        recommendation = RecommendationCode.OnTrack,
    )

    val controlPaused = controlNormal.copy(state = SessionState.Paused, running = false)

    /** Mockup 04: five minutes left, nine slides pending, gentle speed-up advice. */
    val controlAlert = snapshot(
        slide = 11,
        elapsedMs = 10 * MINUTE,
        plannedMs = 15 * MINUTE,
        pace = PaceStatus.Behind,
        recommendation = RecommendationCode.SpeedUpSlightly,
        alert = Alert(AlertKind.TimeRunningOut, AlertSeverity.Warning, 10 * MINUTE, 5 * MINUTE, 9, -2),
    )

    val controlOvertimeAlert = snapshot(
        slide = 17,
        elapsedMs = 16 * MINUTE + 10_000,
        plannedMs = 15 * MINUTE,
        pace = PaceStatus.Behind,
        recommendation = RecommendationCode.Overtime,
        alert = Alert(AlertKind.Overtime, AlertSeverity.Critical, 15 * MINUTE + 1_000, -(MINUTE + 10_000), 3, -3),
    )

    /** Overtime after the alert was acknowledged. */
    val controlOvertime = controlOvertimeAlert.copy(activeAlert = null)

    val controlBehindAlert = snapshot(
        slide = 4,
        elapsedMs = 5 * MINUTE,
        plannedMs = 15 * MINUTE,
        pace = PaceStatus.Behind,
        recommendation = RecommendationCode.SpeedUpNow,
        alert = Alert(AlertKind.BehindSchedule, AlertSeverity.Warning, 5 * MINUTE, 10 * MINUTE, 16, -5),
    )

    val controlCalculating = snapshot(
        slide = 1,
        elapsedMs = 20_000,
        plannedMs = 15 * MINUTE,
        pace = PaceStatus.NotEnoughData,
        recommendation = null,
    )

    private fun snapshot(
        slide: Int,
        elapsedMs: Long,
        plannedMs: Long,
        pace: PaceStatus,
        recommendation: RecommendationCode?,
        alert: Alert? = null,
        total: Int = 20,
    ) = SessionSnapshot(
        state = SessionState.Running,
        name = "Fake session",
        currentSlide = slide,
        totalSlides = total,
        plannedDurationMs = plannedMs,
        elapsedMs = elapsedMs,
        remainingMs = plannedMs - elapsedMs,
        running = true,
        progressRatio = slide.toFloat() / total,
        paceStatus = pace,
        slidesRemaining = total - slide,
        averageSlidesPerMinute = 0.0,
        finalState = null,
        activeAlert = alert,
        recommendation = recommendation?.let { Recommendation(it, total - slide, plannedMs - elapsedMs) },
    )

    /** Mockup 05: 14:32 for 20 slides, average 1.37, within the planned 15 minutes. */
    val summaryWithinTime = item("Fake", 0, 20, 20, 14 * MINUTE + 32_000, 15 * MINUTE, 1.37)

    /** Finished 02:05 after the planned time. */
    val summaryOverTime = item("Fake", 0, 20, 20, 12 * MINUTE + 5_000, 10 * MINUTE, 1.65)

    val summaryInterrupted = item(
        "Fake", 0, 20, 11, 9 * MINUTE + 3_000, 15 * MINUTE, 1.21,
        finalState = FinalState.InterruptedByDisconnection,
    )
}
