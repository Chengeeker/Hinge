package com.hinge.office

import android.content.Context
import android.net.Network
import android.os.Environment
import android.os.PowerManager
import android.os.SystemClock
import io.flutter.plugin.common.EventChannel
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.util.ArrayDeque
import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/** Delivers native-service connection state and protocol frames to Flutter. */
object HingeNativeConnectionEvents {
    private const val MAX_PENDING_EVENTS = 128
    private const val MAX_QUEUED_FRAME_BYTES = 64 * 1024
    private val lock = Any()
    private val mainHandler = android.os.Handler(android.os.Looper.getMainLooper())
    private val pending = ArrayDeque<Map<String, Any?>>()
    private var sink: EventChannel.EventSink? = null

    fun attach(nextSink: EventChannel.EventSink?) {
        val queued: List<Map<String, Any?>>
        synchronized(lock) {
            sink = nextSink
            queued = pending.toList()
            pending.clear()
        }
        queued.forEach { post(nextSink, it) }
    }

    fun detach() {
        synchronized(lock) { sink = null }
    }

    fun isAttached(): Boolean = synchronized(lock) { sink != null }

    fun emit(event: Map<String, Any?>) {
        val current: EventChannel.EventSink?
        synchronized(lock) {
            current = sink
            if (current == null) {
                val payload = event["payload"] as? ByteArray
                if (event["event"] == "frame" &&
                    payload != null &&
                    payload.size > MAX_QUEUED_FRAME_BYTES
                ) {
                    return
                }
                if (pending.size >= MAX_PENDING_EVENTS) pending.removeFirst()
                pending.addLast(event)
                return
            }
        }
        post(current, event)
    }

    private fun post(target: EventChannel.EventSink?, event: Map<String, Any?>) {
        if (target == null) return
        mainHandler.post {
            try {
                target.success(event)
            } catch (_: Exception) {
                // Flutter may detach while an Activity is being recreated.
            }
        }
    }
}

private object HingeProtocol {
    const val headerSize = 52
    const val maxPayloadSize = 16 * 1024 * 1024
    const val sessionInit = 0x0010
    const val sessionAck = 0x0011
    const val heartbeatPing = 0x0012
    const val heartbeatPong = 0x0013
    const val fileOffer = 0x0030
    const val fileAccept = 0x0031
    const val fileReject = 0x0032
    const val fileChunk = 0x0033
    const val fileComplete = 0x0034
    const val compressedControl = 0x0082
    const val compressionCapability = "control-compression-zlib-v1"
    val magic = byteArrayOf(0x4f, 0x53, 0x50, 0x31)
}

private val IMAGE_EXTENSIONS = setOf(
    ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".bmp", ".tif", ".tiff",
)
private val VIDEO_EXTENSIONS = setOf(
    ".mp4", ".mov", ".mkv", ".avi", ".webm", ".3gp", ".m4v",
)

private const val FOREGROUND_HEARTBEAT_INTERVAL_MS = 5_000L
private const val BACKGROUND_HEARTBEAT_INTERVAL_MS = 20_000L
private const val FOREGROUND_CONNECTION_TIMEOUT_MS = 45_000L
private const val BACKGROUND_CONNECTION_SUSPEND_AFTER_MS = 180_000L
private const val FOREGROUND_RECOVERY_GRACE_MS = 15_000L
private const val FOREGROUND_RECOVERY_HEARTBEAT_INTERVAL_MS = 1_000L
private val RECONNECT_BACKOFF_MS = longArrayOf(
    2_000L,
    5_000L,
    10_000L,
    30_000L,
    60_000L,
)

private data class NativeProtocolFrame(
    val version: Int,
    val type: Int,
    val messageId: ByteArray,
    val timestamp: Long,
    val sessionId: ByteArray,
    val payload: ByteArray,
)

private data class NativeIdentity(
    val deviceId: String,
    val name: String,
    val manufacturer: String,
    val model: String,
)

private data class NativePeer(
    val deviceId: String,
    val name: String,
    val manufacturer: String,
    val model: String,
    val platform: String,
    val capabilities: List<String>,
    val pairingRequired: Boolean,
)

private data class NativePeerConfig(
    val deviceId: String,
    val address: String,
    val port: Int,
    val remotePairingCode: String,
)

private data class PendingTransferTask(
    val id: String,
    val path: String,
    val name: String,
    val mimeType: String,
    val targetDeviceId: String,
    val deleteAfter: Boolean,
    val taskFile: File,
)

private data class TransferResponse(
    val accepted: Boolean,
    val offset: Long,
    val reason: String,
)

private data class NativeIncomingTransfer(
    val transferId: String,
    val fileName: String,
    val fileSize: Long,
    val expectedHash: String,
    val tempFile: File,
    val finalFile: File,
    val output: java.io.FileOutputStream,
    var bytesReceived: Long,
)

/**
 * Owns Android's long-lived TCP listener, sessions, heartbeats, reconnect
 * policy and durable outbound file tasks. Flutter talks to it through the
 * small bridge in session_manager.dart and remains a UI/feature consumer.
 */
class HingeNativeConnectionBroker(private val context: Context) {
    private val appContext = context.applicationContext
    private val diagnostics = HingeDiagnostics.from(appContext)
    private val preferences = appContext.getSharedPreferences(
        "native_connection_service",
        Context.MODE_PRIVATE,
    )
    private val executor = Executors.newCachedThreadPool()
    // MethodChannel handlers run on Android's main thread. All frames coming
    // from Flutter must cross this ordered I/O executor before touching a
    // socket, otherwise OutputStream.write throws NetworkOnMainThreadException
    // and turns every successful handshake into an immediate disconnect.
    private val bridgeWriteExecutor = Executors.newSingleThreadExecutor()
    private val scheduler: ScheduledExecutorService = Executors.newScheduledThreadPool(2)
    private val listenerLock = Any()
    private val started = AtomicBoolean(false)
    private val connections = ConcurrentHashMap<String, NativeConnection>()
    private val peerConfigs = ConcurrentHashMap<String, NativePeerConfig>()
    private val manualDisconnects = ConcurrentHashMap.newKeySet<String>()
    private val queuedTasks = ConcurrentHashMap<String, PendingTransferTask>()
    private val activeTasks = ConcurrentHashMap.newKeySet<String>()
    private val transferWaiters = ConcurrentHashMap<String, CompletableFuture<TransferResponse>>()
    private val taskRetryFutures = ConcurrentHashMap<String, ScheduledFuture<*>>()
    private val reconnectFutures = ConcurrentHashMap<String, ScheduledFuture<*>>()
    private val reconnectAttempts = ConcurrentHashMap<String, Int>()
    private val incomingTransfers = ConcurrentHashMap<String, NativeIncomingTransfer>()
    private val taskDirectory = File(appContext.filesDir, "hinge_transfer_queue")

    @Volatile
    private var listener: ServerSocket? = null
    @Volatile
    private var identity: NativeIdentity? = null
    @Volatile
    private var localPairingCode = ""
    @Volatile
    private var listenPort = 52831
    @Volatile
    private var physicalNetwork: Network? = null
    @Volatile
    private var physicalNetworkStateKnown = false
    @Volatile
    private var physicalNetworkAvailable = true
    @Volatile
    private var lowPowerStandby = false

    fun start() {
        if (!started.compareAndSet(false, true)) return
        identity = identity ?: readIdentity()
        localPairingCode = normalizeCode(
            preferences.getString("local_pairing_code", "") ?: "",
        )
        loadPeerConfigs()
        loadPendingTasks()
        ensureListener()
        diagnostics.log("connection_service_start", mapOf("port" to listenPort))
        scheduleHistoricalReconnects()
        dispatchPendingTasks()
    }

