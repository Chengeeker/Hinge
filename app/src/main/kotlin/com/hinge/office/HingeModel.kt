package com.hinge.office

import android.os.Build
import android.util.AtomicFile
import androidx.compose.runtime.*
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.channels.Channel
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.io.Closeable
import java.io.File
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.InetAddress
import java.security.SecureRandom
import java.time.Instant
import java.util.UUID

class HingeModel(val activity: MainActivity, val api: NativeApi) : Closeable {
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    var settings by mutableStateOf<Map<String, Any?>>(emptyMap())
    var identity by mutableStateOf<Map<String, Any?>>(emptyMap())
    var devices by mutableStateOf<List<Map<String, Any?>>>(emptyList())
    var connections by mutableStateOf<Map<String, Map<String, Any?>>>(emptyMap())
    var history by mutableStateOf<List<Map<String, Any?>>>(emptyList())
    var shares by mutableStateOf<List<Map<String, Any?>>>(emptyList())
    var message by mutableStateOf("")
    var desktop by mutableStateOf<Map<String, Any?>>(emptyMap())
    var initialized by mutableStateOf(false)
    var foreground by mutableStateOf(true)
    var discoveryListening by mutableStateOf(false)
    var discoveryError by mutableStateOf("")
    var connectingDevice by mutableStateOf("")
    private val expectedPeers=mutableMapOf<String,String>()
    private val automaticAttempts=mutableMapOf<String,Long>()
    private val subscriptions = mutableListOf<Closeable>()
    private val commands = Semaphore(4)
    private val settingsWrite = Mutex()
    private val incoming = Channel<Map<String, Any?>>(16)
    private var pendingFrameBytes = 0
    private var pendingCommands = 0
    private var historyWrite: Job? = null
    private var historyWritable = false
    var presetRevision by mutableIntStateOf(0)
    var pairingRequest by mutableStateOf<Map<String,Any?>?>(null)
    private val storage = Dispatchers.IO.limitedParallelism(1)
    private var discoverySocket: DatagramSocket? = null
    private val suppressedHistory = linkedSetOf<String>()
    private val smsQueue = ArrayDeque<Map<String, Any?>>()
    private val known = mutableMapOf<String, Map<String, Any?>>()
    private val seen = mutableMapOf<String, Long>()
    private val frames = mutableListOf<(String, Int, ByteArray) -> Unit>()
    private var relayBusy = false
    private var shareBusy = false
    private var discoveryBusy = false
    private var lastSweep=0L
    private val replies=mutableMapOf<String,Long>()
    private val requests=mutableMapOf<String,Long>()
    private fun isManuallyDisconnected(id: String) =
        HingeForegroundService.current?.connectionBroker()?.isManuallyDisconnected(id) == true
    var recentDevices by mutableStateOf<List<Map<String,Any?>>>(emptyList())
    var remote: RemoteController? = null
    var remoteRevision by mutableIntStateOf(0)
    var selectedPeer by mutableStateOf<Map<String, Any?>>(emptyMap())
    val ready: List<Map<String, Any?>> get() = connectedDeviceRows(connections.values)
    fun isConnectionReady(connectionId: String): Boolean =
        connections[connectionId]?.text("state") == "connected" &&
            HingeForegroundService.current?.connectionBroker()?.connectionState(connectionId) == "connected"
    fun isCheckingConnection(deviceId: String): Boolean = connections.values.any {
        it.text("deviceId") == deviceId && it.text("state") == "suspended"
    }

