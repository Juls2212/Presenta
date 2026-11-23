package com.presenta.wear.ui

import android.content.Context
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.core.app.ApplicationProvider
import com.presenta.wear.AppContainer
import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.format.StringMapper
import com.presenta.wear.haptics.HapticPattern
import com.presenta.wear.haptics.HapticsCoordinator
import com.presenta.wear.model.Alert
import com.presenta.wear.model.AlertKind
import com.presenta.wear.model.AlertSeverity
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.haptics.RecordingHapticsPlayer
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.navigation.PresentaApp
import com.presenta.wear.ui.screens.control.ControlAccent
import com.presenta.wear.ui.screens.control.ControlActions
import com.presenta.wear.ui.screens.control.ControlScreen
import com.presenta.wear.ui.screens.control.ControlUiState
import com.presenta.wear.ui.screens.control.accent
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import kotlinx.coroutines.flow.MutableSharedFlow
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/**
 * Every alert kind reaches the Control screen as a state of it (never a destination), with Next and Previous enabled,
 * an "Entendido" that acknowledges it, and red used for overtime only.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class AlertStatesTest {
    @get:Rule
    val rule = createComposeRule()

    private val mapper = StringMapper(ApplicationProvider.getApplicationContext<Context>().resources)

    private val kinds = listOf(
        Alert(AlertKind.TimeRunningOut, AlertSeverity.Warning, 600_000, 300_000, 9, -2, 1),
        Alert(AlertKind.TimeCritical, AlertSeverity.Critical, 840_000, 60_000, 3, -2, 2),
        Alert(AlertKind.Overtime, AlertSeverity.Critical, 901_000, -1_000, 3, -3, 3),
        Alert(AlertKind.BehindSchedule, AlertSeverity.Warning, 300_000, 600_000, 16, -5, 4),
        Alert(AlertKind.TooFast, AlertSeverity.Warning, 120_000, 780_000, 12, 4, 5),
    )

    private fun uiState(alert: Alert?, remainingMs: Long = 300_000) = ControlUiState(
        connection = ConnectionState.Connected, currentSlide = 7, totalSlides = 20, elapsedMs = 900_000 - remainingMs,
        remainingMs = remainingMs, paused = false, progress = 0.5f,
        paceStatus = com.presenta.wear.model.PaceStatus.Behind, recommendation = null, alert = alert, slidesRemaining = 13,
    )

    private fun noActions(onAcknowledge: () -> Unit = {}) = ControlActions({}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {}, onAcknowledge)

    @Test
    fun each_alert_kind_is_a_state_of_control_with_navigation_enabled_and_an_acknowledge_button() {
        var current by androidx.compose.runtime.mutableStateOf(kinds.first())
        var acknowledged = 0
        rule.setContent {
            RoundHarness(LARGE_ROUND_DP, 1f) {
                ControlScreen(uiState(current, remainingMs = current.remainingMs), noActions { acknowledged++ })
            }
        }
        for (alert in kinds) {
            acknowledged = 0
            current = alert
            rule.waitForIdle()

            rule.onNodeWithText(mapper.alertHeadline(alert)).assertExists()
            rule.onNodeWithContentDescription("Siguiente diapositiva").assertIsEnabled().assertHasClickAction()
            rule.onNodeWithContentDescription("Diapositiva anterior").assertIsEnabled().assertHasClickAction()
            rule.onNodeWithText("Entendido").performClick()
            assertEquals("acknowledge for ${alert.kind}", 1, acknowledged)
        }
    }

    @Test
    fun red_is_used_for_overtime_only() {
        val accents = kinds.associate { it.kind to uiState(it, remainingMs = it.remainingMs).accent() }

        assertEquals(ControlAccent.Critical, accents.getValue(AlertKind.Overtime))
        for (kind in AlertKind.entries - AlertKind.Overtime) {
            assertNotEquals("$kind must not be red", ControlAccent.Critical, accents.getValue(kind))
            assertEquals(ControlAccent.Alert, accents.getValue(kind))
        }
        assertEquals(ControlAccent.Normal, uiState(null, remainingMs = 300_000).accent())
        assertEquals(ControlAccent.Critical, uiState(null, remainingMs = -5_000).accent()) // acknowledged overtime stays red
    }

    @Test
    fun an_acknowledged_alert_does_not_come_back_with_later_snapshots_and_the_text_follows_each_snapshot() {
        val clock = FakeMonotonicClock()
        val repository = FakePresentaRepository(clock, TestClocks.wall)
        val ticks = MutableSharedFlow<Unit>(extraBufferCapacity = 8)
        val player = RecordingHapticsPlayer()
        val haptics = HapticsCoordinator(player)
        val container = AppContainer(repository, clock, TestClocks.wall, ticks, haptics)
        haptics.start(kotlinx.coroutines.CoroutineScope(kotlinx.coroutines.Dispatchers.Unconfined), repository.session)
        rule.setContent { RoundHarness(LARGE_ROUND_DP, 1f) { PresentaApp(container) } }
        rule.waitForIdle()
        rule.onNodeWithText("Nueva presentación").performClick()
        rule.waitForIdle()
        rule.onNodeWithText("Iniciar").performClick()
        rule.waitForIdle()

        // The recommendation and the pace pill come from the snapshot.
        rule.onNodeWithText("Calculando ritmo").assertExists()
        repository.scriptPace(com.presenta.wear.model.PaceStatus.OnTrack)
        rule.waitForIdle()
        rule.onNodeWithText("Buen ritmo").assertExists()
        rule.onNodeWithText("Calculando ritmo").assertDoesNotExist()

        val alert = FakeData.controlAlert.activeAlert!!.copy(sequence = 1)
        repository.raiseAlert(alert)
        rule.waitForIdle()
        rule.onNodeWithText("Entendido").assertExists()
        assertEquals(listOf(HapticPattern.Warning), player.played)

        rule.onNodeWithText("Entendido").performClick()
        rule.waitForIdle()
        rule.onNodeWithText("Entendido").assertDoesNotExist()
        repository.refresh()
        repository.scriptPace(com.presenta.wear.model.PaceStatus.OnTrack)
        rule.waitForIdle()
        rule.onNodeWithText("Entendido").assertDoesNotExist()
        assertEquals(1, player.played.size)
        assertEquals(true, repository.session.value is SessionStatus.Active)
    }
}