    fun configure(arguments: Map<*, *>?): Boolean {
        val current = identity ?: readIdentity()
        val configuredId = arguments?.get("deviceId")?.toString()?.trim().orEmpty()
        identity = NativeIdentity(
            deviceId = configuredId.ifEmpty { current?.deviceId.orEmpty() },
            name = arguments?.get("name")?.toString()?.trim()
                ?.ifEmpty { current?.name.orEmpty() }
                .orEmpty()
                .ifEmpty { "Android 设备" },
            manufacturer = arguments?.get("manufacturer")?.toString()
                ?.trim()
                .orEmpty()
                .ifEmpty { current?.manufacturer.orEmpty() },
            model = arguments?.get("model")?.toString()
                ?.trim()
                .orEmpty()
                .ifEmpty { current?.model.orEmpty() },
        )
        arguments?.get("localPairingCode")?.toString()?.let {
            localPairingCode = normalizeCode(it)
            preferences.edit().putString("local_pairing_code", localPairingCode).apply()
        }
        (arguments?.get("listenPort") as? Number)?.toInt()?.takeIf { it > 0 }?.let {
            listenPort = it
        }
        // A Flutter engine restart represents the requested exit-refresh. A
        // manual disconnect is not revived by an ordinary reconnect loop, but
        // a fresh app configuration deliberately clears that in-memory gate.
        manualDisconnects.clear()
        start()
        // The service may have been created before Flutter finished supplying
        // the identity. If the first bind raced a network/process transition,
        // retry it during configuration instead of leaving the foreground
        // notification alive with no TCP listener behind it.
        if (listener == null) ensureListener()
        emitSnapshot()
        diagnostics.log(
            "connection_service_configured",
            mapOf("identity" to identity?.deviceId, "port" to listenPort),
        )
        return listener != null
    }

    fun updateLocalPairingCode(value: String) {
        localPairingCode = normalizeCode(value)
        preferences.edit().putString("local_pairing_code", localPairingCode).apply()
        diagnostics.log(
            "pairing_configuration_changed",
            mapOf("configured" to localPairingCode.isNotEmpty()),
        )
    }

    /**
     * Stop periodic heartbeat scheduling while the phone is locked and idle.
     * The authenticated socket remains owned by the broker, and inbound
     * frames are still handled; a later BLE/manual wake exits this mode and
     * resumes the normal heartbeat/reconnect path.
     */
    fun enterLowPowerStandby(reason: String = "idle_background") {
        if (lowPowerStandby) return
        lowPowerStandby = true
        diagnostics.log("low_power_standby_entered", mapOf("reason" to reason))
        connections.values.forEach { it.pauseForLowPowerStandby() }
        HingeNativeConnectionEvents.emit(
            mapOf("event" to "low_power_standby", "standby" to true, "reason" to reason),
        )
    }

    fun exitLowPowerStandby(reason: String = "wake") {
        if (!lowPowerStandby) return
        lowPowerStandby = false
        diagnostics.log("low_power_standby_exited", mapOf("reason" to reason))
        connections.values.forEach { it.resumeFromLowPowerStandby() }
        scheduleHistoricalReconnects(0L)
        dispatchPendingTasks()
        HingeNativeConnectionEvents.emit(
            mapOf("event" to "low_power_standby", "standby" to false, "reason" to reason),
        )
    }

    /**
     * Called by the CompanionDeviceService after a short Windows BLE wake
     * advertisement. This is deliberately a reconnect hint, not a new
     * transport: saved, authenticated LAN peers remain the source of truth.
     */
    fun requestWake(reason: String = "companion") {
        start()
        exitLowPowerStandby(reason)
        ensureListener()
        diagnostics.log("connection_wake_requested", mapOf("reason" to reason))
        scheduleHistoricalReconnects(0L)
        dispatchPendingTasks()
        emitSnapshot()
    }

    /**
     * The service reports the active physical LAN instead of binding the
     * entire Android process. New outbound sockets are explicitly bound to
     * this Network and existing sessions are rebuilt when the Network object
     * changes, which handles Wi-Fi roam and DHCP renewal without reviving a
     * stale socket.
     */
    fun onPhysicalNetworkAvailable(network: Network) {
        val previous = physicalNetwork
        physicalNetworkStateKnown = true
        physicalNetworkAvailable = true
        physicalNetwork = network
        if (previous == null || previous != network) {
            diagnostics.log("lan_network_changed")
            // The listener may have been created before Android's process
            // network policy changed (for example while a VPN was starting).
            // Recreate it after the service has selected the physical LAN so
            // reverse TCP connections do not keep replying through the old
            // default route.
            restartListener()
            connections.values.toList().forEach {
                it.close(false, "network_changed", reconnect = true)
            }
        }
        HingeNativeConnectionEvents.emit(
            mapOf("event" to "network_policy_changed", "reason" to "physical_network_available"),
        )
        scheduleHistoricalReconnects(500L)
        dispatchPendingTasks()
    }

    fun onPhysicalNetworkLost(network: Network?) {
        if (network != null && physicalNetwork != null && physicalNetwork != network) return
        physicalNetwork = null
        physicalNetworkStateKnown = true
        physicalNetworkAvailable = false
        diagnostics.log("lan_network_lost")
        restartListener()
        HingeNativeConnectionEvents.emit(
            mapOf("event" to "network_policy_changed", "reason" to "physical_network_lost"),
        )
        reconnectFutures.values.forEach { it.cancel(false) }
        reconnectFutures.clear()
        connections.values.toList().forEach {
            it.close(false, "network_lost", reconnect = true)
        }
    }

    /**
     * A VPN was enabled or disabled while the underlying physical LAN stayed
     * available. Rebuild existing sessions so the next socket uses the new
     * local routing policy instead of waiting for a long heartbeat timeout.
     */
    fun onPhysicalNetworkPolicyChanged() {
        if (!physicalNetworkAvailable) return
        diagnostics.log("lan_network_policy_changed")
        // bindProcessToNetwork only affects sockets created afterwards. The
        // native listener was usually created before VPN detection, so it
        // must be recreated on the newly selected route as well.
        restartListener()
        HingeNativeConnectionEvents.emit(
            mapOf("event" to "network_policy_changed", "reason" to "vpn_transition"),
        )
        connections.values.toList().forEach {
            it.close(false, "network_policy_changed", reconnect = true)
        }
        scheduleHistoricalReconnects(500L)
        dispatchPendingTasks()
    }

    fun connect(
        connectionId: String,
        address: String,
        port: Int,
        remotePairingCode: String,
    ): String {
        start()
        val cleanAddress = address.trim()
        if (cleanAddress.isEmpty()) throw IllegalArgumentException("连接地址为空")
        val config = NativePeerConfig(
            deviceId = "",
            address = cleanAddress,
            port = port.takeIf { it > 0 } ?: 52831,
            remotePairingCode = normalizeCode(remotePairingCode),
        )
        cancelReconnect(config)
        peerConfigs["pending:$connectionId"] = config
        val connection = NativeConnection(
            connectionId = connectionId,
            address = config.address,
            port = config.port,
            isOutbound = true,
            remotePairingCode = config.remotePairingCode,
        )
        registerConnection(connection)
        executor.execute { connection.runOutbound() }
        diagnostics.log(
            "socket_connect_requested",
            mapOf("address" to cleanAddress, "port" to config.port),
        )
        return connectionId
    }

