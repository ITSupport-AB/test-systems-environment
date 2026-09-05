package com.example.miningcompanion

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import com.google.firebase.auth.FirebaseAuth
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class AuthViewModel(application: Application) : AndroidViewModel(application) {
    private val sessionStore = SessionStore(application)
    private val auth = runCatching { FirebaseAuth.getInstance() }.getOrNull()
    private val mutableState = MutableStateFlow<AuthState>(AuthState.Loading)
    val state: StateFlow<AuthState> = mutableState.asStateFlow()

    private val authListener = FirebaseAuth.AuthStateListener { firebaseAuth ->
        val user = firebaseAuth.currentUser
        if (user == null) {
            sessionStore.clear()
            mutableState.value = AuthState.SignedOut
        } else {
            user.getIdToken(true)
                .addOnSuccessListener { result ->
                    result.token?.let(sessionStore::saveAccessToken)
                    mutableState.value = AuthState.SignedIn(user.email)
                }
                .addOnFailureListener { error -> mutableState.value = AuthState.Error(error.message ?: "Could not refresh session") }
        }
    }

    init {
        if (auth == null) {
            mutableState.value = AuthState.Error("Firebase is not configured. Add google-services.json to the app module.")
        } else {
            auth.addAuthStateListener(authListener)
        }
    }

    fun signIn(email: String, password: String) {
        val firebaseAuth = auth ?: return notConfigured()
        if (!validInput(email, password)) return
        mutableState.value = AuthState.Loading
        firebaseAuth.signInWithEmailAndPassword(email.trim(), password)
            .addOnFailureListener { error -> mutableState.value = AuthState.Error(error.message ?: "Sign in failed") }
    }

    fun createAccount(email: String, password: String) {
        val firebaseAuth = auth ?: return notConfigured()
        if (!validInput(email, password)) return
        mutableState.value = AuthState.Loading
        firebaseAuth.createUserWithEmailAndPassword(email.trim(), password)
            .addOnFailureListener { error -> mutableState.value = AuthState.Error(error.message ?: "Account creation failed") }
    }

    fun signOut() {
        sessionStore.clear()
        auth?.signOut()
    }

    override fun onCleared() {
        auth?.removeAuthStateListener(authListener)
        super.onCleared()
    }

    private fun validInput(email: String, password: String): Boolean {
        if (email.trim().isEmpty() || password.length < 8) {
            mutableState.value = AuthState.Error("Enter a valid email and a password of at least 8 characters")
            return false
        }
        return true
    }

    private fun notConfigured() {
        mutableState.value = AuthState.Error("Firebase is not configured. Add google-services.json to the app module.")
    }
}