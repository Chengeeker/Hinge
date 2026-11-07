package com.hinge.office

import androidx.compose.runtime.*
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import java.io.Closeable
import java.nio.ByteBuffer

class RemoteCipher(private val key: ByteArray, private val challenge: ByteArray, server:Boolean=false) {
    private var sent = 0L
    private var received = 0L
    init { require(key.size == 32 && challenge.size == 32) }
    private fun direction(name: String): ByteArray = RelayCrypto.hmac(RelayCrypto.hmac(challenge, key), "HingeRemoteInput/v1/$name".toByteArray() + byteArrayOf(1))
    private val sendKey = direction(if(server)"server" else "client")
    private val receiveKey = direction(if(server)"client" else "server")
    fun encrypt(plain: ByteArray): ByteArray {
        val header = ByteBuffer.allocate(9).put(1).putLong(++sent).array()
        val nonce = ByteArray(4) + header.copyOfRange(1, 9)
        return header + RelayCrypto.crypt(true, sendKey, nonce, header + challenge, plain)
    }
    fun decrypt(bytes: ByteArray): ByteArray? {
        if (bytes.size !in 41..16425 || bytes[0] != 1.toByte()) return null
        val sequence = ByteBuffer.wrap(bytes, 1, 8).long
        if (sequence <= received) return null
        return try {
            val result = RelayCrypto.crypt(false, receiveKey, ByteArray(4) + bytes.copyOfRange(1, 9), bytes.copyOfRange(0, 9) + challenge, bytes.copyOfRange(9, bytes.size))
            received = sequence; result
        } catch (_: java.security.GeneralSecurityException) { null }
    }
    fun clear() { key.fill(0); sendKey.fill(0); receiveKey.fill(0) }
}

