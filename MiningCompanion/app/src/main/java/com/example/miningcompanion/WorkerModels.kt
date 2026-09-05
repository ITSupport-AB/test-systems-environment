package com.example.miningcompanion

data class Worker(
    val id: String,
    val name: String,
    val coin: String,
    val network: String,
    val algorithm: String,
    val state: WorkerState,
    val hashrate: String,
    val temperatureCelsius: Int,
    val uptime: String,
    val lastSeen: String
)

enum class WorkerState {
    RUNNING,
    STOPPED,
    OFFLINE,
    UNKNOWN
}

enum class WorkerCommand {
    START,
    STOP,
    RESTART
}

data class WorkerScreenState(
    val isLoading: Boolean = true,
    val workers: List<Worker> = emptyList(),
    val errorMessage: String? = null,
    val pendingCommand: WorkerCommand? = null
)