    fun disconnect(connectionId: String, manual: Boolean) {
        val connection = connections[connectionId] ?: return
        val deviceId = connection.peer?.deviceId.orEmpty()
        if (manual) {
            manualDisconnects.add("pending:$connectionId")
            if (deviceId.isNotEmpty()) manualDisconnects.add(deviceId)
            peerConfigs[deviceId]?.let(::cancelReconnect)
        }
        diagnostics.log(
            "socket_disconnect_requested",
            mapOf("manual" to manual, "device" to deviceId.ifEmpty { "unknown" }),
        )
        // This is an explicit local disposal of one session (including an
        // automatic duplicate/lifecycle cleanup), not a transport failure.
        // Do not immediately create another reconnect attempt for the socket
        // that was intentionally discarded.
        connection.close(
            manual = manual,
            reason = if (manual) "manual_disconnect" else "local_cleanup",
            reconnect = false,
        )
    }

    fun sendFrame(connectionId: String, type: Int, payload: ByteArray) {
        val framePayload = payload.copyOf()
        try {
            bridgeWriteExecutor.execute {
                val connection = connections[connectionId]
                if (connection == null) {
                    diagnostics.log("socket_send_dropped", mapOf("reason" to "unknown_connection"))
                    return@execute
                }
                connection.sendFrame(type, framePayload)
            }
        } catch (_: java.util.concurrent.RejectedExecutionException) {
            diagnostics.log("socket_send_dropped", mapOf("reason" to "service_stopping"))
        }
    }

    fun enqueueFile(
        path: String,
        name: String,
        mimeType: String,
        targetDeviceId: String,
        deleteAfter: Boolean,
    ): Boolean {
        val source = File(path)
        if (!source.isFile || !source.canRead()) {
            diagnostics.log("transfer_enqueue_failed", mapOf("reason" to "source_unavailable"))
            return false
        }
        val id = UUID.randomUUID().toString()
        val safeName = name.trim().ifEmpty { source.name.ifEmpty { "分享文件" } }
            .replace('/', '_')
            .replace('\\', '_')
        val taskFile = File(taskDirectory, "$id.json")
        val task = PendingTransferTask(
            id = id,
            path = source.absolutePath,
            name = safeName,
            mimeType = mimeType.trim().ifEmpty { "application/octet-stream" },
            targetDeviceId = targetDeviceId.trim(),
            deleteAfter = deleteAfter,
            taskFile = taskFile,
        )
        try {
            taskDirectory.mkdirs()
            taskFile.writeText(
                JSONObject().apply {
                    put("id", task.id)
                    put("path", task.path)
                    put("name", task.name)
                    put("mimeType", task.mimeType)
                    put("targetDeviceId", task.targetDeviceId)
                    put("deleteAfter", task.deleteAfter)
                    put("createdAt", System.currentTimeMillis())
                }.toString(),
            )
            queuedTasks[task.id] = task
            diagnostics.log(
                "transfer_queued",
                mapOf("task" to task.id, "bytes" to source.length()),
            )
            HingeNativeConnectionEvents.emit(
                mapOf("event" to "transfer_queued", "taskId" to task.id),
            )
            dispatchPendingTasks()
            return true
        } catch (error: Exception) {
            diagnostics.log("transfer_enqueue_failed", mapOf("reason" to error.javaClass.simpleName))
            runCatching { taskFile.delete() }
            return false
        }
    }

    fun readDiagnostics(): String = diagnostics.read()

    fun clearDiagnostics() = diagnostics.clear()

    fun emitCurrentSnapshotForFlutter() = emitSnapshot()

    fun stop() {
        if (!started.compareAndSet(true, false)) return
        diagnostics.log("connection_service_stop")
        listener?.let { runCatching { it.close() } }
        listener = null
        connections.values.toList().forEach { it.close(false) }
        connections.clear()
        transferWaiters.values.forEach { it.completeExceptionally(IllegalStateException("service_stopped")) }
        transferWaiters.clear()
        incomingTransfers.values.forEach { runCatching { it.output.close() } }
        incomingTransfers.clear()
        taskRetryFutures.values.forEach { it.cancel(false) }
        taskRetryFutures.clear()
        reconnectFutures.values.forEach { it.cancel(false) }
        reconnectFutures.clear()
        reconnectAttempts.clear()
        bridgeWriteExecutor.shutdownNow()
        executor.shutdownNow()
        scheduler.shutdownNow()
    }

    private fun ensureListener() {
        synchronized(listenerLock) {
            if (listener != null || !started.get()) return
            try {
                val server = ServerSocket()
                server.reuseAddress = true
                server.bind(InetSocketAddress("0.0.0.0", listenPort))
                listener = server
                diagnostics.log("tcp_listener_started", mapOf("port" to listenPort))
                executor.execute {
                    while (started.get() && listener === server) {
                        try {
                            val socket = server.accept()
                            socket.tcpNoDelay = true
                            socket.keepAlive = true
                            val id = "native-${UUID.randomUUID()}"
                            registerConnection(
                                NativeConnection(
                                    connectionId = id,
                                    acceptedSocket = socket,
                                    address = socket.inetAddress.hostAddress.orEmpty(),
                                    // socket.port is the peer's ephemeral source
                                    // port, not its Hinge listener port. Persisting
                                    // it makes the next historical reconnect target
                                    // a dead port after the first disconnect.
                                    port = listenPort,
                                    isOutbound = false,
                                    remotePairingCode = "",
                                ),
                            )
                        } catch (error: Exception) {
                            if (started.get()) {
                                diagnostics.log(
                                    "tcp_accept_error",
                                    mapOf("reason" to error.javaClass.simpleName),
                                )
                            }
                            break
                        }
                    }
                }
            } catch (error: Exception) {
                diagnostics.log(
                    "tcp_listener_failed",
                    mapOf("port" to listenPort, "reason" to error.javaClass.simpleName),
                )
                listener = null
            }
        }
    }

    private fun restartListener() {
        synchronized(listenerLock) {
            val previous = listener
            listener = null
            runCatching { previous?.close() }
        }
        ensureListener()
    }

    private fun registerConnection(connection: NativeConnection) {
        connections[connection.connectionId] = connection
        HingeNativeConnectionEvents.emit(
            mapOf(
                "event" to "connection_created",
                "connectionId" to connection.connectionId,
                "isOutbound" to connection.isOutbound,
                "remoteAddress" to connection.address,
            ),
        )
        connection.emitState("connecting")
        executor.execute {
            if (!connection.isOutbound) connection.runAccepted() else Unit
        }
    }

    private fun onPeerIdentified(connection: NativeConnection, peer: NativePeer) {
        val pendingKey = "pending:${connection.connectionId}"
        val old = peerConfigs.remove(pendingKey)
        val address = connection.address.ifEmpty { old?.address.orEmpty() }
        peerConfigs[peer.deviceId] = NativePeerConfig(
            deviceId = peer.deviceId,
            address = address,
            port = old?.port ?: connection.port,
            remotePairingCode = old?.remotePairingCode.orEmpty(),
        )
        peerConfigs.entries.removeIf { (key, value) ->
            key.startsWith("pending:") && value.address == address
        }
        savePeerConfigs()
        diagnostics.log(
            "peer_identified",
            mapOf("device" to peer.deviceId, "platform" to peer.platform),
        )
        dispatchPendingTasks()
    }

    private fun onConnectionReady(connection: NativeConnection) {
        val deviceId = connection.peer?.deviceId.orEmpty()
        if (deviceId.isNotEmpty()) {
            // Do not resolve duplicates while this connection is still only
            // authenticating. Doing so could close the newly clicked session
            // merely because an older socket happened to be ready first.
            removeDuplicateConnections(connection, deviceId)
            peerConfigs[deviceId]?.let(::cancelReconnect)
        }
        diagnostics.log(
            "socket_connected",
            mapOf<String, Any?>("device" to (deviceId.ifEmpty { "unknown" })),
        )
        dispatchPendingTasks()
    }

