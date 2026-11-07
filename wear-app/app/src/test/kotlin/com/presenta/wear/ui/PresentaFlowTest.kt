package com.presenta.wear.ui

import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.longClick
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipe
import com.presenta.wear.AppContainer
import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.SCREEN_TAG
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.navigation.PresentaApp
import kotlinx.coroutines.flow.MutableSharedFlow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** End-to-end flows of the whole app on fake data (no emulator, no network). All text is Spanish. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class PresentaFlowTest {
    @get:Rule
    val rule = createComposeRule()

    private val clock = FakeMonotonicClock()
    private val repository = FakePresentaRepository(clock, TestClocks.wall)
    private val ticks = MutableSharedFlow<Unit>(extraBufferCapacity = 8)
    private val container = AppContainer(repository, clock, TestClocks.wall, ticks)

    private fun launch() {
        rule.setContent { RoundHarness(LARGE_ROUND_DP, fontScale = 1f) { PresentaApp(container) } }
        rule.waitForIdle()
    }

    private fun text(value: String) = rule.onNodeWithText(value)

    private fun description(value: String, substring: Boolean = false) =
        rule.onNodeWithContentDescription(value, substring = substring)

    private fun tick(ms: Long) {
        clock.advance(ms)
        ticks.tryEmit(Unit)
        rule.waitForIdle()
    }

    private fun snapshot() = (repository.session.value as SessionStatus.Active).snapshot

    private fun swipeUpOnControl() {
        rule.onNodeWithTag(SCREEN_TAG).performTouchInput {
            swipe(start = Offset(centerX, height * 0.75f), end = Offset(centerX, height * 0.25f), durationMillis = 200)
        }
        rule.waitForIdle()
    }

    private fun startSessionFromSetup() {
        text("Nueva presentación").performClick()
        rule.waitForIdle()
        text("Configura tu exposición").assertExists()
        text("Iniciar").performClick()
        rule.waitForIdle()
    }

    @Test
    fun full_flow_home_setup_control_summary_history_and_back() {
        launch()

        // Home (connected)
        text("Presenta").assertExists()
        text("PC conectado").assertExists()
        text("Historial").assertExists()

        // Setup: defaults 20 slides / 15 minutes, change slides to 21
        text("Nueva presentación").performClick()
        rule.waitForIdle()
        text("Configura tu exposición").assertExists()
        description("20 diapositivas").assertExists()
        description("15 minutos").assertExists()
        description("Más diapositivas").performClick()
        rule.waitForIdle()
        description("21 diapositivas").assertExists()
        text("Iniciar").performClick()
        rule.waitForIdle()

        // Control: counter, time, pace, navigation
        description("Diapositiva 1 de 21", substring = true).assertExists()
        assertEquals(21, snapshot().totalSlides)
        assertEquals(15 * 60_000L, snapshot().plannedDurationMs)
        assertEquals("Presentación 31 oct", snapshot().name)
        text("Conectado").assertExists()
        text("Quedan 15:00").assertExists()
        text("Calculando ritmo").assertExists()

        // The display clock follows the monotonic clock
        tick(10_000)
        text("00:10").assertExists()
        text("Quedan 14:50").assertExists()

        // Next and previous
        description("Siguiente diapositiva").performClick()
        rule.waitForIdle()
        description("Diapositiva 2 de 21", substring = true).assertExists()
        description("Diapositiva anterior").performClick()
        rule.waitForIdle()
        description("Diapositiva 1 de 21", substring = true).assertExists()

        // Slide selector
        description("Diapositiva 1 de 21", substring = true).performClick()
        rule.waitForIdle()
        text("Ir a diapositiva").assertExists()
        description("Siguiente número").performClick()
        description("Siguiente número").performClick()
        rule.waitForIdle()
        description("Diapositiva 3 de 21").assertExists()
        text("Listo").performClick()
        rule.waitForIdle()
        description("Diapositiva 3 de 21", substring = true).assertExists()
        assertEquals(3, snapshot().currentSlide)

        // Pause and resume with a long press on the clock
        rule.onNode(hasContentDescription("Tiempo transcurrido", substring = true)).performTouchInput { longClick() }
        rule.waitForIdle()
        text("En pausa").assertExists()
        assertEquals(false, snapshot().running)
        rule.onNode(hasContentDescription("Tiempo transcurrido", substring = true)).performTouchInput { longClick() }
        rule.waitForIdle()
        text("En pausa").assertDoesNotExist()
        assertTrue(snapshot().running)

        // Alert state: shown on Control, Next and Previous stay enabled and working
        repository.raiseAlert(FakeData.controlAlert.activeAlert)
        rule.waitForIdle()
        text("Quedan 5 min").assertExists()
        text("Entendido").assertExists()
        description("Siguiente diapositiva").assertIsEnabled().assertHasClickAction()
        description("Diapositiva anterior").assertIsEnabled().assertHasClickAction()
        description("Siguiente diapositiva").performClick()
        rule.waitForIdle()
        assertEquals(4, snapshot().currentSlide)
        text("Entendido").performClick()
        rule.waitForIdle()
        text("Entendido").assertDoesNotExist()

        // Finish: swipe up opens the confirmation; cancel returns, confirm ends the session
        swipeUpOnControl()
        text("¿Finalizar presentación?").assertExists()
        text("Cancelar").performClick()
        rule.waitForIdle()
        text("¿Finalizar presentación?").assertDoesNotExist()
        description("Siguiente diapositiva").assertExists()
        swipeUpOnControl()
        text("Finalizar").performClick()
        rule.waitForIdle()

        // Summary (the companion has already saved it; the button only navigates)
        text("Presentación finalizada").assertExists()
        text("DURACIÓN REAL").assertExists()
        text("4 diapositivas").assertExists()
        text("Terminaste dentro del tiempo previsto").assertExists()
        text("Guardar y volver al inicio").performClick()
        rule.waitForIdle()
        text("Nueva presentación").assertExists()
        assertEquals(SessionStatus.NoSession, repository.session.value)

        // History: the new record, the fake ones, and the read-only summary
        text("Historial").performClick()
        rule.waitForIdle()
        text("Ayer").assertExists()
        text("08 Oct").assertExists()
        rule.onAllNodes(hasContentDescription("Hoy", substring = true)).assertCountEquals(2)
        description("Ayer", substring = true).performClick()
        rule.waitForIdle()
        text("Presentación finalizada").assertExists()
        text("Volver").performClick()
        rule.waitForIdle()
        text("Ayer").assertExists()
    }

    @Test
    fun empty_history_shows_the_empty_message_and_a_way_back() {
        repository.setHistory(emptyList())
        launch()

        text("Historial").performClick()
        rule.waitForIdle()

        text("Aún no hay presentaciones guardadas").assertExists()
        description("Volver").performClick()
        rule.waitForIdle()
        text("Nueva presentación").assertExists()
    }

    @Test
    fun disconnected_home_retries_and_recovers() {
        repository.setConnection(ConnectionState.Disconnected)
        launch()

        text("Sin conexión al computador").assertExists()
        text("Verifica que la aplicación de Windows esté abierta").assertExists()
        text("Nueva presentación").assertDoesNotExist()

        text("Reintentar").performClick()
        rule.waitForIdle()
        assertEquals(1, repository.retryRequests)
        text("Reconectando…").assertExists()
        text("Buscando el computador").assertExists()

        repository.setConnection(ConnectionState.Connected)
        rule.waitForIdle()
        text("PC conectado").assertExists()
    }

    @Test
    fun a_failed_command_shows_a_clear_message_without_ending_the_session() {
        launch()
        startSessionFromSetup()
        repository.scriptPace(com.presenta.wear.model.PaceStatus.OnTrack)
        rule.waitForIdle()
        text("Buen ritmo").assertExists()

        repository.failNextCommandWith(ErrorCode.InvalidState)
        description("Siguiente diapositiva").performClick()
        rule.waitForIdle()

        text("Ahora no se puede hacer eso").assertExists()
        assertTrue(repository.session.value is SessionStatus.Active)
        assertEquals(1, snapshot().currentSlide)
    }

    @Test
    fun losing_the_connection_during_a_session_keeps_the_controls_and_says_so() {
        launch()
        startSessionFromSetup()

        repository.setConnection(ConnectionState.Reconnecting)
        rule.waitForIdle()

        text("Reconectando…").assertExists()
        description("Siguiente diapositiva").assertIsEnabled()
        assertTrue(repository.session.value is SessionStatus.Active)
    }

    @Test
    fun a_session_interrupted_by_disconnection_shows_the_interrupted_summary() {
        launch()
        startSessionFromSetup()

        repository.interruptSession()
        rule.waitForIdle()

        text("La conexión se perdió antes de terminar").assertExists()
        text("Guardar y volver al inicio").performClick()
        rule.waitForIdle()
        text("Nueva presentación").assertExists()
    }

    @Test
    fun a_session_that_ran_over_time_says_by_how_much() {
        launch()
        startSessionFromSetup()
        tick(17 * 60_000L) // planned 15:00, so 02:00 over

        swipeUpOnControl()
        text("Finalizar").performClick()
        rule.waitForIdle()

        text("Terminaste 02:00 después del tiempo previsto").assertExists()
    }

    @Test
    fun overtime_alert_and_acknowledged_overtime_are_shown_on_control() {
        launch()
        startSessionFromSetup()
        tick(16 * 60_000L + 10_000)
        repository.raiseAlert(FakeData.controlOvertimeAlert.activeAlert)
        rule.waitForIdle()

        text("Tiempo agotado").assertExists()
        text("Llevas 01:10 de más").assertExists()
        description("Siguiente diapositiva").assertIsEnabled()

        text("Entendido").performClick()
        rule.waitForIdle()
        text("Pasaste 01:10").assertExists()
    }

    @Test
    fun the_finish_confirmation_also_works_from_a_paused_session() {
        launch()
        startSessionFromSetup()
        rule.onNode(hasContentDescription("Tiempo transcurrido", substring = true)).performTouchInput { longClick() }
        rule.waitForIdle()
        text("En pausa").assertExists()

        swipeUpOnControl()
        text("Finalizar").performClick()
        rule.waitForIdle()

        text("Presentación finalizada").assertExists()
    }
}
