package com.hinge.office

import android.companion.CompanionDeviceManager
import android.content.Context
import android.content.pm.PackageManager
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28, 35], application = android.app.Application::class)
class CompanionAssociationTest {
    private lateinit var context: Context
    private lateinit var manager: CompanionDeviceManager
    private val address = "12:34:56:78:9A:BC"

    @Before fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        shadowOf(context.packageManager).setSystemFeature(PackageManager.FEATURE_COMPANION_DEVICE_SETUP, true)
        manager = context.getSystemService(CompanionDeviceManager::class.java)
        context.getSharedPreferences("hinge_companion", Context.MODE_PRIVATE).edit().clear().commit()
    }

    @Test fun restoredAddressDoesNotClaimSystemAssociation() {
        context.getSharedPreferences("hinge_companion", Context.MODE_PRIVATE).edit()
            .putString("windows_ble_address", address).commit()
        assertEquals(false, HingeCompanionManager.status(context)["associated"])
        assertFalse(HingeCompanionManager.startObserving(context, address))
        assertFalse(HingeCompanionManager.saveAssociation(context, address))
    }

    @Test fun realAssociationSurvivesMissingLocalPreferences() {
        shadowOf(manager).addAssociation(address)
        assertTrue(address.equals(HingeCompanionManager.associationAddress(context), ignoreCase = true))
        assertTrue(HingeCompanionManager.saveAssociation(context, address.lowercase()))
        assertEquals(true, HingeCompanionManager.status(context)["associated"])
    }

    @Test fun systemRevocationIsVisibleImmediately() {
        shadowOf(manager).addAssociation(address)
        assertTrue(HingeCompanionManager.saveAssociation(context, address))
        @Suppress("DEPRECATION")
        manager.disassociate(address)
        assertEquals(false, HingeCompanionManager.status(context)["associated"])
        assertTrue(HingeCompanionManager.removeAssociation(context))
        assertNull(context.getSharedPreferences("hinge_companion", Context.MODE_PRIVATE)
            .getString("windows_ble_address", null))
    }

    @Test fun multipleSystemAssociationsRequireExplicitSelection() {
        shadowOf(manager).addAssociation(address)
        shadowOf(manager).addAssociation("12:34:56:78:9A:BD")
        assertNull(HingeCompanionManager.associationAddress(context))
        assertFalse(HingeCompanionManager.removeAssociation(context))
        assertTrue(HingeCompanionManager.saveAssociation(context, address))
        assertTrue(address.equals(HingeCompanionManager.associationAddress(context), ignoreCase = true))
    }
}
