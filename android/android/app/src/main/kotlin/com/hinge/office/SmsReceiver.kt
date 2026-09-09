package com.hinge.office

import android.Manifest
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Telephony
import java.util.UUID
import java.util.regex.Pattern

class SmsReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent?) {
        if (intent?.action != Telephony.Sms.Intents.SMS_RECEIVED_ACTION ||
            !SmsRelaySettings.isEnabled(context) ||
            !hasPermission(context, Manifest.permission.RECEIVE_SMS)
        ) {
            return
        }

        val messages = try {
            Telephony.Sms.Intents.getMessagesFromIntent(intent)
        } catch (_: Exception) {
            null
        } ?: return
        if (messages.isEmpty()) return

        val body = messages.joinToString(separator = "") { it.messageBody.orEmpty() }.trim()
        if (body.isEmpty()) return
        val sender = messages.firstNotNullOfOrNull { it.originatingAddress?.trim() }
            ?.takeIf { it.isNotEmpty() }
            ?: "未知号码"
        val code = VerificationCodeExtractor.find(body)

        SmsRelayBridge.emit(
            mapOf(
                "messageId" to UUID.randomUUID().toString(),
                "source" to "sms",
                "sender" to sender,
                "body" to body,
                "timestamp" to System.currentTimeMillis(),
                "isVerificationCode" to (code != null),
                "verificationCode" to code,
            ),
        )
    }

    private fun hasPermission(context: Context, permission: String): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
    }
}

/** MMS broadcasts do not expose a portable text body to a non-default SMS app.
 * We still forward the arrival event without reading the inbox or attachment.
 */
class MmsReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent?) {
        if (intent?.action != "android.provider.Telephony.WAP_PUSH_RECEIVED" ||
            !SmsRelaySettings.isEnabled(context) ||
            !hasPermission(context, Manifest.permission.RECEIVE_MMS)
        ) {
            return
        }

        SmsRelayBridge.emit(
            mapOf(
                "messageId" to UUID.randomUUID().toString(),
                "source" to "mms",
                "sender" to (intent.getStringExtra("address") ?: "未知号码"),
                "body" to "收到一条彩信，请在手机短信应用中查看",
                "timestamp" to System.currentTimeMillis(),
                "isVerificationCode" to false,
            ),
        )
    }

    private fun hasPermission(context: Context, permission: String): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
    }
}

internal object VerificationCodeExtractor {
    private val keywordPattern = Pattern.compile(
        "验证码|校验码|动态码|确认码|安全码|登录码|提取码|verification\\s+code|one[- ]time\\s+password|otp|security\\s+code",
        Pattern.CASE_INSENSITIVE,
    )
    private val codePattern = Pattern.compile("(?<!\\d)\\d{4,8}(?!\\d)")

    fun find(body: String): String? {
        if (!keywordPattern.matcher(body).find()) return null
        val matcher = codePattern.matcher(body)
        return if (matcher.find()) matcher.group() else null
    }
}

internal object SmsRelaySettings {
    private const val PREFS = "app_settings"
    private const val KEY_ENABLED = "sms_relay_enabled"

    fun isEnabled(context: Context): Boolean = context
        .getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        .getBoolean(KEY_ENABLED, false)
}