    private fun onDisconnected(connection: NativeConnection) {
        connections.remove(connection.connectionId)
        val deviceId = connection.peer?.deviceId.orEmpty()
        val pendingConfig = peerConfigs.remove("pending:${connection.connectionId}")
        transferWaiters.entries
            .filter { it.key.startsWith("${connection.connectionId}:") }
            .forEach { (_, waiter) ->
                waiter.completeExceptionally(IllegalStateException("connection_lost"))
            }
        diagnostics.log(
            "socket_disconnected",
            mapOf("device" to deviceId.ifEmpty { "unknown" }),
        )
        val manuallySuppressed =
            manualDisconnects.contains("pending:${connection.connectionId}") ||
                deviceId.isNotEmpty() && manualDisconnects.contains(deviceId)
        if (!started.get() || manuallySuppressed || connection.reconnectSuppressed) {
            return
        }
        val config = peerConfigs[deviceId] ?: pendingConfig
        // A fresh UI request can probe several historical addresses. Do not
        // turn a failed anonymous probe into its own endless reconnect loop;
        // only an endpoint already associated with a real device is eligible
        // for service-owned historical recovery.
        if (config != null && config.deviceId.isNotEmpty()) {
            scheduleReconnect(config)
        }
    }

    private fun removeDuplicateConnections(
        identified: NativeConnection,
        remoteDeviceId: String,
    ) {
        if (!identified.isReady) return
        val duplicates = connections.values.filter {
            it !== identified && it.isReady && it.peer?.deviceId == remoteDeviceId
        }
        if (duplicates.isEmpty()) return
        val local = identity?.deviceId.orEmpty()
        val preferOutbound = local.compareTo(remoteDeviceId) < 0
        val keep = (listOf(identified) + duplicates).firstOrNull {
            it.isOutbound == preferOutbound
        } ?: identified
        (listOf(identified) + duplicates).filter { it !== keep }.forEach {
            diagnostics.log("duplicate_socket_closed", mapOf("device" to remoteDeviceId))
            it.close(false, "duplicate_socket", reconnect = false)
        }
    }

    private fun scheduleHistoricalReconnects(delayMs: Long = 1500L) {
        peerConfigs.values
            .filter { it.deviceId.isNotEmpty() }
            .distinctBy { reconnectKey(it) }
            .forEach { scheduleReconnect(it, delayMs, resetBackoff = true) }
    }

    private fun scheduleReconnect(
        config: NativePeerConfig,
        delayMs: Long? = null,
        resetBackoff: Boolean = false,
    ) {
        val key = reconnectKey(config)
        if (resetBackoff) {
            reconnectAttempts.remove(key)
            reconnectFutures.remove(key)?.cancel(false)
        }
        if (!started.get() ||
            (config.deviceId.isNotEmpty() && manualDisconnects.contains(config.deviceId)) ||
            (physicalNetworkStateKnown && !physicalNetworkAvailable)
        ) {
            return
        }
        if (hasMatchingConnection(config)) {
            return
        }
        val pending = reconnectFutures[key]
        if (pending != null && !pending.isDone && !pending.isCancelled) return
        val attempt = reconnectAttempts.merge(key, 1) { current, _ -> current + 1 } ?: 1
        val effectiveDelayMs = delayMs ?: RECONNECT_BACKOFF_MS[
            (attempt - 1).coerceAtMost(RECONNECT_BACKOFF_MS.lastIndex)
        ]
        diagnostics.log(
            "reconnect_scheduled",
            mapOf(
                "device" to config.deviceId.ifEmpty { "unknown" },
                "delayMs" to effectiveDelayMs,
                "attempt" to attempt,
            ),
        )
        val future = scheduler.schedule({
            reconnectFutures.remove(key)
            if (!started.get() ||
                hasMatchingConnection(config) ||
                (physicalNetworkStateKnown && !physicalNetworkAvailable)
            ) return@schedule
            val id = "native-${UUID.randomUUID()}"
            peerConfigs["pending:$id"] = config
            val connection = NativeConnection(
                connectionId = id,
                address = config.address,
                port = config.port,
                isOutbound = true,
                remotePairingCode = config.remotePairingCode,
            )
            registerConnection(connection)
            executor.execute { connection.runOutbound() }
            diagnostics.log(
                "reconnect_attempt",
                mapOf(
                    "device" to config.deviceId.ifEmpty { "unknown" },
                    "attempt" to attempt,
                ),
            )
        }, effectiveDelayMs, TimeUnit.MILLISECONDS)
        reconnectFutures[key] = future
    }

    private fun reconnectKey(config: NativePeerConfig): String =
        config.deviceId.ifEmpty { "${config.address}:${config.port}" }

    private fun hasMatchingConnection(config: NativePeerConfig): Boolean =
        connections.values.any { connection ->
            !connection.isClosed &&
                connection.state != "disconnected" &&
                (
                    (config.deviceId.isNotEmpty() &&
                        connection.peer?.deviceId == config.deviceId) ||
                        connection.address == config.address
                )
        }

    private fun cancelReconnect(config: NativePeerConfig) {
        val key = reconnectKey(config)
        reconnectFutures.remove(key)?.cancel(false)
        reconnectAttempts.remove(key)
    }

    private fun onFrame(connection: NativeConnection, frame: NativeProtocolFrame) {
        if (frame.type == HingeProtocol.fileAccept || frame.type == HingeProtocol.fileReject) {
            val transferId = jsonTransferId(frame.payload)
            if (transferId != null) {
                val waiter = transferWaiters["${connection.connectionId}:$transferId"]
                if (waiter != null) {
                    val json = runCatching { JSONObject(String(frame.payload, Charsets.UTF_8)) }.getOrNull()
                    waiter.complete(
                        TransferResponse(
                            accepted = frame.type == HingeProtocol.fileAccept &&
                                (json?.optBoolean("accepted", true) ?: true),
                            offset = json?.optLong("offset", 0L) ?: 0L,
                            reason = json?.optString("reason", "") ?: "",
                        ),
                    )
                    return
                }
            }
        }
        if (!HingeNativeConnectionEvents.isAttached()) {
            when (frame.type) {
                HingeProtocol.fileOffer -> if (handleIncomingOffer(connection, frame.payload)) return
                HingeProtocol.fileChunk -> if (handleIncomingChunk(connection, frame.payload)) return
                HingeProtocol.fileComplete -> if (handleIncomingComplete(connection, frame.payload)) return
            }
        }
        HingeNativeConnectionEvents.emit(
            mapOf(
                "event" to "frame",
                "connectionId" to connection.connectionId,
                "version" to frame.version,
                "type" to frame.type,
                "messageId" to frame.messageId,
                "timestamp" to frame.timestamp,
                "sessionId" to frame.sessionId,
                "payload" to frame.payload,
            ),
        )
    }

