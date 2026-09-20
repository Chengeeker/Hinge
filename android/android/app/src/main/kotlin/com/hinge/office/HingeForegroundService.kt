package com.hinge.office

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import android.content.pm.ServiceInfo
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Keeps the native LAN listener, TCP sessions and background maintenance alive
 * while the app is not in the foreground. The notification is intentional:
 * Android requires a user-visible foreground service for a connection that
 * continues in the background.
 */
class HingeForegroundService : Service() {
    private lateinit var nativeConnectionBroker: HingeNativeConnectionBroker
    private var multicastLock: WifiManager.MulticastLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var smsContentObserver: SmsContentObserver? = null
    private var connectivityManager: ConnectivityManager? = null
    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private var boundNetwork: Network? = null
    private var cpuWakeLock: PowerManager.WakeLock? = null
    private val discoveryHandler = Handler(Looper.getMainLooper())
    private val discoveryExecutor = Executors.newSingleThreadExecutor()
    private val discoveryBeaconInFlight = AtomicBoolean(false)
    private var discoveryBeaconRunnable: Runnable? = null

    override fun onCreate() {
        super.onCreate()
        current = this
        HingeDiagnostics.from(this).log("service_on_create")
        nativeConnectionBroker = HingeNativeConnectionBroker(this)
        if (ENABLE_NATIVE_CONNECTION_BROKER) {
            nativeConnectionBroker.start()
        } else {
            HingeDiagnostics.from(this).log("native_broker_disabled_using_dart_transport")
        }
        createNotificationChannel()
        acquireCpuWakeLock()
        registerPhysicalNetworkCallback()
        acquireMulticastLock()
        acquireWifiLock()
        smsContentObserver = SmsContentObserver(this)
        startDiscoveryBeaconLoop()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            HingeDiagnostics.from(this).log("service_stop_requested")
            nativeConnectionBroker.stop()
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }

        if (ENABLE_NATIVE_CONNECTION_BROKER && intent?.action == ACTION_CONFIGURE) {
            nativeConnectionBroker.configure(
                mapOf(
                    "deviceId" to intent.getStringExtra("deviceId"),
                    "name" to intent.getStringExtra("name"),
                    "manufacturer" to intent.getStringExtra("manufacturer"),
                    "model" to intent.getStringExtra("model"),
                    "localPairingCode" to intent.getStringExtra("localPairingCode"),
                    "listenPort" to intent.getIntExtra("listenPort", SESSION_PORT),
                ),
            )
        }

        refreshSmsContentObserver()

