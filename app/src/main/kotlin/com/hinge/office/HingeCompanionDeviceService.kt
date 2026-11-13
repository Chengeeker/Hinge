package com.hinge.office

import android.companion.CompanionDeviceService
import android.companion.AssociationInfo
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
        HingeDiagnostics.from(this).log("companion_device_appeared_callback", mapOf("api" to "address"))
        HingeCompanionManager.requestBleWakeForAddress(applicationContext, address)
    }

    @androidx.annotation.RequiresApi(33)
    override fun onDeviceAppeared(associationInfo: AssociationInfo) {
        HingeDiagnostics.from(this).log(
            "companion_device_appeared_callback",
            mapOf("api" to "association", "associationId" to associationInfo.id),
        )
        HingeCompanionManager.requestBleWakeForAssociation(
            applicationContext,
            associationInfo.id,
            HingeCompanionManager.BLE_WAKE_REASON,
        )
    }

    @androidx.annotation.RequiresApi(36)
    override fun onDevicePresenceEvent(event: DevicePresenceEvent) {
        HingeDiagnostics.from(this).log(
            "companion_presence_event_callback",
            mapOf("event" to event.event, "associationId" to event.associationId),
        )
        val reason = when (event.event) {
            DevicePresenceEvent.EVENT_BLE_APPEARED -> HingeCompanionManager.BLE_WAKE_REASON
            DevicePresenceEvent.EVENT_BT_CONNECTED -> HingeCompanionManager.BT_WAKE_REASON
            else -> return
        }
        if (shouldWakeForPresenceEvent(event.event, event.associationId,
                HingeCompanionManager.selectedAssociationId(applicationContext))
        ) {
            HingeCompanionManager.requestBleWakeForAssociation(
                applicationContext,
                event.associationId,
                reason,
            )
        }
    }

    @Suppress("DEPRECATION")
    override fun onDeviceDisappeared(address: String) {
        HingeDiagnostics.from(this).log(
            "companion_device_disappeared",
            mapOf("address" to address),
        )
    }

    @androidx.annotation.RequiresApi(33)
    override fun onDeviceDisappeared(associationInfo: AssociationInfo) {
        HingeDiagnostics.from(this).log(
            "companion_device_disappeared",
            mapOf("associationId" to associationInfo.id),
        )
    }
}

@androidx.annotation.RequiresApi(36)
internal fun shouldWakeForPresenceEvent(
    event: Int,
    associationId: Int,
    selectedAssociationId: Int?,
): Boolean = associationId == selectedAssociationId &&
    (event == DevicePresenceEvent.EVENT_BLE_APPEARED ||
        event == DevicePresenceEvent.EVENT_BT_CONNECTED)
