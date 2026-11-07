package com.presenta.wear.ui.viewmodel

import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.model.SetupLimits
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.TestClocks
import com.presenta.wear.ui.screens.control.ControlOverlay
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class ViewModelTests {
    private val clock = FakeMonotonicClock()
    private val repository = FakePresentaRepository(clock, TestClocks.wall)
    private val ticks = MutableSharedFlow<Unit>(extraBufferCapacity = 4)

    @Before
    fun setUp() {
        Dispatchers.setMain(UnconfinedTestDispatcher())
    }

    @After
    fun tearDown() {
        Dispatchers.resetMain()
    }

    // ---- setup ----

    @Test
    fun setup_starts_with_the_documented_defaults() {
        val state = SetupViewModel(repository).state.value

        assertEquals(20, state.slides)
        assertEquals(15, state.minutes)
        assertEquals(SetupLimits.DEFAULT_SLIDES, state.slides)
        assertEquals(SetupLimits.DEFAULT_MINUTES, state.minutes)
    }

    @Test
    fun setup_steppers_stop_at_their_limits() {
        val vm = SetupViewModel(repository)

        repeat(500) { vm.increaseSlides() }
        repeat(500) { vm.increaseMinutes() }
        assertEquals(200, vm.state.value.slides)
        assertEquals(180, vm.state.value.minutes)
        assertFalse(vm.state.value.canIncreaseSlides)
        assertFalse(vm.state.value.canIncreaseMinutes)

        repeat(500) { vm.decreaseSlides() }
        repeat(500) { vm.decreaseMinutes() }
        assertEquals(1, vm.state.value.slides)
        assertEquals(1, vm.state.value.minutes)
        assertFalse(vm.state.value.canDecreaseSlides)
        assertFalse(vm.state.value.canDecreaseMinutes)
    }

    @Test
    fun setup_start_sends_the_configuration_and_reports_success() = runTest(UnconfinedTestDispatcher()) {
        val vm = SetupViewModel(repository)
        vm.increaseSlides()
        var started = false

        vm.start("Test session") { started = true }

        assertTrue(started)
        val snapshot = (repository.session.value as SessionStatus.Active).snapshot
        assertEquals(21, snapshot.totalSlides)
        assertEquals(15 * 60_000L, snapshot.plannedDurationMs)
        assertEquals("Test session", snapshot.name)
        assertFalse(vm.state.value.starting)
    }

    @Test
    fun setup_start_failure_keeps_the_error_for_the_screen() = runTest(UnconfinedTestDispatcher()) {
        repository.failNextCommandWith(ErrorCode.InvalidMessage)
        val vm = SetupViewModel(repository)
        var started = false

        vm.start("x") { started = true }

        assertFalse(started)
        assertEquals(ErrorCode.InvalidMessage, vm.state.value.error)
        vm.increaseMinutes()
        assertNull(vm.state.value.error)
    }

    // ---- control ----

    private fun TestScope.controlViewModel(): ControlViewModel {
        val vm = ControlViewModel(repository, clock, ticks)
        backgroundScope.launch { vm.state.collect {} }
        return vm
    }

    @Test
    fun control_has_no_state_without_a_session() = runTest(UnconfinedTestDispatcher()) {
        assertNull(controlViewModel().state.value)
    }

    @Test
    fun control_exposes_the_started_session() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 20, 15 * 60_000L)

        val state = need(vm.state.value)
        assertEquals(1, state.currentSlide)
        assertEquals(20, state.totalSlides)
        assertEquals(15 * 60_000L, state.remainingMs)
        assertFalse(state.paused)
    }

    private fun <T : Any> need(value: T?): T = value ?: error("value was null")

    @Test
    fun control_interpolates_the_clock_between_snapshots() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 20, 15 * 60_000L)

        clock.advance(8 * 60_000L + 32_500)
        ticks.emit(Unit)

        val state = need(vm.state.value)
        assertEquals(8 * 60_000L + 32_000, state.elapsedMs)
        assertEquals(6 * 60_000L + 28_000, state.remainingMs)
    }

    @Test
    fun control_commands_reach_the_repository() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 20, 15 * 60_000L)

        vm.next()
        vm.next()
        vm.previous()
        assertEquals(2, need(vm.state.value).currentSlide)

        vm.togglePause()
        assertTrue(need(vm.state.value).paused)
        vm.togglePause()
        assertFalse(need(vm.state.value).paused)
    }

    @Test
    fun control_selector_moves_within_bounds_and_confirms() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 5, 10 * 60_000L)

        vm.openSelector()
        assertEquals(ControlOverlay.SlideSelector, need(vm.state.value).overlay)
        repeat(10) { vm.selectorIncrease() }
        assertEquals(5, need(vm.state.value).selectorSlide)
        repeat(10) { vm.selectorDecrease() }
        assertEquals(1, need(vm.state.value).selectorSlide)
        vm.selectorIncrease()
        vm.selectorIncrease()
        vm.selectorConfirm()

        val state = need(vm.state.value)
        assertEquals(ControlOverlay.None, state.overlay)
        assertEquals(3, state.currentSlide)
    }

    @Test
    fun control_selector_can_be_cancelled_without_changing_the_slide() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 5, 10 * 60_000L)

        vm.openSelector()
        vm.selectorIncrease()
        vm.selectorCancel()

        val state = need(vm.state.value)
        assertEquals(ControlOverlay.None, state.overlay)
        assertEquals(1, state.currentSlide)
    }

    @Test
    fun control_finish_needs_a_confirmation() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 5, 10 * 60_000L)

        vm.openFinish()
        assertEquals(ControlOverlay.FinishConfirmation, need(vm.state.value).overlay)
        vm.finishCancel()
        assertEquals(ControlOverlay.None, need(vm.state.value).overlay)
        assertTrue(repository.session.value is SessionStatus.Active)

        vm.openFinish()
        vm.finishConfirm()
        assertEquals(SessionStatus.NoSession, repository.session.value)
    }

    @Test
    fun control_shows_a_message_when_a_command_fails_and_clears_it() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 5, 10 * 60_000L)
        repository.failNextCommandWith(ErrorCode.InvalidState)

        vm.next()
        assertEquals(ErrorCode.InvalidState, need(vm.state.value).message)
        assertEquals(1, need(vm.state.value).currentSlide)

        vm.clearMessage()
        assertNull(need(vm.state.value).message)
    }

    @Test
    fun control_alert_is_part_of_the_state_and_navigation_stays_possible() = runTest(UnconfinedTestDispatcher()) {
        val vm = controlViewModel()
        repository.startSession("Demo", 20, 15 * 60_000L)
        repository.raiseAlert(FakeData.controlAlert.activeAlert)

        need(need(vm.state.value).alert)
        vm.next()
        assertEquals(2, need(vm.state.value).currentSlide)
        vm.acknowledgeAlert()
        assertNull(need(vm.state.value).alert)
    }

    // ---- history and app ----

    @Test
    fun history_loads_and_finds_items() = runTest(UnconfinedTestDispatcher()) {
        val vm = HistoryViewModel(repository)

        vm.load()

        val loaded = vm.state.value as HistoryLoadState.Loaded
        assertEquals(3, loaded.items.size)
        val first = loaded.items.first()
        assertEquals(first, vm.find(first.finishedAtEpochMs))
        assertNull(vm.find(-1))
    }

    @Test
    fun history_failure_is_reported() = runTest(UnconfinedTestDispatcher()) {
        repository.failHistoryWith(ErrorCode.InternalError)
        val vm = HistoryViewModel(repository)

        vm.load()

        assertEquals(HistoryLoadState.Failed(ErrorCode.InternalError), vm.state.value)
    }

    @Test
    fun app_view_model_keeps_the_last_summary_until_it_is_consumed() = runTest(UnconfinedTestDispatcher()) {
        val vm = AppViewModel(repository)
        repository.startSession("Demo", 5, 10 * 60_000L)
        assertTrue(vm.sessionActive.value)
        assertNull(vm.lastSummary.value)

        repository.finish()

        assertFalse(vm.sessionActive.value)
        need(vm.lastSummary.value)
        vm.consumeSummary()
        assertNull(vm.lastSummary.value)
    }

    @Test
    fun home_reflects_the_connection_state() = runTest(UnconfinedTestDispatcher()) {
        val vm = HomeViewModel(repository)
        backgroundScope.launch { vm.state.collect {} }

        repository.setConnection(ConnectionState.Reconnecting)

        assertEquals(ConnectionState.Reconnecting, vm.state.value.connection)
    }
}
