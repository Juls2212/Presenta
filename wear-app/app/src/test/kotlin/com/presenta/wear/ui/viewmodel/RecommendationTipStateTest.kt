package com.presenta.wear.ui.viewmodel

import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.Recommendation
import com.presenta.wear.model.RecommendationCode
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.ui.screens.control.ControlOverlay
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** The Control state prefers the companion's tip when there is a usable one and falls back to the code otherwise. */
class RecommendationTipStateTest {
    private fun stateWith(freeText: String?, code: RecommendationCode = RecommendationCode.SpeedUpNow) = ControlStateFactory.build(
        ConnectionState.Connected,
        SessionStatus.Active(FakeData.controlAlert.copy(recommendation = Recommendation(code, 9, 300_000, freeText)), receivedAtMs = 0),
        nowMs = 0,
        overlay = ControlOverlay.None,
        selectorSlide = 7,
        message = null,
    )!!

    @Test
    fun a_usable_tip_is_carried_to_the_state_and_the_code_is_kept_beside_it() {
        val state = stateWith("Acelera un poco")

        assertEquals("Acelera un poco", state.recommendationText)
        assertEquals(RecommendationCode.SpeedUpNow, state.recommendation)
    }

    @Test
    fun no_tip_means_no_text_so_the_screen_uses_the_wording_of_the_code() {
        assertNull(stateWith(null).recommendationText)
        assertNull(stateWith("   ").recommendationText)
        assertNull(stateWith("x".repeat(81)).recommendationText)
        assertNull(stateWith("Acelera\u0000").recommendationText)
        assertEquals(RecommendationCode.WrapUp, stateWith(null, RecommendationCode.WrapUp).recommendation)
    }

    @Test
    fun a_tip_with_line_breaks_is_made_into_one_line() {
        assertEquals("Acelera un poco", stateWith("Acelera\nun   poco").recommendationText)
    }

    @Test
    fun a_snapshot_without_a_recommendation_has_no_tip() {
        val state = ControlStateFactory.build(
            ConnectionState.Connected,
            SessionStatus.Active(FakeData.controlAlert.copy(recommendation = null), receivedAtMs = 0),
            0, ControlOverlay.None, 7, null,
        )!!

        assertNull(state.recommendationText)
        assertNull(state.recommendation)
    }
}
