package com.hinge.office

import android.os.Handler
import android.os.Looper
import io.flutter.plugin.common.EventChannel
import java.util.ArrayDeque

/**
 * Process-local bridge from Android broadcast receivers to Flutter.
 *
 * SMS contents are kept only in memory while the Flutter event channel is
 * attaching. No inbox query, file write, or log output is performed here.
 */
object SmsRelayBridge {
    private const val MAX_PENDING_EVENTS = 8
    private val lock = Any()
    private val mainHandler = Handler(Looper.getMainLooper())
    private val pending = ArrayDeque<Map<String, Any?>>()
    private var sink: EventChannel.EventSink? = null

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
        synchronized(lock) {
            current = sink
            if (current == null) {
                if (pending.size >= MAX_PENDING_EVENTS) pending.removeFirst()
                pending.addLast(event)
                return
            }
        }
        post(current, event)
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
}
