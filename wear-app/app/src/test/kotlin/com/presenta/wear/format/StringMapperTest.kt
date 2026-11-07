package com.presenta.wear.format

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PaceStatus
import com.presenta.wear.model.RecommendationCode
import java.time.LocalDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class StringMapperTest {
    private val mapper = StringMapper(ApplicationProvider.getApplicationContext<Context>().resources)

    private fun alert(kind: AlertKind, remainingMs: Long = 0) =
        Alert(kind, AlertSeverity.Warning, raisedAtElapsedMs = 0, remainingMs = remainingMs, slidesRemaining = 0, slideDeviation = 0)

    private fun assertText(text: String) {
        assertTrue("empty text", text.isNotBlank())
        assertFalse("unformatted placeholder in '$text'", text.contains("%"))
    }

    @Test
    fun every_connection_state_maps_to_text() {
        ConnectionState.entries.forEach { assertText(mapper.connectionStatus(it)) }
        assertEquals("Conectado", mapper.connectionStatus(ConnectionState.Connected))
        assertEquals("Reconectando…", mapper.connectionStatus(ConnectionState.Reconnecting))
        assertEquals("Sin conexión", mapper.connectionStatus(ConnectionState.Disconnected))
    }

    @Test
    fun every_recommendation_code_maps_to_text() {
        RecommendationCode.entries.forEach { assertText(mapper.recommendation(it)) }
        assertEquals("Buen ritmo", mapper.recommendation(RecommendationCode.OnTrack))
        assertEquals("Acelera ligeramente", mapper.recommendation(RecommendationCode.SpeedUpSlightly))
        assertEquals("Acelera ahora", mapper.recommendation(RecommendationCode.SpeedUpNow))
        assertEquals("Reduce el ritmo", mapper.recommendation(RecommendationCode.SlowDown))
        assertEquals("Ve cerrando", mapper.recommendation(RecommendationCode.WrapUp))
        assertEquals("Termina cuanto antes", mapper.recommendation(RecommendationCode.Overtime))
    }

    @Test
    fun every_error_code_maps_to_text() {
        ErrorCode.entries.forEach { assertText(mapper.error(it)) }
        assertEquals("Código incorrecto", mapper.error(ErrorCode.InvalidPairingCode))
        assertEquals("Demasiados intentos. Espera un momento", mapper.error(ErrorCode.PairingLockedOut))
        assertEquals("No se pudo enviar el comando", mapper.error(ErrorCode.InvalidMessage))
        assertEquals("Ahora no se puede hacer eso", mapper.error(ErrorCode.InvalidState))
        assertEquals("Esa diapositiva no existe", mapper.error(ErrorCode.SlideOutOfRange))
        assertEquals("No hay una presentación activa", mapper.error(ErrorCode.NoActiveSession))
        assertEquals("Elige la ventana de la presentación en el computador", mapper.error(ErrorCode.TargetWindowUnavailable))
        assertEquals("No se pudo enviar el comando a la presentación", mapper.error(ErrorCode.KeyDeliveryFailed))
    }

    @Test
    fun error_texts_are_all_different() {
        val texts = ErrorCode.entries.map(mapper::error)
        assertEquals(texts.size, texts.toSet().size)
    }

    @Test
    fun pace_pill_prefers_the_recommendation_and_falls_back_to_the_status() {
        assertEquals("Acelera ahora", mapper.pacePill(PaceStatus.Behind, RecommendationCode.SpeedUpNow))
        assertEquals("Calculando ritmo", mapper.pacePill(PaceStatus.NotEnoughData, null))
        assertEquals("Buen ritmo", mapper.pacePill(PaceStatus.OnTrack, null))
    }

    @Test
    fun every_alert_kind_has_a_headline_and_a_supporting_line() {
        AlertKind.entries.forEach { kind ->
            val alert = alert(kind, remainingMs = 300_000)
            assertText(mapper.alertHeadline(alert))
            assertText(mapper.alertSupporting(alert, liveRemainingMs = -70_000, liveSlidesRemaining = 9))
        }
    }

    @Test
    fun time_alerts_show_minutes_with_singular_and_plural_forms() {
        assertEquals("Quedan 5 min", mapper.alertHeadline(alert(AlertKind.TimeRunningOut, 300_000)))
        assertEquals("Quedan 5 min", mapper.alertHeadline(alert(AlertKind.TimeRunningOut, 299_001)))
        assertEquals("Queda 1 min", mapper.alertHeadline(alert(AlertKind.TimeCritical, 60_000)))
        assertEquals("Quedan 2 min", mapper.alertHeadline(alert(AlertKind.TimeRunningOut, 61_000)))
    }

    @Test
    fun time_alerts_show_seconds_below_one_minute() {
        assertEquals("Quedan 30 s", mapper.alertHeadline(alert(AlertKind.TimeCritical, 30_000)))
        assertEquals("Queda 1 s", mapper.alertHeadline(alert(AlertKind.TimeCritical, 1_000)))
        assertEquals("Queda 1 s", mapper.alertHeadline(alert(AlertKind.TimeCritical, 0)))
    }

    @Test
    fun pace_and_overtime_headlines() {
        assertEquals("Vas atrasado", mapper.alertHeadline(alert(AlertKind.BehindSchedule)))
        assertEquals("Vas muy rápido", mapper.alertHeadline(alert(AlertKind.TooFast)))
        assertEquals("Tiempo agotado", mapper.alertHeadline(alert(AlertKind.Overtime)))
    }

    @Test
    fun overtime_supporting_line_uses_the_live_overtime() {
        assertEquals(
            "Llevas 01:10 de más",
            mapper.alertSupporting(alert(AlertKind.Overtime), liveRemainingMs = -70_000, liveSlidesRemaining = 3),
        )
    }

    @Test
    fun pending_slides_are_plural_aware() {
        assertEquals("Tienes 9 diapositivas pendientes", mapper.slidesPending(9))
        assertEquals("Tienes 1 diapositiva pendiente", mapper.slidesPending(1))
        assertEquals("Ya estás en la última diapositiva", mapper.slidesPending(0))
        assertEquals(
            "Tienes 9 diapositivas pendientes",
            mapper.alertSupporting(alert(AlertKind.TimeRunningOut), liveRemainingMs = 300_000, liveSlidesRemaining = 9),
        )
    }

    @Test
    fun slide_counts_are_plural_aware() {
        assertEquals("20 diapositivas", mapper.slidesCount(20))
        assertEquals("1 diapositiva", mapper.slidesCount(1))
    }

    @Test
    fun day_labels_follow_the_mockup() {
        assertEquals("Hoy", mapper.dayLabel(DayLabel.Today))
        assertEquals("Ayer", mapper.dayLabel(DayLabel.Yesterday))
        assertEquals("08 Oct", mapper.dayLabel(DayLabel.Date(8, 9)))
        assertEquals("31 Dic", mapper.dayLabel(DayLabel.Date(31, 11)))
        assertEquals("01 Ene", mapper.dayLabel(DayLabel.Date(1, 0)))
    }

    @Test
    fun every_month_has_a_label() {
        (0..11).forEach { assertText(mapper.dayLabel(DayLabel.Date(15, it))) }
    }

    @Test
    fun session_name_is_built_from_a_resource() {
        assertEquals("Presentación 31 oct", mapper.sessionName(LocalDate.of(2026, 10, 31)))
        assertEquals("Presentación 5 ene", mapper.sessionName(LocalDate.of(2027, 1, 5)))
    }
}