    fun refreshNativeConnectionStates() {
        val broker = HingeForegroundService.current?.connectionBroker()
        connections = connections.mapNotNull { (id, row) ->
            val state = broker?.connectionState(id) ?: return@mapNotNull null
            id to (row + ("state" to state))
        }.toMap()
    }
    fun observeFrames(listener: (String, Int, ByteArray) -> Unit): Closeable { frames.add(listener); return Closeable { frames.remove(listener) } }
    fun run(action: suspend () -> Unit) = scope.launch {
        try { action() } catch (e: CancellationException) { throw e }
        catch (e: Exception) { message = e.message ?: "操作失败" }
    }
    fun start() {
        scope.launch {
            for (event in incoming) {
                val payload = event["payload"] as? ByteArray ?: byteArrayOf()
                var accounted=payload.size
                var ownedByCommand=false
                try {
                    val (type, bytes) = withContext(Dispatchers.Default) { Wire.unwrap(event.number("type").toInt(), payload) }
                    val expansion=(bytes.size-payload.size).coerceAtLeast(0)
                    require(pendingFrameBytes+expansion<=64*1024*1024){"设备消息超过处理容量，请稍后重试"}
                    accounted+=expansion;pendingFrameBytes+=expansion
                    val id = event.text("connectionId")
                    if (type == 0x80) {
                        if (pendingCommands >= 8) {
                            val request = withContext(Dispatchers.Default){runCatching { JSONObject(String(bytes)).optString("commandId") }.getOrDefault("")}
                            sendJson(id, 0x81, mapOf("commandId" to request,"success" to false,"error" to "手机正在处理其他请求，请稍后重试"))
                        } else {
                            pendingCommands++;ownedByCommand=true
                            run { try { commands.withPermit { withContext(Dispatchers.Default){handleCommand(id, bytes)} } } finally { pendingCommands--;pendingFrameBytes-=accounted } }
                        }
                    }
                    if (type == 0x20) message = String(bytes, Charsets.UTF_8).take(4096)
                    if (type == 0x83) syncGameProfile(id, bytes)
                    frames.toList().forEach { it(id, type, bytes) }
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) { message = e.message ?: "无法解析设备消息" }
                finally { if(!ownedByCommand)pendingFrameBytes -= accounted }
            }
        }
        subscriptions += api.listen("hinge/native_connection/events") { onNative(it.asMap()) }
        subscriptions += api.listen("hinge/share/events") { shares = (shares + it.asMap()["files"].asRows()).distinctBy{it.text("path")}; dispatchShares() }
        subscriptions += api.listen("hinge/desktop_video/events") { desktop = desktop + it.asMap() }
        subscriptions += api.listen("hinge/sms/events") { onSms(it.asMap()) }
        run {
            settings = mapOf("floatingCapsuleNavigation" to true, "useLiquidGlass" to false, "dynamicColorEnabled" to true) + api.call("loadSettings").asMap()
            settings = settings + ("hapticFeedbackEnabled" to (api.call("hapticFeedbackEnabled") != false))
            if(listOf("imageStoragePath","videoStoragePath","fileStoragePath").any{settings.text(it).isEmpty()}){
                val paths=api.call("storagePaths").asMap()
                saveStoragePaths(settings.text("imageStoragePath").ifEmpty{paths.text("imagePath")},settings.text("videoStoragePath").ifEmpty{paths.text("videoPath")},settings.text("fileStoragePath").ifEmpty{paths.text("filePath")})
            }
            val hardware=api.call("deviceInfo").asMap()
            identity = withContext(storage) {
                val file = File(activity.filesDir, "identity.json")
                val legacy=System.getenv("HOME")?.takeIf{it.isNotBlank()}?.let{File(it,".hinge/identity.json")}
                val old=listOfNotNull(file,legacy).firstNotNullOfOrNull{candidate->runCatching{JSONObject(atomicRead(candidate)).toMap().takeIf{it.text("deviceId").isNotBlank()}}.getOrNull()}
                val value=(old?:mapOf("deviceId" to UUID.randomUUID().toString(),"publicKey" to ByteArray(32).also{SecureRandom().nextBytes(it)}.joinToString(""){"%02x".format(it)}))+mapOf("name" to hardware.text("name",old?.text("name",Build.MODEL)?:Build.MODEL),"manufacturer" to hardware.text("manufacturer",Build.MANUFACTURER),"model" to hardware.text("model",Build.MODEL))
                atomicWrite(file,JSONObject(value).toString());value
            }
            try {
                val saved = withContext(storage) { loadTransferHistory(File(activity.filesDir, "transfer_history.json")) }
                history = saved.first
                suppressedHistory.addAll(saved.second)
                historyWritable = true
            } catch (_: IOException) {
                message = "无法读取传输记录，已保留原文件"
            }
            recentDevices=withContext(storage){readList("trust_store.json").filter{it.text("trustState")=="trusted"}.sortedByDescending{it.number("lastSeen")}}
            api.call("startNativeSession", identity + mapOf("localPairingCode" to settings.text("localPairingCode"), "listenPort" to 52831))
            api.call("pendingNativeTransferReceipts").asRows().forEach(::onNative)
            val active = (api.call("activeTransferHistoryIds") as? List<*>)?.map { it.toString() }?.toSet() ?: emptySet()
            val reconciled = reconcileTransferHistory(history, active)
            if (reconciled != history) {
                history = reconciled
                run { persistHistory() }
            }
            api.call("acquireDiscoveryMulticastLock")
            startDiscovery()
            scope.launch { while(isActive) { if(foreground) refreshDiscovery(); delay(3000) } }
            initialized = true
            dispatchShares()
            scope.launch { while (isActive) { delay(30000); pollRelay() } }
        }
    }
    suspend fun saveSetting(key: String, value: Any?) = settingsWrite.withLock {
        if (key == "hapticFeedbackEnabled") api.call("setHapticFeedbackEnabled", mapOf("enabled" to value))
        val pairingChanged = key == "localPairingCode" && settings[key] != value
        val next = settings + (key to value)
        api.call("saveSettings", next)
        settings = next
        if (pairingChanged) {
            api.call("updateNativePairingCode", mapOf(key to value))
            remote?.close()
            message = if (value == "") "配对码已清除，原有局域网连接已断开。"
                else "配对码已更新，请使用新配对码重新连接。"
        }
    }
    suspend fun saveStoragePaths(images:String,videos:String,files:String)=settingsWrite.withLock {
        val next=settings+mapOf("imageStoragePath" to images.trim(),"videoStoragePath" to videos.trim(),"fileStoragePath" to files.trim())
        api.call("saveSettings",next);settings=next
    }
    fun connect(address: String, port: Int = 52831, code: String = "") = run {
        require(code.isEmpty()||code.matches(Regex("[0-9]{6}"))){"配对码必须为 6 位数字"}
        require(address.isNotBlank() && address.length <= 256 && port in 1..65535) { "请输入有效的地址和端口" }
        api.call("nativeConnect", mapOf("connectionId" to UUID.randomUUID().toString(), "address" to address.trim(), "port" to port, "remotePairingCode" to code.trim(), "manualConnect" to true))
    }
    fun disconnect(connection:Map<String,Any?>)=run{
        val id = connection.text("deviceId")
        remote?.takeIf { it.peer.text("deviceId") == id }?.close()
        api.call("nativeDisconnectDevice", mapOf("deviceId" to id))
    }
    fun connectPeer(peer:Map<String,Any?>,code:String="",automatic:Boolean=false)=run{
        val id=peer.text("deviceId")
        if(connectingDevice.isNotEmpty()||ready.any{it.text("deviceId")==id})return@run
        if (automatic && (isManuallyDisconnected(id) || isCheckingConnection(id))) return@run
        require(code.isEmpty()||code.matches(Regex("[0-9]{6}"))){"配对码必须为 6 位数字"}
        connectingDevice=id
        try{
            val addresses=(listOf(peer.text("address"))+(peer["networkAddresses"] as? List<*>).orEmpty().filterIsInstance<String>()).filter{it.isNotBlank()&&it.length<=256}.distinct().take(4)
            if(peer["pairingRequired"]!=true)for(address in addresses){val target=withContext(Dispatchers.IO){InetAddress.getByName(address)};probePeer(target,peer.number("discoveryPort",52830).toInt().takeIf{it in 1..65535}?:52830,reverse=true,automatic=automatic,targetDeviceId=id)}
            outer@for(address in addresses)for(port in listOf(peer.number("port",52831).toInt(),52831).distinct()){
                if(ready.any{it.text("deviceId")==id}||(automatic&&isManuallyDisconnected(id)))break@outer
                if(port !in 1..65535)continue
                val connection=UUID.randomUUID().toString();expectedPeers[connection]=id
                try{
                    api.call("nativeConnect",mapOf("connectionId" to connection,"address" to address,"port" to port,"remotePairingCode" to code,"manualConnect" to !automatic,"expectedDeviceId" to id))
                    withTimeoutOrNull(2500){while(ready.none{it.text("deviceId")==id}&&(!automatic||!isManuallyDisconnected(id)))delay(50)}
                    if(ready.none{it.text("deviceId")==id})api.call("nativeDisconnect",mapOf("connectionId" to connection,"manual" to false))
                }finally{expectedPeers.remove(connection)}
            }
            if(!automatic&&ready.none{it.text("deviceId")==id})message="无法连接 ${peer.text("name")}，请检查网络和配对码"
        }finally{connectingDevice=""}
    }
    fun probeManualIp(host:String)=run{
        require(host.trim().matches(Regex("[0-9a-fA-F:.%]+"))){"请输入有效的设备 IP 地址"}
        val address=withContext(Dispatchers.IO){InetAddress.getByName(host.trim())}
        probePeer(address,52830).join();message="已向 ${host.trim()} 发送定向探测"
    }
    fun dispatchShares()=run {
        if(!initialized||shareBusy||shares.isEmpty())return@run
        shareBusy=true
        try{
            val peer=ready.firstOrNull()?:recentDevices.firstOrNull()
            while(shares.isNotEmpty()){
                val file = shares.first()
                try {
                    sendFile(file, peer?.text("deviceId").orEmpty())
                }
                catch(e:Exception){
                    if(e is CancellationException)throw e
                    val result=api.call("nativeEnqueueFile",file+mapOf("targetDeviceId" to peer?.text("deviceId").orEmpty(),"deleteAfter" to true)).asMap()
                    if(result["accepted"]==true)shares=shares.filterNot{it.text("path")==file.text("path")} else throw e
                }
            }
            message=if(ready.isEmpty())"已加入原生发送队列，连接恢复后自动发送" else "连接已就绪，正在发送分享文件"
        }finally{shareBusy=false}
    }
    suspend fun sendFrame(connection: String, type: Int, bytes: ByteArray) {
        api.call("nativeSendFrame", mapOf("connectionId" to connection, "type" to type, "payload" to bytes))
    }
    suspend fun sendJson(connection: String, type: Int, data: Map<String, Any?>) = sendFrame(connection, type, JSONObject(data).toString().toByteArray())
    suspend fun sendFile(file: Map<String, Any?>, deviceId: String) {
        if (ready.none { it.text("deviceId") == deviceId } && settings["cloudRelay"].asMap()["enabled"] == true && deviceId.isNotEmpty()) {
            val trusted = withContext(storage) { readList("trust_store.json").any { it.text("deviceId") == deviceId && it.text("trustState") == "trusted" } }
            if (trusted) {
                val source = File(file.text("path"))
                val id = Relay(settings["cloudRelay"].asMap(), identity).send(source, file.text("name"), file.text("mimeType"), deviceId) { id, bytes, total, stage ->
                    withContext(Dispatchers.Main) { relayProgress(id, file.text("name"), bytes, total, stage, "send", source.path) }
                }
                relayProgress(id, file.text("name"), source.length(), source.length(), "awaitingPickup", "send", source.path)
                shares = shares.filterNot { it.text("path") == file.text("path") }
                return
            }
        }
        val cached=withContext(storage){runCatching{File(file.text("path")).canonicalPath.startsWith(activity.cacheDir.canonicalPath+File.separator)}.getOrDefault(false)}
        val args = file + mapOf("targetDeviceId" to deviceId, "deleteAfter" to cached)
        val result = api.call("nativeEnqueueFile", args).asMap()
        check(result["accepted"] == true) { "文件不存在或无法加入发送队列" }
        shares = shares.filterNot { it.text("path") == file.text("path") }
    }
    fun sendUri(uri: String, name: String, mime: String, deviceId: String) = run {
        enqueueUri(uri, name, mime, deviceId)
    }
    private suspend fun enqueueUri(uri: String, name: String, mime: String, deviceId: String) {
        val path = api.call("copyUriToCache", mapOf("uri" to uri, "fileName" to name)) as? String ?: error("无法读取文件")
        sendFile(mapOf("path" to path, "name" to name, "mimeType" to mime), deviceId)
    }
    fun clearHistory() = run {
        suppressedHistory.addAll(history.map { it.text("id") })
        while (suppressedHistory.size > 1000) suppressedHistory.remove(suppressedHistory.first())
        history = emptyList(); persistHistory()
    }
    fun deleteHistory(id:String) = run {
        val row=history.firstOrNull{it.text("id")==id}?:return@run
        if(row.text("status") in setOf("queued","transferring"))return@run
        suppressedHistory.add(id);while(suppressedHistory.size>1000)suppressedHistory.remove(suppressedHistory.first());history=history.filterNot{it.text("id")==id};persistHistory()
    }
    private fun onNative(event: Map<String, Any?>) {
        val id = event.text("connectionId")
        when (event.text("event")) {
            "active_connections" -> {
                val activeIds = (event["connectionIds"] as? List<*>).orEmpty().toSet()
                connections = connections.filterKeys { it in activeIds }
                remote?.takeIf { it.peer.text("connectionId") !in activeIds }?.connectionLost()
            }
            "connection_created", "peer", "state" -> {
                if (id.isEmpty()) return
                // Queued snapshots and callbacks can outlive their native socket.
                val state = HingeForegroundService.current?.connectionBroker()?.connectionState(id)
                if (state == null && event.text("state") != "disconnected") return
                val merged = connections[id].orEmpty() + event + ("state" to (state ?: "disconnected"))
                if(merged.text("state")=="connected"&&expectedPeers[id]?.let{it!=merged.text("deviceId")}==true){run{api.call("nativeDisconnect",mapOf("connectionId" to id,"manual" to true));message="目标设备身份不匹配"};return}
                connections=connections+(id to merged)
                if (state == null) {
                    connections = connections - id
                    remote?.takeIf { it.peer.text("connectionId")==id }?.connectionLost()
                }
                if (event.text("state") == "connected" && state == "connected") {
                    if (foreground && remote?.let { it.isClosed && it.peer.text("connectionId") == id } == true) {
                        remoteRevision++
                    }
                    run { connections[id]?.let { saveTrusted(it) }; flushSms();dispatchShares() }
                }
            }
            "frame" -> {
                val bytes = event["payload"] as? ByteArray ?: return
                if (pendingFrameBytes + bytes.size > 64 * 1024 * 1024 || !incoming.trySend(event).isSuccess) {
                    remote?.close()
                    run { api.call("nativeDisconnect",mapOf("connectionId" to id,"manual" to false)); message="设备消息拥塞，连接已重置" }
                } else pendingFrameBytes += bytes.size
            }
            else -> if (event.text("event") == "file_received" || event.text("event").startsWith("file_transfer_") || event.text("event").startsWith("transfer_")) recordTransfer(event)
        }
    }
    private fun recordTransfer(event: Map<String, Any?>) {
        val incoming = event.text("event").startsWith("file_")
        val id = if (incoming) "lan-${event.text("transferId")}" else "native-${event.text("taskId")}"
        if (id in suppressedHistory) return
        val previous = history.firstOrNull { it.text("id") == id }.orEmpty()
        val kind = event.text("event")
        val status = when { kind == "file_received" || kind.endsWith("completed") -> "completed"; kind == "transfer_failed" || kind.endsWith("queued") -> "queued"; kind.endsWith("failed") -> "failed"; else -> "transferring" }
        if (previous.text("status") == "completed" && status != "completed") return
        val now = Instant.now().toString()
        val record = previous + mapOf("id" to id, "transferId" to event.text("transferId", previous.text("transferId")),
            "deviceId" to event.text("deviceId", event.text("targetDeviceId", previous.text("deviceId"))),
            "deviceName" to event.text("deviceName", previous.text("deviceName")), "fileName" to event.text("name", event.text("fileName", previous.text("fileName"))),
            "localFilePath" to event.text("localFilePath", event.text("path", previous.text("localFilePath"))),
            "direction" to if (incoming) "receive" else "send", "status" to status,
            "bytesTransferred" to event.number("bytesTransferred", event.number("bytes", previous.number("bytesTransferred"))),
            "totalBytes" to event.number("totalBytes", previous.number("totalBytes")),
            "createdAt" to previous.text("createdAt", now), "updatedAt" to now, "error" to event.text("error"))
        history = (listOf(record) + history.filterNot { it.text("id") == id }).take(100)
        if(status=="transferring"&&event.text("receiptId").isEmpty()) {
            if(historyWrite?.isActive!=true)historyWrite=run{delay(500);persistHistory()}
            return
        }
        historyWrite?.cancel()
        run {
            if (persistHistory()) {
                event.text("receiptId").takeIf { it.isNotEmpty() }?.let { api.call("ackNativeTransferReceipts", mapOf("receiptIds" to listOf(it))) }
            }
        }
    }
    private suspend fun persistHistory(): Boolean {
        if (!historyWritable) return false
        val snapshot = history
        val suppressed = suppressedHistory.toList()
        withContext(storage) { atomicWrite(File(activity.filesDir, "transfer_history.json"), JSONObject(mapOf("records" to snapshot, "suppressedTransferIds" to suppressed)).toString()) }
        return true
    }
    private suspend fun relayProgress(id: String, name: String, bytes: Long, total: Long, stage: String, direction: String, path: String = "") {
        val key = "cloud-${if(direction=="send")"upload" else "download"}-$id"
        val previous = history.firstOrNull { it.text("id") == key }.orEmpty()
        val now = Instant.now().toString()
        val row = previous + mapOf("id" to key, "transferId" to id, "fileName" to name, "localFilePath" to path.ifEmpty{previous.text("localFilePath")},
            "direction" to direction, "status" to if (stage in setOf("completed", "awaitingPickup","failed")) stage else "transferring",
            "bytesTransferred" to bytes, "totalBytes" to total, "createdAt" to previous.text("createdAt", now), "updatedAt" to now)
        if (key !in suppressedHistory) history = (listOf(row) + history.filterNot { it.text("id") == key }).take(100)
        persistHistory()
        val nativeDirection=if(direction=="send")"upload" else "download"
        if(stage in setOf("completed","awaitingPickup","failed"))api.call("finishCloudRelayTransferNotification",mapOf("transferId" to id,"direction" to nativeDirection,"fileName" to name,"succeeded" to (stage!="failed")))
        else api.call("showCloudRelayTransferProgress", mapOf("transferId" to id, "fileName" to name, "bytesTransferred" to bytes, "totalBytes" to total, "stage" to stage, "direction" to nativeDirection,"percentage" to if(total>0)(bytes.toDouble()/total*100).toInt().coerceIn(0,100)else 0))
    }
    fun pollRelay() = run {
        val config = settings["cloudRelay"].asMap()
        if (relayBusy || config["enabled"] != true || config.text("deviceToken").isEmpty()) return@run
        relayBusy = true
        try {
            val trusted = withContext(storage) { readList("trust_store.json").filter { it.text("trustState") == "trusted" }.map { it.text("deviceId") }.toSet() }
            val path = settings.text("fileStoragePath").ifEmpty { "/storage/emulated/0/Download/Hinge" }
            val relay=Relay(config,identity)
            val journal=withContext(storage){val file=File(activity.filesDir,"compose-relay-received.json");if(atomicExists(file))JSONObject(atomicRead(file)).toMap().mapValues{it.value.toString()}.toMutableMap() else mutableMapOf<String,String>()}
            relay.receipts().forEach{receipt->
                if(receipt.text("status")=="delivered"){
                    val key="cloud-upload-${receipt.text("transferId")}"
                    history=history.map{if(it.text("id")==key)it+mapOf("status" to "completed","updatedAt" to Instant.now().toString())else it}
                    if (persistHistory()) relay.deleteReceipt(receipt.text("transferId"))
                }
            }
            relay.receive(File(path),trusted,journal,fileReady={id,file->
                journal[id]=file.path
                withContext(storage){atomicWrite(File(activity.filesDir,"compose-relay-received.json"),JSONObject(journal).toString())}
                withContext(Dispatchers.Main){
                    relayProgress(id,file.name,file.length(),file.length(),"completed","receive",file.path)
                    api.call("showFileReceivedNotification",mapOf("path" to file.path))
                }
            }) { id, name, bytes, total, stage ->
                withContext(Dispatchers.Main) { relayProgress(id, name, bytes, total, stage, "receive") }
            }
        } finally { relayBusy = false }
    }
    suspend fun describeDesktop(connection: String): Map<String, Any?> {
        val id = UUID.randomUUID().toString()
        val pending = CompletableDeferred<Map<String, Any?>>()
        val observer = observeFrames { source, type, bytes ->
            if (source == connection && type == 0x84 && bytes.size <= 8192) runCatching { JSONObject(String(bytes)).toMap() }.getOrNull()?.let {
                if (it.text("action") == "description" && it.text("requestId") == id) pending.complete(it)
            }
        }
        try { sendJson(connection, 0x84, mapOf("action" to "describe", "requestId" to id)); return withTimeout(8000) { pending.await() } }
        finally { observer.close() }
    }
    private suspend fun saveTrusted(peer: Map<String, Any?>) = withContext(storage) {
        val id = peer.text("deviceId")
        if (id.isBlank()) return@withContext
        val all = readList("trust_store.json")
        val old = all.firstOrNull { it.text("deviceId") == id }.orEmpty()
        val now = System.currentTimeMillis()
        val updated = old + mapOf("deviceId" to id, "name" to peer.text("name"), "publicKey" to old.text("publicKey"), "pairedAt" to old.number("pairedAt", now), "lastSeen" to now, "trustState" to "trusted")
        atomicWrite(File(activity.filesDir, "trust_store.json"), JSONArray(all.filterNot { it.text("deviceId") == id } + updated).toString())
        withContext(Dispatchers.Main){recentDevices=listOf(updated)+recentDevices.filterNot{it.text("deviceId")==id}}
    }
    private suspend fun handleCommand(connection: String, bytes: ByteArray) {
        var id = ""
        try {
            val command = JSONObject(String(bytes, Charsets.UTF_8)); id = command.optString("commandId")
            val name = command.optString("command")
            val data = command.optJSONObject("payload")?.toMap().orEmpty()
            val result: Any? = when (name) {
                "getDeviceSummary" -> api.call("storageInfo")
                "browseFiles" -> api.call("storageDirectory", data)
                "browseFilesPage" -> api.call("storageDirectoryPage", data)
                "pairDevice" -> { saveTrusted(connections[connection].orEmpty()); mapOf("paired" to true) }
                "photoBytes", "photoThumbnailBytes", "photoPreviewBytes" -> (api.call(name, data) as? ByteArray)?.let { android.util.Base64.encodeToString(it, android.util.Base64.NO_WRAP) }
                "sendMediaToComputer" -> {
                    require(data.text("uri").isNotBlank())
                    val deviceId = withContext(Dispatchers.Main) {
                        connections[connection].orEmpty().text("deviceId")
                    }
                    require(deviceId.isNotBlank()) { "设备会话已断开" }
                    withContext(Dispatchers.Main) {
                        enqueueUri(data.text("uri"), data.text("fileName", "手机文件"), data.text("mimeType"), deviceId)
                    }
                    mapOf("started" to true, "fileName" to data.text("fileName"))
                }
                "deleteFiles" -> mapOf("deleted" to api.call(name, data))
                "notificationHistoryAction" -> when (data.text("action", "open")) {
                    "delete" -> mapOf("deleted" to api.call("deleteNotificationHistoryItem", data))
                    "clear" -> mapOf("cleared" to true, "deletedCount" to api.call("clearNotificationHistory"))
                    else -> mapOf("opened" to api.call("openNotificationHistoryItem", data))
                }
                "saveNote", "saveTask" -> { api.call(name, data); mapOf("saved" to true) }
                "deleteNote", "deleteTask" -> { api.call(name, data.text("id")); mapOf("deleted" to true) }
                "deviceInfo", "calendarEvents", "calendarCreate", "photoAlbums", "photos", "photosPage", "mediaMetadata", "files", "notificationHistory", "loadNotes", "loadTasks" -> api.call(name, data)
                else -> error("不支持的工作区命令：$name")
            }
            sendJson(connection, 0x81, mapOf("commandId" to id, "success" to true, "payload" to result))
        } catch (e: Exception) {
            if (e is CancellationException) throw e
            sendJson(connection, 0x81, mapOf("commandId" to id, "success" to false, "error" to (e.message ?: "操作失败")))
        }
    }
    private suspend fun syncGameProfile(connection:String,bytes:ByteArray){
        val peer=connections[connection]?:return
        if(peer.text("state")!="connected"||!peer.text("platform").contains("windows",true)||bytes.isEmpty()||bytes.size>1024*1024)return
        if(!withContext(storage){readList("trust_store.json").any{it.text("deviceId")==peer.text("deviceId")&&it.text("trustState")=="trusted"}})return
        try{
            val data=JSONObject(String(bytes)).toMap()
            if(data.number("version")!=1L)return
            when(data.text("action")){
                "get"->{val doc=loadGameDocument(this,false);saveGameDocument(this,doc,false);sendJson(connection,0x83,mapOf("version" to 1,"action" to "data","document" to JSONObject(doc.encode()).toMap()))}
                "set"->{val doc=PresetDocument.decode(JSONObject(data["document"].asMap()).toString());saveGameDocument(this,doc,false);presetRevision++;sendJson(connection,0x83,mapOf("version" to 1,"action" to "ack"))}
            }
        }catch(e:CancellationException){throw e}catch(_:Exception){sendJson(connection,0x83,mapOf("version" to 1,"action" to "error"))}
    }
    private fun onSms(event: Map<String, Any?>) {
        if (smsQueue.size >= 8) smsQueue.removeFirst()
        val source = event.text("source", "sms")
        smsQueue.addLast(mapOf("notificationId" to event.text("messageId", UUID.randomUUID().toString()), "packageName" to if (source == "mms") "android.provider.Telephony.MMS" else "android.provider.Telephony.SMS",
            "appName" to if (source == "mms") "彩信" else "短信", "title" to event.text("sender"), "content" to event.text("body"), "source" to source,
            "isVerificationCode" to event["isVerificationCode"], "verificationCode" to event["verificationCode"], "timestamp" to event.number("timestamp"), "canReply" to false, "actions" to if(event["isVerificationCode"]==true&&event.text("verificationCode").isNotBlank())listOf("copy_code")else emptyList<String>()))
        run { flushSms() }
    }
    private suspend fun flushSms() {
        fun recipients()=ready.filter{it.text("platform").contains("windows",true)&&recentDevices.any{saved->saved.text("deviceId")==it.text("deviceId")}}
        if(recipients().isEmpty())return
        while(smsQueue.isNotEmpty()&&recipients().isNotEmpty()){
            val value = smsQueue.first()
            recipients().forEach { sendJson(it.text("connectionId"), 0x70, value) }
            smsQueue.removeFirst()
        }
    }
    private fun startDiscovery() {
        scope.launch(Dispatchers.IO) {
            try {
                fun bind(port:Int)=DatagramSocket(null).apply{try{reuseAddress=true;broadcast=true;bind(InetSocketAddress(port));soTimeout=1000}catch(e:Exception){close();throw e}}
                val socket=try{bind(52830)}catch(_:java.net.BindException){bind(0)}
                withContext(Dispatchers.Main){discoveryListening=true;discoveryError=""}
                discoverySocket = socket
                val buffer = ByteArray(65507)
                while (isActive) {
                    try {
                        val packet = DatagramPacket(buffer, buffer.size); socket.receive(packet)
                        val peer = JSONObject(String(packet.data, 0, packet.length, Charsets.UTF_8)).toMap()
                        if (peer.text("targetDeviceId").isNotEmpty() && peer.text("targetDeviceId") != identity.text("deviceId")) continue
                        val id = peer.text("deviceId")
                        if (id.isBlank() || id.length > 256 || id == identity.text("deviceId") || peer.text("name").length>256) continue
                        withContext(Dispatchers.Main) {
                            val now=System.currentTimeMillis()
                            seen.filterValues{now-it>=180000}.keys.toList().forEach{known.remove(it);seen.remove(it);replies.remove(it);requests.remove(it)}
                            if(known.size>=256&&id !in known)return@withContext
                            known[id] = peer + ("address" to packet.address.hostAddress)
                            seen[id] = now

                            devices = known.values.filter { System.currentTimeMillis() - (seen[it.text("deviceId")] ?: 0) < 180000 }
                            if(peer["connectionRequested"]==true&&now-(requests[id]?:0)>2000&&ready.none{it.text("deviceId")==id}){
                                requests[id] = now

                                if(peer["automaticReconnect"]!=true||!isManuallyDisconnected(id)){
                                    if(peer["pairingRequired"]!=true)connectPeer(known[id].orEmpty(),automatic=peer["automaticReconnect"]==true)
                                    else if(pairingRequest==null)pairingRequest=known[id]
                                }
                            }
                            if(foreground&&ready.isEmpty()&&connectingDevice.isEmpty()&&!isManuallyDisconnected(id)&&peer["pairingRequired"]!=true&&recentDevices.any{it.text("deviceId")==id}&&now-(automaticAttempts[id]?:0)>=5000){automaticAttempts[id]=now;connectPeer(known[id].orEmpty(),automatic=true)}
                            if(now-(replies[id]?:0)>5000){replies[id]=now;probePeer(packet.address,peer.number("discoveryPort",52830).toInt().takeIf{it in 1..65535}?:52830)}
                        }
                    } catch (_: java.net.SocketTimeoutException) { }
                    catch (_: org.json.JSONException) { }
                }
            } catch (e: Exception) { if (isActive) withContext(Dispatchers.Main) { discoveryListening=false;discoveryError=e.message.orEmpty();message = "设备发现不可用：${e.message}，可使用手动连接" } }
        }
    }
    fun refreshDiscovery() = run {
        if(!initialized || !foreground || discoveryBusy) return@run
        discoveryBusy=true
        val sweep=System.currentTimeMillis()-lastSweep>=10000
        if(sweep)lastSweep=System.currentTimeMillis()
        try { withContext(Dispatchers.IO) {
            val manager=activity.getSystemService(android.content.Context.CONNECTIVITY_SERVICE) as android.net.ConnectivityManager
            val networks=manager.allNetworks.filter { network -> manager.getNetworkCapabilities(network)?.let { it.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI)||it.hasTransport(android.net.NetworkCapabilities.TRANSPORT_ETHERNET) } == true }
            val data=discoveryMessage()
            for(network in networks) {
                runCatching { DatagramSocket().use { socket ->
                    network.bindSocket(socket);socket.broadcast=true
                    socket.send(DatagramPacket(data,data.size,InetAddress.getByName("255.255.255.255"),52830))
                    manager.getLinkProperties(network)?.linkAddresses?.filter{it.address is java.net.Inet4Address}?.forEach{link->
                        val address=link.address.address
                        val broadcast=address.copyOf();val prefix=link.prefixLength
                        for(bit in prefix until 32)broadcast[bit/8]=(broadcast[bit/8].toInt() or (1 shl (7-bit%8))).toByte()
                        socket.send(DatagramPacket(data,data.size,InetAddress.getByAddress(broadcast),52830))
                        val probePrefix=maxOf(prefix,24)
                        if(sweep&&probePrefix<=30){
                            val base=address.fold(0L){value,byte->(value shl 8) or (byte.toLong() and 255)} and (0xffffffffL shl (32-probePrefix))
                            for(host in 1 until (1 shl (32-probePrefix))-1){val target=base+host;val ip=ByteArray(4){((target ushr (24-it*8)) and 255).toByte()};if(!ip.contentEquals(address))runCatching{socket.send(DatagramPacket(data,data.size,InetAddress.getByAddress(ip),52830))}}
                        }
                    }
                } }
            }
        }}finally{discoveryBusy=false}
    }
    private fun discoveryMessage(reverse:Boolean=false,automatic:Boolean=false,targetDeviceId:String="")=JSONObject(identity+mapOf("connectionRequested" to reverse,"targetDeviceId" to targetDeviceId,"automaticReconnect" to automatic,"version" to BuildConfig.VERSION_NAME,"platform" to "android","port" to 52831,"discoveryPort" to (discoverySocket?.localPort?:52830),"protocolVersion" to "1.0","timestamp" to System.currentTimeMillis()/1000,"capabilities" to listOf("file_transfer","remote_control","backup"),"pairingRequired" to settings.text("localPairingCode").isNotEmpty())).toString().toByteArray()
    private fun probePeer(address:InetAddress,port:Int,reverse:Boolean=false,automatic:Boolean=false,targetDeviceId:String="")=scope.launch(Dispatchers.IO){
        runCatching{
            val data=discoveryMessage(reverse,automatic,targetDeviceId);val manager=activity.getSystemService(android.content.Context.CONNECTIVITY_SERVICE) as android.net.ConnectivityManager
            val networks=manager.allNetworks.filter{manager.getNetworkCapabilities(it)?.let{c->c.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI)||c.hasTransport(android.net.NetworkCapabilities.TRANSPORT_ETHERNET)}==true}
            if(networks.isEmpty())discoverySocket?.send(DatagramPacket(data,data.size,address,port))
            else networks.forEach{network->DatagramSocket().use{socket->network.bindSocket(socket);socket.send(DatagramPacket(data,data.size,address,port))}}
        }
    }
    private fun readList(name: String): List<Map<String, Any?>> {
        val file = File(activity.filesDir, name)
        if (!atomicExists(file)) return emptyList()
        return jsonValue(JSONArray(atomicRead(file))).asRows()
    }
    override fun close() { remote?.close(); discoverySocket?.close(); subscriptions.forEach { it.close() }; scope.cancel() }
}

