package com.presenta.wear.format

import android.content.res.Resources
import com.presenta.wear.R
import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.RecommendationCode
import java.time.LocalDate
import kotlin.math.abs

/**
 * The single place where structured codes and numeric parameters become Spanish text.
 * Every string comes from Android resources; plural-aware where a count is shown.
 */
class StringMapper(private val resources: Resources) {

    fun connectionStatus(state: ConnectionState): String = resources.getString(
        when (state) {
            ConnectionState.Connected -> R.string.status_connected
            ConnectionState.Reconnecting -> R.string.status_reconnecting
            ConnectionState.Disconnected -> R.string.status_disconnected
        },
    )

    fun recommendation(code: RecommendationCode): String = resources.getString(
        when (code) {
            RecommendationCode.OnTrack -> R.string.pace_on_track
            RecommendationCode.SpeedUpSlightly -> R.string.recommendation_speed_up_slightly
            RecommendationCode.SpeedUpNow -> R.string.recommendation_speed_up_now
            RecommendationCode.SlowDown -> R.string.recommendation_slow_down
            RecommendationCode.WrapUp -> R.string.recommendation_wrap_up
            RecommendationCode.Overtime -> R.string.recommendation_overtime
        },
    )

    /** Text of the pace pill: the recommendation when there is one, otherwise a text for the pace status. */
    fun pacePill(status: PaceStatus, recommendation: RecommendationCode?): String = when {
        recommendation != null -> recommendation(recommendation)
        status == PaceStatus.NotEnoughData -> resources.getString(R.string.pace_calculating)
        else -> resources.getString(R.string.pace_on_track)
    }

    /** Headline of an alert. Time alerts show the time that was left when they were raised. */
    fun alertHeadline(alert: Alert): String = when (alert.kind) {
        AlertKind.TimeRunningOut, AlertKind.TimeCritical -> timeLeft(alert.remainingMs)
        AlertKind.Overtime -> resources.getString(R.string.alert_time_up)
        AlertKind.BehindSchedule -> resources.getString(R.string.alert_behind)
        AlertKind.TooFast -> resources.getString(R.string.alert_too_fast)
    }

    /**
     * Supporting line of an alert, from live values: overtime shows how long the presenter is over time,
     * every other alert shows the pending slides.
     */
    fun alertSupporting(alert: Alert, liveRemainingMs: Long, liveSlidesRemaining: Int): String =
        if (alert.kind == AlertKind.Overtime) {
            resources.getString(R.string.alert_overtime_by, DurationFormat.clock(abs(liveRemainingMs)))
        } else {
            slidesPending(liveSlidesRemaining)
        }

    fun slidesPending(count: Int): String =
        if (count <= 0) {
            resources.getString(R.string.alert_last_slide)
        } else {
            resources.getQuantityString(R.plurals.slides_pending, count, count)
        }

    fun slidesCount(count: Int): String = resources.getQuantityString(R.plurals.summary_slides, count, count)

    fun error(code: ErrorCode): String = resources.getString(
        when (code) {
            ErrorCode.InvalidPairingCode -> R.string.error_invalid_pairing_code
            ErrorCode.PairingLockedOut -> R.string.error_pairing_locked_out
            ErrorCode.NotPaired -> R.string.error_not_paired
            ErrorCode.UnsupportedVersion -> R.string.error_unsupported_version
            ErrorCode.InvalidMessage -> R.string.error_invalid_message
            ErrorCode.InvalidState -> R.string.error_invalid_state
            ErrorCode.SlideOutOfRange -> R.string.error_slide_out_of_range
            ErrorCode.NoActiveSession -> R.string.error_no_active_session
            ErrorCode.TargetWindowUnavailable -> R.string.error_target_window_unavailable
            ErrorCode.KeyDeliveryFailed -> R.string.error_key_delivery_failed
            ErrorCode.InternalError -> R.string.error_internal
        },
    )

    fun dayLabel(label: DayLabel): String = when (label) {
        DayLabel.Today -> resources.getString(R.string.day_today)
        DayLabel.Yesterday -> resources.getString(R.string.day_yesterday)
        is DayLabel.Date -> resources.getString(
            R.string.day_month_format,
            label.day,
            monthShort(label.monthIndex).replaceFirstChar { it.uppercase() },
        )
    }

    /** Default session name built on the watch from a string resource: the day number and the short month. */
    fun sessionName(date: LocalDate): String =
        resources.getString(R.string.session_default_name, date.dayOfMonth, monthShort(date.monthValue - 1))

    private fun monthShort(index: Int): String = resources.getStringArray(R.array.month_short)[index]

    private fun timeLeft(remainingMs: Long): String {
        val safe = remainingMs.coerceAtLeast(0)
        return if (safe >= 60_000) {
            val minutes = ((safe + 59_999) / 60_000).toInt()
            resources.getQuantityString(R.plurals.alert_minutes_left, minutes, minutes)
        } else {
            val seconds = ((safe + 999) / 1000).toInt().coerceAtLeast(1)
            resources.getQuantityString(R.plurals.alert_seconds_left, seconds, seconds)
        }
    }
}
