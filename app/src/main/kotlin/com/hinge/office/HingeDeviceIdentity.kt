package com.hinge.office

import android.content.Context
import android.os.Build
import android.provider.Settings
import org.json.JSONObject
import java.io.File
import java.security.SecureRandom
import java.util.UUID

/**
 * The network identity must belong to the physical Android installation, not
 * to a restoreable identity.json. Migrating an app backup between phones can
 * copy a random legacy UUID and make two phones look like one TCP peer.
 *
 * Android 8+ provides an app-signing-key/user/device scoped ANDROID_ID. We
 * derive an app-domain-separated, pseudonymous UUID; never transmit or store
 * the raw Android ID. This deliberately migrates legacy UUIDs once, without
 * clearing other app files, settings, transfer history or trusted peers.
 */
internal fun sameDeviceId(left: String?, right: String?): Boolean =
    !left.isNullOrBlank() && !right.isNullOrBlank() && left.equals(right, ignoreCase = true)

internal object HingeDeviceIdentity {
    private const val NAMESPACE = "com.hinge.office/android-physical-device/v1/"

    fun stableId(androidId: String): String =
        UUID.nameUUIDFromBytes(
            (NAMESPACE + androidId.trim().lowercase()).toByteArray(Charsets.UTF_8)
        ).toString()

    @Synchronized
    fun loadOrCreate(
        context: Context,
        hardware: Map<String, Any?> = emptyMap(),
    ): Map<String, Any?> {
        val file = File(context.filesDir, "identity.json")
        val legacy = System.getenv("HOME")?.takeIf { it.isNotBlank() }
            ?.let { File(it, ".hinge/identity.json") }
        val old = listOfNotNull(file, legacy).firstNotNullOfOrNull { candidate ->
            runCatching {
                JSONObject(atomicRead(candidate)).toMap()
                    .takeIf { it.text("deviceId").isNotBlank() }
            }.getOrNull()
        }
        val androidId = runCatching {
            Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
        }.getOrNull()?.takeIf { it.isNotBlank() }

        // ANDROID_ID is available on supported modern Android devices. The
        // no-backup fallback also avoids cloning an identity via Auto Backup.
        val resolvedId = androidId?.let(::stableId) ?: fallbackId(context)
        val migrated = old?.text("deviceId") != resolvedId
        // A copied backup may contain both a former phone's display name and
        // its public key. Neither is a valid identity for this physical phone.
        val base = if (migrated) (old ?: emptyMap()) + mapOf(
            "publicKey" to ByteArray(32)
                .also { SecureRandom().nextBytes(it) }
                .joinToString("") { "%02x".format(it) }
        ) else old.orEmpty()
        val deviceName = hardware["name"]?.toString()?.takeIf { it.isNotBlank() }
            ?: if (migrated) "${Build.MANUFACTURER} ${Build.MODEL}"
            else old?.text("name", Build.MODEL) ?: Build.MODEL
        val value = base + mapOf(
            "deviceId" to resolvedId,
            "name" to deviceName,
            "manufacturer" to (hardware["manufacturer"]?.toString()?.takeIf { it.isNotBlank() }
                ?: Build.MANUFACTURER),
            "model" to (hardware["model"]?.toString()?.takeIf { it.isNotBlank() }
                ?: Build.MODEL),
            "identityScheme" to "android-physical-v1"
        )
        if (old != value || !file.exists()) atomicWrite(file, JSONObject(value).toString())
        return value
    }

    private fun fallbackId(context: Context): String {
        val file = File(context.noBackupFilesDir, "hinge-physical-identity.txt")
        val existing = runCatching { file.readText().trim() }.getOrNull()
        if (!existing.isNullOrBlank()) return existing
        val value = UUID.randomUUID().toString()
        file.parentFile?.mkdirs()
        atomicWrite(file, value)
        return value
    }
}