fun atomicWrite(file: File, text: String) {
    file.parentFile?.mkdirs()
    val atomic = AtomicFile(file)
    val stream = atomic.startWrite()
    try { stream.write(text.toByteArray(Charsets.UTF_8)); atomic.finishWrite(stream) }
    catch (e: Exception) { atomic.failWrite(stream); throw e }
}

fun atomicRead(file:File):String=AtomicFile(file).openRead().bufferedReader(Charsets.UTF_8).use{it.readText()}
internal fun loadTransferHistory(file: File): Pair<List<Map<String, Any?>>, List<String>> {
    if (!atomicExists(file)) return emptyList<Map<String, Any?>>() to emptyList()
    val raw = atomicRead(file)
    return try {
        readTransferHistory(raw)
    } catch (error: Exception) {
        if (error !is JSONException && error !is IllegalArgumentException) throw error
        val backup = File(file.parentFile, "${file.name}.unreadable-${UUID.randomUUID()}")
        if (!file.renameTo(backup)) throw IOException("无法备份传输记录", error)
        emptyList<Map<String, Any?>>() to emptyList()
    }
}

internal fun reconcileTransferHistory(rows: List<Map<String, Any?>>, active: Set<String>): List<Map<String, Any?>> =
    rows.map { row ->
        val id = row.text("id")
        val status = row.text("status")
        if ((status == "transferring" || (status == "queued" && id.startsWith("native-"))) && id !in active) {
            row + mapOf("status" to "failed", "error" to "应用重启后传输已中断，可重新发送")
        } else row
    }

fun readTransferHistory(raw:String):Pair<List<Map<String,Any?>>,List<String>>{
    val value=jsonValue(org.json.JSONTokener(raw).nextValue())
    if(value is List<*>)return value.asRows() to emptyList()
    require(value is Map<*,*>){"传输历史格式无效"}
    val document=value.asMap()
    require(document["records"] is List<*>){"传输历史缺少记录列表"}
    return document["records"].asRows() to (document["suppressedTransferIds"] as? List<*>).orEmpty().map{it.toString()}
}

fun atomicExists(file:File)=file.exists()||File(file.path+".bak").exists()


internal fun connectedDeviceRows(sessions: Collection<Map<String, Any?>>): List<Map<String, Any?>> =
    sessions.filter { it.text("state") == "connected" && it.text("deviceId").isNotBlank() }
        .distinctBy { it.text("deviceId") }

internal fun connectedDeviceLabel(peer: Map<String, Any?>, devices: List<Map<String, Any?>>): String {
    val name = peer.text("name")
    val id = peer.text("deviceId")
    return if (devices.any { it.text("deviceId") != id && it.text("name").equals(name, ignoreCase = true) })
        "$name · ${id.take(8)}"
    else name
}
