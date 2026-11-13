package com.hinge.office

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.SystemClock
import android.util.Base64
import android.companion.AssociationInfo
import android.companion.CompanionDeviceManager
import android.companion.ObservingDevicePresenceRequest
import androidx.core.content.edit

/** Owns the durable association address and system presence observation. */
object HingeCompanionManager {
    internal const val BLE_WAKE_REASON = "ble_appeared"
    internal const val BT_WAKE_REASON = "bt_connected"
    private const val PREFERENCES = "hinge_companion"
    private const val ADDRESS_KEY = "windows_ble_address"
    private const val PAYLOAD_KEY = "windows_ble_payload"
    private const val PAYLOAD_ASSOCIATION_KEY = "windows_ble_payload_association"
    private var lastBleWakeAt: Long? = null

    fun isSupported(context: Context): Boolean {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
            context.packageManager.hasSystemFeature(
                PackageManager.FEATURE_COMPANION_DEVICE_SETUP,
            )
    }

    fun status(context: Context): Map<String, Any> {
        val address = associationAddress(context)
        val hasPayload = address != null && boundPayload(context) != null
        return mapOf(
            "supported" to isSupported(context),
            "associated" to !address.isNullOrBlank(),
            "address" to (address ?: ""),
            "presenceObservationSupported" to (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S),
            "scanNeedsPermission" to (hasPayload && !HingeBleWakeReceiver.hasScanPermission(context)),
            "scanNeedsBindingPayload" to (Build.VERSION.SDK_INT >= 34 && address != null && !hasPayload),
        )
    }