class RemoteController(private val model: HingeModel, val peer: Map<String, Any?>, val desktopMode: Boolean) : Closeable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    var allowRecovery=true
    var ready by mutableStateOf(false)
    var status by mutableStateOf("正在验证远控密钥…")
    var needsKey by mutableStateOf(false)
    var supportsGameKeys by mutableStateOf(false)
    val encrypted get()=desktopMode||cipher!=null
    val isClosed get() = closed
    private var sessionId: Long? = null
    private var cipher: RemoteCipher? = null
    private var challenge: ByteArray? = null
    private var plaintext = false
    private var closed = false
    private val queue = Channel<ByteArray>(128)
    val mouseHeld get()=held.keys.any{it<0}
    private val held = mutableMapOf<Int,Int>()
    private lateinit var sender:Job
    private var frameSubscription: Closeable? = null
    private var eventSubscription: Closeable? = null
    private var keepAlive: Job? = null
    private var deadline: Job? = null
    private var retry:Job?=null
    private var stable:Job?=null
    private var retries=0
    private var starting=false
    init {
        sender=scope.launch {
            try { for (event in queue) transmit(event) }
            catch (e: Exception) { fail(e.message ?: "控制指令发送失败") }
        }
    }
    fun start() {
        if (desktopMode) {
            eventSubscription = model.api.listen("hinge/desktop_video/events") { value ->
                val event=value.asMap()
                if(sessionId!=null&&event.number("sessionId")!=sessionId)return@listen
                when(event.text("state")){
                    "connecting"->{status="身份已验证，正在接收首帧…"}
                    "ready"->{ready=event["canControl"]==true;supportsGameKeys=ready&&event["relativeMouse"]==true;status="桌面画面已加密";stable?.cancel();stable=scope.launch{delay(10000);retries=0}}
                    "error"->{fail(event.text("message"));if(event["retryable"]==true)scheduleRecovery()}
                    "stopped"->{if(!starting&&retry==null)fail("桌面共享已停止")}
                }
            }
            scope.launch{connectDesktop()}
        } else {
            frameSubscription = model.observeFrames { id, type, bytes -> if (id == peer.text("connectionId") && type in setOf(0x50, 0x51)) receive(type, bytes) }
            scope.launch { try{model.sendFrame(peer.text("connectionId"), 0x50, Wire.input(0x30))}catch(e:Exception){fail(e.message?:"连接已断开") } }
            deadline = scope.launch { delay(8000); if (!ready && !needsKey) fail("验证超时，请核对密钥，并更新电脑端 Hinge。") }
        }
    }
    private suspend fun connectDesktop(){
        if(closed||starting||!model.foreground)return
        starting=true
        try{
            sessionId?.let{model.api.call("stop",mapOf("sessionId" to it),"hinge/desktop_video")}
            sessionId=null
            val description=model.describeDesktop(peer.text("connectionId"))
            check(description["enabled"]==true){"请在电脑的“设置 → 投屏与远控 → 手机远控电脑”开启桌面共享"}
            check(description.number("version")==1L)
            if(closed)return
            status="正在连接电脑桌面…"
            model.desktop=emptyMap()
            val result=model.api.call("start",mapOf("host" to peer.text("remoteAddress"),"port" to description.number("port").toInt(),"peerId" to peer.text("deviceId"),"deviceId" to model.identity.text("deviceId"),"name" to model.identity.text("name")),"hinge/desktop_video").asMap()
            sessionId=result.number("sessionId")
        }catch(e:CancellationException){throw e}catch(e:Exception){fail(e.message?:"连接失败");if((e as? NativeFailure)?.details.asMap()["retryable"]==true)scheduleRecovery()}
        finally{starting=false}
    }
    private fun scheduleRecovery(){
        stable?.cancel()
        if(closed||!allowRecovery||!model.foreground||retry!=null||retries>=3||!model.isConnectionReady(peer.text("connectionId")))return
        val wait=longArrayOf(500,1500,4000)[retries++]
        status="连接中断，正在恢复（$retries/3）…"
        retry=scope.launch{delay(wait);retry=null;connectDesktop()}
    }
    private fun receive(type: Int, bytes: ByteArray) {
        if (closed) return
        if (type == 0x50 && bytes.size == 48 && bytes[0] == 0x31.toByte() && cipher == null) {
            challenge = bytes.copyOfRange(16, 48); deadline?.cancel()
            scope.launch {
                val saved = runCatching{model.api.call("loadRemoteControlKey", mapOf("peerId" to peer.text("deviceId")), "hinge/game_controls") as? ByteArray}.getOrNull()
                if(closed)return@launch
                if (saved != null) acceptKey(saved, false) else needsKey = true
            }
            return
        }
        val plain = if (type == 0x51) cipher?.decrypt(bytes) else bytes
        if (plain == null || plain.size < 16) return
        val flags = ByteBuffer.wrap(plain).getShort(2).toInt()
        if (type == 0x50 && flags and 2 == 0) return
        if (type == 0x50) plaintext = true
        when (plain[0].toInt() and 0xff) {
            0x22 -> {
                ready = true; supportsGameKeys=flags and 1!=0; deadline?.cancel(); status = if (type == 0x51) "已连接 · 加密控制" else "已连接 · 局域网未加密"
                keepAlive?.cancel(); keepAlive = scope.launch { while (isActive && ready) { enqueue(Wire.input(0x25)); delay(500) } }
            }
            0x23, 0x24 -> fail("电脑端已停止或拒绝控制，请检查远控开关。")
        }
    }
    suspend fun acceptKey(key: ByteArray, remember: Boolean) {
        require(key.size == 32)
        if (remember) model.api.call("saveRemoteControlKey", mapOf("peerId" to peer.text("deviceId"), "key" to key), "hinge/game_controls")
        cipher = RemoteCipher(key.copyOf(), requireNotNull(challenge)); needsKey = false
        enqueue(Wire.input(0x20)); key.fill(0)
        deadline = scope.launch { delay(8000); if (!ready) fail("验证超时，请核对密钥并重新连接。") }
    }
    private suspend fun transmit(bytes: ByteArray) {
        if (desktopMode) {
            val id = sessionId ?: return
            check(model.api.call("sendInput", mapOf("sessionId" to id, "data" to bytes), "hinge/desktop_video") == true) { "桌面控制通道已关闭" }
        } else {
            val encrypted = cipher?.encrypt(bytes)
            if (encrypted != null) model.sendFrame(peer.text("connectionId"), 0x51, encrypted)
            else if (plaintext) model.sendFrame(peer.text("connectionId"), 0x50, bytes)
        }
    }
    private fun enqueue(bytes: ByteArray) { if (!closed && !queue.trySend(bytes).isSuccess) { fail("控制指令拥塞，已停止控制"); close() } }
    fun input(bytes: ByteArray) { if (ready) enqueue(bytes) }
    fun sendText(text:String){
        val chunk=StringBuilder();var count=0
        text.codePoints().forEach{code->val value=String(Character.toChars(code));val size=value.toByteArray(Charsets.UTF_8).size
            if(count+size>4000){input(Wire.input(0x12,text=chunk.toString()));chunk.setLength(0);count=0}
            chunk.append(value);count+=size
        }
        if(count>0)input(Wire.input(0x12,text=chunk.toString()))
    }
    fun key(key: Int, down: Boolean) {
        if (!ready || key == 0) return
        val count=held[key]?:0
        if(down){held[key]=count+1;if(count>0)return}
        else{if(count==0)return;if(count>1){held[key]=count-1;return};held.remove(key)}
        enqueue(if (key < 0) Wire.input(if (down) 2 else 3, -key - 1) else Wire.input(if (down) 0x10 else 0x11, key))
    }
    fun releaseKeys() { held.keys.toList().forEach { held[it]=1; key(it, false) } }
    fun shortcut(keys:List<Int>){keys.forEach{key(it,true)};keys.asReversed().forEach{key(it,false)}}
    private fun fail(message: String) { stable?.cancel();releaseKeys(); ready = false; status = message; keepAlive?.cancel() }
    fun connectionLost(){fail("电脑连接已断开，请重新连接");close()}
    override fun close() {
        if (closed) return
        releaseKeys()
        if (ready && !desktopMode) enqueue(Wire.input(0x21))
        closed = true; ready = false; keepAlive?.cancel(); deadline?.cancel();retry?.cancel();stable?.cancel()
        frameSubscription?.close(); eventSubscription?.close()
        queue.close()
        scope.launch {
            try {
                withTimeoutOrNull(1000){sender.join()}
                if (desktopMode) sessionId?.let{model.api.call("stop", mapOf("sessionId" to it), "hinge/desktop_video")}
            } catch (error: NativeFailure) {
                // Activity destruction can unregister the bridge while the sender drains.
                android.util.Log.w("HingeDesktopVideo", "结束桌面会话：${error.code}", error)
            } finally { cipher?.clear(); scope.cancel() }
        }
    }
}
