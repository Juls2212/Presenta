package com.presenta.wear.protocol

import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.FinalState
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.Recommendation
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.model.SessionSnapshot
import com.presenta.wear.model.SessionState

/**
 * Converts wire models to the UI models. Enum names are identical on both sides (a test compares them with the
 * schemas), so conversion is by name. No pace or alert rules live here: the values come from the companion.
 */
object WireMapper {
    fun WireErrorCode.toModel(): ErrorCode = ErrorCode.valueOf(name)

    fun SessionPayload.toModel(): SessionSnapshot = SessionSnapshot(
        state = SessionState.valueOf(state.name),
        name = name,
        currentSlide = currentSlide,
        totalSlides = totalSlides,
        plannedDurationMs = plannedDurationMs,
        elapsedMs = elapsedMs,
        remainingMs = remainingMs,
        running = running,
        progressRatio = progressRatio.toFloat(),
        paceStatus = PaceStatus.valueOf(paceStatus.name),
        slidesRemaining = slidesRemaining,
        averageSlidesPerMinute = averageSlidesPerMinute,
        finalState = finalState?.let { FinalState.valueOf(it.name) },
        activeAlert = activeAlert?.toModel(),
        recommendation = recommendation?.toModel(),
    )

    fun AlertPayload.toModel(): Alert = Alert(
        kind = AlertKind.valueOf(kind.name),
        severity = AlertSeverity.valueOf(severity.name),
        raisedAtElapsedMs = raisedAtElapsedMs,
        remainingMs = remainingMs,
        slidesRemaining = slidesRemaining,
        slideDeviation = slideDeviation,
    )

    fun RecommendationPayload.toModel(): Recommendation = Recommendation(
        code = RecommendationCode.valueOf(code.name),
        slidesRemaining = slidesRemaining,
        timeRemainingMs = timeRemainingMs,
    )

    fun HistoryItemPayload.toModel(): HistoryItem = HistoryItem(
        name = name,
        finishedAtEpochMs = finishedAtUtc.toEpochMilli(),
        plannedDurationMs = plannedDurationMs,
        actualDurationMs = actualDurationMs,
        plannedSlides = plannedSlides,
        registeredSlides = registeredSlides,
        averageSlidesPerMinute = averageSlidesPerMinute,
        finalState = FinalState.valueOf(finalState.name),
        endedWithinPlannedTime = endedWithinPlannedTime,
    )
}
