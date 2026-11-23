package com.presenta.wear.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import android.view.View
import com.presenta.wear.AppContainer
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.components.KeepScreenOn
import com.presenta.wear.ui.navigation.PresentaApp
import kotlinx.coroutines.flow.MutableSharedFlow
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** The screen stays on only during a session (ADR-62): the view flag is set and cleared, and nothing else is used. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class KeepScreenOnTest {
    @get:Rule
    val rule = createComposeRule()

    @Test
    fun the_flag_follows_the_enabled_value_and_is_cleared_when_it_leaves_the_composition() {
        var enabled by mutableStateOf(false)
        var present by mutableStateOf(true)
        var view: View? = null
        rule.setContent {
            view = LocalView.current
            if (present) KeepScreenOn(enabled)
        }
        rule.waitForIdle()
        assertFalse(view!!.keepScreenOn)

        enabled = true
        rule.waitForIdle()
        assertTrue(view!!.keepScreenOn)

        enabled = false
        rule.waitForIdle()
        assertFalse(view!!.keepScreenOn)

        enabled = true
        rule.waitForIdle()
        assertTrue(view!!.keepScreenOn)
        present = false
        rule.waitForIdle()
        assertFalse(view!!.keepScreenOn)
    }

    @Test
    fun the_app_keeps_the_screen_on_from_session_start_until_the_summary() {
        val clock = FakeMonotonicClock()
        val repository = FakePresentaRepository(clock, TestClocks.wall)
        val container = AppContainer(repository, clock, TestClocks.wall, MutableSharedFlow(extraBufferCapacity = 8))
        var view: View? = null
        rule.setContent {
            view = LocalView.current
            RoundHarness(LARGE_ROUND_DP, 1f) { PresentaApp(container) }
        }
        rule.waitForIdle()
        assertFalse("home", view!!.keepScreenOn)

        rule.onNodeWithText("Nueva presentación").performClick()
        rule.waitForIdle()
        assertFalse("setup", view!!.keepScreenOn)

        rule.onNodeWithText("Iniciar").performClick()
        rule.waitForIdle()
        assertTrue("running", view!!.keepScreenOn)

        kotlinx.coroutines.runBlocking { repository.pause() }
        rule.waitForIdle()
        assertTrue("paused", view!!.keepScreenOn)

        kotlinx.coroutines.runBlocking { repository.finish() }
        rule.waitForIdle()
        rule.onNodeWithText("Presentación finalizada").assertExists()
        assertFalse("summary", view!!.keepScreenOn)

        rule.onNodeWithText("Guardar y volver al inicio").performClick()
        rule.waitForIdle()
        assertFalse("home again", view!!.keepScreenOn)
    }
}
