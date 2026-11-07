package com.presenta.wear.ui

import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performRotaryScrollInput
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PairingState
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.LARGE_ROUND_DP
import com.presenta.wear.support.LayoutChecks
import com.presenta.wear.support.RoundHarness
import com.presenta.wear.support.SCREEN_TAG
import com.presenta.wear.support.SMALL_ROUND_DP
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.screens.pairing.PairingActions
import com.presenta.wear.ui.screens.pairing.PairingScreen
import com.presenta.wear.ui.theme.PresentaTheme
import com.presenta.wear.ui.viewmodel.PairingViewModel
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

/** The pairing screen: digit picker, focus order, targets, messages. All text is Spanish. */
@OptIn(ExperimentalTestApi::class)
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w480dp-h480dp-xhdpi")
class PairingUiTest {
    @get:Rule
    val rule = createComposeRule()

    private val repository = FakePresentaRepository(FakeMonotonicClock(), TestClocks.wall).also { it.setPairing(PairingState.NeedsCode) }

    private fun show(sizeDp: Int = LARGE_ROUND_DP, fontScale: Float = 1f): PairingViewModel {
        val viewModel = PairingViewModel(repository)
        rule.setContent {
            RoundHarness(sizeDp, fontScale) {
                PresentaTheme {
                    val state by viewModel.state.collectAsState()
                    PairingScreen(
                        state,
                        PairingActions(
                            onIncrease = viewModel::increase,
                            onDecrease = viewModel::decrease,
                            onPreviousDigit = viewModel::previousDigit,
                            onNextOrConnect = viewModel::nextOrConnect,
                        ),
                    )
                }
            }
        }
        rule.waitForIdle()
        return viewModel
    }

    private fun click(label: String) {
        rule.onNodeWithContentDescription(label).performClick()
        rule.waitForIdle()
    }

    @Test
    fun the_screen_starts_on_the_first_digit_with_the_hint() {
        show()

        rule.onNodeWithText("Vincular").assertExists()
        rule.onNodeWithText("Escribe el código del computador").assertExists()
        rule.onNodeWithContentDescription("Dígito 1 de 6: 0").assertExists()
        rule.onNodeWithText("Siguiente").assertExists()
    }

    @Test
    fun a_code_is_entered_digit_by_digit_and_connected_on_the_last_step() {
        show()
        // 4 9 2 9 1 3
        repeat(4) { click("Subir dígito") }
        rule.onNodeWithContentDescription("Dígito 1 de 6: 4").assertExists()
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()

        click("Bajar dígito") // 0 wraps down to 9
        rule.onNodeWithContentDescription("Dígito 2 de 6: 9").assertExists()
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()

        repeat(2) { click("Subir dígito") }
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()
        repeat(9) { click("Subir dígito") }
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()
        click("Subir dígito")
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()

        rule.onNodeWithContentDescription("Dígito 6 de 6: 0").assertExists()
        repeat(3) { click("Subir dígito") }
        rule.onNodeWithText("Siguiente").assertDoesNotExist()
        rule.onNodeWithText("Conectar").performClick()
        rule.waitForIdle()

        assertEquals("492913", repository.lastConnectCode)
    }

    @Test
    fun digits_wrap_around_in_both_directions() {
        show()

        click("Bajar dígito")
        rule.onNodeWithContentDescription("Dígito 1 de 6: 9").assertExists()
        click("Subir dígito")
        rule.onNodeWithContentDescription("Dígito 1 de 6: 0").assertExists()
    }

    @Test
    fun the_previous_digit_button_is_disabled_on_the_first_digit_and_goes_back_later() {
        show()
        rule.onNodeWithContentDescription("Dígito anterior").assertIsNotEnabled()

        click("Subir dígito")
        rule.onNodeWithText("Siguiente").performClick()
        rule.waitForIdle()
        rule.onNodeWithContentDescription("Dígito anterior").assertIsEnabled()
        rule.onNodeWithContentDescription("Dígito 2 de 6: 0").assertExists()

        click("Dígito anterior")
        rule.onNodeWithContentDescription("Dígito 1 de 6: 1").assertExists()
    }

