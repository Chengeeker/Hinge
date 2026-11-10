package com.hinge.office

import android.content.ActivityNotFoundException
import android.content.Context
import android.provider.Settings
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.Implements
import org.robolectric.annotation.Implementation
import org.robolectric.shadows.ShadowContentResolver

@Implements(android.content.ContentResolver::class)
class UnavailableClipboardResolver {
    @Implementation
    fun query(uri: android.net.Uri, projection: Array<String>?, selection: String?,
        selectionArgs: Array<String>?, sortOrder: String?): android.database.Cursor? = throw SecurityException("Blocked by system")
}

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = android.app.Application::class)
class ClipboardSyncTest {
    @Test fun shellReadsLivePhonePolicy() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val prefs = context.getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        prefs.edit().clear().commit()
        assertTrue(ClipboardSyncSettings.enabled(context))
        val controller = Robolectric.buildContentProvider(HingeDiagnosticsProvider::class.java).create()
        ShadowContentResolver.registerProviderInternal("com.hinge.office.diagnostics", controller.get())
        assertTrue(ClipboardSyncSettings.allowedByPhone(context.contentResolver))
        prefs.edit().putBoolean(ClipboardSyncSettings.KEY, false).commit()
        assertFalse(ClipboardSyncSettings.allowedByPhone(context.contentResolver))
        prefs.edit().putBoolean(ClipboardSyncSettings.KEY, true).commit()
        assertTrue(ClipboardSyncSettings.allowedByPhone(context.contentResolver))
    }

    @Test @Config(shadows = [UnavailableClipboardResolver::class])
    fun unavailablePolicyFailsClosed() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        assertFalse(ClipboardSyncSettings.allowedByPhone(context.contentResolver))
    }

    @Test fun policyProviderIsProtectedFromOtherApps() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val info = context.packageManager.getProviderInfo(android.content.ComponentName(context, HingeDiagnosticsProvider::class.java), 0)
        assertEquals("android.permission.DUMP", info.readPermission)
    }

    @Test fun scannerTriesDirectThenFallsBackWithoutLaunchingTwice() {
        val attempts = mutableListOf<String>()
        val result = launchDebuggingSettings(true, 35) {
            attempts += it.component?.className ?: it.action.orEmpty()
            if (it.component != null) throw SecurityException("Not exported")
        }
        assertEquals(DebuggingDestination.Wireless, result)
        assertEquals(listOf("com.android.settings.development.AdbQrCodeActivity", "android.service.quicksettings.action.QS_TILE_PREFERENCES"), attempts)
        assertTrue(debuggingGuidance(result, true).contains("使用二维码配对设备"))
        var count = 0
        assertEquals(DebuggingDestination.Scanner, launchDebuggingSettings(true, 35) { count++ })
        assertEquals(1, count)
    }

    @Test fun codePairingSkipsScannerAndOemFallbackKeepsNextStepClear() {
        val attempts = mutableListOf<String?>()
        val result = launchDebuggingSettings(false, 30) {
            assertNull(it.component)
            attempts += it.action
            if (it.action != Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS) throw ActivityNotFoundException()
        }
        assertEquals(DebuggingDestination.Developer, result)
        assertEquals(2, attempts.size)
        assertTrue(debuggingGuidance(result, false).contains("使用配对码配对设备"))
    }

    @Test fun unsupportedAndMissingSettingsNeverReportSuccess() {
        assertEquals(DebuggingDestination.Unavailable, launchDebuggingSettings(true, 29) { fail("Must not launch") })
        assertEquals(DebuggingDestination.Unavailable, launchDebuggingSettings(true, 35) { throw ActivityNotFoundException() })
        assertEquals(DebuggingDestination.DeviceInfo, launchDebuggingSettings(true, 35, developerEnabled = false) {
            assertEquals(Settings.ACTION_DEVICE_INFO_SETTINGS, it.action)
        })
    }

    @Test fun quickSettingsShortcutUsesSystemTileAndStopsOnSuccess() {
        var attempts = 0
        val result = launchDebuggingSettings(false, 35) {
            attempts++
            if (it.action == "android.settings.WIRELESS_DEBUGGING_SETTINGS") throw ActivityNotFoundException()
            assertEquals("android.service.quicksettings.action.QS_TILE_PREFERENCES", it.action)
            assertEquals("com.android.settings", it.`package`)
            val component = it.getParcelableExtra<android.content.ComponentName>(android.content.Intent.EXTRA_COMPONENT_NAME)
            assertEquals("com.android.settings.development.qstile.DevelopmentTiles\$WirelessDebugging", component?.className)
        }
        assertEquals(DebuggingDestination.Wireless, result)
        assertEquals(1, attempts)
    }
    @Test fun exportedPageTakesPriorityAndPermissionFailureFallsBack() {
        val page = android.content.ComponentName("com.vivo.settings", "com.vivo.settings.Settings\$WirelessDebuggingActivity")
        val attempts = mutableListOf<android.content.Intent>()
        assertEquals(DebuggingDestination.Wireless, launchDebuggingSettings(false, 35, directPages = listOf(page)) {
            attempts += it
        })
        assertEquals(page, attempts.single().component)
        attempts.clear()
        launchDebuggingSettings(false, 35, directPages = listOf(page)) {
            attempts += it
            if (it.component != null) throw SecurityException("Private page")
        }
        assertEquals(2, attempts.size)
        assertEquals("android.service.quicksettings.action.QS_TILE_PREFERENCES", attempts.last().action)
    }

    @Test fun pageDiscoveryExcludesPrivateDisabledAndUnrelatedActivities() {
        val info = android.content.pm.ActivityInfo().apply {
            name = "com.android.settings.Settings\$WirelessDebuggingActivity"
            exported = true; enabled = true
        }
        assertTrue(isWirelessDebuggingPage(info))
        info.exported = false; assertFalse(isWirelessDebuggingPage(info))
        info.exported = true; info.enabled = false; assertFalse(isWirelessDebuggingPage(info))
        info.enabled = true; info.name = "com.android.settings.Settings\$DevelopmentSettingsActivity"
        assertFalse(isWirelessDebuggingPage(info))
        info.metaData = android.os.Bundle().apply {
            putString("com.android.settings.FRAGMENT_CLASS", "com.android.settings.development.WirelessDebuggingFragment")
        }
        assertTrue(isWirelessDebuggingPage(info))
    }

}
