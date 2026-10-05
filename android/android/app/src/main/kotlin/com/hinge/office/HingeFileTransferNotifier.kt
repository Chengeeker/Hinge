package com.hinge.office

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import java.io.File

/** Shared received-file notification path for Activity and background receiver. */
internal object HingeFileTransferNotifier {
    const val ACTION_OPEN_RECEIVED_FILE = "com.hinge.office.OPEN_RECEIVED_FILE"
    const val EXTRA_RECEIVED_FILE_PATH = "com.hinge.office.RECEIVED_FILE_PATH"
    const val EXTRA_RECEIVED_FILE_MIME = "com.hinge.office.RECEIVED_FILE_MIME"
    const val CHANNEL_ID = "hinge_file_transfer"

    fun show(context: Context, path: String): Boolean {
        val file = File(path.trim())
        if (path.isBlank() || !file.isFile || !hasNotificationPermission(context)) {
            return false
        }
        val appContext = context.applicationContext
        val manager = appContext.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(
                    CHANNEL_ID,
                    "文件传输",
                    NotificationManager.IMPORTANCE_DEFAULT,
                ).apply { description = "Hinge 接收文件提醒" },
            )
        }
        val mimeType = HingeMimeDetector.fromName(file.name)
        val intent = Intent(appContext, MainActivity::class.java).apply {
            action = ACTION_OPEN_RECEIVED_FILE
            putExtra(EXTRA_RECEIVED_FILE_PATH, file.absolutePath)
            putExtra(EXTRA_RECEIVED_FILE_MIME, mimeType)
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or
                Intent.FLAG_ACTIVITY_CLEAR_TOP or
                Intent.FLAG_ACTIVITY_NEW_TASK
        }
        val pendingFlags = PendingIntent.FLAG_UPDATE_CURRENT or
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                PendingIntent.FLAG_IMMUTABLE
            } else {
                0
            }
        val pendingIntent = PendingIntent.getActivity(
            appContext,
            file.absolutePath.hashCode(),
            intent,
            pendingFlags,
        )
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(appContext, CHANNEL_ID)
        } else {
            Notification.Builder(appContext)
        }
        manager.notify(
            file.absolutePath.hashCode(),
            builder
                .setSmallIcon(android.R.drawable.stat_sys_download_done)
                .setContentTitle("收到文件")
                .setContentText("${file.name} · 点击使用默认应用打开")
                .setCategory(Notification.CATEGORY_PROGRESS)
                .setAutoCancel(true)
                // MainActivity may also refresh this notification when Flutter
                // receives the same completion event; don't alert twice.
                .setOnlyAlertOnce(true)
                .setContentIntent(pendingIntent)
                .build(),
        )
        return true
    }

    private fun hasNotificationPermission(context: Context): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
}
