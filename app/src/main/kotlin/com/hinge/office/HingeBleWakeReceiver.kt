package com.hinge.office

import android.Manifest
import android.app.PendingIntent
import android.bluetooth.BluetoothManager
import android.bluetooth.le.BluetoothLeScanner
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build

/** Android Companion presence matches an address. This scan matches the paired host's beacon. */
class HingeBleWakeReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (Build.VERSION.SDK_INT < 34 || intent.action != ACTION_SCAN) return
        val error = intent.getIntExtra(BluetoothLeScanner.EXTRA_ERROR_CODE, 0)
        if (error != 0) {
            HingeDiagnostics.from(context).log("companion_scan_failed", mapOf("code" to error))
            return
        }
        val bound = HingeCompanionManager.boundPayload(context) ?: return
        val results = intent.getParcelableArrayListExtra(
            BluetoothLeScanner.EXTRA_LIST_SCAN_RESULT, ScanResult::class.java,
        )
        if (results.orEmpty().any { result ->
                val received = result.scanRecord?.getManufacturerSpecificData(HingeWakeProtocol.COMPANY_ID)
                received != null && HingeWakeProtocol.matchesHost(bound, received)
            }
        ) {
            HingeCompanionManager.requestBleWake(context)
        }
    }

    companion object {
        private const val ACTION_SCAN = "com.hinge.office.BLE_WAKE_SCAN"

        fun hasScanPermission(context: Context): Boolean =
            Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                context.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED

        fun start(context: Context) {
            if (Build.VERSION.SDK_INT < 34) return
            val bound = HingeCompanionManager.boundPayload(context) ?: return
            if (!hasScanPermission(context)) return
            try {
                val scanner = scanner(context) ?: return
                val filter = ScanFilter.Builder()
                    .setManufacturerData(HingeWakeProtocol.COMPANY_ID, bound, HingeWakeProtocol.HOST_FILTER_MASK)
                    .build()
                val settings = ScanSettings.Builder()
                    .setScanMode(ScanSettings.SCAN_MODE_LOW_POWER)
                    .setCallbackType(ScanSettings.CALLBACK_TYPE_FIRST_MATCH)
                    .build()
                val callback = callback(context, PendingIntent.FLAG_UPDATE_CURRENT) ?: return
                val code = scanner.startScan(listOf(filter), settings, callback)
                if (code != ScanCallback.SCAN_FAILED_ALREADY_STARTED) {
                    HingeDiagnostics.from(context).log(
                        if (code == 0) "companion_scan_registered" else "companion_scan_failed",
                        mapOf("code" to code),
                    )
                }
            } catch (error: Exception) {
                HingeDiagnostics.from(context).log("companion_scan_failed",
                    mapOf("reason" to error.javaClass.simpleName))
            }
        }

        @androidx.annotation.RequiresApi(34)
        fun stop(context: Context) {
            val callback = callback(context, PendingIntent.FLAG_NO_CREATE) ?: return
            try {
                if (hasScanPermission(context)) scanner(context)?.stopScan(callback)
            } catch (_: SecurityException) {
                // Permission may have been revoked since registration.
            } finally {
                callback.cancel()
            }
        }

        private fun scanner(context: Context): BluetoothLeScanner? =
            context.getSystemService(BluetoothManager::class.java)?.adapter?.bluetoothLeScanner

        @androidx.annotation.RequiresApi(34)
        private fun callback(context: Context, flags: Int): PendingIntent? =
            PendingIntent.getBroadcast(context, 0,
                Intent(context, HingeBleWakeReceiver::class.java).setAction(ACTION_SCAN),
                flags or PendingIntent.FLAG_MUTABLE)
    }
}
