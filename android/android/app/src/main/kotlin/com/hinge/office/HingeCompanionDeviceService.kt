package com.hinge.office

import android.companion.CompanionDeviceService

/**
 * Bound by Android when the associated Windows BLE advertiser appears.
 * Keeping this component tiny is intentional: it only starts the existing
 * native connection owner and never tries to carry the file transfer itself.
 */
class HingeCompanionDeviceService : CompanionDeviceService() {
    @Suppress("DEPRECATION")
    override fun onDeviceAppeared(address: String) {
        HingeCompanionManager.requestWake(
            applicationContext,
            "ble_appeared:${address.takeLast(5)}",
        )
    }

    @Suppress("DEPRECATION")
    override fun onDeviceDisappeared(address: String) {
        HingeDiagnostics.from(this).log(
            "companion_device_disappeared",
            mapOf("address" to address),
        )
    }
}
