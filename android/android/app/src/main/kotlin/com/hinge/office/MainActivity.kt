package com.hinge.office

import android.Manifest
import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.content.Context
import android.content.res.Configuration
import android.content.ContentUris
import android.content.pm.PackageManager
import android.bluetooth.BluetoothAdapter
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.HandlerThread
import android.os.PowerManager
import android.os.StatFs
import java.io.File
import android.util.Size
import android.provider.CalendarContract
import android.provider.MediaStore
import android.provider.Settings
import android.provider.DocumentsContract
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.view.Display
import android.view.WindowManager
import android.graphics.PixelFormat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel
import io.flutter.plugin.common.EventChannel
import org.json.JSONArray
import org.json.JSONObject
import java.util.LinkedHashMap
import java.util.Locale
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import kotlin.math.max
import kotlin.math.min

class MainActivity : FlutterActivity() {
    private val channelName = "hinge/platform"
    private val calendarPermissionRequest = 4101
    private val photosPermissionRequest = 4102
    private val screenCapturePermissionRequest = 4201
    private val notificationPermissionRequest = 4103
    private val mediaPermissionRequest = 4104
    // MediaStore/Calendar work is off the UI thread. A small adaptive pool
    // keeps thumbnail and metadata requests responsive on modern phones while
    // avoiding an unbounded thread explosion on low-end devices.
    private val contentExecutor: ExecutorService = Executors.newFixedThreadPool(
        max(2, min(4, Runtime.getRuntime().availableProcessors() - 1)),
    )
    private var screenCaptureResult: MethodChannel.Result? = null
    private var screenCaptureWidth = 1080
    private var screenCaptureHeight = 1920
    private var screenCaptureFps = 60
    private var mediaProjection: MediaProjection? = null
    private var virtualDisplay: android.hardware.display.VirtualDisplay? = null
    private var imageReader: ImageReader? = null
    private var captureThread: HandlerThread? = null
    private var captureHandler: Handler? = null
    private var lastCaptureAt = 0L
    private var encodingFrame = false
    private var screenEventSink: EventChannel.EventSink? = null
    private var discoveryMulticastLock: WifiManager.MulticastLock? = null
    private var calendarPermissionResult: MethodChannel.Result? = null
    private var photosPermissionResult: MethodChannel.Result? = null
    private var notificationPermissionResult: MethodChannel.Result? = null
    private var mediaPermissionResult: MethodChannel.Result? = null
    private data class StorageCacheEntry(
        val expiresAt: Long,
        val entries: List<Map<String, Any?>>,
    )

