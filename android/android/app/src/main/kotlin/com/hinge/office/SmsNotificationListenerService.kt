package com.hinge.office

import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import java.util.concurrent.ConcurrentHashMap

/**
 * NotificationListenerService is used for two explicit opt-in capabilities:
 * generic notification history and the SMS compatibility relay. The same
 * service is intentionally shared so Android only needs one system listener
 * authorization.
 */
class SmsNotificationListenerService : NotificationListenerService() {
    private val recentlyForwarded = LinkedHashMap<String, Long>()
    private val activeContentIntents = ConcurrentHashMap<String, PendingIntent>()
    private val historyStore by lazy { NotificationHistoryStore(applicationContext) }

    override fun onCreate() {
        super.onCreate()
        instance = this
    }

    override fun onListenerConnected() {
        super.onListenerConnected()
        // Capture notifications that are still active when the user enables
        // the listener. This is useful without pretending Android can rebuild
        // notifications that were already dismissed before authorization.
        if (NotificationHistorySettings.isEnabled(this)) {
            activeNotifications?.forEach(::handleNotification)
        }
    }

    override fun onNotificationPosted(sbn: StatusBarNotification?) {
        val posted = sbn ?: return
        handleNotification(posted)
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification?) {
        sbn?.key?.let(activeContentIntents::remove)
    }

    override fun onDestroy() {
        activeContentIntents.clear()
        historyStore.close()
        if (instance === this) instance = null
        super.onDestroy()
    }

    private fun handleNotification(posted: StatusBarNotification) {
        val notification = posted.notification ?: return
        // Do not record Hinge's own foreground-service/file notifications.
        if (posted.packageName == packageName) return

        notification.contentIntent?.let { activeContentIntents[posted.key] = it }

        val text = extractNotificationText(notification)
        val title = text.first
        val body = text.second
        if (title.isEmpty() && body.isEmpty()) return

        if (NotificationHistorySettings.isEnabled(this)) {
            val appName = applicationLabel(posted.packageName)
            val record = NotificationHistoryStore.Record(
                id = "${posted.packageName}|${posted.key}|${posted.postTime}",
                packageName = posted.packageName,
                appName = appName,
                title = title,
                content = body,
                timestamp = posted.postTime,
                category = notification.category.orEmpty(),
                ongoing = notification.flags and Notification.FLAG_ONGOING_EVENT != 0,
                notificationKey = posted.key,
            )
            if (historyStore.upsert(record)) {
                NotificationHistoryBridge.emit(
                    mapOf(
                        "id" to record.id,
                        "packageName" to record.packageName,
                        "appName" to record.appName,
                        "title" to record.title,
                        "content" to record.content,
                        "timestamp" to record.timestamp,
                    ),
                )
            }
        }

        if (!SmsRelaySettings.isEnabled(this) || !isSmsApplication(posted.packageName)) return
        if (notification.category != null &&
            notification.category != Notification.CATEGORY_MESSAGE &&
            notification.category != Notification.CATEGORY_EMAIL
        ) return
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

    private fun extractNotificationText(notification: Notification): Pair<String, String> {
        val extras = notification.extras
        val title = sequenceOf(
            extras.getCharSequence(Notification.EXTRA_TITLE_BIG)?.toString(),
            extras.getCharSequence(Notification.EXTRA_TITLE)?.toString(),
            extras.getCharSequence(Notification.EXTRA_SUB_TEXT)?.toString(),
        ).firstOrNull { !it.isNullOrBlank() }?.trim().orEmpty()

        val messagingText = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            try {
                @Suppress("DEPRECATION")
                val messages = notification.extras.getParcelableArray(
                    Notification.EXTRA_MESSAGES,
                )
                Notification.MessagingStyle.Message.getMessagesFromBundleArray(messages)
                    .lastOrNull()
                    ?.getText()
                    ?.toString()
            } catch (_: Exception) {
                null
            }
        } else {
            null
        }
        val body = sequenceOf(
            extras.getCharSequence(Notification.EXTRA_BIG_TEXT)?.toString(),
            extras.getCharSequence(Notification.EXTRA_TEXT)?.toString(),
            extras.getCharSequenceArray(Notification.EXTRA_TEXT_LINES)
                ?.joinToString("\n") { it.toString() },
            messagingText,
        ).firstOrNull { !it.isNullOrBlank() }?.trim().orEmpty()
        return title to body
    }

    private fun applicationLabel(packageName: String): String {
        return try {
            packageManager.getApplicationInfo(packageName, 0)
                .loadLabel(packageManager)
                .toString()
        } catch (_: Exception) {
            packageName
        }
    }

    companion object {
        @Volatile
        private var instance: SmsNotificationListenerService? = null

        fun openHistoryItem(context: Context, packageName: String, notificationKey: String): Boolean {
            val service = instance
            if (service != null && notificationKey.isNotBlank()) {
                val pendingIntent = service.activeContentIntents[notificationKey]
                if (pendingIntent != null) {
                    try {
                        pendingIntent.send()
                        return true
                    } catch (_: PendingIntent.CanceledException) {
                        service.activeContentIntents.remove(notificationKey)
                    }
                }
            }

            if (packageName.isBlank()) return false
            val launchIntent = context.packageManager.getLaunchIntentForPackage(packageName)
                ?: return false
            launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            return try {
                context.startActivity(launchIntent)
                true
            } catch (_: Exception) {
                false
            }
        }

        fun captureActiveNotifications() {
            instance?.takeIf { NotificationHistorySettings.isEnabled(it) }
                ?.activeNotifications
                ?.forEach { instance?.handleNotification(it) }
        }
    }

    private fun isSmsApplication(packageName: String): Boolean {
        val normalized = packageName.lowercase()
        return normalized in knownSmsPackages ||
            normalized.contains(".mms") ||
            normalized.contains("messaging")
    }

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
