package com.hinge.office

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [24], application = android.app.Application::class)
class CompanionCompatibilityTest {
    @Test fun androidSevenDoesNotAccessUnavailableCompanionService() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        assertEquals(false, HingeCompanionManager.status(context)["associated"])
        assertFalse(HingeCompanionManager.saveAssociation(context, "12:34:56:78:9A:BC"))
        assertFalse(HingeCompanionManager.startObserving(context))
        assertTrue(HingeCompanionManager.removeAssociation(context))
    }
}
