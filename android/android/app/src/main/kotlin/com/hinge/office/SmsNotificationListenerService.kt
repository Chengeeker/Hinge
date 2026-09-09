package com.hinge.office

import android.app.Notification
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification

/**
 * Optional fallback for vendor ROMs that suppress SMS_RECEIVED or delay SMS
 * provider updates. The service ignores every notification except notifications
 * emitted by a known/identifiable SMS application.
 */
class SmsNotificationListenerService : NotificationListenerService() {
    private val recentlyForwarded = LinkedHashMap<String, Long>()

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        val posted = sbn ?: return
        if (!SmsRelaySettings.isEnabled(this) || !isSmsApplication(posted.packageName)) return

        val notification = posted.notification ?: return
        if (notification.category != null &&
            notification.category != Notification.CATEGORY_MESSAGE &&
            notification.category != Notification.CATEGORY_EMAIL
        ) return

        val extras = notification.extras
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim().orEmpty()
        val body = sequenceOf(
            extras.getCharSequence(Notification.EXTRA_BIG_TEXT)?.toString(),
            extras.getCharSequence(Notification.EXTRA_TEXT)?.toString(),
            extras.getCharSequenceArray(Notification.EXTRA_TEXT_LINES)
                ?.joinToString("\n") { it.toString() },
        ).firstOrNull { !it.isNullOrBlank() }?.trim().orEmpty()
        if (body.isEmpty()) return

        val now = System.currentTimeMillis()
        val key = "${posted.packageName}|$title|$body"
        synchronized(recentlyForwarded) {
            recentlyForwarded.entries.removeAll { now - it.value > 30_000L }
            if (recentlyForwarded.containsKey(key)) return
            recentlyForwarded[key] = now
        }

        val code = VerificationCodeExtractor.find(body)
        SmsRelayBridge.emit(
            mapOf(
                "messageId" to "sms-notification-${posted.key}-$now",
                "source" to "sms",
                "sender" to title.ifEmpty { "短信" },
                "body" to body,
                "timestamp" to posted.postTime,
                "isVerificationCode" to (code != null),
                "verificationCode" to code,
            ),
        )
    }

    private fun isSmsApplication(packageName: String): Boolean {
        val normalized = packageName.lowercase()
        return normalized in knownSmsPackages ||
            normalized.contains(".mms") ||
            normalized.contains("messaging")
    }

    companion object {
        private val knownSmsPackages = setOf(
            "com.android.mms",
            "com.android.messaging",
            "com.google.android.apps.messaging",
            "com.vivo.mms",
            "com.bbk.mms",
            "com.coloros.mms",
            "com.oplus.mms",
            "com.miui.mms",
            "com.samsung.android.messaging",
            "com.huawei.message",
        )
    }
}
