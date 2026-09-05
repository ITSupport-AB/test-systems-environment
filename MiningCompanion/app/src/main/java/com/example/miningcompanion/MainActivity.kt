package com.example.miningcompanion

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bolt
import androidx.compose.material.icons.outlined.Cloud
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.StopCircle
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

private val Ink = Color(0xFF182320)
private val Canvas = Color(0xFFF5F7F3)
private val Mint = Color(0xFF1E7A66)
private val Sun = Color(0xFFF2B544)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { MiningCompanionApp() }
    }
}

@androidx.compose.runtime.Composable
private fun MiningCompanionApp() {
    var isConnected by rememberSaveable { mutableStateOf(true) }
    var isRunning by rememberSaveable { mutableStateOf(false) }

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
                        IconButton(onClick = { isConnected = !isConnected }) {
                            Icon(Icons.Outlined.Refresh, contentDescription = "Refresh connection", tint = Ink)
                        }
                    }
                }
                item {
                    Card(
                        colors = CardDefaults.cardColors(containerColor = Ink),
                        shape = RoundedCornerShape(20.dp)
                    ) {
                        Column(Modifier.padding(20.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Icon(Icons.Outlined.Cloud, contentDescription = null, tint = Sun, modifier = Modifier.size(20.dp))
                                Text("  REMOTE WORKER", color = Sun, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                            }
                            Spacer(Modifier.height(10.dp))
                            Text("Atlas Rig 01", color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                            Text(if (isConnected) "Connected securely" else "Connection paused", color = Color(0xFFB8CCC5), fontSize = 14.sp)
                            Spacer(Modifier.height(18.dp))
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                                Metric("HASHRATE", if (isRunning) "187.4 MH/s" else "0.0 MH/s")
                                Metric("TEMP", if (isRunning) "61 C" else "39 C")
                                Metric("UPTIME", if (isRunning) "04:18:22" else "--")
                            }
                        }
                    }
                }
                item {
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth()) {
                        Button(
                            onClick = { isRunning = !isRunning },
                            modifier = Modifier.weight(1f).height(54.dp),
                            shape = RoundedCornerShape(14.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = if (isRunning) Color(0xFFC45744) else Mint)
                        ) {
                            Icon(if (isRunning) Icons.Outlined.StopCircle else Icons.Outlined.Bolt, contentDescription = null)
                            Text(if (isRunning) "  Stop worker" else "  Start worker", fontWeight = FontWeight.Bold)
                        }
                    }
                }
                item {
                    Text("Performance", color = Ink, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(8.dp))
                    Card(colors = CardDefaults.cardColors(containerColor = Color.White), shape = RoundedCornerShape(16.dp)) {
                        Column(Modifier.padding(18.dp)) {
                            Text("Last 60 minutes", color = Color(0xFF6B7772), fontSize = 13.sp)
                            Text(if (isRunning) "187.4 MH/s" else "Ready", color = Ink, fontSize = 26.sp, fontWeight = FontWeight.Bold)
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
}

@androidx.compose.runtime.Composable
private fun Metric(label: String, value: String) {
    Column {
        Text(label, color = Color(0xFF8FA9A0), fontSize = 10.sp, fontWeight = FontWeight.Bold)
        Text(value, color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.Bold)
    }
}