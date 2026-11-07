package com.presenta.wear.ui.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.presenta.wear.data.PresentaRepository
import com.presenta.wear.model.PairingState
import com.presenta.wear.ui.screens.pairing.PAIRING_CODE_LENGTH
import com.presenta.wear.ui.screens.pairing.PairingUiState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** Digit-by-digit entry of the pairing code. The last code used is prefilled. */
class PairingViewModel(private val repository: PresentaRepository) : ViewModel() {
    private data class Entry(val digits: List<Int> = List(PAIRING_CODE_LENGTH) { 0 }, val index: Int = 0)

    private val entry = MutableStateFlow(Entry())

    val state: StateFlow<PairingUiState> = combine(entry, repository.pairing) { entry, pairing ->
        PairingUiState(
            digits = entry.digits,
            index = entry.index,
            connecting = pairing is PairingState.Connecting,
            error = (pairing as? PairingState.Rejected)?.code,
        )
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), PairingUiState())

    init {
        viewModelScope.launch {
            val saved = repository.savedPairingCode()
            if (saved != null && saved.length == PAIRING_CODE_LENGTH && saved.all { it.isDigit() }) {
                entry.update { it.copy(digits = saved.map { c -> c.digitToInt() }) }
            }
        }
    }

    /** + on the selected digit; 9 wraps to 0. */
    fun increase() = changeDigit(+1)

    /** − on the selected digit; 0 wraps to 9. */
    fun decrease() = changeDigit(-1)

    fun previousDigit() = entry.update { it.copy(index = (it.index - 1).coerceAtLeast(0)) }

    /** Moves to the next digit, or connects on the last one. */
    fun nextOrConnect() {
        val current = entry.value
        if (current.index < PAIRING_CODE_LENGTH - 1) {
            entry.update { it.copy(index = it.index + 1) }
        } else {
            repository.connectWithCode(current.digits.joinToString(""))
        }
    }

    private fun changeDigit(delta: Int) = entry.update {
        val digits = it.digits.toMutableList()
        digits[it.index] = Math.floorMod(digits[it.index] + delta, 10)
        it.copy(digits = digits)
    }
}
