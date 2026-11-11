package com.hinge.office

import android.provider.Settings
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowSystemProperties

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28], application = android.app.Application::class)
class DeviceNamingTest {
    private fun deviceName(): String {
        val activity = Robolectric.buildActivity(MainActivity::class.java).get()
        val method = MainActivity::class.java.getDeclaredMethod("deviceInfo").apply { isAccessible = true }
        return (method.invoke(activity) as Map<*, *>)["name"] as String
    }

    @Test fun oppoMarketNameWinsOverInternalProductCode() {
        org.robolectric.shadows.ShadowBuild.setManufacturer("OPPO")
        ShadowSystemProperties.override("ro.product.odm.name", "ossi")
        ShadowSystemProperties.override("ro.oppo.market.name", "OPPO Find X9s Pro")
        assertEquals("OPPO Find X9s Pro", deviceName())
    }

    @Test fun configuredNameWinsWhenOnlyProductCodeIsAvailable() {
        ShadowSystemProperties.override("ro.product.odm.name", "ossi")
        val context = org.robolectric.RuntimeEnvironment.getApplication()
        Settings.Global.putString(context.contentResolver, Settings.Global.DEVICE_NAME, "我的手机")
        assertEquals("我的手机", deviceName())
    }

    @Test fun catalogResolvesMultipleBrandsWithoutNetwork() {
        val assets = org.robolectric.RuntimeEnvironment.getApplication().assets
        val examples = listOf(
            listOf("Samsung", "SM-S928B", "e3q", "Galaxy S24 Ultra"),
            listOf("Google", "Pixel 9", "tokay", "Pixel 9"),
            listOf("Xiaomi", "23127PN0CG", "houji", "Xiaomi 14"),
            listOf("Huawei", "VOG-L29", "HWVOG", "P30 Pro"),
            listOf("Honor", "BVL-AN16", "HNBVL", "HONOR Magic6 Pro"),
            listOf("Sony", "XQ-EC54", "XQ-EC54", "Xperia 1 VI"),
            listOf("Asus", "ASUS_AI2401_A", "ASUS_AI2401", "ROG Phone 8 series"),
            listOf("Nothing", "A065", "Pong", "Nothing Phone (2)"),
            listOf("realme", "RMX3840", "RE5C82L1", "realme 12 Pro+ 5G"),
            listOf("OnePlus", "CPH2581", "OP595DL1", "OnePlus 12"),
            listOf("Vivo", "V2419A", "PD2419", "vivo X200 Pro mini")
        )
        for ((brand, model, device, expected) in examples) {
            assertEquals(brand, expected, DeviceMarketNames.lookup(assets, brand, brand, model, device))
        }
        assertNull(DeviceMarketNames.lookup(assets, "ZTE", "ZTE", "NX769J", "NX769J"))
        assertNull(DeviceMarketNames.lookup(assets, "Motorola", "Motorola", "201M", "unknown-device"))
        assertEquals("Droid Razr M", DeviceMarketNames.lookup(assets, "Motorola", "Motorola", "201M", "scorpion_mini_t"))
        assertNull(DeviceMarketNames.lookup(assets, "unknown-brand", "unknown-brand", "SM-S928B", "e3q"))
        assertNull(DeviceMarketNames.lookup(assets, "Samsung", "Samsung", "unlisted", "e3q"))
    }

    @Test fun brandSpecificPropertiesCannotOverrideAnotherBrand() {
        assertFalse(DeviceMarketNames.propertyKeys("Xiaomi", "Redmi").contains("ro.vivo.marketname"))
        assertTrue(DeviceMarketNames.propertyKeys("Huawei", "Huawei").contains("ro.config.marketing_name"))
        assertTrue(DeviceMarketNames.propertyKeys("vivo", "iQOO").contains("ro.vivo.marketname"))
    }
}