        val notificationBuilder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }
        val notification = notificationBuilder
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle("Hinge 正在保持设备连接")
            .setContentText("局域网发现与连接保活在后台运行")
            .setCategory(Notification.CATEGORY_SERVICE)
            .setOngoing(persistentNotificationEnabled())
            .setAutoCancel(!persistentNotificationEnabled())
            .setContentIntent(mainActivityIntent())
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        // The native beacon is deliberately independent from the Flutter
        // isolate. If an OEM suspends or reclaims Dart while the foreground
        // notification remains visible, a Windows client that starts later
        // must still be able to discover this phone.
        broadcastDiscoveryBeacon()
        return START_STICKY
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        // Some OEM launchers treat a removed task as an app-stop gesture. The
        // foreground service is the connection owner, so explicitly schedule
        // it again instead of allowing the LAN session to disappear with the
        // last activity task.
        try {
            val restart = Intent(this, HingeForegroundService::class.java).apply {
                action = ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(restart)
            } else {
                startService(restart)
            }
        } catch (_: Exception) {
            // The system may already be shutting the process down.
        }
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        HingeDiagnostics.from(this).log("service_on_destroy")
        if (::nativeConnectionBroker.isInitialized) nativeConnectionBroker.stop()
        if (current === this) current = null
        discoveryBeaconRunnable?.let(discoveryHandler::removeCallbacks)
        discoveryBeaconRunnable = null
        discoveryExecutor.shutdownNow()
        releaseCpuWakeLock()
        smsContentObserver?.close()
        smsContentObserver = null
        unregisterPhysicalNetworkCallback()
        releaseMulticastLock()
        releaseWifiLock()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    fun connectionBroker(): HingeNativeConnectionBroker = nativeConnectionBroker

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "设备连接",
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "保持 Hinge 的局域网设备连接"
                setShowBadge(false)
            },
        )
    }

    private fun mainActivityIntent(): PendingIntent {
        val intent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                PendingIntent.FLAG_IMMUTABLE
            } else {
                0
            }
        return PendingIntent.getActivity(this, 0, intent, flags)
    }

    private fun acquireMulticastLock() {
        try {
            val wifiManager = applicationContext
                .getSystemService(Context.WIFI_SERVICE) as WifiManager
            multicastLock = wifiManager.createMulticastLock("HingeBackgroundDiscovery").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
            multicastLock = null
        }
    }

    private fun releaseMulticastLock() {
        try {
            if (multicastLock?.isHeld == true) multicastLock?.release()
        } catch (_: Exception) {
            // The Wi-Fi adapter may disappear while the service is stopping.
        } finally {
            multicastLock = null
        }
    }

    private fun acquireWifiLock() {
        try {
            val wifiManager = applicationContext
                .getSystemService(Context.WIFI_SERVICE) as WifiManager
            val mode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                WifiManager.WIFI_MODE_FULL_LOW_LATENCY
            } else {
                WifiManager.WIFI_MODE_FULL_HIGH_PERF
            }
            wifiLock = wifiManager.createWifiLock(mode, "HingeConnection").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
            wifiLock = null
        }
    }

    private fun releaseWifiLock() {
        try {
            if (wifiLock?.isHeld == true) wifiLock?.release()
        } catch (_: Exception) {
            // Wi-Fi may be disabled while the service is being torn down.
        } finally {
            wifiLock = null
        }
    }

    /**
     * A foreground service keeps the process important, but it does not keep
     * the CPU executing while the screen is off. The native TCP broker and its
     * heartbeat timer otherwise stop making progress even though this service
     * notification remains visible. This lock is held only for the lifetime
     * of the user-visible connection service and is released during teardown.
     */
    private fun acquireCpuWakeLock() {
        try {
            val powerManager = getSystemService(Context.POWER_SERVICE) as PowerManager
            cpuWakeLock = powerManager.newWakeLock(
                PowerManager.PARTIAL_WAKE_LOCK,
                "Hinge::Connection",
            ).apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
            cpuWakeLock = null
        }
    }

    private fun releaseCpuWakeLock() {
        try {
            if (cpuWakeLock?.isHeld == true) cpuWakeLock?.release()
        } catch (_: Exception) {
            // The power service may already be unavailable during teardown.
        } finally {
            cpuWakeLock = null
        }
    }

    /**
     * Keep the process bound to the current physical LAN while the Activity is
     * paused. MainActivity performs the same binding before creating Dart
     * sockets, but that callback only runs when the UI is alive. A Wi-Fi roam,
     * DHCP renewal or access-point change can otherwise leave an already
     * running foreground service attached to a stale Network object until the
     * user opens Hinge again.
     */
    private fun registerPhysicalNetworkCallback() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        val manager = getSystemService(Context.CONNECTIVITY_SERVICE)
            as? ConnectivityManager ?: return
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .addTransportType(NetworkCapabilities.TRANSPORT_ETHERNET)
            .build()
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                bindToPhysicalNetwork(manager, network)
                broadcastDiscoveryBeacon()
            }

            override fun onCapabilitiesChanged(
                network: Network,
                capabilities: NetworkCapabilities,
            ) {
                if (isPhysicalLan(capabilities)) {
                    bindToPhysicalNetwork(manager, network)
                    broadcastDiscoveryBeacon()
                }
            }

            override fun onLost(network: Network) {
                if (boundNetwork != network) return
                boundNetwork = null
                val replacement = manager.allNetworks.firstOrNull { candidate ->
                    val capabilities = manager.getNetworkCapabilities(candidate)
                    capabilities != null && isPhysicalLan(capabilities)
                }
                if (replacement != null) {
                    bindToPhysicalNetwork(manager, replacement)
                    broadcastDiscoveryBeacon()
                } else {
                    try {
                        manager.bindProcessToNetwork(null)
                    } catch (_: Exception) {
                        // The process may already be shutting down.
                    }
                }
            }
        }

        try {
            manager.registerNetworkCallback(request, callback)
            connectivityManager = manager
            networkCallback = callback
            manager.allNetworks
                .asSequence()
                .mapNotNull { network ->
                    manager.getNetworkCapabilities(network)?.let { network to it }
                }
                .firstOrNull { (_, capabilities) -> isPhysicalLan(capabilities) }
                ?.first
                ?.let { bindToPhysicalNetwork(manager, it) }
        } catch (_: Exception) {
            connectivityManager = null
            networkCallback = null
        }
    }

    private fun unregisterPhysicalNetworkCallback() {
        val manager = connectivityManager
        val callback = networkCallback
        try {
            if (manager != null && callback != null) {
                manager.unregisterNetworkCallback(callback)
            }
            if (manager != null && boundNetwork != null) {
                manager.bindProcessToNetwork(null)
            }
        } catch (_: Exception) {
            // The network service may already be unavailable during teardown.
        } finally {
            connectivityManager = null
            networkCallback = null
            boundNetwork = null
        }
    }

    private fun bindToPhysicalNetwork(
        manager: ConnectivityManager,
        network: Network,
    ) {
        val capabilities = try {
            manager.getNetworkCapabilities(network)
        } catch (_: Exception) {
            null
        } ?: return
        if (!isPhysicalLan(capabilities) || boundNetwork == network) return
        try {
            if (manager.bindProcessToNetwork(network)) {
                boundNetwork = network
            }
        } catch (_: Exception) {
            // MainActivity/Dart will retry the binding on its next refresh.
        }
    }

    private fun isPhysicalLan(capabilities: NetworkCapabilities): Boolean =
        !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN) &&
            (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))

    private fun startDiscoveryBeaconLoop() {
        if (discoveryBeaconRunnable != null) return
        val runnable = object : Runnable {
            override fun run() {
                broadcastDiscoveryBeacon()
                discoveryHandler.postDelayed(this, DISCOVERY_BEACON_INTERVAL_MS)
            }
        }
        discoveryBeaconRunnable = runnable
        discoveryHandler.post(runnable)
    }

    /**
     * Sends a small discovery announcement from the foreground service itself.
     * Flutter's DiscoveryService still consumes the same discovery payload,
     * while this fallback keeps device discovery alive when Android pauses the
     * Flutter isolate or its UDP socket while the foreground notification is
     * present.
     */
    private fun broadcastDiscoveryBeacon() {
        if (!discoveryBeaconInFlight.compareAndSet(false, true)) return
        discoveryExecutor.execute {
            try {
                val payload = discoveryPayload() ?: return@execute
                val bytes = payload.toString().toByteArray(Charsets.UTF_8)
                val endpoints = physicalDiscoveryEndpoints()
                var sent = false

                for ((address, broadcast) in endpoints) {
                    try {
                        DatagramSocket(null).use { socket ->
                            socket.reuseAddress = true
                            socket.broadcast = true
                            socket.bind(InetSocketAddress(address, 0))
                            sendDiscoveryPacket(socket, bytes, InetAddress.getByName("255.255.255.255"))
                            if (broadcast != null && broadcast != InetAddress.getByName("255.255.255.255")) {
                                sendDiscoveryPacket(socket, bytes, broadcast)
                            }
                            sent = true
                        }
                    } catch (_: Exception) {
                        // Continue with another physical interface or fallback.
                    }
                }

                if (!sent) {
                    try {
                        DatagramSocket().use { socket ->
                            socket.broadcast = true
                            sendDiscoveryPacket(
                                socket,
                                bytes,
                                InetAddress.getByName("255.255.255.255"),
                            )
                        }
                    } catch (_: Exception) {
                        // Flutter's discovery loop remains an additional path.
                    }
                }
            } finally {
                discoveryBeaconInFlight.set(false)
            }
        }
    }

    private fun sendDiscoveryPacket(
        socket: DatagramSocket,
        bytes: ByteArray,
        target: InetAddress,
    ) {
        socket.send(DatagramPacket(bytes, bytes.size, target, DISCOVERY_PORT))
    }

    private fun discoveryPayload(): JSONObject? {
        val identityFile = File(filesDir, "identity.json")
        val identity = try {
            if (!identityFile.isFile) return null
            JSONObject(identityFile.readText())
        } catch (_: Exception) {
            return null
        }

        val deviceId = identity.optString("deviceId").trim()
        if (deviceId.isEmpty()) return null

        val capabilities = JSONArray().apply {
            put("file_transfer")
            put("clipboard")
            put("remote_control")
            put("backup")
        }
        val addresses = JSONArray().apply {
            physicalDiscoveryEndpoints().forEach { (address, _) ->
                put(address.hostAddress)
            }
        }
        val appVersion = try {
            packageManager.getPackageInfo(packageName, 0).versionName
                ?.takeIf { it.isNotBlank() }
                ?: "unknown"
        } catch (_: Exception) {
            "unknown"
        }
        return JSONObject().apply {
            put("version", appVersion)
            put("deviceId", deviceId)
            put("name", identity.optString("name", "Android 设备"))
            put("manufacturer", identity.optString("manufacturer"))
            put("model", identity.optString("model"))
            put("platform", "android")
            put("port", SESSION_PORT)
            put("discoveryPort", DISCOVERY_PORT)
            put("capabilities", capabilities)
            put("protocolVersion", PROTOCOL_VERSION)
            put("timestamp", System.currentTimeMillis() / 1000L)
            put("connectionRequested", false)
            put("automaticReconnect", false)
            put("addresses", addresses)
        }
    }

    private fun physicalDiscoveryEndpoints(): List<Pair<Inet4Address, Inet4Address?>> {
        val endpoints = mutableListOf<Pair<Inet4Address, Inet4Address?>>()
        val interfaces = try {
            NetworkInterface.getNetworkInterfaces()
        } catch (_: Exception) {
            null
        } ?: return endpoints

        while (interfaces.hasMoreElements()) {
            val networkInterface = interfaces.nextElement()
            val name = networkInterface.name.lowercase()
            if (!runCatching { networkInterface.isUp }.getOrDefault(false) ||
                networkInterface.isLoopback ||
                networkInterface.isPointToPoint ||
                name.contains("tun") ||
                name.contains("tap") ||
                name.contains("vpn") ||
                name.contains("rmnet") ||
                name.contains("dummy")) {
                continue
            }

            for (interfaceAddress in networkInterface.interfaceAddresses) {
                val address = interfaceAddress.address
                if (address !is Inet4Address ||
                    address.isLoopbackAddress ||
                    address.isLinkLocalAddress) {
                    continue
                }
                val broadcast = interfaceAddress.broadcast as? Inet4Address
                if (broadcast != null) endpoints += address to broadcast
            }
        }
        return endpoints.distinctBy { "${it.first.hostAddress}/${it.second?.hostAddress}" }
    }

    private fun persistentNotificationEnabled(): Boolean =
        getSharedPreferences("app_settings", MODE_PRIVATE)
            .getBoolean("persistent_notification_enabled", true)

    private fun refreshSmsContentObserver() {
        val observer = smsContentObserver ?: return
        val canRead = Build.VERSION.SDK_INT < Build.VERSION_CODES.M ||
            checkSelfPermission(android.Manifest.permission.READ_SMS) ==
            android.content.pm.PackageManager.PERMISSION_GRANTED
        if (SmsRelaySettings.isEnabled(this) && canRead) observer.start() else observer.stop()
    }

    companion object {
        // The native broker owns TCP while the foreground service is alive;
        // Flutter observes and controls the session but does not own its life.
        const val ENABLE_NATIVE_CONNECTION_BROKER = true
        @Volatile
        var current: HingeForegroundService? = null

        const val ACTION_START = "com.hinge.office.START_CONNECTION_SERVICE"
        const val ACTION_STOP = "com.hinge.office.STOP_CONNECTION_SERVICE"
        const val ACTION_CONFIGURE = "com.hinge.office.CONFIGURE_CONNECTION_SERVICE"
        private const val CHANNEL_ID = "hinge_connection"
        private const val NOTIFICATION_ID = 52831
        private const val DISCOVERY_PORT = 52830
        private const val SESSION_PORT = 52831
        private const val PROTOCOL_VERSION = "0.1"
        private const val DISCOVERY_BEACON_INTERVAL_MS = 5_000L
    }
}
