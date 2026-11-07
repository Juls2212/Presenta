package com.presenta.wear.ui.viewmodel

import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.SessionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.screens.control.ControlUiState

/**
 * Turns the repository state into what Control draws. Pure and unit-tested. It interpolates elapsed time between
 * snapshots but applies no pace or alert rules: those come from the companion in the snapshot.
 */
object ControlStateFactory {
    fun build(
        connection: ConnectionState,
        status: SessionStatus,
        nowMs: Long,
        overlay: ControlOverlay,
        selectorSlide: Int,
        message: ErrorCode?,
    ): ControlUiState? {
        val active = status as? SessionStatus.Active ?: return null
        val snapshot = active.snapshot
        val sinceSnapshot = if (snapshot.running) (nowMs - active.receivedAtMs).coerceAtLeast(0) else 0
        // Whole seconds, so that elapsed + remaining always adds up to the planned duration on screen.
        val elapsedMs = ((snapshot.elapsedMs + sinceSnapshot) / 1000) * 1000
        return ControlUiState(
            connection = connection,
            currentSlide = snapshot.currentSlide,
            totalSlides = snapshot.totalSlides,
            elapsedMs = elapsedMs,
            remainingMs = snapshot.plannedDurationMs - elapsedMs,
            paused = snapshot.state == SessionState.Paused,
            // The ring shows elapsed time (not slide progress); the slide number stays textual.
            progress = if (snapshot.plannedDurationMs > 0) (elapsedMs.toFloat() / snapshot.plannedDurationMs).coerceIn(0f, 1f) else 0f,
            paceStatus = snapshot.paceStatus,
            recommendation = snapshot.recommendation?.code,
            alert = snapshot.activeAlert,
            slidesRemaining = snapshot.slidesRemaining,
            overlay = overlay,
            selectorSlide = selectorSlide,
            message = message,
        )
    }
}
