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
    private const val MAX_PENDING_SMS_FALLBACKS = 8
    private const val RECENT_SMS_TTL_MS = 30_000L
    private const val SMS_TIMESTAMP_MATCH_WINDOW_MS = 5_000L
    private const val SMS_ARRIVAL_MATCH_WINDOW_MS = 2_000L
    private const val SMS_NOTIFICATION_FALLBACK_DELAY_MS = 1_500L
    private const val SMS_FALLBACK_MATCH_WINDOW_MS = 10_000L
    private const val SMS_FALLBACK_ARRIVAL_MATCH_WINDOW_MS = 5_000L
    private const val SMS_NOTIFICATION_ORIGIN = "sms_notification"

    private val lock = Any()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val pending = ArrayDeque<Map<String, Any?>>()
    private val recentSms = ArrayDeque<SmsFingerprint>()
    private val pendingSmsFallbacks = ArrayDeque<PendingSmsFallback>()
    private var nextFallbackId = 0L
    private var sink: EventChannel.EventSink? = null

    private data class SmsFingerprint(
        val origin: String,
        val sender: String,
        val body: String,
        val verificationCode: String?,
        val timestamp: Long,
        val receivedAtElapsed: Long,
    )

    private data class PendingSmsFallback(
        val id: Long,
        val event: Map<String, Any?>,
        val fingerprint: SmsFingerprint,
        val flushRunnable: Runnable,
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
        val forwardedEvent = event - "origin"
        val fingerprint = createSmsFingerprint(event)
        synchronized(lock) {
            if (fingerprint == null) {
                forwardLocked(forwardedEvent)
                return
            }

            pruneRecentSmsLocked(fingerprint.receivedAtElapsed)
            if (fingerprint.origin == SMS_NOTIFICATION_ORIGIN) {
                // Notification text/title is a compatibility-only source and
                // can be incomplete. Give SMS_RECEIVED/Provider events time to
                // arrive so only the best version of the message is relayed.
                if (isDuplicateOfRecentSmsLocked(fingerprint)) return
                scheduleNotificationFallbackLocked(forwardedEvent, fingerprint)
                return
            }

            val matchingFallbacks = removeMatchingFallbacksLocked(fingerprint)
            if (isDuplicateOfRecentSmsLocked(fingerprint)) return

            // Prefer an event that actually carries a recognized code. In the
            // usual case the direct SMS event wins; if its body is incomplete,
            // the delayed compatibility event can supply the actionable code.
            val selectedEvent = matchingFallbacks.fold(forwardedEvent) { selected, fallback ->
                preferVerificationCodeEvent(selected, fallback.event)
            }
            matchingFallbacks.forEach { rememberSmsLocked(it.fingerprint) }
            rememberSmsLocked(fingerprint)
            forwardLocked(selectedEvent)
        }
    }

    private fun createSmsFingerprint(event: Map<String, Any?>): SmsFingerprint? {
        if (!event["source"].toString().equals("sms", ignoreCase = true)) return null
        val origin = event["origin"]?.toString()?.takeIf { it.isNotBlank() } ?: return null
        val body = event["body"]?.toString()?.trim()?.replace(whitespace, " ")
            ?.takeIf { it.isNotEmpty() } ?: return null
        val sender = event["sender"]?.toString()?.trim()?.replace(whitespace, " ")
            ?.lowercase(Locale.ROOT).orEmpty()
        val timestamp = (event["timestamp"] as? Number)?.toLong()
            ?.takeIf { it > 0L } ?: System.currentTimeMillis()
        val verificationCode = event["verificationCode"]?.toString()
            ?.takeIf { it.isNotBlank() }
        return SmsFingerprint(
            origin = origin,
            sender = sender,
            body = body,
            verificationCode = verificationCode,
            timestamp = timestamp,
            receivedAtElapsed = SystemClock.elapsedRealtime(),
        )
    }

    private fun scheduleNotificationFallbackLocked(
        event: Map<String, Any?>,
        fingerprint: SmsFingerprint,
    ) {
        if (pendingSmsFallbacks.size >= MAX_PENDING_SMS_FALLBACKS) {
            flushNotificationFallbackLocked(pendingSmsFallbacks.first)
        }

        val id = ++nextFallbackId
        val runnable = Runnable { flushNotificationFallback(id) }
        val fallback = PendingSmsFallback(id, event, fingerprint, runnable)
        pendingSmsFallbacks.addLast(fallback)
        if (!mainHandler.postDelayed(runnable, SMS_NOTIFICATION_FALLBACK_DELAY_MS)) {
            flushNotificationFallbackLocked(fallback)
        }
    }

    private fun flushNotificationFallback(id: Long) {
        synchronized(lock) {
            val fallback = pendingSmsFallbacks.firstOrNull { it.id == id } ?: return
            flushNotificationFallbackLocked(fallback)
        }
    }

    private fun flushNotificationFallbackLocked(fallback: PendingSmsFallback) {
        if (!pendingSmsFallbacks.remove(fallback)) return
        mainHandler.removeCallbacks(fallback.flushRunnable)
        pruneRecentSmsLocked(SystemClock.elapsedRealtime())
        if (isDuplicateOfRecentSmsLocked(fallback.fingerprint)) return
        rememberSmsLocked(fallback.fingerprint)
        forwardLocked(fallback.event)
    }

    private fun removeMatchingFallbacksLocked(
        fingerprint: SmsFingerprint,
    ): List<PendingSmsFallback> {
        val matches = mutableListOf<PendingSmsFallback>()
        val iterator = pendingSmsFallbacks.iterator()
        while (iterator.hasNext()) {
            val fallback = iterator.next()
            if (areSameSms(fallback.fingerprint, fingerprint)) {
                iterator.remove()
                mainHandler.removeCallbacks(fallback.flushRunnable)
                matches.add(fallback)
            }
        }
        return matches
    }

    /**
     * Compare only short-lived in-memory fingerprints. Notification-listener
     * events get a slightly wider match window because their posted timestamp
     * and contact-name sender can differ from the SMS broadcast/provider row.
     */
    private fun areSameSms(first: SmsFingerprint, second: SmsFingerprint): Boolean {
        if (first.origin == second.origin) return false

        val timestampDelta = abs(first.timestamp - second.timestamp)
        val arrivalDelta = abs(first.receivedAtElapsed - second.receivedAtElapsed)
        val sameSender = first.sender.isNotEmpty() && first.sender == second.sender
        val senderUnavailable = first.sender in genericSenders || second.sender in genericSenders
        val sameBody = first.body == second.body
        val ordinaryMatch = sameBody &&
            ((sameSender && timestampDelta <= SMS_TIMESTAMP_MATCH_WINDOW_MS) ||
                timestampDelta <= 1_000L ||
                (arrivalDelta <= SMS_ARRIVAL_MATCH_WINDOW_MS &&
                    (sameSender || senderUnavailable)))
        if (ordinaryMatch) return true

        val involvesNotificationFallback = first.origin == SMS_NOTIFICATION_ORIGIN ||
            second.origin == SMS_NOTIFICATION_ORIGIN
        if (!involvesNotificationFallback) return false

        val closeFallbackEvent = timestampDelta <= SMS_FALLBACK_MATCH_WINDOW_MS &&
            arrivalDelta <= SMS_FALLBACK_ARRIVAL_MATCH_WINDOW_MS
        val sameCode = first.verificationCode != null &&
            first.verificationCode == second.verificationCode
        val sameCodeInBothBodies = (first.verificationCode ?: second.verificationCode)
            ?.let { code -> first.body.contains(code) && second.body.contains(code) } == true
        val sameCodeEvidence = sameCode || sameCodeInBothBodies
        return (sameBody && closeFallbackEvent) ||
            (sameCodeEvidence &&
                timestampDelta <= SMS_TIMESTAMP_MATCH_WINDOW_MS &&
                arrivalDelta <= SMS_ARRIVAL_MATCH_WINDOW_MS)
    }

    private fun isDuplicateOfRecentSmsLocked(fingerprint: SmsFingerprint): Boolean =
        recentSms.any { previous -> areSameSms(previous, fingerprint) }

    private fun pruneRecentSmsLocked(nowElapsed: Long) {
        while (recentSms.isNotEmpty() &&
            nowElapsed - recentSms.first().receivedAtElapsed > RECENT_SMS_TTL_MS
        ) {
            recentSms.removeFirst()
        }
    }

    private fun rememberSmsLocked(fingerprint: SmsFingerprint) {
        pruneRecentSmsLocked(SystemClock.elapsedRealtime())
        if (recentSms.size >= MAX_RECENT_SMS) recentSms.removeFirst()
        recentSms.addLast(fingerprint)
    }

    private fun preferVerificationCodeEvent(
        current: Map<String, Any?>,
        candidate: Map<String, Any?>,
    ): Map<String, Any?> {
        val currentHasCode = current["isVerificationCode"] == true &&
            !current["verificationCode"]?.toString().isNullOrBlank()
        val candidateHasCode = candidate["isVerificationCode"] == true &&
            !candidate["verificationCode"]?.toString().isNullOrBlank()
        return if (candidateHasCode && !currentHasCode) candidate else current
    }

    private fun forwardLocked(event: Map<String, Any?>) {
        val current = sink
        if (current == null) {
            if (pending.size >= MAX_PENDING_EVENTS) pending.removeFirst()
            pending.addLast(event)
        } else {
            post(current, event)
        }
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
