package com.hinge.office

import android.companion.CompanionDeviceService
import android.companion.DevicePresenceEvent

/**
 * Bound by Android when the associated Windows BLE advertiser appears.
 * Keeping this component tiny is intentional: it only starts the existing
 * native connection owner and never tries to carry the file transfer itself.
 */
@androidx.annotation.RequiresApi(31)
class HingeCompanionDeviceService : CompanionDeviceService() {
    @Suppress("DEPRECATION")
    override fun onDeviceAppeared(address: String) {
        HingeCompanionManager.requestBleWake(applicationContext, address)
    }

    @androidx.annotation.RequiresApi(36)
    override fun onDevicePresenceEvent(event: DevicePresenceEvent) {
        if (event.event == DevicePresenceEvent.EVENT_BLE_APPEARED &&
            event.associationId == HingeCompanionManager.selectedAssociationId(applicationContext)
        ) {
            HingeCompanionManager.requestBleWake(applicationContext)
        }
    }

    @Suppress("DEPRECATION")
    override fun onDeviceDisappeared(address: String) {
        HingeDiagnostics.from(this).log(
            "companion_device_disappeared",
            mapOf("address" to address),
        )
    }
}