    private fun associatedAddresses(context: Context): List<String>? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || !isSupported(context)) return emptyList()
        return try {
            val manager = context.getSystemService(CompanionDeviceManager::class.java) ?: return null
            if (Build.VERSION.SDK_INT >= 33) {
                manager.myAssociations.mapNotNull { it.deviceMacAddress?.toString() }
            } else {
                @Suppress("DEPRECATION")
                manager.associations
            }
        } catch (error: Exception) {
            HingeDiagnostics.from(context).log("companion_association_check_failed",
                mapOf("reason" to error.javaClass.simpleName))
            null
        }
    }

    fun associationAddress(context: Context): String? {
        val saved = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .getString(ADDRESS_KEY, null)?.trim()
        val addresses = associatedAddresses(context) ?: return null
        // Restored preferences are not a system association on a replacement phone.
        return addresses.firstOrNull { it.equals(saved, ignoreCase = true) }
            ?: addresses.singleOrNull()
    }

    fun saveAssociation(context: Context, address: String): Boolean {
        val cleanAddress = associatedAddresses(context)
            ?.firstOrNull { it.equals(address.trim(), ignoreCase = true) } ?: return false
        val preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        if (Build.VERSION.SDK_INT >= 34 &&
            !cleanAddress.equals(preferences.getString(ADDRESS_KEY, null), ignoreCase = true)
        ) {
            HingeBleWakeReceiver.stop(context)
        }
        preferences
            .edit()
            .putString(ADDRESS_KEY, cleanAddress)
            .apply()
        // Association itself is durable even on API 26-30, where presence
        // observation is unavailable. Treat observation as best effort so the
        // UI does not report a successful pairing as a failure.
        startObserving(context, cleanAddress)
        HingeBleWakeReceiver.start(context)
        return true
    }

    fun startObserving(context: Context, address: String? = associationAddress(context)): Boolean {
        if (!isSupported(context) || Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            return false
        }
        val cleanAddress = associatedAddresses(context)
            ?.firstOrNull { it.equals(address?.trim(), ignoreCase = true) } ?: return false
        return try {
            val manager = context.getSystemService(CompanionDeviceManager::class.java)
                ?: return false
            if (Build.VERSION.SDK_INT >= 36) {
                val id = manager.myAssociations
                    .firstOrNull { cleanAddress.equals(it.deviceMacAddress?.toString(), ignoreCase = true) }
                    ?.id ?: return false
                val request = ObservingDevicePresenceRequest.Builder().setAssociationId(id).build()
                manager.startObservingDevicePresence(request)
            } else {
                @Suppress("DEPRECATION")
                manager.startObservingDevicePresence(cleanAddress)
            }
            HingeDiagnostics.from(context).log(
                "companion_presence_observing",
                mapOf("address" to cleanAddress),
            )
            true
        } catch (error: Exception) {
            HingeDiagnostics.from(context).log(
                "companion_presence_observe_failed",
                mapOf("error" to (error.message ?: error.javaClass.simpleName)),
            )
            false
        }
    }

    fun removeAssociation(context: Context): Boolean {
        val addresses = associatedAddresses(context) ?: return false
        val address = associationAddress(context)
        if (address == null && addresses.isNotEmpty()) return false
        return try {
            if (address != null && isSupported(context)) {
                context.getSystemService(CompanionDeviceManager::class.java)
                    ?.disassociate(address)
            }
            if (Build.VERSION.SDK_INT >= 34) HingeBleWakeReceiver.stop(context)
            context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
                .edit()
                .remove(ADDRESS_KEY)
                .remove(PAYLOAD_KEY)
                .remove(PAYLOAD_ASSOCIATION_KEY)
                .apply()
            HingeDiagnostics.from(context).log(
                "companion_association_removed",
                mapOf("address" to address),
            )
            true
        } catch (error: Exception) {
            HingeDiagnostics.from(context).log(
                "companion_association_remove_failed",
                mapOf("error" to (error.message ?: error.javaClass.simpleName)),
            )
            false
        }
    }

    @androidx.annotation.RequiresApi(33)
    private fun selectedAssociation(context: Context): AssociationInfo? {
        val address = associationAddress(context) ?: return null
        return runCatching {
            context.getSystemService(CompanionDeviceManager::class.java)?.myAssociations
                ?.firstOrNull { address.equals(it.deviceMacAddress?.toString(), ignoreCase = true) }
        }.getOrNull()
    }

    @androidx.annotation.RequiresApi(33)
    internal fun selectedAssociationId(context: Context): Int? = selectedAssociation(context)?.id

    internal fun boundPayload(context: Context): ByteArray? {
        if (Build.VERSION.SDK_INT < 34) return null
        val association = selectedAssociation(context) ?: return null
        val key = "${association.id}:${association.deviceMacAddress}"
        val preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        // Android does not persist AssociatedDevice across system restarts.
        val payload = association.associatedDevice?.bleDevice?.scanRecord
            ?.getManufacturerSpecificData(HingeWakeProtocol.COMPANY_ID)
        if (payload != null && HingeWakeProtocol.isWakePayload(payload)) {
            val encoded = Base64.encodeToString(payload, Base64.NO_WRAP)
            if (preferences.getString(PAYLOAD_ASSOCIATION_KEY, null) != key ||
                preferences.getString(PAYLOAD_KEY, null) != encoded
            ) {
                preferences.edit {
                    putString(PAYLOAD_ASSOCIATION_KEY, key)
                    putString(PAYLOAD_KEY, encoded)
                }
            }
            return payload
        }
        if (preferences.getString(PAYLOAD_ASSOCIATION_KEY, null) != key) return null
        val stored = preferences.getString(PAYLOAD_KEY, null) ?: return null
        return runCatching { Base64.decode(stored, Base64.NO_WRAP) }.getOrNull()
            ?.takeIf(HingeWakeProtocol::isWakePayload)
    }

    internal fun requestBleWakeForAddress(
        context: Context,
        address: String,
        reason: String = BLE_WAKE_REASON,
    ) {
        if (address.equals(associationAddress(context), ignoreCase = true)) {
            requestBleWake(context, reason)
        }
    }

    @androidx.annotation.RequiresApi(33)
    internal fun requestBleWakeForAssociation(context: Context, associationId: Int, reason: String) {
        if (associationId != selectedAssociationId(context)) {
            HingeDiagnostics.from(context).log(
                "companion_presence_ignored",
                mapOf("reason" to "association_mismatch", "event" to reason),
            )
            return
        }
        requestBleWake(context, reason)
    }

    internal fun requestBleWake(context: Context, reason: String = BLE_WAKE_REASON) {
        val now = SystemClock.elapsedRealtime()
        if (lastBleWakeAt?.let { now - it < 8_000L } == true) return
        if (requestWake(context, reason)) lastBleWakeAt = now
    }

    /** Starts the existing native service; it will immediately retry saved LAN peers. */
    fun requestWake(context: Context, reason: String): Boolean {
        val intent = Intent(context, HingeForegroundService::class.java).apply {
            action = HingeForegroundService.ACTION_WAKE
            putExtra(HingeForegroundService.EXTRA_WAKE_REASON, reason)
        }
        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
            HingeDiagnostics.from(context).log(
                "companion_wake_requested",
                mapOf("reason" to reason),
            )
            true
        } catch (error: Exception) {
            HingeDiagnostics.from(context).log(
                "companion_wake_start_failed",
                mapOf("reason" to reason, "error" to (error.message ?: error.javaClass.simpleName)),
            )
            false
        }
    }
}
