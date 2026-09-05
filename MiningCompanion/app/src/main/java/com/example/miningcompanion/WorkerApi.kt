package com.example.miningcompanion

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

class WorkerApi(
    private val baseUrl: String,
    private val sessionStore: SessionStore
) {
    suspend fun getWorkers(): List<Worker> = withContext(Dispatchers.IO) {
        val response = request("GET", "/v1/workers")
        val workers = if (response.trimStart().startsWith("[")) {
            JSONArray(response)
        } else {
            JSONObject(response).optJSONArray("workers") ?: JSONArray()
        }
        List(workers.length()) { index -> workers.getJSONObject(index).toWorker() }
    }

    suspend fun sendCommand(workerId: String, command: WorkerCommand) = withContext(Dispatchers.IO) {
        val body = JSONObject()
            .put("command", command.name)
            .put("idempotencyKey", UUID.randomUUID().toString())
        request("POST", "/v1/workers/$workerId/commands", body.toString())
    }

    private fun request(method: String, path: String, body: String? = null): String {
        val token = sessionStore.readAccessToken()
            ?: throw IOException("Sign in is required before connecting to a worker")
        val connection = (URL(baseUrl.trimEnd('/') + path).openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = 10_000
            readTimeout = 15_000
            useCaches = false
            setRequestProperty("Accept", "application/json")
            setRequestProperty("Authorization", "Bearer $token")
            if (body != null) {
                doOutput = true
                setRequestProperty("Content-Type", "application/json")
            }
        }

        try {
            val http = connection
            body?.let { http.outputStream.use { stream -> stream.write(it.toByteArray()) } }
            val status = http.responseCode
            val stream = if (status in 200..299) http.inputStream else http.errorStream
            val response = stream?.bufferedReader()?.use { it.readText() }.orEmpty()
            if (status !in 200..299) throw IOException("Worker API request failed with HTTP $status")
            return response
        } finally {
            connection.disconnect()
        }
    }

    private fun JSONObject.toWorker(): Worker = Worker(
        id = getString("id"),
        name = getString("name"),
        algorithm = optString("algorithm", "unknown"),
        state = runCatching { WorkerState.valueOf(optString("state").uppercase()) }.getOrDefault(WorkerState.UNKNOWN),
        hashrate = optString("hashrate", "--"),
        temperatureCelsius = optInt("temperatureCelsius", 0),
        uptime = optString("uptime", "--"),
        lastSeen = optString("lastSeen", "--")
    )
}