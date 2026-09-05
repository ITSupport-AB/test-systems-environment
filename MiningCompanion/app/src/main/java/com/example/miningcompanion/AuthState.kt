package com.example.miningcompanion

sealed interface AuthState {
    data object Loading : AuthState
    data object SignedOut : AuthState
    data class SignedIn(val email: String?) : AuthState
    data class Error(val message: String) : AuthState
}