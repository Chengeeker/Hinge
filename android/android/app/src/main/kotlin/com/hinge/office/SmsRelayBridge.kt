package com.hinge.office

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import io.flutter.plugin.common.EventChannel
import java.util.ArrayDeque
import java.util.Locale
import kotlin.math.abs

/**
 * Process-local bridge from Android broadcast receivers to Flutter.
 *
 * SMS contents are kept only in memory while the Flutter event channel is
 * attaching. No inbox query, file write, or log output is performed here.
 */
object SmsRelayBridge {
    private const val MAX_PENDING_EVENTS = 8
    private const val MAX_RECENT_SMS = 64
    private const val RECENT_SMS_TTL_MS = 30_000L
    private const val SMS_TIMESTAMP_MATCH_WINDOW_MS = 5_000L
    private const val SMS_ARRIVAL_MATCH_WINDOW_MS = 2_000L
    private val lock = Any()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val pending = ArrayDeque<Map<String, Any?>>()
    private val recentSms = ArrayDeque<SmsFingerprint>()
    private var sink: EventChannel.EventSink? = null

    private data class SmsFingerprint(
        val origin: String,
        val sender: String,
        val body: String,
        val timestamp: Long,
        val receivedAtElapsed: Long,
    )

    fun attach(nextSink: EventChannel.EventSink?) {
        val queued: List<Map<String, Any?>>
        synchronized(lock) {
            sink = nextSink
            queued = pending.toList()
            pending.clear()
        }
        if (nextSink == null) return
        queued.forEach { event -> post(nextSink, event) }
    }

    fun detach() {
        synchronized(lock) {
            sink = null
        }
    }

    fun emit(event: Map<String, Any?>) {
        val current: EventChannel.EventSink?
        val forwardedEvent = event - "origin"
        synchronized(lock) {
            if (isDuplicateSmsLocked(event)) return
            current = sink
            if (current == null) {
                if (pending.size >= MAX_PENDING_EVENTS) pending.removeFirst()
                pending.addLast(forwardedEvent)
                return
            }
        }
        post(current, forwardedEvent)
    }

    /**
     * SMS can arrive through SMS_RECEIVED, the inbox observer, and the
     * notification-listener compatibility path. Those sources have different
     * IDs, so compare only a short-lived in-memory fingerprint before queuing
     * or forwarding. No message content is persisted or logged.
     */
    private fun isDuplicateSmsLocked(event: Map<String, Any?>): Boolean {
        if (!event["source"].toString().equals("sms", ignoreCase = true)) return false
        val origin = event["origin"]?.toString()?.takeIf { it.isNotBlank() } ?: return false
        val body = event["body"]?.toString()?.trim()?.replace(whitespace, " ")
            ?.takeIf { it.isNotEmpty() } ?: return false
        val sender = event["sender"]?.toString()?.trim()?.replace(whitespace, " ")
            ?.lowercase(Locale.ROOT).orEmpty()
        val now = SystemClock.elapsedRealtime()
        while (recentSms.isNotEmpty() && now - recentSms.first().receivedAtElapsed > RECENT_SMS_TTL_MS) {
            recentSms.removeFirst()
        }

        val timestamp = (event["timestamp"] as? Number)?.toLong()
            ?.takeIf { it > 0L } ?: System.currentTimeMillis()
        val duplicate = recentSms.any { previous ->
            val timestampDelta = abs(previous.timestamp - timestamp)
            val sameSender = sender.isNotEmpty() && previous.sender == sender
            val senderUnavailable = sender in genericSenders ||
                previous.sender in genericSenders
            previous.origin != origin && previous.body == body &&
                ((sameSender && timestampDelta <= SMS_TIMESTAMP_MATCH_WINDOW_MS) ||
                    timestampDelta <= 1_000L ||
                    (now - previous.receivedAtElapsed <= SMS_ARRIVAL_MATCH_WINDOW_MS &&
                        (sameSender || senderUnavailable)))
        }
        if (duplicate) return true

        if (recentSms.size >= MAX_RECENT_SMS) recentSms.removeFirst()
        recentSms.addLast(SmsFingerprint(origin, sender, body, timestamp, now))
        return false
    }

    private fun post(target: EventChannel.EventSink?, event: Map<String, Any?>) {
        if (target == null) return
        mainHandler.post {
            try {
                target.success(event)
            } catch (_: Exception) {
                // The Flutter engine may detach between a receiver callback
                // and the main-thread delivery.
            }
        }
    }

    private val whitespace = Regex("\\s+")
    private val genericSenders = setOf("", "未知号码", "短信")
}