    /**
     * When no Flutter engine is attached, the service finishes incoming file
     * transfers itself instead of retaining hundreds of large raw frames in
     * an EventChannel queue. With the UI attached, the existing Dart transfer
     * manager remains the visible/interactive path.
     */
    private fun handleIncomingOffer(
        connection: NativeConnection,
        payload: ByteArray,
    ): Boolean {
        val json = runCatching { JSONObject(String(payload, Charsets.UTF_8)) }.getOrNull() ?: return false
        val transferId = json.optString("transferId").trim()
        val fileName = json.optString("fileName", "接收文件")
            .replace('/', '_')
            .replace('\\', '_')
            .ifEmpty { "接收文件" }
        if (transferId.isEmpty()) return false
        val size = json.optLong("fileSize", 0L).coerceAtLeast(0L)
        val directory = receiveDirectory(
            json.optString("mimeType"),
            fileName,
        )
        return try {
            directory.mkdirs()
            val finalFile = File(directory, fileName)
            val tempFile = File(directory, "$fileName.$transferId.part")
            var existing = if (tempFile.isFile) tempFile.length() else 0L
            if (existing > size) {
                tempFile.delete()
                existing = 0L
            }
            val transfer = NativeIncomingTransfer(
                transferId = transferId,
                fileName = fileName,
                fileSize = size,
                expectedHash = json.optString("sha256"),
                tempFile = tempFile,
                finalFile = finalFile,
                output = java.io.FileOutputStream(tempFile, true),
                bytesReceived = existing,
            )
            incomingTransfers[transferId]?.let { runCatching { it.output.close() } }
            incomingTransfers[transferId] = transfer
            connection.sendJson(
                HingeProtocol.fileAccept,
                JSONObject().apply {
                    put("transferId", transferId)
                    put("accepted", true)
                    put("offset", existing)
                },
            )
            diagnostics.log("incoming_transfer_accepted", mapOf("task" to transferId, "bytes" to size))
            true
        } catch (error: Exception) {
            connection.sendJson(
                HingeProtocol.fileReject,
                JSONObject().apply {
                    put("transferId", transferId)
                    put("reason", "保存目录不可用")
                },
            )
            diagnostics.log("incoming_transfer_rejected", mapOf("reason" to error.javaClass.simpleName))
            true
        }
    }

    private fun handleIncomingChunk(
        connection: NativeConnection,
        payload: ByteArray,
    ): Boolean {
        if (payload.size < 28) return false
        val transferId = uuidString(payload.copyOfRange(0, 16))
        val transfer = incomingTransfers[transferId] ?: return false
        return try {
            transfer.output.write(payload, 28, payload.size - 28)
            transfer.bytesReceived += payload.size - 28L
            true
        } catch (error: Exception) {
            diagnostics.log("incoming_transfer_write_failed", mapOf("reason" to error.javaClass.simpleName))
            runCatching { transfer.output.close() }
            incomingTransfers.remove(transferId)
            true
        }
    }

    private fun handleIncomingComplete(
        connection: NativeConnection,
        payload: ByteArray,
    ): Boolean {
        val json = runCatching { JSONObject(String(payload, Charsets.UTF_8)) }.getOrNull() ?: return false
        val transferId = json.optString("transferId").trim()
        val transfer = incomingTransfers.remove(transferId) ?: return false
        return try {
            transfer.output.flush()
            transfer.output.close()
            val expected = json.optString("sha256").ifEmpty { transfer.expectedHash }
            val actual = sha256(transfer.tempFile)
            if (expected.isNotEmpty() && !expected.equals(actual, ignoreCase = true)) {
                transfer.tempFile.delete()
                diagnostics.log("incoming_transfer_hash_failed", mapOf("task" to transferId))
                return true
            }
            if (transfer.finalFile.exists()) transfer.finalFile.delete()
            transfer.tempFile.renameTo(transfer.finalFile)
            diagnostics.log(
                "incoming_transfer_completed",
                mapOf("task" to transferId, "bytes" to transfer.bytesReceived),
            )
            HingeNativeConnectionEvents.emit(
                mapOf(
                    "event" to "file_received",
                    "connectionId" to connection.connectionId,
                    "path" to transfer.finalFile.absolutePath,
                    "name" to transfer.fileName,
                ),
            )
            true
        } catch (error: Exception) {
            runCatching { transfer.output.close() }
            runCatching { transfer.tempFile.delete() }
            diagnostics.log("incoming_transfer_complete_failed", mapOf("reason" to error.javaClass.simpleName))
            true
        }
    }

    private fun receiveDirectory(mimeType: String, fileName: String): File {
        val settings = appContext.getSharedPreferences("app_settings", Context.MODE_PRIVATE)
        val defaultRoot = File(
            Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
            "Hinge",
        )
        val lower = "${mimeType.lowercase()} ${fileName.lowercase()}"
        val key = when {
            lower.contains("image/") || hasExtension(fileName, IMAGE_EXTENSIONS) -> "image_storage_path"
            lower.contains("video/") || hasExtension(fileName, VIDEO_EXTENSIONS) -> "video_storage_path"
            else -> "file_storage_path"
        }
        val configured = settings.getString(key, "").orEmpty().trim()
        return if (configured.isNotEmpty()) {
            File(configured)
        } else {
            File(defaultRoot, when (key) {
                "image_storage_path" -> "图片"
                "video_storage_path" -> "视频"
                else -> "文件"
            })
        }
    }

    private fun hasExtension(name: String, extensions: Set<String>): Boolean {
        val lower = name.lowercase()
        return extensions.any { lower.endsWith(it) }
    }

    private fun uuidString(bytes: ByteArray): String {
        if (bytes.size != 16) return ""
        val hex = bytes.joinToString("") { "%02x".format(it) }
        return "${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-" +
            "${hex.substring(16, 20)}-${hex.substring(20, 32)}"
    }

    private fun dispatchPendingTasks() {
        if (lowPowerStandby || queuedTasks.isEmpty()) return
        queuedTasks.values.forEach { task ->
            if (activeTasks.contains(task.id)) return@forEach
            val connection = connections.values.firstOrNull {
                it.isReady &&
                    (task.targetDeviceId.isEmpty() || it.peer?.deviceId == task.targetDeviceId)
            } ?: return@forEach
            if (!activeTasks.add(task.id)) return@forEach
            executor.execute { transferTask(task, connection) }
        }
    }

    private fun transferTask(task: PendingTransferTask, connection: NativeConnection) {
        val file = File(task.path)
        try {
            if (!file.isFile) throw IllegalStateException("source_unavailable")
            val hash = sha256(file)
            val waiter = CompletableFuture<TransferResponse>()
            transferWaiters["${connection.connectionId}:${task.id}"] = waiter
            connection.sendJson(
                HingeProtocol.fileOffer,
                JSONObject().apply {
                    put("transferId", task.id)
                    put("fileName", task.name)
                    put("fileSize", file.length())
                    put("mimeType", task.mimeType)
                    put("modifiedTime", file.lastModified() / 1000L)
                    put("sha256", hash)
                },
            )
            val response = waiter.get(30, TimeUnit.SECONDS)
            if (!response.accepted) throw IllegalStateException(
                response.reason.ifEmpty { "peer_rejected" },
            )
            val offset = response.offset.coerceIn(0L, file.length())
            FileInputStream(file).use { input ->
                skipFully(input, offset)
                // Match the Windows sender's bulk-transfer frame size. The
                // receiver remains compatible with older smaller chunks.
                val buffer = ByteArray(2 * 1024 * 1024)
                var bytesSent = offset
                var chunkIndex = 0
                while (bytesSent < file.length()) {
                    if (!connection.isReady) throw IllegalStateException("connection_lost")
                    val count = input.read(buffer)
                    if (count <= 0) break
                    val payload = ByteBuffer.allocate(28 + count).order(ByteOrder.BIG_ENDIAN)
                    payload.put(uuidBytes(task.id))
                    payload.putInt(chunkIndex++)
                    payload.putLong(bytesSent)
                    payload.put(buffer, 0, count)
                    connection.sendFrame(HingeProtocol.fileChunk, payload.array())
                    bytesSent += count
                    HingeNativeConnectionEvents.emit(
                        mapOf(
                            "event" to "transfer_progress",
                            "taskId" to task.id,
                            "bytes" to bytesSent,
                            "totalBytes" to file.length(),
                        ),
                    )
                }
            }
            connection.sendJson(
                HingeProtocol.fileComplete,
                JSONObject().apply {
                    put("transferId", task.id)
                    put("sha256", hash)
                    put("success", true)
                },
            )
            queuedTasks.remove(task.id)
            runCatching { task.taskFile.delete() }
            if (task.deleteAfter) runCatching { file.delete() }
            diagnostics.log("transfer_completed", mapOf("task" to task.id, "bytes" to file.length()))
            HingeNativeConnectionEvents.emit(
                mapOf("event" to "transfer_completed", "taskId" to task.id),
            )
        } catch (error: Exception) {
            diagnostics.log(
                "transfer_failed",
                mapOf("task" to task.id, "reason" to error.javaClass.simpleName),
            )
            HingeNativeConnectionEvents.emit(
                mapOf(
                    "event" to "transfer_failed",
                    "taskId" to task.id,
                    "reason" to error.javaClass.simpleName,
                ),
            )
            taskRetryFutures.remove(task.id)?.cancel(false)
            taskRetryFutures[task.id] = scheduler.schedule({
                taskRetryFutures.remove(task.id)
                dispatchPendingTasks()
            }, 5000L, TimeUnit.MILLISECONDS)
        } finally {
            transferWaiters.remove("${connection.connectionId}:${task.id}")
            activeTasks.remove(task.id)
        }
    }

