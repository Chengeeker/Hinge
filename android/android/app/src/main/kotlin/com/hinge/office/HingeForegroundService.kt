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
import android.os.SystemClock
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
 * Owns the native LAN listener, active TCP sessions and background
 * wake/resume lifecycle while the app is not in the foreground. When the
 * device is idle, it can enter low-power standby and retain only the
 * foreground-service status and wake entry. The notification is intentional:
 * Android requires a user-visible foreground service for this background
 * connection and wake lifecycle.
 */
class HingeForegroundService : Service() {
    private lateinit var nativeConnectionBroker: HingeNativeConnectionBroker
    private var multicastLock: WifiManager.MulticastLock? = null
    private var wifiHighPerformanceLock: WifiManager.WifiLock? = null
    private var wifiLowLatencyLock: WifiManager.WifiLock? = null
    private var smsContentObserver: SmsContentObserver? = null
    private var connectivityManager: ConnectivityManager? = null
    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private var vpnNetworkCallback: ConnectivityManager.NetworkCallback? = null
    @Volatile
    private var boundNetwork: Network? = null
    @Volatile
    private var processBoundNetwork: Network? = null
    @Volatile
    private var vpnCompatibilityPresent = false
    private var cpuWakeLock: PowerManager.WakeLock? = null
    private val discoveryHandler = Handler(Looper.getMainLooper())
    private val discoveryExecutor = Executors.newSingleThreadExecutor()
    private val discoveryBeaconInFlight = AtomicBoolean(false)
    private var discoveryBeaconRunnable: Runnable? = null
    private val standbyHandler = Handler(Looper.getMainLooper())
    private var standbyCheckRunnable: Runnable? = null
    @Volatile
    private var activityForeground = false
    @Volatile
    private var lowPowerStandby = false
    @Volatile
    private var activeWindowUntilElapsed = 0L

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
        registerPhysicalNetworkCallback()
        smsContentObserver = SmsContentObserver(this)
        startStandbyMonitor()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        if (action == ACTION_STOP) {
            HingeDiagnostics.from(this).log("service_stop_requested")
            nativeConnectionBroker.stop()
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }

        if (ENABLE_NATIVE_CONNECTION_BROKER && action == ACTION_CONFIGURE) {
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
            activateConnectionWindow("configure")
        }

        if (action == ACTION_START) {
            activateConnectionWindow("service_start")
        }

        if (ENABLE_NATIVE_CONNECTION_BROKER && action == ACTION_WAKE) {
            activateConnectionWindow("wake")
            nativeConnectionBroker.requestWake(
                intent.getStringExtra(EXTRA_WAKE_REASON).orEmpty().ifEmpty { "companion" },
            )
        }

        refreshSmsContentObserver()

        publishForegroundNotification()
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
        standbyCheckRunnable?.let(standbyHandler::removeCallbacks)
        standbyCheckRunnable = null
        standbyHandler.removeCallbacksAndMessages(null)
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

    fun setActivityForeground(foreground: Boolean) {
        activityForeground = foreground
        if (foreground) {
            activateConnectionWindow("activity_foreground")
        } else {
            activeWindowUntilElapsed = maxOf(
                activeWindowUntilElapsed,
                SystemClock.elapsedRealtime() + ACTIVE_WINDOW_MS,
            )
        }
    }

