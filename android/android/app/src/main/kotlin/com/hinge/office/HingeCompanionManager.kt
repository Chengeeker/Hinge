package com.hinge.office

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.companion.CompanionDeviceManager

/** Owns the durable association address and system presence observation. */
object HingeCompanionManager {
    private const val PREFERENCES = "hinge_companion"
    private const val ADDRESS_KEY = "windows_ble_address"

    fun isSupported(context: Context): Boolean {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O &&
            context.packageManager.hasSystemFeature(
                PackageManager.FEATURE_COMPANION_DEVICE_SETUP,
            )
    }

    fun status(context: Context): Map<String, Any> {
        val address = associationAddress(context)
        return mapOf(
            "supported" to isSupported(context),
            "associated" to !address.isNullOrBlank(),
            "address" to (address ?: ""),
            "presenceObservationSupported" to (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S),
        )
    }

    fun associationAddress(context: Context): String? = context
        .getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        .getString(ADDRESS_KEY, null)
        ?.trim()
        ?.takeIf { it.isNotEmpty() }

    fun saveAssociation(context: Context, address: String): Boolean {
        val cleanAddress = address.trim()
        if (cleanAddress.isEmpty()) return false
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .edit()
            .putString(ADDRESS_KEY, cleanAddress)
            .apply()
        // Association itself is durable even on API 26-30, where presence
        // observation is unavailable. Treat observation as best effort so the
        // UI does not report a successful pairing as a failure.
        startObserving(context, cleanAddress)
        return true
    }

    fun startObserving(context: Context, address: String? = associationAddress(context)): Boolean {
        if (!isSupported(context) || Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            return false
        }
        val cleanAddress = address?.trim().orEmpty()
        if (cleanAddress.isEmpty()) return false
        return try {
            val manager = context.getSystemService(CompanionDeviceManager::class.java)
                ?: return false
            @Suppress("DEPRECATION")
            manager.startObservingDevicePresence(cleanAddress)
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
        val address = associationAddress(context) ?: return true
        return try {
            if (isSupported(context)) {
                context.getSystemService(CompanionDeviceManager::class.java)
                    ?.disassociate(address)
            }
            context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
                .edit()
                .remove(ADDRESS_KEY)
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

    /** Starts the existing native service; it will immediately retry saved LAN peers. */
    fun requestWake(context: Context, reason: String) {
        val intent = Intent(context, HingeForegroundService::class.java).apply {
            action = HingeForegroundService.ACTION_WAKE
            putExtra(HingeForegroundService.EXTRA_WAKE_REASON, reason)
        }
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
            HingeDiagnostics.from(context).log(
                "companion_wake_requested",
                mapOf("reason" to reason),
            )
        } catch (error: Exception) {
            HingeDiagnostics.from(context).log(
                "companion_wake_start_failed",
                mapOf("reason" to reason, "error" to (error.message ?: error.javaClass.simpleName)),
            )
        }
    }
}
