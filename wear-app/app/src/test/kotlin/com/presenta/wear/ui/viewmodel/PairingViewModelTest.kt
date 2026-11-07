package com.presenta.wear.ui.viewmodel

import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.PairingState
import com.presenta.wear.support.FakeMonotonicClock
import com.presenta.wear.support.TestClocks
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
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
class PairingViewModelTest {
    private val repository = FakePresentaRepository(FakeMonotonicClock(), TestClocks.wall).also { it.setPairing(PairingState.NeedsCode) }

    @Before
    fun setUp() = Dispatchers.setMain(UnconfinedTestDispatcher())

    @After
    fun tearDown() = Dispatchers.resetMain()

    private fun TestScope.viewModel(): PairingViewModel {
        val vm = PairingViewModel(repository)
        backgroundScope.launch { vm.state.collect {} }
        return vm
    }

    @Test
    fun the_code_starts_as_six_zeros_on_the_first_digit() = runTest(UnconfinedTestDispatcher()) {
        val state = viewModel().state.value

        assertEquals(listOf(0, 0, 0, 0, 0, 0), state.digits)
        assertEquals(0, state.index)
        assertFalse(state.connecting)
        assertNull(state.error)
        assertFalse(state.isLastDigit)
    }

    @Test
    fun plus_and_minus_wrap_around() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()

        vm.decrease()
        assertEquals(9, vm.state.value.digits[0])
        vm.increase()
        assertEquals(0, vm.state.value.digits[0])
        repeat(10) { vm.increase() }
        assertEquals(0, vm.state.value.digits[0])
    }

    @Test
    fun next_moves_through_the_digits_and_the_last_step_connects_with_the_whole_code() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()

        for (digit in listOf(4, 8, 2, 9, 1, 3)) {
            repeat(digit) { vm.increase() }
            vm.nextOrConnect()
        }

        assertEquals("482913", repository.lastConnectCode)
        assertTrue(vm.state.value.isLastDigit)
        assertEquals("482913", vm.state.value.code)
    }

    @Test
    fun next_does_not_connect_before_the_last_digit() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()

        repeat(5) { vm.nextOrConnect() }

        assertNull(repository.lastConnectCode)
        assertEquals(5, vm.state.value.index)
    }

    @Test
    fun previous_digit_goes_back_and_stops_at_the_first() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()
        vm.nextOrConnect()
        vm.nextOrConnect()

        vm.previousDigit()
        vm.previousDigit()
        vm.previousDigit()

        assertEquals(0, vm.state.value.index)
    }

    @Test
    fun the_last_code_used_is_prefilled() = runTest(UnconfinedTestDispatcher()) {
        repository.connectWithCode("482913")
        repository.setPairing(PairingState.NeedsCode)

        val state = viewModel().state.value

        assertEquals(listOf(4, 8, 2, 9, 1, 3), state.digits)
    }

    @Test
    fun a_malformed_saved_code_is_ignored() = runTest(UnconfinedTestDispatcher()) {
        repository.connectWithCode("12ab")

        assertEquals(listOf(0, 0, 0, 0, 0, 0), viewModel().state.value.digits)
    }

    @Test
    fun the_pairing_outcome_is_shown() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()

        repository.setPairing(PairingState.Connecting)
        assertTrue(vm.state.value.connecting)

        repository.setPairing(PairingState.Rejected(ErrorCode.PairingLockedOut))
        assertFalse(vm.state.value.connecting)
        assertEquals(ErrorCode.PairingLockedOut, vm.state.value.error)

        repository.setPairing(PairingState.Paired)
        assertNull(vm.state.value.error)
    }

    @Test
    fun editing_after_a_rejection_keeps_the_digits() = runTest(UnconfinedTestDispatcher()) {
        val vm = viewModel()
        repeat(3) { vm.increase() }
        repository.setPairing(PairingState.Rejected(ErrorCode.InvalidPairingCode))

        vm.increase()

        assertEquals(4, vm.state.value.digits[0])
        assertEquals(ErrorCode.InvalidPairingCode, vm.state.value.error)
    }
}
