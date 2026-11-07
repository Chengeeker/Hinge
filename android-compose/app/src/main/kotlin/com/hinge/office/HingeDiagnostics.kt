package com.hinge.office

import android.content.Context
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Small bounded diagnostic log for the background connection service.
 *
 * The log deliberately contains lifecycle, network and socket facts only.
 * It never receives notification text, SMS contents, pairing codes or file
 * bytes. Keeping this in app-private storage makes it survive an Activity
 * restart and an APK update without exposing it through shared storage.
 */
class HingeDiagnostics private constructor(context: Context) {
    private val directory = File(context.filesDir, "diagnostics")
    private val logFile = File(directory, "hinge.log")
    private val backupFile = File(directory, "hinge.log.1")
    private val backupFile2 = File(directory, "hinge.log.2")
    private val lock = Any()
    private val timestamp = SimpleDateFormat(
        "yyyy-MM-dd'T'HH:mm:ss.SSSXXX",
        Locale.ROOT,
    )

    fun log(event: String, details: Map<String, Any?> = emptyMap()) {
        val safeEvent = sanitize(event)
        val suffix = details.entries
            .asSequence()
            .filter { it.key.isNotBlank() && it.value != null }
            .map { "${sanitize(it.key)}=${sanitize(it.value.toString())}" }
            .joinToString(" ")
        val line = buildString {
            append(timestamp.format(Date()))
            append(" ")
            append(safeEvent)
            if (suffix.isNotEmpty()) {
                append(" ")
                append(suffix)
            }
            append("\n")
        }

        synchronized(lock) {
            try {
                directory.mkdirs()
                rotateIfNeeded(line.length)
                logFile.appendText(line, Charsets.UTF_8)
            } catch (_: Exception) {
                // Diagnostics must never affect the connection service.
            }
        }
    }

    fun read(maxBytes: Int = 256 * 1024): String {
        synchronized(lock) {
            return try {
                if (!logFile.isFile) return ""
                val bytes = logFile.readBytes()
                val start = (bytes.size - maxBytes.coerceAtLeast(1)).coerceAtLeast(0)
                String(bytes, start, bytes.size - start, Charsets.UTF_8)
            } catch (_: Exception) {
                ""
            }
        }
    }

    fun clear() {
        synchronized(lock) {
            runCatching { logFile.delete() }
            runCatching { backupFile.delete() }
            runCatching { backupFile2.delete() }
        }
    }

    private fun rotateIfNeeded(nextBytes: Int) {
        val maxBytes = 512 * 1024
        if (!logFile.exists() || logFile.length() + nextBytes <= maxBytes) return
        runCatching { if (backupFile2.exists()) backupFile2.delete() }
        runCatching { if (backupFile.exists()) backupFile.renameTo(backupFile2) }
        runCatching { if (logFile.exists()) logFile.renameTo(backupFile) }
    }

    private fun sanitize(value: String): String = value
        .replace('\n', ' ')
        .replace('\r', ' ')
        .replace('\t', ' ')
        .take(512)

    companion object {
        @Volatile
        private var instance: HingeDiagnostics? = null

        fun from(context: Context): HingeDiagnostics {
            return instance ?: synchronized(this) {
                instance ?: HingeDiagnostics(context.applicationContext).also {
                    instance = it
                }
            }
        }
    }
}
