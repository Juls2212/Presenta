package com.presenta.wear.ui

import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.test.assertRangeInfoEquals
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import com.presenta.wear.AppContainer
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PairingState
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.navigation.PresentaApp
import com.presenta.wear.ui.screens.control.ELAPSED_RING_TAG
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** The whole app on the fake repository: pairing screen routing, and the elapsed-time ring. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class PairingFlowTest {
    @get:Rule
    val rule = createComposeRule()

    private val clock = FakeMonotonicClock()
    private val repository = FakePresentaRepository(clock, TestClocks.wall)
    private val ticks = MutableSharedFlow<Unit>(extraBufferCapacity = 8)
    private val container = AppContainer(repository, clock, TestClocks.wall, ticks)

    private fun launch() {
        rule.setContent { RoundHarness(LARGE_ROUND_DP, 1f) { PresentaApp(container) } }
        rule.waitForIdle()
    }

    private fun click(description: String) {
        rule.onNodeWithContentDescription(description).performClick()
        rule.waitForIdle()
    }

    @Test
    fun without_a_stored_code_the_app_opens_on_the_pairing_screen_and_returns_home_once_paired() {
        repository.setPairing(PairingState.NeedsCode)
        repository.setConnection(ConnectionState.Disconnected)
        launch()

        rule.onNodeWithText("Vincular").assertExists()
        repeat(5) {
            rule.onNodeWithText("Siguiente").performClick()
            rule.waitForIdle()
        }
        click("Subir dígito")
        rule.onNodeWithText("Conectar").performClick()
        rule.waitForIdle()

        assertEquals("000001", repository.lastConnectCode)
        rule.onNodeWithText("Nueva presentación").assertExists()
        rule.onNodeWithText("Vincular").assertDoesNotExist()
    }

    @Test
    fun a_rejected_code_brings_the_pairing_screen_back_with_the_message() {
        launch()
        rule.onNodeWithText("Nueva presentación").assertExists()

        repository.setPairing(PairingState.Rejected(ErrorCode.InvalidPairingCode))
        rule.waitForIdle()

        rule.onNodeWithText("Vincular").assertExists()
        rule.onNodeWithText("Código incorrecto").assertExists()
    }

    @Test
    fun a_lockout_shows_its_message_on_the_pairing_screen() {
        launch()

        repository.setPairing(PairingState.Rejected(ErrorCode.PairingLockedOut))
        rule.waitForIdle()

        rule.onNodeWithText("Demasiados intentos. Espera un momento").assertExists()
    }

    @Test
    fun the_disconnected_screen_offers_the_pairing_screen_when_there_is_no_valid_code() {
        repository.setConnection(ConnectionState.Disconnected)
        launch()
        repository.setPairing(PairingState.Rejected(ErrorCode.InvalidPairingCode))
        rule.waitForIdle()
        rule.onNodeWithText("Código incorrecto").assertExists()
    }

    @Test
    fun the_ring_shows_elapsed_time_and_advances_with_the_clock() {
        launch()
        runBlocking { repository.startSession("Ring", 20, 600_000) }
        rule.waitForIdle()

        rule.onNodeWithTag(ELAPSED_RING_TAG).assertRangeInfoEquals(ProgressBarRangeInfo(0f, 0f..1f))

        clock.advance(150_000) // a quarter of ten minutes
        ticks.tryEmit(Unit)
        rule.waitForIdle()
        rule.onNodeWithTag(ELAPSED_RING_TAG).assertRangeInfoEquals(ProgressBarRangeInfo(0.25f, 0f..1f))

        runBlocking { repository.setSlide(20) } // the slide number does not move the ring
        rule.waitForIdle()
        rule.onNodeWithTag(ELAPSED_RING_TAG).assertRangeInfoEquals(ProgressBarRangeInfo(0.25f, 0f..1f))

        clock.advance(900_000) // far past the plan: clamped to a full ring
        ticks.tryEmit(Unit)
        rule.waitForIdle()
        rule.onNodeWithTag(ELAPSED_RING_TAG).assertRangeInfoEquals(ProgressBarRangeInfo(1f, 0f..1f))
    }

    @Test
    fun the_session_screen_stays_visible_and_says_reconnecting_when_the_connection_drops() {
        launch()
        runBlocking { repository.startSession("Ring", 20, 600_000) }
        rule.waitForIdle()

        repository.setConnection(ConnectionState.Reconnecting)
        rule.waitForIdle()
        rule.onNodeWithText("Reconectando…").assertExists()
        rule.onNodeWithContentDescription("Siguiente diapositiva").assertExists()

        repository.setConnection(ConnectionState.Connected)
        rule.waitForIdle()
        rule.onNodeWithText("Conectado").assertExists()
    }
}