    private fun loadPeerConfigs() {
        if (peerConfigs.isNotEmpty()) return
        val raw = preferences.getString("peer_configs", null) ?: return
        runCatching {
            val array = JSONArray(raw)
            for (index in 0 until array.length()) {
                val item = array.getJSONObject(index)
                val address = item.optString("address").trim()
                if (address.isEmpty()) continue
                val config = NativePeerConfig(
                    deviceId = item.optString("deviceId"),
                    address = address,
                    port = item.optInt("port", 52831),
                    remotePairingCode = normalizeCode(item.optString("remotePairingCode")),
                )
                peerConfigs[config.deviceId.ifEmpty { "address:${config.address}" }] = config
            }
        }
    }

    private fun savePeerConfigs() {
        // Pending connection attempts are process-local. Persisting them made
        // a service restart fan out stale anonymous reconnects before Flutter
        // attached, producing a reconnect storm and overflowing bridge events.
        val unique = peerConfigs.values
            .filter { it.deviceId.isNotEmpty() }
            .distinctBy { "${it.deviceId}|${it.address}|${it.port}" }
        val array = JSONArray()
        unique.forEach { config ->
            array.put(
                JSONObject().apply {
                    put("deviceId", config.deviceId)
                    put("address", config.address)
                    put("port", config.port)
                    put("remotePairingCode", config.remotePairingCode)
                },
            )
        }
        preferences.edit().putString("peer_configs", array.toString()).apply()
    }

    private fun loadPendingTasks() {
        taskDirectory.mkdirs()
        taskDirectory.listFiles { file -> file.extension == "json" }?.forEach { file ->
            runCatching {
                val json = JSONObject(file.readText())
                val task = PendingTransferTask(
                    id = json.optString("id").ifEmpty { file.nameWithoutExtension },
                    path = json.optString("path"),
                    name = json.optString("name", "分享文件"),
                    mimeType = json.optString("mimeType", "application/octet-stream"),
                    targetDeviceId = json.optString("targetDeviceId"),
                    deleteAfter = json.optBoolean("deleteAfter", false),
                    taskFile = file,
                )
                if (File(task.path).isFile) queuedTasks[task.id] = task
                else file.delete()
            }
        }
    }

    private fun emitSnapshot() {
        connections.values.forEach { connection ->
            HingeNativeConnectionEvents.emit(
                mapOf(
                    "event" to "connection_created",
                    "connectionId" to connection.connectionId,
                    "isOutbound" to connection.isOutbound,
                    "remoteAddress" to connection.address,
                ),
            )
            connection.emitPeer()
            connection.emitState(connection.state)
        }
    }

    private fun readIdentity(): NativeIdentity? {
        val file = File(appContext.filesDir, "identity.json")
        return runCatching {
            val json = JSONObject(file.readText())
            NativeIdentity(
                deviceId = json.optString("deviceId"),
                name = json.optString("name", "Android 设备"),
                manufacturer = json.optString("manufacturer"),
                model = json.optString("model"),
            )
        }.getOrNull()?.takeIf { it.deviceId.isNotEmpty() }
    }

    private fun jsonTransferId(payload: ByteArray): String? = runCatching {
        JSONObject(String(payload, Charsets.UTF_8)).optString("transferId").trim()
    }.getOrNull()?.takeIf { it.isNotEmpty() }

