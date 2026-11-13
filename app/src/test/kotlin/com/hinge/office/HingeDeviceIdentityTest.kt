package com.hinge.office

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.UUID

class HingeDeviceIdentityTest {
    @Test fun stableOnSamePhysicalAndroidDevice() {
        val first = HingeDeviceIdentity.stableId("7afced1234567890")
        assertEquals(first, HingeDeviceIdentity.stableId("7AFCED1234567890"))
        UUID.fromString(first)
    }

    @Test fun restoringSameIdentityJsonToDifferentPhoneCannotCloneNetworkIdentity() {
        val phoneA = HingeDeviceIdentity.stableId("7afced1234567890")
        val phoneB = HingeDeviceIdentity.stableId("2afced1234567890")
        assertNotEquals(phoneA, phoneB)
        assertTrue(phoneA.isNotBlank())
        assertTrue(phoneB.isNotBlank())
    }
}