    private val storageDirectoryCache = ConcurrentHashMap<String, StorageCacheEntry>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        startConnectionService()
        // A notification click can create the activity from a cold start.
        // Handle the same deep link path as onNewIntent so the directory
        // shortcut works whether Flutter is already running or not.
        if (intent?.action == ACTION_OPEN_RECEIVED_DIRECTORY) {
            openReceivedDirectory(intent.getStringExtra(EXTRA_RECEIVED_FILE_PATH))
        }
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, channelName)
            .setMethodCallHandler { call, result -> handleMethod(call, result) }
        EventChannel(
            flutterEngine.dartExecutor.binaryMessenger,
            "hinge/screen_capture/events",
        ).setStreamHandler(object : EventChannel.StreamHandler {
            override fun onListen(arguments: Any?, events: EventChannel.EventSink?) {
                screenEventSink = events
            }

            override fun onCancel(arguments: Any?) {
                screenEventSink = null
            }
        })
    }

    override fun onDestroy() {
        stopScreenCapture()
        releaseDiscoveryMulticastLock()
        contentExecutor.shutdownNow()
        super.onDestroy()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != screenCapturePermissionRequest) return
        val pending = screenCaptureResult
        screenCaptureResult = null
        if (resultCode != Activity.RESULT_OK || data == null) {
            pending?.success(false)
            return
        }
        val started = startScreenCapture(data)
        pending?.success(started)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        if (intent.action == ACTION_OPEN_RECEIVED_DIRECTORY) {
            openReceivedDirectory(intent.getStringExtra(EXTRA_RECEIVED_FILE_PATH))
        }
    }

    private fun handleMethod(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "acquireDiscoveryMulticastLock" -> result.success(acquireDiscoveryMulticastLock())
            "releaseDiscoveryMulticastLock" -> result.success(releaseDiscoveryMulticastLock())
            "storageInfo" -> result.success(readStorageInfo())
            "storagePaths" -> result.success(storagePaths())
            "deviceName" -> result.success(deviceDisplayName())
            "deviceInfo" -> result.success(deviceInfo())
            "systemAccentColor" -> result.success(systemAccentColor())
            "systemDynamicColors" -> result.success(systemDynamicColors())
            "loadSettings" -> result.success(loadSettings())
            "saveSettings" -> saveSettings(call, result)
            "hapticFeedbackEnabled" -> result.success(hapticFeedbackEnabled())
            "setHapticFeedbackEnabled" -> setHapticFeedbackEnabled(call, result)
            "hasNotificationPermission" -> result.success(hasNotificationPermission())
            "requestNotificationPermission" -> requestNotificationPermission(result)
            "persistentNotificationEnabled" -> result.success(persistentNotificationEnabled())
            "setPersistentNotificationEnabled" -> setPersistentNotificationEnabled(call, result)
            "keepAliveStatus" -> result.success(keepAliveStatus())
            "openNotificationSettings" -> result.success(openNotificationSettings())
            "openBatteryOptimizationSettings" -> result.success(openBatteryOptimizationSettings())
            "openBackgroundProtectionSettings" -> result.success(openBackgroundProtectionSettings())
            "moveTaskToBack" -> result.success(moveTaskToBack(true))
            "showFileReceivedNotification" -> showFileReceivedNotification(call, result)
            "openAppSettings" -> openAppSettings(result)
            "openProjectUrl" -> result.success(openProjectUrl())
            "startScreenCapture" -> requestScreenCapture(call, result)
            "stopScreenCapture" -> {
                stopScreenCapture()
                result.success(true)
            }
            "hasCalendarPermission" -> result.success(hasCalendarPermission())
            "requestCalendarPermission" -> requestCalendarPermission(result)
            "calendarEvents" -> readCalendarEvents(result)
            "hasPhotosPermission" -> result.success(hasPhotosPermission())
            "requestPhotosPermission" -> requestPhotosPermission(result)
            "hasMediaPermission" -> result.success(hasMediaPermission())
            "requestMediaPermission" -> requestMediaPermission(result)
            "hasAllFilesAccess" -> result.success(hasAllFilesAccess())
            "openAllFilesAccessSettings" -> openAllFilesAccessSettings(result)
            "photoAlbums" -> readPhotoAlbums(result)
            "photos" -> readPhotos(call, result)
            "photosPage" -> readPhotoPage(call, result)
            "photoBytes" -> readPhotoBytes(call, result)
            "photoThumbnailBytes" -> readPhotoThumbnailBytes(call, result)
            "photoPreviewBytes" -> readPhotoPreviewBytes(call, result)
            "copyUriToCache" -> copyUriToCache(call, result)
            "files" -> readFiles(result)
            "storageDirectory" -> readStorageDirectory(call, result)
            "storageDirectoryPage" -> readStorageDirectoryPage(call, result)
            "deleteFiles" -> deleteFiles(call, result)
            "loadNotes" -> result.success(readWorkspaceList("notes"))
            "saveNote" -> saveWorkspaceItem("notes", call.arguments, result)
            "deleteNote" -> deleteWorkspaceItem("notes", call.arguments, result)
            "loadTasks" -> result.success(readWorkspaceList("tasks"))
            "saveTask" -> saveWorkspaceItem("tasks", call.arguments, result)
            "deleteTask" -> deleteWorkspaceItem("tasks", call.arguments, result)
            else -> result.notImplemented()
        }
    }

    private fun startConnectionService() {
        val intent = Intent(this, HingeForegroundService::class.java).apply {
            action = HingeForegroundService.ACTION_START
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
    }

    private fun openAppSettings(result: MethodChannel.Result) {
        try {
            startActivity(appDetailsIntent())
            result.success(true)
        } catch (error: Exception) {
            result.error("settings_failed", "无法打开应用权限设置：${error.message}", null)
        }
    }

    private fun appDetailsIntent(): Intent =
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
            data = Uri.parse("package:$packageName")
        }

    private fun keepAliveStatus(): Map<String, Any> {
        val powerManager = getSystemService(Context.POWER_SERVICE) as? PowerManager
        val batteryOptimizationIgnored = Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            powerManager?.isIgnoringBatteryOptimizations(packageName) == true
        return mapOf(
            "notificationPermission" to hasNotificationPermission(),
            "persistentNotification" to persistentNotificationEnabled(),
            "batteryOptimizationIgnored" to batteryOptimizationIgnored,
            "manufacturer" to Build.MANUFACTURER,
            "model" to Build.MODEL,
            "apiLevel" to Build.VERSION.SDK_INT,
        )
    }

    private fun openNotificationSettings(): Boolean {
        val intent = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
                putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            }
        } else {
            appDetailsIntent()
        }
        return startSettingsIntent(intent, appDetailsIntent())
    }

    private fun openBatteryOptimizationSettings(): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            return startSettingsIntent(
                Intent(Settings.ACTION_BATTERY_SAVER_SETTINGS),
                appDetailsIntent(),
            )
        }
        val requestIntent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
            data = Uri.parse("package:$packageName")
        }
        val listIntent = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
        return startSettingsIntent(requestIntent, listIntent) ||
            startSettingsIntent(listIntent, appDetailsIntent())
    }

    private fun openBackgroundProtectionSettings(): Boolean {
        val manufacturer = Build.MANUFACTURER.lowercase(Locale.ROOT)
        val candidates = when {
            manufacturer.contains("vivo") -> listOf(
                componentIntent("com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.BgStartUpManagerActivity"),
                componentIntent("com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.PurviewTabActivity"),
            )
            manufacturer.contains("iqoo") -> listOf(
                componentIntent("com.iqoo.secure", "com.iqoo.secure.ui.phoneoptimize.BgStartUpManager"),
                componentIntent("com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.BgStartUpManagerActivity"),
            )
            manufacturer.contains("xiaomi") || manufacturer.contains("redmi") -> listOf(
                componentIntent("com.miui.securitycenter", "com.miui.permcenter.autostart.AutoStartManagementActivity"),
            )
            manufacturer.contains("huawei") || manufacturer.contains("honor") -> listOf(
                componentIntent("com.huawei.systemmanager", "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity"),
                componentIntent("com.hihonor.systemmanager", "com.hihonor.systemmanager.startupmgr.ui.StartupNormalAppListActivity"),
            )
            manufacturer.contains("oppo") || manufacturer.contains("realme") -> listOf(
                componentIntent("com.coloros.safecenter", "com.coloros.safecenter.startupapp.StartupAppListActivity"),
                componentIntent("com.oppo.safe", "com.oppo.safe.permission.startup.StartupAppListActivity"),
            )
            manufacturer.contains("oneplus") -> listOf(
                componentIntent("com.oneplus.security", "com.oneplus.security.chainlaunch.view.ChainLaunchAppListActivity"),
            )
            else -> emptyList()
        }
        for (candidate in candidates) {
            if (startSettingsIntent(candidate, null)) return true
        }
        return startSettingsIntent(appDetailsIntent(), null)
    }

    private fun componentIntent(packageName: String, className: String): Intent =
        Intent().setClassName(packageName, className)

    private fun startSettingsIntent(intent: Intent, fallback: Intent?): Boolean {
        return try {
            if (intent.resolveActivity(packageManager) == null) {
                if (fallback != null) return startSettingsIntent(fallback, null)
                return false
            }
            startActivity(intent)
            true
        } catch (_: Exception) {
            if (fallback != null) startSettingsIntent(fallback, null) else false
        }
    }

    private fun openProjectUrl(): Boolean {
        val intent = Intent(
            Intent.ACTION_VIEW,
            Uri.parse("https://github.com/Chengeeker/Hinge"),
        ).apply {
            addCategory(Intent.CATEGORY_BROWSABLE)
        }
        return try {
            // Do not depend on a particular browser package. Android may hide
            // browser activities from resolveActivity unless the manifest has
            // an explicit VIEW query, so starting the external intent directly
            // is the reliable path and is still guarded by ActivityNotFound.
            startActivity(intent)
            true
        } catch (_: Exception) {
            try {
                startActivity(Intent.createChooser(intent, "在浏览器中打开"))
                true
            } catch (_: Exception) {
                false
            }
        }
    }

    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray,
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        when (requestCode) {
            calendarPermissionRequest -> {
                val pending = calendarPermissionResult
                calendarPermissionResult = null
                pending?.success(hasCalendarPermission())
            }
            photosPermissionRequest -> {
                val pending = photosPermissionResult
                photosPermissionResult = null
                pending?.success(hasPhotosPermission())
            }
            notificationPermissionRequest -> {
                val pending = notificationPermissionResult
                notificationPermissionResult = null
                pending?.success(hasNotificationPermission())
            }
            mediaPermissionRequest -> {
                val pending = mediaPermissionResult
                mediaPermissionResult = null
                pending?.success(hasMediaPermission())
            }
        }
    }

    private fun acquireDiscoveryMulticastLock(): Boolean {
        return try {
            if (discoveryMulticastLock?.isHeld != true) {
                val wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
                discoveryMulticastLock = wifiManager.createMulticastLock("HingeDiscovery").apply {
                    setReferenceCounted(false)
                    acquire()
                }
            }
            discoveryMulticastLock?.isHeld == true
        } catch (_: Exception) {
            false
        }
    }

    private fun releaseDiscoveryMulticastLock(): Boolean {
        return try {
            if (discoveryMulticastLock?.isHeld == true) discoveryMulticastLock?.release()
            discoveryMulticastLock = null
            true
        } catch (_: Exception) {
            discoveryMulticastLock = null
            false
        }
    }

    private fun readStorageInfo(): Map<String, Long> {
        val stat = StatFs(Environment.getDataDirectory().path)
        val total = stat.totalBytes
        val free = stat.availableBytes
        return mapOf(
            "totalBytes" to total,
            "usedBytes" to (total - free).coerceAtLeast(0L),
            "freeBytes" to free,
        )
    }

    private fun systemAccentColor(): Int? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return null
        val resourceId = resources.getIdentifier("system_accent1_600", "color", "android")
        return if (resourceId == 0) null else getColor(resourceId)
    }

    /**
     * Read Android 12+'s wallpaper-derived Material roles directly from the
     * framework resources. Flutter's ColorScheme.fromSeed is intentionally
     * not used for this path: it cannot reproduce the system's HCT palette
     * from one sampled accent color.
     */
    private fun systemDynamicColors(): Map<String, Long>? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return null

        // Android 14 introduced public, already role-mapped Monet resources.
        // Use those resources when available instead of reconstructing a
        // Material scheme from one accent palette.  The values are maintained
        // by SystemUI and are derived from the current wallpaper/color source.
        // Android 12/13 fall back to the public accent/neutral tonal palettes
        // below because these role resources do not exist there.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            val directColors = linkedMapOf<String, Long>()

            fun addDirectRoles(prefix: String, mode: String) {
                val roles = mapOf(
                    "primary" to "primary",
                    "onPrimary" to "on_primary",
                    "primaryContainer" to "primary_container",
                    "onPrimaryContainer" to "on_primary_container",
                    "secondary" to "secondary",
                    "onSecondary" to "on_secondary",
                    "secondaryContainer" to "secondary_container",
                    "onSecondaryContainer" to "on_secondary_container",
                    "tertiary" to "tertiary",
                    "onTertiary" to "on_tertiary",
                    "tertiaryContainer" to "tertiary_container",
                    "onTertiaryContainer" to "on_tertiary_container",
                    "surface" to "surface",
                    "onSurface" to "on_surface",
                    "onSurfaceVariant" to "on_surface_variant",
                    "outline" to "outline",
                    "outlineVariant" to "outline_variant",
                    "surfaceContainerLowest" to "surface_container_lowest",
                    "surfaceContainerLow" to "surface_container_low",
                    "surfaceContainer" to "surface_container",
                    "surfaceContainerHigh" to "surface_container_high",
                    "surfaceContainerHighest" to "surface_container_highest",
                    "surfaceBright" to "surface_bright",
                    "surfaceDim" to "surface_dim",
                    "surfaceVariant" to "surface_variant",
                    "error" to "error",
                    "onError" to "on_error",
                    "errorContainer" to "error_container",
                    "onErrorContainer" to "on_error_container",
                )
                for ((role, resourceRole) in roles) {
                    val resourceId = resources.getIdentifier(
                        "system_${resourceRole}_$mode",
                        "color",
                        "android",
                    )
                    if (resourceId != 0) {
                        directColors["$prefix.$role"] =
                            getColor(resourceId).toLong() and 0xffffffffL
                    }
                }
            }

            addDirectRoles("light", "light")
            addDirectRoles("dark", "dark")
            if (directColors.containsKey("light.primary") &&
                directColors.containsKey("dark.primary")) {
                directColors["light.inversePrimary"] =
                    directColors["dark.primary"]!!
                directColors["dark.inversePrimary"] =
                    directColors["light.primary"]!!
                directColors["light.inverseSurface"] =
                    directColors["dark.surface"]!!
                directColors["dark.inverseSurface"] =
                    directColors["light.surface"]!!
                directColors["light.onInverseSurface"] =
                    directColors["dark.onSurface"]!!
                directColors["dark.onInverseSurface"] =
                    directColors["light.onSurface"]!!
                return directColors
            }
        }

        val lightContext = dynamicColorContext(dark = false)
        val darkContext = dynamicColorContext(dark = true)
        val colors = linkedMapOf<String, Long>()

        fun color(context: Context, palette: String, tone: Int): Long? {
            val resourceId = context.resources.getIdentifier(
                "system_${palette}_${tone}",
                "color",
                "android",
            )
            if (resourceId == 0) return null
            return context.getColor(resourceId).toLong() and 0xffffffffL
        }

        fun put(
            prefix: String,
            role: String,
            context: Context,
            palette: String,
            tone: Int,
        ) {
            color(context, palette, tone)?.let { colors["$prefix.$role"] = it }
        }

        fun addAccentRoles(prefix: String, context: Context, dark: Boolean) {
            val primaryTone = if (dark) 200 else 600
            val onPrimaryTone = if (dark) 800 else 0
            val containerTone = if (dark) 700 else 100
            val onContainerTone = if (dark) 100 else 900
            put(prefix, "primary", context, "accent1", primaryTone)
            put(prefix, "onPrimary", context, "accent1", onPrimaryTone)
            put(prefix, "primaryContainer", context, "accent1", containerTone)
            put(prefix, "onPrimaryContainer", context, "accent1", onContainerTone)
            put(prefix, "secondary", context, "accent2", primaryTone)
            put(prefix, "onSecondary", context, "accent2", onPrimaryTone)
            put(prefix, "secondaryContainer", context, "accent2", containerTone)
            put(prefix, "onSecondaryContainer", context, "accent2", onContainerTone)
            put(prefix, "tertiary", context, "accent3", primaryTone)
            put(prefix, "onTertiary", context, "accent3", onPrimaryTone)
            put(prefix, "tertiaryContainer", context, "accent3", containerTone)
            put(prefix, "onTertiaryContainer", context, "accent3", onContainerTone)
            put(prefix, "inversePrimary", context, "accent1", if (dark) 600 else 200)
        }

        fun addNeutralRoles(prefix: String, context: Context, dark: Boolean) {
            val surfaceTone = if (dark) 900 else 10
            val onSurfaceTone = if (dark) 100 else 900
            val variantTone = if (dark) 200 else 700
            val outlineTone = if (dark) 500 else 500
            val inverseSurfaceTone = if (dark) 100 else 800
            val inverseOnSurfaceTone = if (dark) 900 else 100
            put(prefix, "surface", context, "neutral1", surfaceTone)
            put(prefix, "onSurface", context, "neutral1", onSurfaceTone)
            put(prefix, "onSurfaceVariant", context, "neutral2", variantTone)
            put(prefix, "outline", context, "neutral2", outlineTone)
            put(prefix, "outlineVariant", context, "neutral2", if (dark) 300 else 200)
            put(prefix, "inverseSurface", context, "neutral1", inverseSurfaceTone)
            put(prefix, "onInverseSurface", context, "neutral1", inverseOnSurfaceTone)
            put(prefix, "surfaceContainerLowest", context, "neutral1", if (dark) 1000 else 0)
            put(prefix, "surfaceContainerLow", context, "neutral1", if (dark) 950 else 10)
            put(prefix, "surfaceContainer", context, "neutral1", if (dark) 900 else 30)
            put(prefix, "surfaceContainerHigh", context, "neutral1", if (dark) 800 else 50)
            put(prefix, "surfaceContainerHighest", context, "neutral1", if (dark) 700 else 60)
        }

        addAccentRoles("light", lightContext, dark = false)
        addNeutralRoles("light", lightContext, dark = false)
        addAccentRoles("dark", darkContext, dark = true)
        addNeutralRoles("dark", darkContext, dark = true)
        return colors.takeIf { it.containsKey("light.primary") && it.containsKey("dark.primary") }
    }

    private fun dynamicColorContext(dark: Boolean): Context {
        val configuration = Configuration(resources.configuration)
        configuration.uiMode = (configuration.uiMode and Configuration.UI_MODE_TYPE_MASK) or
            if (dark) Configuration.UI_MODE_NIGHT_YES else Configuration.UI_MODE_NIGHT_NO
        return createConfigurationContext(configuration)
    }

    private fun deviceInfo(): Map<String, String> {
        val manufacturer = Build.MANUFACTURER.trim()
        val model = Build.MODEL.trim()
        // Android 没有一个跨厂商统一的“零售机型名称” API。优先读取厂商市场名，
        // 再读取用户在系统设置中的设备名/蓝牙名，最后才退回到 Build.MODEL。
        // 不同厂商会把市场名放在不同的只读属性中，且某些属性仍然只返回型号代码，
        // 因此必须逐项过滤，不能只取第一个非空属性。
        val configuredNames = listOf(
            runCatching {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                    Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME)
                } else {
                    null
                }
            }.getOrNull(),
            runCatching { Settings.System.getString(contentResolver, "device_name") }.getOrNull(),
            runCatching {
                Settings.System.getString(contentResolver, "device_name_for_settings")
            }.getOrNull(),
        )
        val secureNames = listOf(
            runCatching {
                Settings.Secure.getString(contentResolver, "bluetooth_name")
            }.getOrNull(),
            runCatching {
                Settings.Global.getString(contentResolver, "bluetooth_name")
            }.getOrNull(),
        )
        val bluetoothName = if (
            Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
                checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED
        ) {
            runCatching { BluetoothAdapter.getDefaultAdapter()?.name?.trim() }.getOrNull()
        } else {
            null
        }
        val marketNames = listOf(
            // vivo/OriginOS 常见属性
            "ro.vivo.marketname",
            "ro.vivo.market.name",
            "ro.vivo.product.marketname",
            "ro.vivo.product.model",
            "ro.vivo.product.name",
            // AOSP 及其他厂商常见属性
            "ro.product.marketname",
            "ro.product.market_name",
            "ro.product.odm.marketname",
            "ro.product.vendor.marketname",
            "ro.product.system.marketname",
            "ro.product.product.marketname",
            "ro.product.odm.name",
            "ro.product.vendor.name",
            "ro.product.system.name",
            "ro.product.product.name",
            "ro.product.name",
            "ro.product.odm.model",
            "ro.product.vendor.model",
            "ro.product.system.model",
            "ro.product.product.model",
        ).mapNotNull(::readSystemProperty)

        // Android 的公开 API 不提供“型号代码 -> 零售名称”的通用映射。
        // 对已经确认的官方型号提供本地兜底，避免 vivo V2419A 在系统属性不可读时
        // 退化成用户无法理解的技术代码。新增型号时应补充官方来源后再加入这里。
        val knownMarketName = when {
            manufacturer.equals("vivo", ignoreCase = true) &&
                model.equals("V2419A", ignoreCase = true) -> "vivo X200 Pro mini"
            else -> null
        }

        val technicalNames = setOf(
            model,
            Build.DEVICE,
            Build.PRODUCT,
            Build.BRAND,
            manufacturer,
        ).filter { it.isNotBlank() }.map { it.lowercase(Locale.ROOT) }.toSet()

        fun usableName(value: String?): String? {
            val candidate = value?.trim().orEmpty()
            if (candidate.isBlank()) return null
            val normalized = candidate.lowercase(Locale.ROOT)
            if (normalized in technicalNames) return null
            if (normalized in setOf(
                    "android",
                    "android device",
                    "android 设备",
                    "phone",
                    "mobile",
                    "unknown",
                    "unknown device",
                    "未命名设备",
                    "null",
                    "none",
                )
            ) {
                return null
            }
            return candidate
        }

        // 市场名放在最前面，避免系统蓝牙名或历史身份名把 V2419A 的真实名称遮住。
        // 如果用户明确设置了一个非技术名，而厂商没有公开市场名，则保留该名称。
        val marketName = marketNames.asSequence()
            .mapNotNull(::usableName)
            .firstOrNull()
        val configuredName = configuredNames.asSequence()
            .mapNotNull(::usableName)
            .firstOrNull()
        val secureName = secureNames.asSequence()
            .mapNotNull(::usableName)
            .firstOrNull()
        val name = sequenceOf(
            marketName,
            knownMarketName,
            configuredName,
            secureName,
            usableName(bluetoothName),
        ).filterNotNull().firstOrNull()
            ?: listOf(manufacturer, model).filter { it.isNotBlank() }.joinToString(" ")
                .ifBlank { "Android 设备" }

        return mapOf(
            "name" to name,
            "manufacturer" to manufacturer,
            "model" to model,
        )
    }

    private fun readSystemProperty(key: String): String? = runCatching {
        Class.forName("android.os.SystemProperties")
            .getMethod("get", String::class.java)
            .invoke(null, key) as? String
    }.getOrNull()

    private fun deviceDisplayName(): String {
        return deviceInfo()["name"] ?: "Android 设备"
    }

    private fun storagePaths(): Map<String, String> {
        val publicDownloads = Environment.getExternalStoragePublicDirectory(
            Environment.DIRECTORY_DOWNLOADS,
        )
        val publicRoot = File(publicDownloads, "Hinge")
        val root = if ((publicRoot.exists() || publicRoot.mkdirs()) && publicRoot.canWrite()) {
            publicRoot
        } else {
            File(
                getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: filesDir,
                "Hinge",
            )
        }
        val image = File(root, "图片")
        val video = File(root, "视频")
        val file = File(root, "文件")
        image.mkdirs()
        video.mkdirs()
        file.mkdirs()
        return mapOf(
            "imagePath" to image.absolutePath,
            "videoPath" to video.absolutePath,
            "filePath" to file.absolutePath,
        )
    }

    private fun requestScreenCapture(call: MethodCall, result: MethodChannel.Result) {
        if (mediaProjection != null) {
            result.success(true)
            return
        }
        val requestedWidth = call.argument<Int>("width") ?: 1080
        val requestedHeight = call.argument<Int>("height") ?: 1920
        val metrics = resources.displayMetrics
        screenCaptureWidth = (if (requestedWidth > 0) requestedWidth else metrics.widthPixels)
            .coerceIn(360, 1920)
        screenCaptureHeight = (if (requestedHeight > 0) requestedHeight else metrics.heightPixels)
            .coerceIn(360, 2400)
        screenCaptureFps = (call.argument<Int>("fps") ?: 60).coerceIn(5, 60)
        screenCaptureResult = result
        val manager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        startActivityForResult(
            manager.createScreenCaptureIntent(),
            screenCapturePermissionRequest,
        )
    }

    private fun startScreenCapture(permissionData: Intent): Boolean {
        return try {
            val manager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
            mediaProjection = manager.getMediaProjection(Activity.RESULT_OK, permissionData)
            val projection = mediaProjection ?: return false
            captureThread = HandlerThread("HingeScreenCapture").also { it.start() }
            captureHandler = Handler(captureThread!!.looper)
            imageReader = ImageReader.newInstance(
                screenCaptureWidth,
                screenCaptureHeight,
                PixelFormat.RGBA_8888,
                2,
            )
            imageReader?.setOnImageAvailableListener(
                { reader -> encodeLatestScreenImage(reader) },
                captureHandler,
            )
            val display = projection.createVirtualDisplay(
                "Hinge Screen Mirror",
                screenCaptureWidth,
                screenCaptureHeight,
                resources.displayMetrics.densityDpi,
                android.hardware.display.DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                imageReader?.surface,
                null,
                captureHandler,
            )
            virtualDisplay = display
            if (display == null) {
                stopScreenCapture()
                false
            } else {
                true
            }
        } catch (error: Exception) {
            stopScreenCapture()
            false
        }
    }

    private fun encodeLatestScreenImage(reader: ImageReader) {
        if (encodingFrame) return
        val image = reader.acquireLatestImage() ?: return
        val now = System.currentTimeMillis()
        if (now - lastCaptureAt < 1000L / screenCaptureFps) {
            image.close()
            return
        }
        lastCaptureAt = now
        encodingFrame = true
        try {
            val plane = image.planes[0]
            val pixelStride = plane.pixelStride
            val rowStride = plane.rowStride
            val rowPadding = rowStride - pixelStride * image.width
            val bitmapWidth = image.width + rowPadding / pixelStride
            val bitmap = Bitmap.createBitmap(bitmapWidth, image.height, Bitmap.Config.ARGB_8888)
            bitmap.copyPixelsFromBuffer(plane.buffer)
            val cropped = if (bitmapWidth == image.width) {
                bitmap
            } else {
                Bitmap.createBitmap(bitmap, 0, 0, image.width, image.height).also { bitmap.recycle() }
            }
            val output = java.io.ByteArrayOutputStream()
            cropped.compress(Bitmap.CompressFormat.JPEG, 84, output)
            if (cropped !== bitmap) cropped.recycle()
            else bitmap.recycle()
            val bytes = output.toByteArray()
            runOnUiThread { screenEventSink?.success(bytes) }
        } catch (_: Exception) {
            // A frame can be invalid while the virtual display is rotating.
        } finally {
            image.close()
            encodingFrame = false
        }
    }

    private fun stopScreenCapture() {
        virtualDisplay?.release()
        virtualDisplay = null
        imageReader?.close()
        imageReader = null
        captureHandler?.removeCallbacksAndMessages(null)
        captureThread?.quitSafely()
        captureThread = null
        captureHandler = null
        mediaProjection?.stop()
        mediaProjection = null
        encodingFrame = false
    }

    private fun loadSettings(): Map<String, Any> {
        val preferences = getSharedPreferences("app_settings", MODE_PRIVATE)
        return mapOf(
            "themePreference" to preferences.getString("theme_preference", "system").orEmpty(),
            "clipboardSyncEnabled" to preferences.getBoolean("clipboard_sync_enabled", true),
            "pureBlackDarkMode" to preferences.getBoolean("pure_black_dark_mode", false),
            // Monet is enabled by default. An explicit false remains false.
            "dynamicColorEnabled" to preferences.getBoolean("dynamic_color_enabled", true),
            "fontWeightLevel" to preferences.getInt("font_weight_level", 0),
            "seedColor" to preferences.getInt("seed_color", 0xFF6750A4.toInt()),
            "navigationStyle" to preferences.getString("navigation_style", "adaptive").orEmpty(),
            "floatingCapsuleNavigation" to preferences.getBoolean("floating_capsule_navigation", false),
            "imageStoragePath" to preferences.getString("image_storage_path", "").orEmpty(),
            "videoStoragePath" to preferences.getString("video_storage_path", "").orEmpty(),
            "fileStoragePath" to preferences.getString("file_storage_path", "").orEmpty(),
        )
    }

    private fun saveSettings(call: MethodCall, result: MethodChannel.Result) {
        val values = call.arguments as? Map<*, *>
        if (values == null) {
            result.error("invalid_argument", "设置数据无效", null)
            return
        }
        getSharedPreferences("app_settings", MODE_PRIVATE)
            .edit()
            .putString("theme_preference", values["themePreference"]?.toString() ?: "system")
            .putBoolean("clipboard_sync_enabled", values["clipboardSyncEnabled"] as? Boolean ?: true)
            .putBoolean("pure_black_dark_mode", values["pureBlackDarkMode"] as? Boolean ?: false)
            .putBoolean("dynamic_color_enabled", values["dynamicColorEnabled"] as? Boolean ?: false)
            .putInt("font_weight_level", (values["fontWeightLevel"] as? Number)?.toInt() ?: 0)
            .putInt("seed_color", (values["seedColor"] as? Number)?.toInt() ?: 0xFF6750A4.toInt())
            .putString("navigation_style", values["navigationStyle"]?.toString() ?: "adaptive")
            .putBoolean("floating_capsule_navigation", values["floatingCapsuleNavigation"] as? Boolean ?: false)
            .putString("image_storage_path", values["imageStoragePath"]?.toString() ?: "")
            .putString("video_storage_path", values["videoStoragePath"]?.toString() ?: "")
            .putString("file_storage_path", values["fileStoragePath"]?.toString() ?: "")
            .apply()
        result.success(true)
    }

    private fun hapticFeedbackEnabled(): Boolean {
        return getSharedPreferences("app_settings", MODE_PRIVATE)
            .getBoolean("haptic_feedback_enabled", true)
    }

    private fun setHapticFeedbackEnabled(call: MethodCall, result: MethodChannel.Result) {
        val enabled = call.argument<Boolean>("enabled") ?: true
        getSharedPreferences("app_settings", MODE_PRIVATE)
            .edit()
            .putBoolean("haptic_feedback_enabled", enabled)
            .apply()
        result.success(true)
    }

    private fun hasNotificationPermission(): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU ||
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun requestNotificationPermission(result: MethodChannel.Result) {
        if (hasNotificationPermission() || Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            result.success(true)
            return
        }
        notificationPermissionResult = result
        requestPermissions(
            arrayOf(Manifest.permission.POST_NOTIFICATIONS),
            notificationPermissionRequest,
        )
    }

    private fun persistentNotificationEnabled(): Boolean {
        return getSharedPreferences("app_settings", MODE_PRIVATE)
            .getBoolean("persistent_notification_enabled", true)
    }

    private fun setPersistentNotificationEnabled(call: MethodCall, result: MethodChannel.Result) {
        val enabled = call.argument<Boolean>("enabled") ?: true
        getSharedPreferences("app_settings", MODE_PRIVATE)
            .edit()
            .putBoolean("persistent_notification_enabled", enabled)
            .apply()
        startConnectionService()
        result.success(true)
    }

    private fun showFileReceivedNotification(call: MethodCall, result: MethodChannel.Result) {
        if (!hasNotificationPermission()) {
            result.success(false)
            return
        }
        val path = call.argument<String>("path")?.trim().orEmpty()
        if (path.isEmpty()) {
            result.success(false)
            return
        }
        val file = File(path)
        createFileNotificationChannel()
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, FILE_CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }
        val notification = builder
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentTitle("收到文件")
            .setContentText("${file.name} · 点击打开所在目录")
            .setCategory(Notification.CATEGORY_PROGRESS)
            .setAutoCancel(true)
            .setContentIntent(receivedDirectoryIntent(path))
            .build()
        getSystemService(NotificationManager::class.java)
            .notify(path.hashCode(), notification)
        result.success(true)
    }

    private fun createFileNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(
                FILE_CHANNEL_ID,
                "文件传输",
                NotificationManager.IMPORTANCE_DEFAULT,
            ).apply { description = "Hinge 接收文件提醒" },
        )
    }

    private fun receivedDirectoryIntent(path: String): PendingIntent {
        val intent = Intent(this, MainActivity::class.java).apply {
            action = ACTION_OPEN_RECEIVED_DIRECTORY
            putExtra(EXTRA_RECEIVED_FILE_PATH, path)
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                PendingIntent.FLAG_IMMUTABLE
            } else {
                0
            }
        return PendingIntent.getActivity(this, path.hashCode(), intent, flags)
    }

    private fun openReceivedDirectory(path: String?) {
        if (path.isNullOrBlank()) return
        try {
            val directory = File(path).canonicalFile.parentFile ?: return
            val storageRoot = Environment.getExternalStorageDirectory().canonicalFile
            val rootPath = storageRoot.path.trimEnd(File.separatorChar)
            val directoryPath = directory.path
            val relativePath = if (directoryPath.startsWith(rootPath + File.separator)) {
                directoryPath.removePrefix(rootPath + File.separator)
                    .replace(File.separatorChar, '/')
            } else {
                ""
            }
            val intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && relativePath.isNotEmpty()) {
                intent.putExtra(
                    DocumentsContract.EXTRA_INITIAL_URI,
                    DocumentsContract.buildTreeDocumentUri(
                        "com.android.externalstorage.documents",
                        "primary:$relativePath",
                    ),
                )
            }
            startActivity(intent)
        } catch (_: Exception) {
            startActivity(Intent(Intent.ACTION_OPEN_DOCUMENT_TREE))
        }
    }

    private fun hasCalendarPermission(): Boolean {
        return checkSelfPermission(Manifest.permission.READ_CALENDAR) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun requestCalendarPermission(result: MethodChannel.Result) {
        if (hasCalendarPermission()) {
            result.success(true)
            return
        }
        if (calendarPermissionResult != null) {
            result.error("permission_in_progress", "日历权限请求正在进行", null)
            return
        }
        calendarPermissionResult = result
        requestPermissions(arrayOf(Manifest.permission.READ_CALENDAR), calendarPermissionRequest)
    }

    private fun readCalendarEvents(result: MethodChannel.Result) {
        if (!hasCalendarPermission()) {
            result.error("permission_required", "需要日历读取权限", null)
            return
        }

        contentExecutor.execute {
            try {
                val events = queryCalendarEvents()
                runOnUiThread { result.success(events) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取日历失败：${error.message}", null)
                }
            }
        }
    }

    private fun queryCalendarEvents(): List<Map<String, Any?>> {
        val events = ArrayList<Map<String, Any?>>()
        val projection = arrayOf(
            CalendarContract.Instances.EVENT_ID,
            CalendarContract.Instances.TITLE,
            CalendarContract.Instances.BEGIN,
            CalendarContract.Instances.END,
            CalendarContract.Instances.EVENT_LOCATION,
            CalendarContract.Instances.ALL_DAY,
        )
        val now = System.currentTimeMillis()
        val from = now - 365L * 24L * 60L * 60L * 1000L
        val until = now + 730L * 24L * 60L * 60L * 1000L
        val instancesUri = CalendarContract.Instances.CONTENT_URI.buildUpon().also {
            ContentUris.appendId(it, from)
            ContentUris.appendId(it, until)
        }.build()
        val cursor = contentResolver.query(
            instancesUri,
            projection,
            null,
            null,
            "${CalendarContract.Instances.BEGIN} ASC",
        )

        cursor?.use {
            val idIndex = it.getColumnIndex(CalendarContract.Instances.EVENT_ID)
            val titleIndex = it.getColumnIndex(CalendarContract.Instances.TITLE)
            val startIndex = it.getColumnIndex(CalendarContract.Instances.BEGIN)
            val endIndex = it.getColumnIndex(CalendarContract.Instances.END)
            val locationIndex = it.getColumnIndex(CalendarContract.Instances.EVENT_LOCATION)
            val allDayIndex = it.getColumnIndex(CalendarContract.Instances.ALL_DAY)
            var count = 0
            while (it.moveToNext() && count < 1000) {
                events.add(
                    mapOf(
                        "id" to it.getString(idIndex),
                        "title" to (it.getString(titleIndex) ?: "未命名日程"),
                        "start" to it.getLong(startIndex),
                        "end" to if (endIndex >= 0 && !it.isNull(endIndex)) it.getLong(endIndex) else null,
                        "location" to if (locationIndex >= 0) it.getString(locationIndex).orEmpty() else "",
                        "allDay" to (allDayIndex >= 0 && it.getInt(allDayIndex) == 1),
                    ),
                )
                count++
            }
        }
        return events
    }

    private fun hasPhotosPermission(): Boolean {
        if (hasAllFilesAccess()) return true
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(
                Manifest.permission.READ_MEDIA_IMAGES,
            ) == PackageManager.PERMISSION_GRANTED
        ) {
            return true
        }
        if (Build.VERSION.SDK_INT >= 34 && checkSelfPermission(
                Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
            ) == PackageManager.PERMISSION_GRANTED
        ) {
            return true
        }
        return Build.VERSION.SDK_INT < 33 && checkSelfPermission(
            Manifest.permission.READ_EXTERNAL_STORAGE,
        ) == PackageManager.PERMISSION_GRANTED
    }

    private fun requestPhotosPermission(result: MethodChannel.Result) {
        if (hasPhotosPermission()) {
            result.success(true)
            return
        }
        if (photosPermissionResult != null) {
            result.error("permission_in_progress", "照片权限请求正在进行", null)
            return
        }
        photosPermissionResult = result
        val permissions = when {
            Build.VERSION.SDK_INT >= 34 -> arrayOf(
                Manifest.permission.READ_MEDIA_IMAGES,
                Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
            )
            Build.VERSION.SDK_INT >= 33 -> arrayOf(Manifest.permission.READ_MEDIA_IMAGES)
            else -> arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }
        requestPermissions(permissions, photosPermissionRequest)
    }

    private fun hasMediaPermission(): Boolean {
        if (hasAllFilesAccess()) return true
        if (Build.VERSION.SDK_INT >= 33) {
            return checkSelfPermission(Manifest.permission.READ_MEDIA_IMAGES) == PackageManager.PERMISSION_GRANTED &&
                checkSelfPermission(Manifest.permission.READ_MEDIA_VIDEO) == PackageManager.PERMISSION_GRANTED &&
                checkSelfPermission(Manifest.permission.READ_MEDIA_AUDIO) == PackageManager.PERMISSION_GRANTED
        }
        return checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) ==
            PackageManager.PERMISSION_GRANTED
    }

    private fun hasAllFilesAccess(): Boolean {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.R ||
            Environment.isExternalStorageManager()
    }

    private fun openAllFilesAccessSettings(result: MethodChannel.Result) {
        if (hasAllFilesAccess()) {
            result.success(true)
            return
        }
        try {
            val intent = Intent(
                Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                Uri.parse("package:$packageName"),
            )
            startActivity(intent)
            result.success(true)
        } catch (error: Exception) {
            try {
                startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
                result.success(true)
            } catch (fallbackError: Exception) {
                result.error("settings_failed", "无法打开所有文件访问设置：${fallbackError.message}", null)
            }
        }
    }

    private fun requestMediaPermission(result: MethodChannel.Result) {
        if (hasMediaPermission()) {
            result.success(true)
            return
        }
        if (mediaPermissionResult != null) {
            result.error("permission_in_progress", "媒体和文件权限请求正在进行", null)
            return
        }
        mediaPermissionResult = result
        val permissions = when {
            Build.VERSION.SDK_INT >= 34 -> arrayOf(
                Manifest.permission.READ_MEDIA_IMAGES,
                Manifest.permission.READ_MEDIA_VIDEO,
                Manifest.permission.READ_MEDIA_AUDIO,
                Manifest.permission.READ_MEDIA_VISUAL_USER_SELECTED,
            )
            Build.VERSION.SDK_INT >= 33 -> arrayOf(
                Manifest.permission.READ_MEDIA_IMAGES,
                Manifest.permission.READ_MEDIA_VIDEO,
                Manifest.permission.READ_MEDIA_AUDIO,
            )
            else -> arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE)
        }
        requestPermissions(permissions, mediaPermissionRequest)
    }

    private data class PhotoAlbumAccumulator(
        val id: String,
        val name: String,
        var count: Int = 0,
        var totalSizeBytes: Long = 0L,
        var coverUri: String = "",
        var latestTakenAt: Long = 0L,
        var relativePath: String = "",
    )

    private fun readPhotoAlbums(result: MethodChannel.Result) {
        if (!hasPhotosPermission()) {
            result.error("permission_required", "需要读取照片权限", null)
            return
        }

        contentExecutor.execute {
            try {
                val albums = LinkedHashMap<String, PhotoAlbumAccumulator>()
                val projection = mutableListOf(
                    MediaStore.Images.Media._ID,
                    MediaStore.Images.Media.BUCKET_ID,
                    MediaStore.Images.Media.BUCKET_DISPLAY_NAME,
                    MediaStore.Images.Media.DATE_TAKEN,
                    MediaStore.Images.Media.SIZE,
                )
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    projection += MediaStore.Images.Media.RELATIVE_PATH
                }
                val cursor = contentResolver.query(
                    MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                    projection.toTypedArray(),
                    null,
                    null,
                    "${MediaStore.Images.Media.DATE_TAKEN} DESC",
                )

                cursor?.use {
                    val idIndex = it.getColumnIndexOrThrow(MediaStore.Images.Media._ID)
                    val bucketIdIndex = it.getColumnIndex(MediaStore.Images.Media.BUCKET_ID)
                    val bucketNameIndex = it.getColumnIndex(MediaStore.Images.Media.BUCKET_DISPLAY_NAME)
                    val dateIndex = it.getColumnIndex(MediaStore.Images.Media.DATE_TAKEN)
                    val sizeIndex = it.getColumnIndex(MediaStore.Images.Media.SIZE)
                    val relativePathIndex = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        it.getColumnIndex(MediaStore.Images.Media.RELATIVE_PATH)
                    } else {
                        -1
                    }
                    while (it.moveToNext()) {
                        val imageId = it.getLong(idIndex)
                        val albumId = if (bucketIdIndex >= 0) {
                            it.getString(bucketIdIndex).orEmpty()
                        } else {
                            "all"
                        }.ifBlank { "all" }
                        val albumName = if (bucketNameIndex >= 0) {
                            it.getString(bucketNameIndex).orEmpty()
                        } else {
                            ""
                        }.ifBlank { "其他图片" }
                        val takenAt = if (dateIndex >= 0 && !it.isNull(dateIndex)) {
                            it.getLong(dateIndex)
                        } else {
                            0L
                        }
                        val album = albums.getOrPut(albumId) {
                            PhotoAlbumAccumulator(albumId, albumName)
                        }
                        album.count++
                        if (sizeIndex >= 0 && !it.isNull(sizeIndex)) {
                            album.totalSizeBytes += it.getLong(sizeIndex)
                        }
                        if (album.relativePath.isEmpty() && relativePathIndex >= 0) {
                            album.relativePath = it.getString(relativePathIndex).orEmpty()
                                .replace('\\', '/')
                                .trim('/')
                        }
                        if (album.coverUri.isEmpty()) {
                            album.coverUri = Uri.withAppendedPath(
                                MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                                imageId.toString(),
                            ).toString()
                        }
                        if (takenAt > album.latestTakenAt) album.latestTakenAt = takenAt
                    }
                }

                val response = albums.values.map { album ->
                    mapOf(
                        "id" to album.id,
                        "name" to album.name,
                        "count" to album.count,
                        "totalSizeBytes" to album.totalSizeBytes,
                        "coverUri" to album.coverUri,
                        "latestTakenAt" to album.latestTakenAt,
                        "relativePath" to album.relativePath,
                    )
                }
                runOnUiThread { result.success(response) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取相册集失败：${error.message}", null)
                }
            }
        }
    }

    private fun readPhotos(call: MethodCall, result: MethodChannel.Result) {
        readPhotosInternal(call, result, paged = false)
    }

    private fun readPhotoPage(call: MethodCall, result: MethodChannel.Result) {
        readPhotosInternal(call, result, paged = true)
    }

    private fun readPhotosInternal(
        call: MethodCall,
        result: MethodChannel.Result,
        paged: Boolean,
    ) {
        if (!hasPhotosPermission()) {
            result.error("permission_required", "需要读取照片权限", null)
            return
        }

        val albumId = call.argument<String>("albumId")?.trim().orEmpty()
        val offset = call.argument<Int>("offset")?.coerceAtLeast(0) ?: 0
        val limit = call.argument<Int>("limit")?.coerceIn(1, 200) ?: 200
        contentExecutor.execute {
            try {
                val photos = ArrayList<Map<String, Any?>>()
                var total = 0
                val projection = arrayOf(
                    MediaStore.Images.Media._ID,
                    MediaStore.Images.Media.DISPLAY_NAME,
                    MediaStore.Images.Media.DATE_TAKEN,
                    MediaStore.Images.Media.WIDTH,
                    MediaStore.Images.Media.HEIGHT,
                    MediaStore.Images.Media.SIZE,
                )
                val selection = if (albumId.isEmpty()) {
                    null
                } else {
                    "${MediaStore.Images.Media.BUCKET_ID} = ?"
                }
                val selectionArgs = if (albumId.isEmpty()) null else arrayOf(albumId)
                val cursor = contentResolver.query(
                    MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                    projection,
                    selection,
                    selectionArgs,
                    "${MediaStore.Images.Media.DATE_TAKEN} DESC",
                )
                cursor?.use {
                    val idIndex = it.getColumnIndexOrThrow(MediaStore.Images.Media._ID)
                    val nameIndex = it.getColumnIndex(MediaStore.Images.Media.DISPLAY_NAME)
                    val dateIndex = it.getColumnIndex(MediaStore.Images.Media.DATE_TAKEN)
                    val widthIndex = it.getColumnIndex(MediaStore.Images.Media.WIDTH)
                    val heightIndex = it.getColumnIndex(MediaStore.Images.Media.HEIGHT)
                    val sizeIndex = it.getColumnIndex(MediaStore.Images.Media.SIZE)
                    while (it.moveToNext()) {
                        val rowIndex = total++
                        if (paged && (rowIndex < offset || photos.size >= limit)) {
                            continue
                        }
                        val id = it.getLong(idIndex)
                        val uri = Uri.withAppendedPath(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id.toString())
                        photos.add(
                            mapOf(
                                "id" to id.toString(),
                                "name" to if (nameIndex >= 0) it.getString(nameIndex).orEmpty() else "图片 $id",
                                "uri" to uri.toString(),
                                "takenAt" to if (dateIndex >= 0) it.getLong(dateIndex) else 0L,
                                "width" to if (widthIndex >= 0) it.getInt(widthIndex) else 0,
                                "height" to if (heightIndex >= 0) it.getInt(heightIndex) else 0,
                                "sizeBytes" to if (sizeIndex >= 0 && !it.isNull(sizeIndex)) it.getLong(sizeIndex) else 0L,
                            ),
                        )
                    }
                }
                val response: Any = if (paged) {
                    mapOf("items" to photos, "total" to total)
                } else {
                    photos
                }
                runOnUiThread { result.success(response) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取相册内容失败：${error.message}", null)
                }
            }
        }
    }

    private fun readPhotoBytes(call: MethodCall, result: MethodChannel.Result) {
        val uriText = call.argument<String>("uri")
        if (uriText.isNullOrBlank()) {
            result.error("invalid_argument", "图片地址为空", null)
            return
        }
        contentExecutor.execute {
            try {
                val bytes = contentResolver.openInputStream(Uri.parse(uriText))?.use { input ->
                    input.readBytes()
                }
                runOnUiThread { result.success(bytes) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取图片失败：${error.message}", null)
                }
            }
        }
    }

    private fun readPhotoThumbnailBytes(call: MethodCall, result: MethodChannel.Result) {
        val uriText = call.argument<String>("uri")
        if (uriText.isNullOrBlank()) {
            result.error("invalid_argument", "图片地址为空", null)
            return
        }
        contentExecutor.execute {
            try {
                val uri = Uri.parse(uriText)
                val bitmap = when {
                    uri.scheme == "file" -> BitmapFactory.decodeFile(uri.path)
                    Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q ->
                        contentResolver.loadThumbnail(uri, Size(512, 512), null)
                    else -> contentResolver.openInputStream(uri)?.use(BitmapFactory::decodeStream)
                }
                val bytes = bitmap?.let { image ->
                    val output = java.io.ByteArrayOutputStream()
                    image.compress(Bitmap.CompressFormat.JPEG, 82, output)
                    image.recycle()
                    output.toByteArray()
                }
                runOnUiThread { result.success(bytes) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取图片缩略图失败：${error.message}", null)
                }
            }
        }
    }

    private fun copyUriToCache(call: MethodCall, result: MethodChannel.Result) {
        val uriText = call.argument<String>("uri")?.trim().orEmpty()
        val requestedName = call.argument<String>("fileName")?.trim().orEmpty()
        if (uriText.isBlank()) {
            result.error("invalid_argument", "媒体地址为空", null)
            return
        }
        contentExecutor.execute {
            try {
                val safeName = requestedName
                    .replace(Regex("[^A-Za-z0-9._-]"), "_")
                    .ifBlank { "hinge_media" }
                val target = File(cacheDir, "hinge_${System.currentTimeMillis()}_$safeName")
                val sourceUri = Uri.parse(uriText)
                val input = if (sourceUri.scheme == "file") {
                    File(sourceUri.path.orEmpty()).inputStream()
                } else {
                    contentResolver.openInputStream(sourceUri)
                        ?: throw IllegalStateException("无法打开媒体内容")
                }
                input.use { inputStream ->
                    target.outputStream().use { output -> inputStream.copyTo(output) }
                }
                runOnUiThread { result.success(target.absolutePath) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("copy_failed", "准备媒体文件失败：${error.message}", null)
                }
            }
        }
    }

    private fun readPhotoPreviewBytes(call: MethodCall, result: MethodChannel.Result) {
        val uriText = call.argument<String>("uri")
        if (uriText.isNullOrBlank()) {
            result.error("invalid_argument", "图片地址为空", null)
            return
        }
        contentExecutor.execute {
            try {
                val uri = Uri.parse(uriText)
                // 预览使用原始媒体字节；缩略图接口仍然只用于列表，避免预览被
                // 1600px 的二次压缩限制清晰度。文件管理中的图片 URI 同样来自
                // MediaStore，因此这里也能直接读取。
                val bytes = if (uri.scheme == "file") {
                    java.io.File(uri.path.orEmpty()).takeIf { it.isFile }?.readBytes()
                } else {
                    contentResolver.openInputStream(uri)?.use { input -> input.readBytes() }
                }
                runOnUiThread { result.success(bytes) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取图片预览失败：${error.message}", null)
                }
            }
        }
    }

    private fun readFiles(result: MethodChannel.Result) {
        if (!hasMediaPermission() && !hasAllFilesAccess()) {
            result.error("permission_required", "需要读取文件或媒体权限", null)
            return
        }

        val files = ArrayList<Map<String, Any?>>()
        val collection = MediaStore.Files.getContentUri("external")
        val projection = arrayOf(
            MediaStore.Files.FileColumns._ID,
            MediaStore.Files.FileColumns.DISPLAY_NAME,
            MediaStore.Files.FileColumns.MIME_TYPE,
            MediaStore.Files.FileColumns.SIZE,
            MediaStore.Files.FileColumns.DATE_MODIFIED,
        )
        val cursor = contentResolver.query(
            collection,
            projection,
            "${MediaStore.Files.FileColumns._ID} IS NOT NULL",
            null,
            "${MediaStore.Files.FileColumns.DATE_MODIFIED} DESC",
        )
        cursor?.use {
            val idIndex = it.getColumnIndexOrThrow(MediaStore.Files.FileColumns._ID)
            val nameIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DISPLAY_NAME)
            val mimeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.MIME_TYPE)
            val sizeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.SIZE)
            val dateIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DATE_MODIFIED)
            while (it.moveToNext()) {
                val id = it.getLong(idIndex)
                val uri = Uri.withAppendedPath(collection, id.toString())
                files.add(
                    mapOf(
                        "id" to id.toString(),
                        "name" to if (nameIndex >= 0) it.getString(nameIndex).orEmpty() else "未命名文件",
                        "mimeType" to if (mimeIndex >= 0) it.getString(mimeIndex).orEmpty() else "",
                        "sizeBytes" to if (sizeIndex >= 0) it.getLong(sizeIndex) else 0L,
                        "modifiedAt" to if (dateIndex >= 0) it.getLong(dateIndex) * 1000L else 0L,
                        "uri" to uri.toString(),
                    ),
                )
            }
        }
        result.success(files)
    }

    private fun readStorageDirectory(call: MethodCall, result: MethodChannel.Result) {
        val category = call.argument<String>("category")
            ?.trim()
            ?.lowercase(Locale.ROOT)
            .orEmpty()
            .ifBlank { "storage" }
        val path = call.argument<String>("path")?.trim().orEmpty()
        val forceRefresh = call.argument<Boolean>("forceRefresh") == true
        val cacheKey = "$category\u0000$path"
        val now = System.currentTimeMillis()
        if (!forceRefresh) {
            val cached = storageDirectoryCache[cacheKey]
            if (cached != null && cached.expiresAt > now) {
                result.success(cached.entries)
                return
            }
        }
        contentExecutor.execute {
            try {
                val entries = when (category) {
                    "storage" -> listStorageDirectory(path, category)
                    "wechat", "qq" -> querySocialAlbumEntries(category)
                    "recent", "images", "videos", "audio", "documents", "deleted" ->
                        queryMediaCategoryEntries(category)
                    else -> throw IllegalArgumentException("不支持的文件分类：$category")
                }
                storageDirectoryCache[cacheKey] = StorageCacheEntry(
                    // Directory listings are immutable enough for a short
                    // cache window; the explicit refresh button bypasses it.
                    expiresAt = System.currentTimeMillis() + 10_000L,
                    entries = entries,
                )
                runOnUiThread { result.success(entries) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取手机文件失败：" + error.message, null)
                }
            }
        }
    }

    private fun readStorageDirectoryPage(call: MethodCall, result: MethodChannel.Result) {
        val category = call.argument<String>("category")
            ?.trim()
            ?.lowercase(Locale.ROOT)
            .orEmpty()
            .ifBlank { "storage" }
        val path = call.argument<String>("path")?.trim().orEmpty()
        val offset = (call.argument<Number>("offset")?.toInt() ?: 0).coerceAtLeast(0)
        val limit = (call.argument<Number>("limit")?.toInt() ?: 200).coerceAtLeast(1)
        val forceRefresh = call.argument<Boolean>("forceRefresh") == true
        val cacheKey = "$category\u0000$path"
        val now = System.currentTimeMillis()
        if (!forceRefresh) {
            val cached = storageDirectoryCache[cacheKey]
            if (cached != null && cached.expiresAt > now) {
                result.success(storageDirectoryPagePayload(cached.entries, offset, limit))
                return
            }
        }

        contentExecutor.execute {
            try {
                val entries = when (category) {
                    "storage" -> listStorageDirectory(path, category)
                    "wechat", "qq" -> querySocialAlbumEntries(category)
                    "recent", "images", "videos", "audio", "documents", "deleted" ->
                        queryMediaCategoryEntries(category)
                    else -> throw IllegalArgumentException("不支持的文件分类：$category")
                }
                storageDirectoryCache[cacheKey] = StorageCacheEntry(
                    expiresAt = System.currentTimeMillis() + 10_000L,
                    entries = entries,
                )
                val payload = storageDirectoryPagePayload(entries, offset, limit)
                runOnUiThread { result.success(payload) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("read_failed", "读取手机文件失败：" + error.message, null)
                }
            }
        }
    }

    private fun storageDirectoryPagePayload(
        entries: List<Map<String, Any?>>,
        offset: Int,
        limit: Int,
    ): Map<String, Any?> {
        val safeOffset = offset.coerceAtMost(entries.size)
        val end = (safeOffset + limit).coerceAtMost(entries.size)
        return mapOf(
            "items" to entries.subList(safeOffset, end),
            "total" to entries.size,
        )
    }

    private fun deleteFiles(call: MethodCall, result: MethodChannel.Result) {
        val uris = (call.argument<List<*>>("uris") ?: emptyList<Any?>())
            .mapNotNull { it?.toString()?.trim()?.takeIf(String::isNotEmpty) }
        if (uris.isEmpty()) {
            result.success(0)
            return
        }

        contentExecutor.execute {
            try {
                var deleted = 0
                uris.forEach { uriText ->
                    val uri = Uri.parse(uriText)
                    val wasDeleted = if (uri.scheme.equals("file", ignoreCase = true)) {
                        val root = Environment.getExternalStorageDirectory().canonicalFile
                        val file = File(uri.path.orEmpty()).canonicalFile
                        val insideStorage = file.path == root.path ||
                            file.path.startsWith(root.path + File.separator)
                        insideStorage && file.isFile && file.delete()
                    } else {
                        contentResolver.delete(uri, null, null) > 0
                    }
                    if (wasDeleted) deleted++
                }
                storageDirectoryCache.clear()
                runOnUiThread { result.success(deleted) }
            } catch (error: Exception) {
                runOnUiThread {
                    result.error("delete_failed", "删除文件失败：${error.message}", null)
                }
            }
        }
    }

    private fun listStorageDirectory(
        relativePath: String,
        category: String,
    ): List<Map<String, Any?>> {
        val root = Environment.getExternalStorageDirectory().canonicalFile
        val normalized = relativePath
            .replace('\\', '/')
            .trim('/')
        if (normalized.split('/').any { it == ".." }) {
            throw SecurityException("不允许访问手机存储根目录以外的路径")
        }

        val target = File(root, normalized).canonicalFile
        val rootPath = root.path
        if (target.path != rootPath &&
            !target.path.startsWith(rootPath + File.separator)
        ) {
            throw SecurityException("不允许访问手机存储根目录以外的路径")
        }
        if (!target.isDirectory) {
            throw IllegalArgumentException("目录不存在或不可读取")
        }

        return target.listFiles()
            ?.sortedWith(
                compareBy<File> { !it.isDirectory }
                    .thenByDescending { it.lastModified() }
                    .thenBy { it.name.lowercase(Locale.ROOT) },
            )
            ?.map { file ->
                val entryPath = file.canonicalPath
                    .removePrefix(rootPath)
                    .trimStart(File.separatorChar)
                    .replace(File.separatorChar, '/')
                storageEntry(
                    id = entryPath,
                    name = file.name,
                    relativePath = entryPath,
                    mimeType = if (file.isDirectory) {
                        "inode/directory"
                    } else {
                        guessMimeType(file.name)
                    },
                    sizeBytes = if (file.isFile) file.length() else 0L,
                    modifiedAt = file.lastModified(),
                    uri = Uri.fromFile(file).toString(),
                    isDirectory = file.isDirectory,
                    category = category,
                )
            }
            .orEmpty()
    }

    private fun queryMediaCategoryEntries(category: String): List<Map<String, Any?>> {
        if (category == "deleted" && Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            return emptyList()
        }
        // Query the dedicated media collections. Album/photo pages use the
        // Images collection, so the Windows file manager must use the same
        // source for its image category instead of a second Files view.
        val collection = when (category) {
            "images" -> MediaStore.Images.Media.EXTERNAL_CONTENT_URI
            "videos" -> MediaStore.Video.Media.EXTERNAL_CONTENT_URI
            "audio" -> MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
            else -> MediaStore.Files.getContentUri("external")
        }
        val mimeColumn = MediaStore.Files.FileColumns.MIME_TYPE
        val selection: String
        val selectionArgs: Array<String>?
        when (category) {
            "images" -> {
                selection = mimeColumn + " LIKE ?"
                selectionArgs = arrayOf("image/%")
            }
            "videos" -> {
                selection = mimeColumn + " LIKE ?"
                selectionArgs = arrayOf("video/%")
            }
            "audio" -> {
                selection = mimeColumn + " LIKE ?"
                selectionArgs = arrayOf("audio/%")
            }
            "documents" -> {
                selection = "(" + mimeColumn + " IS NULL OR (" +
                    mimeColumn + " NOT LIKE ? AND " +
                    mimeColumn + " NOT LIKE ? AND " +
                    mimeColumn + " NOT LIKE ?))"
                selectionArgs = arrayOf("image/%", "video/%", "audio/%")
            }
            "deleted" -> {
                selection = mimeColumn + " IS NOT NULL AND is_trashed = 1"
                selectionArgs = null
            }
            else -> {
                // Recent files must include files whose OEM MediaProvider did
                // not infer a MIME type. The filename is still returned and
                // the Windows side can display it normally.
                selection = "_id IS NOT NULL"
                selectionArgs = null
            }
        }

        val projection = arrayOf(
            MediaStore.Files.FileColumns._ID,
            MediaStore.Files.FileColumns.DISPLAY_NAME,
            MediaStore.Files.FileColumns.MIME_TYPE,
            MediaStore.Files.FileColumns.SIZE,
            MediaStore.Files.FileColumns.DATE_MODIFIED,
            MediaStore.Files.FileColumns.RELATIVE_PATH,
        )
        val entries = ArrayList<Map<String, Any?>>()
        val cursor = contentResolver.query(
            collection,
            projection,
            selection,
            selectionArgs,
            MediaStore.Files.FileColumns.DATE_MODIFIED + " DESC",
        )
        cursor?.use {
            val idIndex = it.getColumnIndexOrThrow(MediaStore.Files.FileColumns._ID)
            val nameIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DISPLAY_NAME)
            val mimeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.MIME_TYPE)
            val sizeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.SIZE)
            val dateIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DATE_MODIFIED)
            val pathIndex = it.getColumnIndex(MediaStore.Files.FileColumns.RELATIVE_PATH)
            while (it.moveToNext()) {
                val id = it.getLong(idIndex)
                val name = if (nameIndex >= 0) {
                    it.getString(nameIndex).orEmpty()
                } else {
                    "未命名文件"
                }
                val relativePath = if (pathIndex >= 0) {
                    it.getString(pathIndex).orEmpty() + name
                } else {
                    name
                }
                entries.add(
                    storageEntry(
                        id = id.toString(),
                        name = name,
                        relativePath = relativePath,
                        mimeType = if (mimeIndex >= 0) {
                            it.getString(mimeIndex).orEmpty().ifBlank { guessMimeType(name) }
                        } else {
                            guessMimeType(name)
                        },
                        sizeBytes = if (sizeIndex >= 0 && !it.isNull(sizeIndex)) {
                            it.getLong(sizeIndex)
                        } else {
                            0L
                        },
                        modifiedAt = if (dateIndex >= 0 && !it.isNull(dateIndex)) {
                            it.getLong(dateIndex) * 1000L
                        } else {
                            0L
                        },
                        uri = Uri.withAppendedPath(collection, id.toString()).toString(),
                        isDirectory = false,
                        category = category,
                    ),
                )
            }
        }
        if (category == "documents" && entries.isEmpty() && hasAllFilesAccess()) {
            return scanDocumentFiles()
        }
        return entries
    }

    private fun scanDocumentFiles(): List<Map<String, Any?>> {
        val root = Environment.getExternalStorageDirectory().canonicalFile
        val pending = java.util.ArrayDeque<File>()
        val entries = ArrayList<Map<String, Any?>>()
        pending.add(root)
        val documentExtensions = setOf(
            "pdf", "doc", "docx", "docm", "odt", "xls", "xlsx", "xlsm", "ods",
            "ppt", "pptx", "pptm", "odp", "txt", "md", "csv", "rtf",
        )

        while (pending.isNotEmpty()) {
            val directory = pending.removeFirst()
            val children = try {
                directory.listFiles()
            } catch (_: SecurityException) {
                null
            } ?: continue
            for (file in children) {
                if (file.isDirectory) {
                    // Android's private app directories are not useful to a
                    // user-facing file manager and can be unreadable even
                    // with broad storage access.
                    if (file.name != "Android") pending.addLast(file)
                    continue
                }
                val extension = file.extension.lowercase(Locale.ROOT)
                if (!documentExtensions.contains(extension)) continue
                val relativePath = file.canonicalPath
                    .removePrefix(root.path)
                    .trimStart(File.separatorChar)
                    .replace(File.separatorChar, '/')
                entries.add(
                    storageEntry(
                        id = relativePath,
                        name = file.name,
                        relativePath = relativePath,
                        mimeType = guessMimeType(file.name),
                        sizeBytes = file.length(),
                        modifiedAt = file.lastModified(),
                        uri = Uri.fromFile(file).toString(),
                        isDirectory = false,
                        category = "documents",
                    ),
                )
            }
        }
        entries.sortByDescending { (it["modifiedAt"] as? Long) ?: 0L }
        return entries
    }

    private fun querySocialAlbumEntries(category: String): List<Map<String, Any?>> {
        // 微信和 QQ 在文件管理中对应固定的公共相册目录，而不是模糊匹配
        // 应用名/相册名。这样可以覆盖目录内的图片、视频以及其他媒体文件。
        val collection = MediaStore.Files.getContentUri("external")
        val pathColumn = if (Build.VERSION.SDK_INT >= 29) {
            MediaStore.Files.FileColumns.RELATIVE_PATH
        } else {
            MediaStore.Files.FileColumns.DATA
        }
        val folder = if (category == "wechat") {
            "Pictures/WeiXin/"
        } else {
            "Pictures/QQ/"
        }
        // RELATIVE_PATH can be case-sensitive on some OEM MediaProvider
        // implementations, while DATA is an absolute path on older Android
        // versions. Normalize the queried column so all case variants of the
        // public social album folders are included, including nested folders.
        val normalizedPathColumn = "LOWER($pathColumn)"
        val selection: String
        val args: Array<String>
        if (category == "wechat") {
            selection = "$normalizedPathColumn LIKE ? OR $normalizedPathColumn LIKE ?"
            args = arrayOf("%pictures/weixin%", "%pictures/wechat%")
        } else {
            selection = "$normalizedPathColumn LIKE ? OR " +
                "$normalizedPathColumn LIKE ? OR $normalizedPathColumn LIKE ?"
            args = arrayOf("%pictures/qq%", "%dcim/qq%", "%tencent/qq_images%")
        }
        val projection = arrayOf(
            MediaStore.Files.FileColumns._ID,
            MediaStore.Files.FileColumns.DISPLAY_NAME,
            MediaStore.Files.FileColumns.MIME_TYPE,
            MediaStore.Files.FileColumns.SIZE,
            MediaStore.Files.FileColumns.DATE_MODIFIED,
            pathColumn,
        )
        val entries = ArrayList<Map<String, Any?>>()
        val cursor = contentResolver.query(
            collection,
            projection,
            selection,
            args,
            MediaStore.Files.FileColumns.DATE_MODIFIED + " DESC",
        )
        cursor?.use {
            val idIndex = it.getColumnIndexOrThrow(MediaStore.Files.FileColumns._ID)
            val nameIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DISPLAY_NAME)
            val mimeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.MIME_TYPE)
            val sizeIndex = it.getColumnIndex(MediaStore.Files.FileColumns.SIZE)
            val dateIndex = it.getColumnIndex(MediaStore.Files.FileColumns.DATE_MODIFIED)
            val pathIndex = it.getColumnIndex(pathColumn)
            // Social albums can contain thousands of items. Do not reuse the
            // recent-files safety cap here; the Windows category is an
            // explicit folder view and must expose the complete directory.
            while (it.moveToNext()) {
                val id = it.getLong(idIndex)
                val name = if (nameIndex >= 0) it.getString(nameIndex).orEmpty() else "未命名文件"
                val path = if (pathIndex >= 0) it.getString(pathIndex).orEmpty() else folder
                entries.add(
                    storageEntry(
                        id = id.toString(),
                        name = name,
                        relativePath = path.trimEnd('/') + "/" + name,
                        mimeType = if (mimeIndex >= 0) {
                            it.getString(mimeIndex).orEmpty().ifBlank { guessMimeType(name) }
                        } else {
                            guessMimeType(name)
                        },
                        sizeBytes = if (sizeIndex >= 0 && !it.isNull(sizeIndex)) it.getLong(sizeIndex) else 0L,
                        modifiedAt = if (dateIndex >= 0 && !it.isNull(dateIndex)) it.getLong(dateIndex) * 1000L else 0L,
                        uri = Uri.withAppendedPath(collection, id.toString()).toString(),
                        isDirectory = false,
                        category = category,
                    ),
                )
            }
        }
        return entries
    }

    private fun storageEntry(
        id: String,
        name: String,
        relativePath: String,
        mimeType: String,
        sizeBytes: Long,
        modifiedAt: Long,
        uri: String,
        isDirectory: Boolean,
        category: String,
    ): Map<String, Any?> {
        return mapOf(
            "id" to id,
            "name" to name,
            "relativePath" to relativePath,
            "mimeType" to mimeType,
            "sizeBytes" to sizeBytes,
            "modifiedAt" to modifiedAt,
            "uri" to uri,
            "isDirectory" to isDirectory,
            "category" to category,
        )
    }

    private fun guessMimeType(name: String): String {
        val extension = name.substringAfterLast('.', "").lowercase(Locale.ROOT)
        return android.webkit.MimeTypeMap.getSingleton()
            .getMimeTypeFromExtension(extension)
            ?: "application/octet-stream"
    }

    private fun readWorkspaceList(key: String): List<Map<String, Any?>> {
        val raw = getSharedPreferences("workspace_data", MODE_PRIVATE)
            .getString(key, "[]") ?: "[]"
        val array = JSONArray(raw)
        val result = ArrayList<Map<String, Any?>>()
        for (index in 0 until array.length()) {
            val objectValue = array.optJSONObject(index) ?: continue
            result.add(jsonObjectToMap(objectValue))
        }
        return result
    }

    private fun saveWorkspaceItem(key: String, arguments: Any?, result: MethodChannel.Result) {
        val item = arguments as? Map<*, *>
        val id = item?.get("id")?.toString()
        if (item == null || id.isNullOrBlank()) {
            result.error("invalid_argument", "数据缺少 id", null)
            return
        }
        val items = readWorkspaceList(key).toMutableList()
        val normalized = item.entries.associate { (entryKey, value) -> entryKey.toString() to value }
        val index = items.indexOfFirst { it["id"]?.toString() == id }
        if (index >= 0) items[index] = normalized else items.add(normalized)
        saveWorkspaceList(key, items)
        result.success(true)
    }

    private fun deleteWorkspaceItem(key: String, arguments: Any?, result: MethodChannel.Result) {
        val id = arguments?.toString().orEmpty()
        val items = readWorkspaceList(key).filter { it["id"]?.toString() != id }
        saveWorkspaceList(key, items)
        result.success(true)
    }

    private fun saveWorkspaceList(key: String, items: List<Map<String, Any?>>) {
        val array = JSONArray()
        items.forEach { array.put(JSONObject(it)) }
        getSharedPreferences("workspace_data", MODE_PRIVATE)
            .edit()
            .putString(key, array.toString())
            .apply()
    }

    private fun jsonObjectToMap(value: JSONObject): Map<String, Any?> {
        val map = HashMap<String, Any?>()
        val keys = value.keys()
        while (keys.hasNext()) {
            val key = keys.next()
            val item = value.opt(key)
            map[key] = if (item == JSONObject.NULL) null else item
        }
        return map
    }

    companion object {
        private const val ACTION_OPEN_RECEIVED_DIRECTORY =
            "com.hinge.office.OPEN_RECEIVED_DIRECTORY"
        private const val EXTRA_RECEIVED_FILE_PATH =
            "com.hinge.office.RECEIVED_FILE_PATH"
        private const val FILE_CHANNEL_ID = "hinge_file_transfer"
    }
}