    private fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        FileInputStream(file).use { input ->
            val buffer = ByteArray(1024 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count <= 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    private fun skipFully(input: InputStream, target: Long) {
        var remaining = target
        while (remaining > 0) {
            val skipped = input.skip(remaining)
            if (skipped > 0) remaining -= skipped else if (input.read() < 0) break else remaining--
        }
    }

    private fun uuidBytes(value: String): ByteArray {
        val clean = value.replace("-", "")
        if (clean.length != 32) return ByteArray(16)
        return ByteArray(16) { index -> clean.substring(index * 2, index * 2 + 2).toInt(16).toByte() }
    }

    private fun normalizeCode(value: String?): String =
        value?.trim()?.takeIf { it.matches(Regex("\\d{6}")) } ?: ""

    private inner class NativeConnection(
        val connectionId: String,
        val acceptedSocket: Socket? = null,
        val address: String,
        val port: Int,
        val isOutbound: Boolean,
        val remotePairingCode: String,
    ) {
        @Volatile
        var state: String = "connecting"
            private set
        @Volatile
        var peer: NativePeer? = null
            private set
        @Volatile
        var isReady: Boolean = false
            private set
        @Volatile
        private var closed = false
        val isClosed: Boolean
            get() = closed
        @Volatile
        private var manualClosed = false
        @Volatile
        var reconnectSuppressed = false
            private set
        @Volatile
        private var lastInboundAt = SystemClock.elapsedRealtime()
        private val writeLock = Any()
        private val queuedFrames = ArrayDeque<ByteArray>()
        private var socket: Socket? = acceptedSocket
        private var output: OutputStream? = null
        private var heartbeatFuture: ScheduledFuture<*>? = null
        private var authFuture: ScheduledFuture<*>? = null
        private var interactiveRecoveryStartedAt = 0L
        private val sessionId = uuidBytes(UUID.randomUUID().toString())
        private val localChallenge = randomHex(16)
        private var peerChallenge = ""
        private var peerPairingRequired = false
        private var pairingProofSent = false
        private var pairingAuthenticated = false
        private var pairingError = ""

        fun runOutbound() {
            try {
                if (physicalNetworkStateKnown && !physicalNetworkAvailable) {
                    close(false, "network_unavailable")
                    return
                }
                val network = physicalNetwork
                val client = if (network != null) {
                    runCatching {
                        // Android recommends sockets created by the selected
                        // Network's SocketFactory. This is more reliable than
                        // creating a default socket first when a VPN owns the
                        // process default route.
                        network.socketFactory.createSocket().also {
                            diagnostics.log("tcp_socket_factory_bound_to_lan")
                        }
                    }.getOrElse { error ->
                        diagnostics.log(
                            "tcp_socket_factory_create_failed",
                            mapOf("reason" to error.javaClass.simpleName),
                        )
                        Socket().also {
                            runCatching { network.bindSocket(it) }
                                .onFailure { bindError ->
                                    diagnostics.log(
                                        "tcp_socket_network_bind_failed",
                                        mapOf("reason" to bindError.javaClass.simpleName),
                                    )
                                }
                        }
                    }
                } else {
                    Socket()
                }
                client.tcpNoDelay = true
                client.keepAlive = true
                client.connect(InetSocketAddress(address, port), 5000)
                socket = client
                runSession(client)
            } catch (error: Exception) {
                diagnostics.log(
                    "socket_connect_failed",
                    mapOf("address" to address, "reason" to error.javaClass.simpleName),
                )
                close(false, "connect_failed")
            }
        }

        fun runAccepted() {
            val client = acceptedSocket
            if (client == null) {
                close(false, "missing_accepted_socket")
                return
            }
            try {
                client.keepAlive = true
                runSession(client)
            } catch (error: Exception) {
                diagnostics.log("socket_read_failed", mapOf("reason" to error.javaClass.simpleName))
                close(false, "accepted_session_error")
            }
        }

        private fun runSession(client: Socket) {
            if (closed) return
            client.tcpNoDelay = true
            output = client.getOutputStream()
            updateState("authenticating")
            lastInboundAt = SystemClock.elapsedRealtime()
            sendJson(HingeProtocol.sessionInit, identityPayload())
            heartbeatFuture = scheduler.schedule(
                { heartbeat() },
                FOREGROUND_HEARTBEAT_INTERVAL_MS,
                TimeUnit.MILLISECONDS,
            )
            authFuture = scheduler.schedule({
                if (!isReady && !closed) {
                    pairingError = pairingError.ifEmpty { "identity_timeout" }
                    close(false, "identity_timeout")
                }
            }, 8L, TimeUnit.SECONDS)
            val input = client.getInputStream()
            while (!closed) {
                val header = ByteArray(HingeProtocol.headerSize)
                if (!readFully(input, header)) break
                if (!header.copyOfRange(0, 4).contentEquals(HingeProtocol.magic)) {
                    diagnostics.log("socket_protocol_error", mapOf("reason" to "bad_magic"))
                    break
                }
                val headerBuffer = ByteBuffer.wrap(header).order(ByteOrder.BIG_ENDIAN)
                headerBuffer.position(4)
                val version = headerBuffer.short.toInt() and 0xffff
                val type = headerBuffer.short.toInt() and 0xffff
                val messageId = header.copyOfRange(8, 24)
                headerBuffer.position(24)
                val timestamp = headerBuffer.long
                val incomingSessionId = header.copyOfRange(32, 48)
                headerBuffer.position(48)
                val payloadLength = headerBuffer.int
                if (payloadLength < 0 || payloadLength > HingeProtocol.maxPayloadSize) {
                    diagnostics.log("socket_protocol_error", mapOf("reason" to "payload_too_large"))
                    break
                }
                val payload = ByteArray(payloadLength)
                if (!readFully(input, payload)) break
                // Any valid protocol frame proves that the transport is
                // alive. Do not require a PONG specifically: file traffic,
                // session acknowledgements and control frames are equally
                // useful liveness signals.
                lastInboundAt = SystemClock.elapsedRealtime()
                handleFrame(
                    NativeProtocolFrame(
                        version = version,
                        type = type,
                        messageId = messageId,
                        timestamp = timestamp,
                        sessionId = incomingSessionId,
                        payload = payload,
                    ),
                )
            }
            close(false, "peer_closed")
        }

        fun sendFrame(type: Int, payload: ByteArray) {
            if (closed) return
            val frame = serializeFrame(type, payload)
            synchronized(writeLock) {
                val stream = output
                if (stream == null) {
                    if (queuedFrames.size >= 32) queuedFrames.removeFirst()
                    queuedFrames.addLast(frame)
                    return
                }
                try {
                    stream.write(frame)
                    // SocketOutputStream is not file-buffered. Avoid a
                    // flush for each multi-megabyte file frame, but keep it
                    // for control traffic where prompt delivery matters.
                    if (type != HingeProtocol.fileChunk) stream.flush()
                } catch (error: Exception) {
                    diagnostics.log("socket_write_failed", mapOf("reason" to error.javaClass.simpleName))
                    close(false, "socket_write_failed")
                }
            }
        }

        fun sendJson(type: Int, json: JSONObject) {
            sendFrame(type, json.toString().toByteArray(Charsets.UTF_8))
        }

        fun close(
            manual: Boolean,
            reason: String = "requested",
            reconnect: Boolean = true,
        ) {
            if (manual) manualClosed = true
            if (closed) return
            closed = true
            reconnectSuppressed = !reconnect
            heartbeatFuture?.cancel(false)
            authFuture?.cancel(false)
            heartbeatFuture = null
            authFuture = null
            runCatching { socket?.close() }
            socket = null
            output = null
            isReady = false
            diagnostics.log(
                "socket_close",
                mapOf(
                    "manual" to manual,
                    "reason" to reason,
                    "device" to (peer?.deviceId ?: "unknown"),
                ),
            )
            updateState("disconnected")
        }

        fun emitPeer() {
            val current = peer ?: return
            HingeNativeConnectionEvents.emit(peerEvent(current))
        }

        fun emitState(value: String) {
            HingeNativeConnectionEvents.emit(
                mapOf(
                    "event" to "state",
                    "connectionId" to connectionId,
                    "state" to value,
                    "remoteAddress" to address,
                    "pairingError" to pairingError.ifEmpty { null },
                ),
            )
        }

        private fun handleFrame(frame: NativeProtocolFrame) {
            if (state == "suspended") {
                interactiveRecoveryStartedAt = 0L
                updateState("connected")
                diagnostics.log("heartbeat_resumed")
            }
            when (frame.type) {
                HingeProtocol.sessionInit, HingeProtocol.sessionAck -> {
                    acceptPeerIdentity(frame.payload)
                    if (frame.type == HingeProtocol.sessionInit) {
                        sendJson(HingeProtocol.sessionAck, identityPayload(true))
                    }
                    tryCompleteAuthentication()
                }
                HingeProtocol.heartbeatPing -> sendFrame(HingeProtocol.heartbeatPong, ByteArray(0))
                HingeProtocol.heartbeatPong -> Unit
                else -> onFrame(this, frame)
            }
        }

        private fun acceptPeerIdentity(payload: ByteArray) {
            val json = runCatching { JSONObject(String(payload, Charsets.UTF_8)) }.getOrNull() ?: return
            val deviceId = json.optString("deviceId").trim()
            if (deviceId.isEmpty() || deviceId == identity?.deviceId.orEmpty()) return
            val capabilities = mutableListOf<String>()
            json.optJSONArray("capabilities")?.let { array ->
                for (index in 0 until array.length()) capabilities += array.optString(index)
            }
            peerPairingRequired = json.optBoolean("pairingRequired", false)
            peerChallenge = json.optString("pairingChallenge").trim().lowercase()
            val proof = json.optString("pairingProof")
            pairingAuthenticated = localPairingCode.isEmpty() ||
                verifyProof(localPairingCode, localChallenge, proof)
            pairingError = when {
                localPairingCode.isNotEmpty() && !pairingAuthenticated ->
                    "本机已设置配对码，但对方未提供正确配对码。"
                peerPairingRequired && remotePairingCode.isEmpty() ->
                    "目标设备需要输入 6 位配对码。"
                else -> ""
            }
            peer = NativePeer(
                deviceId = deviceId,
                name = json.optString("name", "未命名设备"),
                manufacturer = json.optString("manufacturer"),
                model = json.optString("model"),
                platform = json.optString("platform", "unknown"),
                capabilities = capabilities.distinct(),
                pairingRequired = peerPairingRequired,
            )
            emitPeer()
            onPeerIdentified(this, peer!!)
        }

        private fun tryCompleteAuthentication() {
            if (peer == null || closed || isReady) return
            if (localPairingCode.isNotEmpty() && !pairingAuthenticated) return
            if (peerPairingRequired && !pairingProofSent) return
            pairingError = ""
            isReady = true
            authFuture?.cancel(false)
            updateState(if (lowPowerStandby) "suspended" else "connected")
            synchronized(writeLock) {
                val stream = output ?: return@synchronized
                while (queuedFrames.isNotEmpty()) {
                    runCatching {
                        stream.write(queuedFrames.removeFirst())
                    }.onFailure { close(false, "queued_write_failed") }
                }
                runCatching { stream.flush() }
                    .onFailure { close(false, "queued_flush_failed") }
            }
            onConnectionReady(this)
        }

        private fun heartbeat() {
            if (closed || !isReady) return
            if (lowPowerStandby) {
                if (state != "suspended") updateState("suspended")
                heartbeatFuture = null
                return
            }
            val now = SystemClock.elapsedRealtime()
            val background = !isScreenInteractive()
            val elapsed = now - lastInboundAt
            if (background && elapsed > BACKGROUND_CONNECTION_SUSPEND_AFTER_MS) {
                if (state != "suspended") {
                    interactiveRecoveryStartedAt = 0L
                    updateState("suspended")
                    diagnostics.log(
                        "heartbeat_suspended",
                        mapOf("elapsedMs" to elapsed),
                    )
                }
            } else if (!background && state == "suspended") {
                if (interactiveRecoveryStartedAt == 0L) {
                    interactiveRecoveryStartedAt = now
                    diagnostics.log("heartbeat_resume_probe_started")
                } else if (now - interactiveRecoveryStartedAt > FOREGROUND_RECOVERY_GRACE_MS) {
                    diagnostics.log(
                        "heartbeat_resume_probe_timeout",
                        mapOf("elapsedMs" to elapsed),
                    )
                    updateState("reconnecting")
                    close(false, "heartbeat_timeout")
                    return
                }
            } else if (!background && elapsed > FOREGROUND_CONNECTION_TIMEOUT_MS) {
                diagnostics.log(
                    "heartbeat_timeout",
                    mapOf("elapsedMs" to elapsed, "background" to false),
                )
                updateState("reconnecting")
                close(false, "heartbeat_timeout")
                return
            }
            sendFrame(HingeProtocol.heartbeatPing, ByteArray(0))
            if (!closed) {
                heartbeatFuture = scheduler.schedule(
                    { heartbeat() },
                    when {
                        background -> BACKGROUND_HEARTBEAT_INTERVAL_MS
                        state == "suspended" -> FOREGROUND_RECOVERY_HEARTBEAT_INTERVAL_MS
                        else -> FOREGROUND_HEARTBEAT_INTERVAL_MS
                    },
                    TimeUnit.MILLISECONDS,
                )
            }
        }

        fun pauseForLowPowerStandby() {
            if (closed) return
            heartbeatFuture?.cancel(false)
            heartbeatFuture = null
            if (isReady && state != "suspended") updateState("suspended")
        }

        fun resumeFromLowPowerStandby() {
            if (closed || !isReady || heartbeatFuture != null) return
            heartbeatFuture = scheduler.schedule({ heartbeat() }, 0L, TimeUnit.MILLISECONDS)
        }

        private fun isScreenInteractive(): Boolean {
            val powerManager = appContext.getSystemService(Context.POWER_SERVICE) as? PowerManager
            return powerManager?.isInteractive ?: true
        }

        private fun identityPayload(ack: Boolean = false): JSONObject {
            val current = identity ?: NativeIdentity("", "Android 设备", "", "")
            val capabilities = JSONArray().put(HingeProtocol.compressionCapability)
            val proof = if (ack) createProof(remotePairingCode, peerChallenge) else ""
            if (peerPairingRequired && proof.isNotEmpty()) pairingProofSent = true
            return JSONObject().apply {
                put("deviceId", current.deviceId)
                put("name", current.name)
                put("manufacturer", current.manufacturer)
                put("model", current.model)
                put("platform", "android")
                put("capabilities", capabilities)
                put("pairingRequired", localPairingCode.isNotEmpty())
                put("pairingChallenge", localChallenge)
                put("pairingProof", proof)
            }
        }

        private fun updateState(next: String) {
            if (state == next) return
            state = next
            emitState(next)
            if (next == "disconnected") onDisconnected(this)
        }

        private fun serializeFrame(type: Int, payload: ByteArray): ByteArray {
            require(payload.size <= HingeProtocol.maxPayloadSize)
            val buffer = ByteBuffer.allocate(HingeProtocol.headerSize + payload.size)
                .order(ByteOrder.BIG_ENDIAN)
            buffer.put(HingeProtocol.magic)
            buffer.putShort(1)
            buffer.putShort(type.toShort())
            buffer.put(randomBytes(16))
            buffer.putLong(System.currentTimeMillis() / 1000L)
            buffer.put(sessionId)
            buffer.putInt(payload.size)
            buffer.put(payload)
            return buffer.array()
        }

        private fun peerEvent(value: NativePeer): Map<String, Any?> = mapOf(
            "event" to "peer",
            "connectionId" to connectionId,
            "deviceId" to value.deviceId,
            "name" to value.name,
            "manufacturer" to value.manufacturer,
            "model" to value.model,
            "platform" to value.platform,
            "capabilities" to value.capabilities,
            "pairingRequired" to value.pairingRequired,
            "pairingAuthenticated" to (
                pairingAuthenticated &&
                    (!value.pairingRequired || pairingProofSent)
                ),
            "pairingError" to pairingError.ifEmpty { null },
            "remoteAddress" to address,
        )

        private fun readFully(input: InputStream, target: ByteArray): Boolean {
            var offset = 0
            while (offset < target.size) {
                val count = input.read(target, offset, target.size - offset)
                if (count < 0) return false
                if (count == 0) continue
                offset += count
            }
            return true
        }
    }

    private fun randomBytes(size: Int): ByteArray {
        val value = ByteArray(size)
        java.security.SecureRandom().nextBytes(value)
        return value
    }

    private fun randomHex(size: Int): String = randomBytes(size)
        .joinToString("") { "%02x".format(it) }

    private fun verifyProof(code: String, challenge: String, actual: String): Boolean {
        val expected = createProof(code, challenge)
        if (expected.isEmpty() || actual.isBlank()) return false
        return MessageDigest.isEqual(
            runCatching { hexBytes(expected) }.getOrNull() ?: return false,
            runCatching { hexBytes(actual.trim()) }.getOrNull() ?: return false,
        )
    }

    private fun createProof(code: String, challenge: String): String {
        val normalized = normalizeCode(code)
        if (normalized.isEmpty() || challenge.isBlank()) return ""
        val key = MessageDigest.getInstance("SHA-256")
            .digest(normalized.toByteArray(Charsets.UTF_8))
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        val context = "Hinge-Pairing-v1|${challenge.trim().lowercase()}"
        return mac.doFinal(context.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
    }

    private fun hexBytes(value: String): ByteArray {
        if (value.length % 2 != 0) throw IllegalArgumentException("invalid_hex")
        return ByteArray(value.length / 2) { index ->
            value.substring(index * 2, index * 2 + 2).toInt(16).toByte()
        }
    }
}
