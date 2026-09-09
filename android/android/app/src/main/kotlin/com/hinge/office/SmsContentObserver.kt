package com.hinge.office

import android.Manifest
import android.content.Context
import android.database.ContentObserver
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Telephony
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * Fallback for devices that do not deliver every verification SMS through
 * SMS_RECEIVED. It watches only rows inserted after registration; it never
 * scans or forwards the existing inbox.
 */
class SmsContentObserver(context: Context) :
    ContentObserver(Handler(Looper.getMainLooper())) {
    private val appContext = context.applicationContext
    private val resolver = appContext.contentResolver
    private val executor: ExecutorService = Executors.newSingleThreadExecutor()
    private var registered = false
    private var lastSeenId = 0L
    @Volatile
    private var acceptingChanges = false

    fun start() {
        if (registered || !hasReadPermission()) return
        lastSeenId = queryLatestId()
        try {
            resolver.registerContentObserver(
                Telephony.Sms.Inbox.CONTENT_URI,
                true,
                this,
            )
            registered = true
            acceptingChanges = true
        } catch (_: SecurityException) {
            registered = false
            acceptingChanges = false
        } catch (_: Exception) {
            registered = false
            acceptingChanges = false
        }
    }

    fun stop() {
        acceptingChanges = false
        if (!registered) return
        try {
            resolver.unregisterContentObserver(this)
        } catch (_: Exception) {
            // The provider may disappear while the app is stopping.
        } finally {
            registered = false
        }
    }

    fun close() {
        stop()
        executor.shutdownNow()
    }

    override fun onChange(selfChange: Boolean) {
        if (!acceptingChanges) return
        executor.execute { emitNewMessages() }
    }

    private fun emitNewMessages() {
        if (!acceptingChanges ||
            !SmsRelaySettings.isEnabled(appContext) ||
            !hasReadPermission()
        ) {
            return
        }

        val projection = arrayOf("_id", "address", "body", "date")
        try {
            resolver.query(
                Telephony.Sms.Inbox.CONTENT_URI,
                projection,
                "_id > ?",
                arrayOf(lastSeenId.toString()),
                "_id ASC",
            )?.use { cursor ->
                val idIndex = cursor.getColumnIndex("_id")
                val addressIndex = cursor.getColumnIndex("address")
                val bodyIndex = cursor.getColumnIndex("body")
                val dateIndex = cursor.getColumnIndex("date")
                if (idIndex < 0 || bodyIndex < 0) return

                while (cursor.moveToNext()) {
                    val rowId = cursor.getLong(idIndex)
                    if (rowId > lastSeenId) lastSeenId = rowId
                    val body = cursor.getString(bodyIndex).orEmpty().trim()
                    if (body.isEmpty()) continue

                    val sender = if (addressIndex >= 0) {
                        cursor.getString(addressIndex)?.trim().orEmpty()
                    } else {
                        ""
                    }.ifEmpty { "未知号码" }
                    val timestamp = if (dateIndex >= 0) {
                        cursor.getLong(dateIndex)
                    } else {
                        System.currentTimeMillis()
                    }
                    val code = VerificationCodeExtractor.find(body)
                    SmsRelayBridge.emit(
                        mapOf(
                            "messageId" to "sms-provider-$rowId",
                            "source" to "sms",
                            "sender" to sender,
                            "body" to body,
                            "timestamp" to timestamp,
                            "isVerificationCode" to (code != null),
                            "verificationCode" to code,
                        ),
                    )
                }
            }
        } catch (_: SecurityException) {
            // READ_SMS may be revoked by the system while the observer lives.
        } catch (_: Exception) {
            // Provider implementations differ across Android vendors.
        }
    }

    private fun queryLatestId(): Long {
        if (!hasReadPermission()) return 0L
        return try {
            resolver.query(
                Telephony.Sms.Inbox.CONTENT_URI,
                arrayOf("_id"),
                null,
                null,
                "_id DESC",
            )?.use { cursor ->
                if (cursor.moveToFirst()) cursor.getLong(0) else 0L
            } ?: 0L
        } catch (_: Exception) {
            0L
        }
    }

    private fun hasReadPermission(): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            appContext.checkSelfPermission(Manifest.permission.READ_SMS) ==
            android.content.pm.PackageManager.PERMISSION_GRANTED
    }
}