    @Test
    fun the_crown_changes_the_selected_digit() {
        show()

        rule.onNodeWithTag(SCREEN_TAG).performRotaryScrollInput { rotateToScrollVertically(120f) }
        rule.waitForIdle()
        rule.onNodeWithContentDescription("Dígito 1 de 6: 1").assertExists()

        rule.onNodeWithTag(SCREEN_TAG).performRotaryScrollInput { rotateToScrollVertically(-120f) }
        rule.waitForIdle()
        rule.onNodeWithContentDescription("Dígito 1 de 6: 0").assertExists()
    }

    @Test
    fun the_focus_order_follows_the_reading_order() {
        show()

        val labels = rule.onAllNodesWithClick().map { node ->
            node.config.getOrNull(SemanticsProperties.ContentDescription)?.joinToString()
                ?: node.config.getOrNull(SemanticsProperties.Text)?.joinToString { it.text }
        }

        assertEquals(listOf("Bajar dígito", "Subir dígito", "Dígito anterior", "Siguiente"), labels)
    }

    @Test
    fun the_code_summary_and_the_selected_digit_have_spoken_descriptions() {
        show()

        rule.onNodeWithContentDescription("Código: 0 0 0 0 0 0. Dígito 1 de 6 seleccionado").assertExists()
        rule.onNodeWithContentDescription("Dígito 1 de 6: 0").assertExists()
    }

    @Test
    fun a_wrong_code_message_is_shown_and_the_code_can_be_edited_again() {
        show()
        repository.setPairing(PairingState.Rejected(ErrorCode.InvalidPairingCode))
        rule.waitForIdle()

        rule.onNodeWithText("Código incorrecto").assertExists()
        click("Subir dígito")
        rule.onNodeWithContentDescription("Dígito 1 de 6: 1").assertExists()
    }

    @Test
    fun a_lockout_message_is_shown() {
        show()

        repository.setPairing(PairingState.Rejected(ErrorCode.PairingLockedOut))
        rule.waitForIdle()

        rule.onNodeWithText("Demasiados intentos. Espera un momento").assertExists()
    }

    @Test
    fun while_connecting_the_message_says_so_and_the_button_is_disabled() {
        show()
        repeat(5) {
            rule.onNodeWithText("Siguiente").performClick()
            rule.waitForIdle()
        }

        repository.setPairing(PairingState.Connecting)
        rule.waitForIdle()

        rule.onNodeWithText("Conectando…").assertExists()
        rule.onNodeWithText("Conectar").assertIsNotEnabled()
    }

    @Test
    fun the_last_code_used_is_prefilled() {
        repository.connectWithCode("482913")
        repository.setPairing(PairingState.NeedsCode)

        show()

        rule.onNodeWithContentDescription("Código: 4 8 2 9 1 3. Dígito 1 de 6 seleccionado").assertExists()
    }

    @Test
    fun targets_labels_and_text_pass_the_round_screen_checks_on_large_round() = checkLayout(LARGE_ROUND_DP, 1f)

    @Test
    fun targets_labels_and_text_pass_the_round_screen_checks_at_a_larger_font() = checkLayout(LARGE_ROUND_DP, 1.3f)

    @Test
    fun targets_labels_and_text_pass_the_round_screen_checks_on_small_round() = checkLayout(SMALL_ROUND_DP, 1f)

    private fun checkLayout(size: Int, scale: Float) {
        show(size, scale)
        val problems = LayoutChecks.problems(rule, size)
        assertTrue("pairing @ ${size}dp x$scale:\n" + problems.joinToString("\n"), problems.isEmpty())
    }

    private fun androidx.compose.ui.test.junit4.ComposeContentTestRule.onAllNodesWithClick() =
        onAllNodes(hasClickAction()).fetchSemanticsNodes()
}
