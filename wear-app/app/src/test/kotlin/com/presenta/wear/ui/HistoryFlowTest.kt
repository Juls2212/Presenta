package com.presenta.wear.ui

import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import com.presenta.wear.AppContainer
import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.FinalState
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.navigation.PresentaApp
import java.time.Instant
import kotlinx.coroutines.flow.MutableSharedFlow
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** History as the mockup shows it: newest first, dates as "Hoy" / "Ayer" / "08 Oct", interrupted records, read-only summary. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class HistoryFlowTest {
    @get:Rule
    val rule = createComposeRule()

    private val clock = FakeMonotonicClock()
    private val repository = FakePresentaRepository(clock, TestClocks.wall)
    private val container = AppContainer(repository, clock, TestClocks.wall, MutableSharedFlow(extraBufferCapacity = 8))

    private fun launch() {
        rule.setContent { RoundHarness(LARGE_ROUND_DP, 1f) { PresentaApp(container) } }
        rule.waitForIdle()
    }

    private fun epoch(utc: String) = Instant.parse(utc).toEpochMilli()

    @Test
    fun rows_are_newest_first_and_dates_follow_the_local_day() {
        // Bogota is UTC-5. Today is 31 Oct, 15:00 local.
        repository.setHistory(
            listOf(
                FakeData.item("old", epoch("2026-10-08T21:05:00Z"), 15, 15, 720_000, 600_000, 1.25),
                FakeData.item("late", epoch("2026-10-31T19:32:00Z"), 20, 20, 872_000, 900_000, 1.37),
                FakeData.item("yesterday", epoch("2026-10-30T17:48:00Z"), 18, 18, 690_000, 720_000, 1.56),
            ),
        )
        launch()
        rule.onNodeWithText("Historial").performClick()
        rule.waitForIdle()

        val rows = rule.onAllNodes(hasContentDescription("diapositivas por minuto", substring = true))
        assertEquals(3, rows.fetchSemanticsNodes().size)
        val order = rows.fetchSemanticsNodes().map {
            it.config[androidx.compose.ui.semantics.SemanticsProperties.ContentDescription].joinToString(" ")
        }
        assertEquals(listOf("Hoy", "Ayer", "08 Oct"), order.map { text -> listOf("Hoy", "Ayer", "08 Oct").first { text.contains(it) } })
        rule.onNodeWithText("14:32").assertExists()
        rule.onNodeWithText("08 Oct").assertExists()
        rule.onNodeWithText("1.37 diap/min").assertExists()
    }

    @Test
    fun an_interrupted_record_opens_a_read_only_summary_that_says_so() {
        repository.setHistory(
            listOf(
                FakeData.item(
                    "cut", epoch("2026-10-31T18:00:00Z"), 20, 11, 543_000, 900_000, 1.21,
                    finalState = FinalState.InterruptedByDisconnection,
                ),
            ),
        )
        launch()
        rule.onNodeWithText("Historial").performClick()
        rule.waitForIdle()
        rule.onNodeWithContentDescription("Hoy", substring = true).performClick()
        rule.waitForIdle()

        rule.onNodeWithText("La conexión se perdió antes de terminar").assertExists()
        rule.onNodeWithText("Guardar y volver al inicio").assertDoesNotExist() // read-only: no save button
        rule.onNodeWithText("Volver").performClick()
        rule.waitForIdle()
        rule.onNodeWithText("Hoy", substring = true).assertExists()
    }
}
