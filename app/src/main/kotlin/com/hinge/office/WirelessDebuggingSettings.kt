package com.hinge.office

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.ComponentName
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings

internal enum class DebuggingDestination { Scanner, Wireless, Developer, DeviceInfo, Unavailable }

internal fun openDebuggingSettings(context: Context, scan: Boolean): DebuggingDestination {
    val developerEnabled = runCatching {
        Settings.Global.getInt(context.contentResolver, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) != 0
    }.getOrDefault(true)
    val directPages = listOf("com.android.settings", "com.vivo.settings").flatMap { packageName ->
        try {
            @Suppress("DEPRECATION")
            val info = context.packageManager.getPackageInfo(packageName,
                PackageManager.GET_ACTIVITIES or PackageManager.GET_META_DATA)
            info.activities.orEmpty().filter { activity ->
                isWirelessDebuggingPage(activity) && (activity.permission.isNullOrEmpty() ||
                    context.checkSelfPermission(activity.permission) == PackageManager.PERMISSION_GRANTED)
            }.map { ComponentName(packageName, it.name) }
        } catch (_: PackageManager.NameNotFoundException) { emptyList() }
        catch (_: SecurityException) { emptyList() }
    }
    return launchDebuggingSettings(scan, Build.VERSION.SDK_INT, developerEnabled, directPages) {
        context.startActivity(it)
    }
}

internal fun isWirelessDebuggingPage(info: ActivityInfo): Boolean {
    val fragment = info.metaData?.getString("com.android.settings.FRAGMENT_CLASS").orEmpty()
    return info.exported && info.enabled &&
        (info.name.endsWith("\$WirelessDebuggingActivity") || info.name.endsWith(".WirelessDebuggingActivity") ||
            fragment.endsWith(".WirelessDebuggingFragment"))
}

internal fun launchDebuggingSettings(scan: Boolean, sdk: Int, developerEnabled: Boolean = true,
    directPages: List<ComponentName> = emptyList(), launch: (Intent) -> Unit): DebuggingDestination {
    if (sdk < 30) return DebuggingDestination.Unavailable
    val candidates = buildList {
        // AOSP keeps this activity private; OEMs may expose it. Never bypass its permission checks.
        if (scan && developerEnabled) add(DebuggingDestination.Scanner to Intent().setClassName(
            "com.android.settings", "com.android.settings.development.AdbQrCodeActivity"))
        if (developerEnabled) {
            directPages.forEach { add(DebuggingDestination.Wireless to Intent().setComponent(it)) }
            // shortcut: some OEMs accept this tile action but stop at developer options; prefer a real exported page.
            if (sdk >= 31) add(DebuggingDestination.Wireless to
                Intent("android.service.quicksettings.action.QS_TILE_PREFERENCES")
                    .setPackage("com.android.settings")
                    .putExtra(Intent.EXTRA_COMPONENT_NAME, ComponentName("com.android.settings",
                        "com.android.settings.development.qstile.DevelopmentTiles\$WirelessDebugging")))
            add(DebuggingDestination.Wireless to Intent("android.settings.WIRELESS_DEBUGGING_SETTINGS")
                .setPackage("com.android.settings"))
            add(DebuggingDestination.Developer to Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS))
        }
        add(DebuggingDestination.DeviceInfo to Intent(Settings.ACTION_DEVICE_INFO_SETTINGS))
    }
    for ((destination, intent) in candidates) {
        try {
            launch(intent)
            return destination
        } catch (_: ActivityNotFoundException) {
        } catch (_: SecurityException) {
        }
    }
    return DebuggingDestination.Unavailable
}

internal fun debuggingGuidance(destination: DebuggingDestination, scan: Boolean): String = when (destination) {
    DebuggingDestination.Scanner -> "扫描电脑 Hinge 显示的配对二维码。"
    DebuggingDestination.Wireless -> if (scan) "请选择“使用二维码配对设备”，扫描电脑上的二维码。" else "请选择“使用配对码配对设备”，将显示的 6 位码输入电脑 Hinge。"
    DebuggingDestination.Developer -> "请进入“无线调试”，再选择“${if (scan) "使用二维码配对设备" else "使用配对码配对设备"}”。"
    DebuggingDestination.DeviceInfo -> "请先启用开发者选项，再进入“无线调试”完成配对。"
    DebuggingDestination.Unavailable -> "无法打开系统页面，请手动进入设置 → 开发者选项 → 无线调试。"
}