    private fun publishForegroundNotification() {
        val notificationBuilder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            Notification.Builder(this)
        }
        val notification = notificationBuilder
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle(
                if (lowPowerStandby) "Hinge 已进入低功耗待命" else "Hinge 正在后台运行",
            )
            .setContentText(
                if (lowPowerStandby) "发送文件时会尝试自动唤醒；点击通知可立即恢复连接"
                else "局域网发现与连接服务已启动，空闲后会进入低功耗待命",
            )
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
    }

    private fun activateConnectionWindow(reason: String) {
        activeWindowUntilElapsed = SystemClock.elapsedRealtime() + ACTIVE_WINDOW_MS
        if (lowPowerStandby) {
            lowPowerStandby = false
            nativeConnectionBroker.exitLowPowerStandby(reason)
            HingeDiagnostics.from(this).log(
                "low_power_standby_exited",
                mapOf("reason" to reason),
            )
        }
        acquireCpuWakeLock()
        acquireMulticastLock()
        acquireWifiLock()
        startDiscoveryBeaconLoop()
        broadcastDiscoveryBeacon()
    }

    private fun startStandbyMonitor() {
        if (standbyCheckRunnable != null) return
        val runnable = object : Runnable {
            override fun run() {
                evaluateLowPowerStandby()
                standbyHandler.postDelayed(this, STANDBY_CHECK_INTERVAL_MS)
            }
        }
        standbyCheckRunnable = runnable
        standbyHandler.postDelayed(runnable, STANDBY_CHECK_INTERVAL_MS)
    }

    private fun evaluateLowPowerStandby() {
        if (lowPowerStandby || activityForeground) return
        val powerManager = getSystemService(Context.POWER_SERVICE) as? PowerManager
        if (powerManager?.isInteractive == true) return
        if (SystemClock.elapsedRealtime() < activeWindowUntilElapsed) return

        lowPowerStandby = true
        activeWindowUntilElapsed = 0L
        nativeConnectionBroker.enterLowPowerStandby()
        stopDiscoveryBeaconLoop()
        releaseCpuWakeLock()
        releaseMulticastLock()
        releaseWifiLock()
        publishForegroundNotification()
        HingeDiagnostics.from(this).log("low_power_standby_entered")
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "设备连接",
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "提供 Hinge 的后台设备发现、连接恢复和唤醒入口"
                setShowBadge(false)
            },
        )
    }

    private fun mainActivityIntent(): PendingIntent {
        val intent = Intent(this, MainActivity::class.java).apply {
            action = ACTION_WAKE
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
        if (multicastLock?.isHeld == true) return
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
        val wifiManager = try {
            applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        } catch (_: Exception) {
            return
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q &&
            wifiLowLatencyLock?.isHeld != true
        ) {
            runCatching {
                wifiManager.createWifiLock(
                    WifiManager.WIFI_MODE_FULL_LOW_LATENCY,
                    "HingeConnectionLowLatency",
                ).apply {
                    setReferenceCounted(false)
                    acquire()
                }
            }.onSuccess { wifiLowLatencyLock = it }
                .onFailure { wifiLowLatencyLock = null }
        }
        // LOW_LATENCY is only effective while the screen is on and the
        // app is foreground. On Android 10-13 keep HIGH_PERF as the
        // screen-off/background companion lock. Android 14 deprecated
        // HIGH_PERF and maps it back to LOW_LATENCY, so do not request it
        // there.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.UPSIDE_DOWN_CAKE &&
            wifiHighPerformanceLock?.isHeld != true
        ) {
            runCatching {
                wifiManager.createWifiLock(
                    WifiManager.WIFI_MODE_FULL_HIGH_PERF,
                    "HingeConnectionHighPerformance",
                ).apply {
                    setReferenceCounted(false)
                    acquire()
                }
            }.onSuccess { wifiHighPerformanceLock = it }
                .onFailure { wifiHighPerformanceLock = null }
        }
    }

    private fun releaseWifiLock() {
        try {
            if (wifiLowLatencyLock?.isHeld == true) wifiLowLatencyLock?.release()
            if (wifiHighPerformanceLock?.isHeld == true) wifiHighPerformanceLock?.release()
        } catch (_: Exception) {
            // Wi-Fi may be disabled while the service is being torn down.
        } finally {
            wifiLowLatencyLock = null
            wifiHighPerformanceLock = null
        }
    }

    /**
     * A foreground service keeps the process important, but it does not keep
     * the CPU executing while the screen is off. The native TCP broker and its
     * heartbeat timer otherwise stop making progress even though this service
     * notification remains visible. This lock is held only during an active
     * connection/wake window; low-power standby releases it while keeping the
     * foreground service and notification alive.
     */
    private fun acquireCpuWakeLock() {
        if (cpuWakeLock?.isHeld == true) return
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
     * Track the current physical LAN without binding the whole Android
     * process in the normal case. The native broker binds each new outbound
     * TCP socket to this Network, so unrelated app traffic and Flutter sockets
     * are not trapped on a stale Wi-Fi handle after a roam or DHCP renewal.
     *
     * A third-party full-device VPN is the deliberate compatibility exception:
     * Dart's RawDatagramSocket has no Android Network.bindSocket bridge, so its
     * discovery listener cannot be forced onto Wi-Fi by the scoped-socket path.
     * While a VPN transport is present, temporarily binding this process to the
     * physical LAN keeps Hinge's local discovery/reconnect traffic on Wi-Fi.
     * The binding is released as soon as the VPN disappears or the LAN is lost.
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
                releaseVpnCompatibilityBinding(manager)
                nativeConnectionBroker.onPhysicalNetworkLost(network)
                val replacement = manager.allNetworks.firstOrNull { candidate ->
                    val capabilities = manager.getNetworkCapabilities(candidate)
                    capabilities != null && isPhysicalLan(capabilities)
                }
                if (replacement != null) {
                    bindToPhysicalNetwork(manager, replacement)
                    broadcastDiscoveryBeacon()
                } else {
                    schedulePhysicalNetworkRefresh(manager, "physical_network_lost")
                }
            }
        }
        val vpnRequest = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_VPN)
            .build()
        val vpnCallback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                refreshPhysicalNetworkAfterVpnChange(manager, "vpn_available")
            }

            override fun onCapabilitiesChanged(
                network: Network,
                capabilities: NetworkCapabilities,
            ) {
                refreshPhysicalNetworkAfterVpnChange(manager, "vpn_capabilities_changed")
            }

            override fun onLost(network: Network) {
                refreshPhysicalNetworkAfterVpnChange(manager, "vpn_lost")
            }
        }

        try {
            manager.registerNetworkCallback(request, callback)
            try {
                manager.registerNetworkCallback(vpnRequest, vpnCallback)
            } catch (error: Exception) {
                runCatching { manager.unregisterNetworkCallback(callback) }
                throw error
            }
            connectivityManager = manager
            networkCallback = callback
            vpnNetworkCallback = vpnCallback
            findPhysicalNetwork(manager)?.let { bindToPhysicalNetwork(manager, it) }
                ?: schedulePhysicalNetworkRefresh(manager, "service_network_unavailable")
        } catch (error: Exception) {
            releaseVpnCompatibilityBinding(manager)
            connectivityManager = null
            networkCallback = null
            vpnNetworkCallback = null
            HingeDiagnostics.from(this).log(
                "network_callback_register_failed",
                mapOf("reason" to error.javaClass.simpleName),
            )
        }
    }

    private fun unregisterPhysicalNetworkCallback() {
        val manager = connectivityManager
        val callback = networkCallback
        val vpnCallback = vpnNetworkCallback
        try {
            if (manager != null && callback != null) {
                manager.unregisterNetworkCallback(callback)
            }
            if (manager != null && vpnCallback != null) {
                manager.unregisterNetworkCallback(vpnCallback)
            }
            if (manager != null) releaseVpnCompatibilityBinding(manager)
            nativeConnectionBroker.onPhysicalNetworkLost(boundNetwork)
        } catch (_: Exception) {
            // The network service may already be unavailable during teardown.
        } finally {
            connectivityManager = null
            networkCallback = null
            vpnNetworkCallback = null
            boundNetwork = null
            processBoundNetwork = null
            vpnCompatibilityPresent = false
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
        if (!isPhysicalLan(capabilities)) return
        val networkChanged = boundNetwork != network
        val routingPolicyChanged = updateVpnCompatibilityBinding(manager, network)
        if (networkChanged) {
            boundNetwork = network
            nativeConnectionBroker.onPhysicalNetworkAvailable(network)
        } else if (routingPolicyChanged) {
            nativeConnectionBroker.onPhysicalNetworkPolicyChanged()
        }
    }

    private fun refreshPhysicalNetworkAfterVpnChange(
        manager: ConnectivityManager,
        reason: String,
    ) {
        val network = findPhysicalNetwork(manager)
        if (network == null) {
            releaseVpnCompatibilityBinding(manager)
            val previous = boundNetwork
            boundNetwork = null
            if (previous != null) {
                nativeConnectionBroker.onPhysicalNetworkLost(previous)
            }
            HingeDiagnostics.from(this).log(
                "vpn_network_transition_waiting_for_lan",
                mapOf("reason" to reason),
            )
            schedulePhysicalNetworkRefresh(manager, reason)
            return
        }
        bindToPhysicalNetwork(manager, network)
        broadcastDiscoveryBeacon()
    }

    private fun schedulePhysicalNetworkRefresh(
        manager: ConnectivityManager,
        reason: String,
    ) {
        HingeDiagnostics.from(this).log(
            "physical_network_refresh_scheduled",
            mapOf("reason" to reason),
        )
        listOf(250L, 1_000L, 3_000L).forEach { delayMs ->
            discoveryHandler.postDelayed({
                if (current === this) {
                    findPhysicalNetwork(manager)?.let {
                        bindToPhysicalNetwork(manager, it)
                        broadcastDiscoveryBeacon()
                    }
                }
            }, delayMs)
        }
    }

    private fun findPhysicalNetwork(manager: ConnectivityManager): Network? =
        manager.allNetworks.firstOrNull { candidate ->
            val capabilities = runCatching {
                manager.getNetworkCapabilities(candidate)
            }.getOrNull()
            capabilities != null && isPhysicalLan(capabilities)
        }

    private fun hasActiveVpn(manager: ConnectivityManager): Boolean =
        manager.allNetworks.any { candidate ->
            runCatching { manager.getNetworkCapabilities(candidate) }
                .getOrNull()
                ?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true
        }

    /**
     * Returns true when the local routing policy changed and existing Hinge
     * sessions should be rebuilt. The process binding is only owned by this
     * service, so teardown cannot accidentally clear another component's
     * network selection.
     */
    private fun updateVpnCompatibilityBinding(
        manager: ConnectivityManager,
        network: Network,
    ): Boolean {
        val vpnPresent = hasActiveVpn(manager)
        val vpnStateChanged = vpnPresent != vpnCompatibilityPresent
        vpnCompatibilityPresent = vpnPresent
        var processBindingChanged = false

        if (vpnPresent) {
            if (processBoundNetwork != network) {
                if (processBoundNetwork != null) {
                    runCatching { manager.bindProcessToNetwork(null) }
                    processBoundNetwork = null
                    processBindingChanged = true
                }
                try {
                    if (manager.bindProcessToNetwork(network)) {
                        processBoundNetwork = network
                        processBindingChanged = true
                        HingeDiagnostics.from(this).log(
                            "vpn_compat_process_network_bound",
                            mapOf("network" to network.toString()),
                        )
                    } else {
                        HingeDiagnostics.from(this).log(
                            "vpn_compat_process_network_bind_failed",
                        )
                    }
                } catch (error: Exception) {
                    HingeDiagnostics.from(this).log(
                        "vpn_compat_process_network_bind_failed",
                        mapOf("reason" to error.javaClass.simpleName),
                    )
                }
            }
        } else if (processBoundNetwork != null) {
            runCatching { manager.bindProcessToNetwork(null) }
                .onFailure {
                    HingeDiagnostics.from(this).log(
                        "vpn_compat_process_network_unbind_failed",
                        mapOf("reason" to it.javaClass.simpleName),
                    )
                }
            processBoundNetwork = null
            processBindingChanged = true
            HingeDiagnostics.from(this).log("vpn_compat_process_network_unbound")
        }
        return vpnStateChanged || processBindingChanged
    }

    private fun releaseVpnCompatibilityBinding(manager: ConnectivityManager) {
        if (processBoundNetwork != null) {
            runCatching { manager.bindProcessToNetwork(null) }
                .onFailure {
                    HingeDiagnostics.from(this).log(
                        "vpn_compat_process_network_unbind_failed",
                        mapOf("reason" to it.javaClass.simpleName),
                    )
                }
            processBoundNetwork = null
            HingeDiagnostics.from(this).log("vpn_compat_process_network_unbound")
        }
        vpnCompatibilityPresent = false
    }

    private fun isPhysicalLan(capabilities: NetworkCapabilities): Boolean =
        !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN) &&
            (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))

    private fun startDiscoveryBeaconLoop() {
        if (lowPowerStandby) return
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

    private fun stopDiscoveryBeaconLoop() {
        discoveryBeaconRunnable?.let(discoveryHandler::removeCallbacks)
        discoveryBeaconRunnable = null
    }

    /**
     * Sends a small discovery announcement from the foreground service itself.
     * Flutter's DiscoveryService still consumes the same discovery payload,
     * while this fallback keeps device discovery alive when Android pauses the
     * Flutter isolate or its UDP socket while the foreground notification is
     * present.
     */
    private fun broadcastDiscoveryBeacon() {
        if (lowPowerStandby) return
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
                            bindDatagramToPhysicalNetwork(socket)
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
                            bindDatagramToPhysicalNetwork(socket)
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

    private fun bindDatagramToPhysicalNetwork(socket: DatagramSocket) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.M) return
        val network = boundNetwork ?: return
        runCatching { network.bindSocket(socket) }
            .onFailure {
                HingeDiagnostics.from(this).log(
                    "discovery_socket_network_bind_failed",
                    mapOf("reason" to it.javaClass.simpleName),
                )
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
        const val ACTION_WAKE = "com.hinge.office.WAKE_CONNECTION_SERVICE"
        const val EXTRA_WAKE_REASON = "wakeReason"
        private const val CHANNEL_ID = "hinge_connection"
        private const val NOTIFICATION_ID = 52831
        private const val DISCOVERY_PORT = 52830
        private const val SESSION_PORT = 52831
        private const val PROTOCOL_VERSION = "0.1"
        private const val DISCOVERY_BEACON_INTERVAL_MS = 5_000L
        private const val ACTIVE_WINDOW_MS = 90_000L
        private const val STANDBY_CHECK_INTERVAL_MS = 15_000L
    }
}
