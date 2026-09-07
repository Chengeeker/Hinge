package com.hinge.office

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.content.pm.ServiceInfo

/**
 * Keeps the LAN listener and the Dart process alive while the app is not in
 * the foreground. The notification is intentional: Android requires a
 * user-visible foreground service for a connection that continues in the
 * background.
 */
class HingeForegroundService : Service() {
    private var multicastLock: WifiManager.MulticastLock? = null
    private var wifiLock: WifiManager.WifiLock? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        acquireMulticastLock()
        acquireWifiLock()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }

        val notificationBuilder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }
        val notification = notificationBuilder
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle("Hinge 正在保持设备连接")
            .setContentText("局域网发现与设备会话在后台运行")
            .setCategory(Notification.CATEGORY_SERVICE)
            .setOngoing(persistentNotificationEnabled())
            .setAutoCancel(!persistentNotificationEnabled())
            .setContentIntent(mainActivityIntent())
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        return START_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // Some OEM launchers treat a removed task as an app-stop gesture. The
        // foreground service is the connection owner, so explicitly schedule
        // it again instead of allowing the LAN session to disappear with the
        // last activity task.
        try {
            val restart = Intent(this, HingeForegroundService::class.java).apply {
                action = ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(restart)
            } else {
                startService(restart)
            }
        } catch (_: Exception) {
            // The system may already be shutting the process down.
        }
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        releaseMulticastLock()
        releaseWifiLock()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "设备连接",
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "保持 Hinge 的局域网设备连接"
                setShowBadge(false)
            },
        )
    }

    private fun mainActivityIntent(): PendingIntent {
        val intent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                PendingIntent.FLAG_IMMUTABLE
            } else {
                0
            }
        return PendingIntent.getActivity(this, 0, intent, flags)
    }

    private fun acquireMulticastLock() {
        try {
            val wifiManager = applicationContext
                .getSystemService(Context.WIFI_SERVICE) as WifiManager
            multicastLock = wifiManager.createMulticastLock("HingeBackgroundDiscovery").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
            multicastLock = null
        }
    }

    private fun releaseMulticastLock() {
        try {
            if (multicastLock?.isHeld == true) multicastLock?.release()
        } catch (_: Exception) {
            // The Wi-Fi adapter may disappear while the service is stopping.
        } finally {
            multicastLock = null
        }
    }

    private fun acquireWifiLock() {
        try {
            val wifiManager = applicationContext
                .getSystemService(Context.WIFI_SERVICE) as WifiManager
            val mode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                WifiManager.WIFI_MODE_FULL_LOW_LATENCY
            } else {
                WifiManager.WIFI_MODE_FULL_HIGH_PERF
            }
            wifiLock = wifiManager.createWifiLock(mode, "HingeConnection").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
            wifiLock = null
        }
    }

    private fun releaseWifiLock() {
        try {
            if (wifiLock?.isHeld == true) wifiLock?.release()
        } catch (_: Exception) {
            // Wi-Fi may be disabled while the service is being torn down.
        } finally {
            wifiLock = null
        }
    }

    private fun persistentNotificationEnabled(): Boolean =
        getSharedPreferences("app_settings", MODE_PRIVATE)
            .getBoolean("persistent_notification_enabled", true)

    companion object {
        const val ACTION_START = "com.hinge.office.START_CONNECTION_SERVICE"
        const val ACTION_STOP = "com.hinge.office.STOP_CONNECTION_SERVICE"
        private const val CHANNEL_ID = "hinge_connection"
        private const val NOTIFICATION_ID = 52831
    }
}
