package com.presenta.wear.ui.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.PresentaRepository
import com.presenta.wear.model.CommandResult
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.HistoryItem
import com.presenta.wear.model.HistoryResult
import com.presenta.wear.model.PairingState
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.model.SetupLimits
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.screens.control.ControlUiState
import com.presenta.wear.ui.screens.home.HomeUiState
import com.presenta.wear.ui.screens.setup.SetupUiState
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.launchIn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.onEach
import kotlinx.coroutines.flow.onStart
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

private const val STOP_TIMEOUT_MS = 5_000L

/**
 * App-level state: connection, whether a session is active, and the summary of the session that just ended.
 * The summary stays until [consumeSummary] so the Summary screen survives recomposition.
 */
class AppViewModel(private val repository: PresentaRepository) : ViewModel() {
    val connection: StateFlow<ConnectionState> = repository.connection

    val pairing: StateFlow<PairingState> = repository.pairing

    val sessionActive: StateFlow<Boolean> = repository.session
        .map { it is SessionStatus.Active }
        .stateIn(viewModelScope, SharingStarted.Eagerly, repository.session.value is SessionStatus.Active)

    private val _lastSummary = MutableStateFlow<HistoryItem?>(null)
    val lastSummary: StateFlow<HistoryItem?> = _lastSummary

    init {
        repository.summaries.onEach { _lastSummary.value = it }.launchIn(viewModelScope)
    }

    fun consumeSummary() {
        _lastSummary.value = null
    }
}

class HomeViewModel(private val repository: PresentaRepository) : ViewModel() {
    val state: StateFlow<HomeUiState> = repository.connection
        .map { HomeUiState(it) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(STOP_TIMEOUT_MS), HomeUiState(repository.connection.value))

    fun retry() = repository.retryConnection()
}

class SetupViewModel(private val repository: PresentaRepository) : ViewModel() {
    private val _state = MutableStateFlow(SetupUiState())
    val state: StateFlow<SetupUiState> = _state

    fun decreaseSlides() = _state.update { it.copy(slides = (it.slides - 1).coerceAtLeast(SetupLimits.MIN_SLIDES), error = null) }

    fun increaseSlides() = _state.update { it.copy(slides = (it.slides + 1).coerceAtMost(SetupLimits.MAX_SLIDES), error = null) }

    fun decreaseMinutes() = _state.update { it.copy(minutes = (it.minutes - 1).coerceAtLeast(SetupLimits.MIN_MINUTES), error = null) }

    fun increaseMinutes() = _state.update { it.copy(minutes = (it.minutes + 1).coerceAtMost(SetupLimits.MAX_MINUTES), error = null) }

    /** Starts a session with [name] (built from resources by the caller) and reports success through [onStarted]. */
    fun start(name: String, onStarted: () -> Unit) {
        val current = _state.value
        if (current.starting) return
        _state.update { it.copy(starting = true, error = null) }
        viewModelScope.launch {
            val result = repository.startSession(name, current.slides, current.minutes * 60_000L)
            when (result) {
                CommandResult.Success -> {
                    _state.update { it.copy(starting = false) }
                    onStarted()
                }
                is CommandResult.Failure -> _state.update { it.copy(starting = false, error = result.code) }
            }
        }
    }
}

class ControlViewModel(
    private val repository: PresentaRepository,
    private val clock: MonotonicClock,
    displayTicks: Flow<Unit>,
) : ViewModel() {
    private val overlay = MutableStateFlow(ControlOverlay.None)
    private val selectorSlide = MutableStateFlow(1)
    private val message = MutableStateFlow<ErrorCode?>(null)

    /** Null while there is no active session. */
    val state: StateFlow<ControlUiState?> = combine(
        repository.connection,
        repository.session,
        displayTicks.onStart { emit(Unit) },
        overlay,
        combine(selectorSlide, message) { slide, error -> slide to error },
    ) { connection, status, _, overlayValue, (slide, error) ->
        ControlStateFactory.build(connection, status, clock.nowMs(), overlayValue, slide, error)
    }.stateIn(
        viewModelScope,
        SharingStarted.WhileSubscribed(STOP_TIMEOUT_MS),
        ControlStateFactory.build(
            repository.connection.value,
            repository.session.value,
            clock.nowMs(),
            ControlOverlay.None,
            1,
            null,
        ),
    )

    fun next() = run { repository.next() }

    fun previous() = run { repository.previous() }

    fun togglePause() {
        val current = state.value ?: return
        run { if (current.paused) repository.resume() else repository.pause() }
    }

    fun acknowledgeAlert() = run { repository.acknowledgeAlert() }

    fun openSelector() {
        val current = state.value ?: return
        selectorSlide.value = current.currentSlide
        overlay.value = ControlOverlay.SlideSelector
    }

    fun selectorDecrease() = selectorSlide.update { (it - 1).coerceAtLeast(1) }

    fun selectorIncrease() {
        val total = state.value?.totalSlides ?: return
        selectorSlide.update { (it + 1).coerceAtMost(total) }
    }

    fun selectorConfirm() {
        val slide = selectorSlide.value
        overlay.value = ControlOverlay.None
        run { repository.setSlide(slide) }
    }

    fun selectorCancel() {
        overlay.value = ControlOverlay.None
    }

    fun openFinish() {
        if (state.value != null) overlay.value = ControlOverlay.FinishConfirmation
    }

    fun finishCancel() {
        overlay.value = ControlOverlay.None
    }

    fun finishConfirm() {
        overlay.value = ControlOverlay.None
        run { repository.finish() }
    }

    fun clearMessage() {
        message.value = null
    }

    private fun run(command: suspend () -> CommandResult) {
        viewModelScope.launch {
            val result = command()
            if (result is CommandResult.Failure) message.value = result.code
        }
    }
}

sealed interface HistoryLoadState {
    data object Loading : HistoryLoadState

    data class Loaded(val items: List<HistoryItem>) : HistoryLoadState

    data class Failed(val code: ErrorCode) : HistoryLoadState
}

class HistoryViewModel(private val repository: PresentaRepository) : ViewModel() {
    private val _state = MutableStateFlow<HistoryLoadState>(HistoryLoadState.Loading)
    val state: StateFlow<HistoryLoadState> = _state

    fun load() {
        viewModelScope.launch {
            _state.value = when (val result = repository.listHistory()) {
                is HistoryResult.Success -> HistoryLoadState.Loaded(result.items)
                is HistoryResult.Failure -> HistoryLoadState.Failed(result.code)
            }
        }
    }

    fun find(finishedAtEpochMs: Long): HistoryItem? =
        (state.value as? HistoryLoadState.Loaded)?.items?.firstOrNull { it.finishedAtEpochMs == finishedAtEpochMs }
}
