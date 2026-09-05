package com.example.miningcompanion

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class WorkerViewModel(application: Application) : AndroidViewModel(application) {
    private val sessionStore = SessionStore(application)
    private val api = WorkerApi(BuildConfig.API_BASE_URL, sessionStore)
    private val mutableState = MutableStateFlow(WorkerScreenState())
    val state: StateFlow<WorkerScreenState> = mutableState.asStateFlow()

    init {
        refresh()
    }

    fun refresh() {
        mutableState.value = mutableState.value.copy(isLoading = true, errorMessage = null)
        viewModelScope.launch {
            runCatching { api.getWorkers() }
                .onSuccess { workers -> mutableState.value = WorkerScreenState(isLoading = false, workers = workers) }
                .onFailure { error -> mutableState.value = WorkerScreenState(isLoading = false, errorMessage = error.message) }
        }
    }

    fun connect(accessToken: String) {
        if (accessToken.isBlank()) return
        sessionStore.saveAccessToken(accessToken.trim())
        refresh()
    }

    fun disconnect() {
        sessionStore.clear()
        mutableState.value = WorkerScreenState(isLoading = false, errorMessage = "Sign in is required before connecting to a worker")
    }

    fun sendCommand(worker: Worker, command: WorkerCommand) {
        if (mutableState.value.pendingCommand != null) return
        mutableState.value = mutableState.value.copy(pendingCommand = command, errorMessage = null)
        viewModelScope.launch {
            runCatching { api.sendCommand(worker.id, command) }
                .onSuccess { refresh() }
                .onFailure { error -> mutableState.value = mutableState.value.copy(pendingCommand = null, errorMessage = error.message) }
        }
    }

    fun loadLunoAddress(asset: String) {
        mutableState.value = mutableState.value.copy(payoutLoading = true, payoutError = null, payoutAsset = asset)
        viewModelScope.launch {
            runCatching { api.getLunoFundingAddress(asset) }
                .onSuccess { (address, _) -> mutableState.value = mutableState.value.copy(payoutLoading = false, payoutAddress = address) }
                .onFailure { error -> mutableState.value = mutableState.value.copy(payoutLoading = false, payoutError = error.message) }
        }
    }
}