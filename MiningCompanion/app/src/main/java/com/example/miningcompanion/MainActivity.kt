package com.example.miningcompanion

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bolt
import androidx.compose.material.icons.outlined.Cloud
import androidx.compose.material.icons.outlined.Logout
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.StopCircle
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.text.input.PasswordVisualTransformation

private val Ink = Color(0xFF182320)
private val Canvas = Color(0xFFF5F7F3)
private val Mint = Color(0xFF1E7A66)
private val Sun = Color(0xFFF2B544)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val authViewModel = androidx.lifecycle.ViewModelProvider(this)[AuthViewModel::class.java]
        val workerViewModel = androidx.lifecycle.ViewModelProvider(this)[WorkerViewModel::class.java]
        setContent { App(authViewModel, workerViewModel) }
    }
}

@androidx.compose.runtime.Composable
private fun App(authViewModel: AuthViewModel, workerViewModel: WorkerViewModel) {
    val authState by authViewModel.state.collectAsStateWithLifecycle()
    when (authState) {
        is AuthState.SignedIn -> MiningCompanionApp(workerViewModel, authViewModel::signOut)
        else -> AuthScreen(authState, authViewModel::signIn, authViewModel::createAccount)
    }
}

@androidx.compose.runtime.Composable
private fun MiningCompanionApp(viewModel: WorkerViewModel, onSignOut: () -> Unit) {
    val screenState by viewModel.state.collectAsStateWithLifecycle()
    val worker = screenState.workers.firstOrNull()
    val isRunning = worker?.state == WorkerState.RUNNING
    var accessToken by rememberSaveable { mutableStateOf("") }
    var requestedCommand by rememberSaveable { mutableStateOf<WorkerCommand?>(null) }

    MaterialTheme {
        Surface(modifier = Modifier.fillMaxSize(), color = Canvas) {
            LazyColumn(
                modifier = Modifier.padding(horizontal = 20.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                item {
                    Spacer(Modifier.height(18.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Column {
                            Text("MINING COMPANION", color = Mint, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                            Text("Control room", color = Ink, fontSize = 30.sp, fontWeight = FontWeight.Bold)
                        }
                        Row {
                            IconButton(onClick = viewModel::refresh) {
                                Icon(Icons.Outlined.Refresh, contentDescription = "Refresh connection", tint = Ink)
                            }
                            IconButton(onClick = onSignOut) {
                                Icon(Icons.Outlined.Logout, contentDescription = "Sign out", tint = Ink)
                            }
                        }
                    }
                }
                item {
                    when {
                        screenState.isLoading -> Box(Modifier.fillMaxWidth().padding(40.dp), contentAlignment = Alignment.Center) {
                            CircularProgressIndicator(color = Mint)
                        }
                        screenState.errorMessage?.contains("Sign in", ignoreCase = true) == true -> ConnectCard(
                            token = accessToken,
                            onTokenChanged = { accessToken = it },
                            onConnect = { viewModel.connect(accessToken) }
                        )
                        screenState.errorMessage != null -> StatusCard(
                            title = "Worker unavailable",
                            message = screenState.errorMessage ?: "Unknown worker API error",
                            actionLabel = "Retry",
                            onAction = viewModel::refresh
                        )
                        worker == null -> StatusCard(
                            title = "No workers connected",
                            message = "Add a worker in the control API, then refresh this screen.",
                            actionLabel = "Refresh",
                            onAction = viewModel::refresh
                        )
                        else -> Card(
                            colors = CardDefaults.cardColors(containerColor = Ink),
                            shape = RoundedCornerShape(20.dp)
                        ) {
                            Column(Modifier.padding(20.dp)) {
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Icon(Icons.Outlined.Cloud, contentDescription = null, tint = Sun, modifier = Modifier.size(20.dp))
                                    Text("  REMOTE WORKER", color = Sun, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                                }
                                Spacer(Modifier.height(10.dp))
                                Text(worker.name, color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                                Text("${worker.coin} ${worker.network} | ${worker.algorithm} | ${worker.state.name.lowercase().replaceFirstChar { it.uppercase() }}", color = Color(0xFFB8CCC5), fontSize = 14.sp)
                                Text("Last seen ${worker.lastSeen}", color = Color(0xFF8FA9A0), fontSize = 12.sp)
                                Spacer(Modifier.height(18.dp))
                                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                    Metric("HASHRATE", worker.hashrate)
                                    Metric("TEMP", "${worker.temperatureCelsius} C")
                                    Metric("UPTIME", worker.uptime)
                                }
                            }
                        }
                    }
                }
                item {
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                        Button(
                            onClick = { requestedCommand = if (isRunning) WorkerCommand.STOP else WorkerCommand.START },
                            enabled = worker != null && screenState.pendingCommand == null,
                            modifier = Modifier.weight(1f).height(54.dp),
                            shape = RoundedCornerShape(14.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = if (isRunning) Color(0xFFC45744) else Mint)
                        ) {
                            Icon(if (isRunning) Icons.Outlined.StopCircle else Icons.Outlined.Bolt, contentDescription = null)
                            Text(if (screenState.pendingCommand != null) "  Sending command..." else if (isRunning) "  Stop worker" else "  Start worker", fontWeight = FontWeight.Bold)
                        }
                    }
                }
                item {
                    Text("Performance", color = Ink, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(8.dp))
                    Card(colors = CardDefaults.cardColors(containerColor = Color.White), shape = RoundedCornerShape(16.dp)) {
                        Column(Modifier.padding(18.dp)) {
                            Text("Last 60 minutes", color = Color(0xFF6B7772), fontSize = 13.sp)
                            Text(worker?.hashrate ?: "--", color = Ink, fontSize = 26.sp, fontWeight = FontWeight.Bold)
                            Text("Estimated yield is calculated from worker-side pool data.", color = Color(0xFF6B7772), fontSize = 13.sp)
                        }
                    }
                }
                item {
                    Text("Safety status", color = Ink, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                    Text("This app controls a remote worker. It does not mine on your phone or access wallet keys.", color = Color(0xFF6B7772), fontSize = 13.sp)
                    Spacer(Modifier.height(18.dp))
                }
            }
        }
    }

    requestedCommand?.let { command ->
        AlertDialog(
            onDismissRequest = { requestedCommand = null },
            title = { Text("Confirm ${command.name.lowercase()} command") },
            text = { Text("Send this command to ${worker?.name ?: "the worker"}? The worker gateway will enforce its safety limits.") },
            confirmButton = {
                TextButton(onClick = {
                    requestedCommand = null
                    worker?.let { viewModel.sendCommand(it, command) }
                }) { Text("Confirm") }
            },
            dismissButton = { TextButton(onClick = { requestedCommand = null }) { Text("Cancel") } }
        )
    }
}

@androidx.compose.runtime.Composable
private fun Metric(label: String, value: String) {
    Column {
        Text(label, color = Color(0xFF8FA9A0), fontSize = 10.sp, fontWeight = FontWeight.Bold)
        Text(value, color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.Bold)
    }
}

@androidx.compose.runtime.Composable
private fun StatusCard(title: String, message: String, actionLabel: String, onAction: () -> Unit) {
    Card(colors = CardDefaults.cardColors(containerColor = Color.White), shape = RoundedCornerShape(16.dp)) {
        Column(Modifier.padding(18.dp)) {
            Text(title, color = Ink, fontSize = 19.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(6.dp))
            Text(message, color = Color(0xFF6B7772), fontSize = 13.sp)
            Spacer(Modifier.height(12.dp))
            Button(onClick = onAction, shape = RoundedCornerShape(12.dp), colors = ButtonDefaults.buttonColors(containerColor = Mint)) {
                Text(actionLabel, fontWeight = FontWeight.Bold)
            }
        }
    }
}

@androidx.compose.runtime.Composable
private fun ConnectCard(token: String, onTokenChanged: (String) -> Unit, onConnect: () -> Unit) {
    Card(colors = CardDefaults.cardColors(containerColor = Color.White), shape = RoundedCornerShape(16.dp)) {
        Column(Modifier.padding(18.dp)) {
            Text("Connect your control API", color = Ink, fontSize = 19.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(6.dp))
            Text("Use a short-lived access token from your worker gateway. It is encrypted on this device.", color = Color(0xFF6B7772), fontSize = 13.sp)
            Spacer(Modifier.height(12.dp))
            OutlinedTextField(
                value = token,
                onValueChange = onTokenChanged,
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text("Access token") },
                visualTransformation = PasswordVisualTransformation()
            )
            Spacer(Modifier.height(12.dp))
            Button(
                onClick = onConnect,
                enabled = token.isNotBlank(),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Mint)
            ) {
                Text("Connect", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@androidx.compose.runtime.Composable
private fun AuthScreen(
    authState: AuthState,
    onSignIn: (String, String) -> Unit,
    onCreateAccount: (String, String) -> Unit
) {
    var email by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var createAccount by rememberSaveable { mutableStateOf(false) }
    val isLoading = authState is AuthState.Loading
    val error = (authState as? AuthState.Error)?.message

    Surface(modifier = Modifier.fillMaxSize(), color = Canvas) {
        Column(
            modifier = Modifier.fillMaxSize().padding(24.dp),
            verticalArrangement = Arrangement.Center
        ) {
            Text("MINING COMPANION", color = Mint, fontSize = 12.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(8.dp))
            Text(if (createAccount) "Create your account" else "Welcome back", color = Ink, fontSize = 30.sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(8.dp))
            Text("Sign in to access your remote worker controls.", color = Color(0xFF6B7772), fontSize = 14.sp)
            Spacer(Modifier.height(22.dp))
            OutlinedTextField(
                value = email,
                onValueChange = { email = it },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text("Email") }
            )
            Spacer(Modifier.height(10.dp))
            OutlinedTextField(
                value = password,
                onValueChange = { password = it },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
                label = { Text("Password") },
                visualTransformation = PasswordVisualTransformation()
            )
            error?.let {
                Spacer(Modifier.height(10.dp))
                Text(it, color = Color(0xFFC45744), fontSize = 13.sp)
            }
            Spacer(Modifier.height(16.dp))
            Button(
                onClick = { if (createAccount) onCreateAccount(email, password) else onSignIn(email, password) },
                enabled = !isLoading,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Mint)
            ) {
                if (isLoading) CircularProgressIndicator(modifier = Modifier.size(20.dp), color = Color.White)
                else Text(if (createAccount) "Create account" else "Sign in", fontWeight = FontWeight.Bold)
            }
            TextButton(onClick = { createAccount = !createAccount }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                Text(if (createAccount) "Already have an account? Sign in" else "New here? Create an account", color = Mint)
            }
        }
    }
}