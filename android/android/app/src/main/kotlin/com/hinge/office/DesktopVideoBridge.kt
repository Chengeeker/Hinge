package com.hinge.office

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.os.Build
import android.view.Surface
import android.view.WindowManager
import io.flutter.plugin.common.BinaryMessenger
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel
import io.flutter.view.TextureRegistry
import org.json.JSONObject
import java.io.Closeable
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.io.EOFException
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketException
import java.net.SocketTimeoutException
import java.security.cert.X509Certificate
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import javax.net.ssl.SSLSocket

internal class DesktopVideoBridge(
    private val context: Context, messenger: BinaryMessenger, private val textures: TextureRegistry,
) : Closeable {
    private val main = Handler(Looper.getMainLooper())
    private val methods = MethodChannel(messenger, "hinge/desktop_video")
    private val events = EventChannel(messenger, "hinge/desktop_video/events")
    private var sink: EventChannel.EventSink? = null
    private var active: Session? = null
    private var nextId = 0L

    init {
        methods.setMethodCallHandler { call, result ->
            try {
                when (call.method) {
                    "start" -> {
                        val host = call.argument<String>("host") ?: error("Missing desktop host")
                        val port = call.argument<Int>("port") ?: error("Missing desktop port")
                        val peer = call.argument<String>("peerId") ?: error("Missing desktop identity")
                        val localId = call.argument<String>("deviceId") ?: error("Missing local identity")
                        val name = call.argument<String>("name") ?: "Android 手机"
                        require(host.length in 1..256 && port in 1..65535 && peer.length in 1..256 && localId.length in 1..256 && name.length in 1..128)
                        stop()
                        val texture = textures.createSurfaceTexture()
                        texture.surfaceTexture().setDefaultBufferSize(1280, 720)
                        val session = Session(++nextId, texture, host, port, peer, localId, name)
                        active = session
                        result.success(mapOf("sessionId" to session.id, "textureId" to texture.id()))
                        Thread({ session.run() }, "HingeDesktopVideo").start()
                    }
                    "confirm" -> {
                        val session = active
                        require(session != null && call.argument<Number>("sessionId")?.toLong() == session.id)
                        session.confirmation.complete(call.argument<Boolean>("allow") == true)
                        result.success(null)
                    }
                    "sendInput" -> {
                        val session = active
                        val bytes = call.argument<ByteArray>("data") ?: error("Missing desktop input")
                        result.success(session != null && call.argument<Number>("sessionId")?.toLong() == session.id && session.sendInput(bytes))
                    }
                    "stop" -> {
                        val expected = call.argument<Number>("sessionId")?.toLong()
                        if (expected == null || active?.id == expected) stop()
                        result.success(null)
                    }
                    "loadInputMode", "saveInputMode" -> {
                        val peer = call.argument<String>("peerId") ?: error("Missing peer")
                        require(peer.length <= 256)
                        val prefs = context.getSharedPreferences("desktop_input_modes", android.content.Context.MODE_PRIVATE)
                        if (call.method == "saveInputMode") {
                            prefs.edit().putBoolean(peer, call.argument<Boolean>("direct") == true).apply()
                            result.success(null)
                        } else result.success(prefs.getBoolean(peer, true))
                    }
                    "forgetPeer" -> {
                        val peer = call.argument<String>("peerId") ?: error("Missing desktop identity")
                        DesktopClientIdentity(context).forgetServer(peer)
                        result.success(null)
                    }
                    else -> result.notImplemented()
                }
            } catch (error: Exception) { result.error("desktop_video", error.message, null) }
        }
        events.setStreamHandler(object : EventChannel.StreamHandler {
            override fun onListen(arguments: Any?, eventSink: EventChannel.EventSink?) { sink = eventSink }
            override fun onCancel(arguments: Any?) { sink = null; stop() }
        })
    }

    fun stop() { val session = active; active = null; session?.stop() }
    override fun close() { stop(); methods.setMethodCallHandler(null); events.setStreamHandler(null); sink = null }

    private inner class Session(
        val id: Long, private val texture: TextureRegistry.SurfaceTextureEntry,
        private val host: String, private val port: Int, private val peer: String,
        private val localId: String, private val name: String,
    ) {
        private val stopped = AtomicBoolean(false)
        private val failed = AtomicBoolean(false)
        @Volatile private var socket: Socket? = null
        val confirmation = CompletableFuture<Boolean>()
        private val received = AtomicLong(0)
        private val scheduler = Executors.newSingleThreadScheduledExecutor()
        private val inputWriter = Executors.newSingleThreadExecutor()
        private val pairingReader = Executors.newSingleThreadExecutor()
        private val inputs = ArrayBlockingQueue<ByteArray>(128)
        @Volatile private var canControl = false
        @Volatile private var established = false
        @Volatile private var videoDecoder: DesktopVideoDecoder? = null
        @Volatile private var relativeMouse = false
        @Volatile private var absoluteMouse = false
        private val readySent = AtomicBoolean(false)
        private val firstFrameDeadline = Runnable {
            if (!readySent.get()) fail("未能在8秒内显示电脑首帧，请检查电脑采集和手机解码状态")
        }
        private var knownServer = false
        private val textureStatistics = DesktopTextureStatistics()

        init {
            texture.setOnFrameConsumedListener {
                if (!stopped.get()) {
                    try { textureStatistics.consumed(texture.surfaceTexture().timestamp, System.nanoTime()) }
                    catch (_: Exception) { textureStatistics.available = false }
                    if (established && (videoDecoder?.decodedFrames ?: 0L) > 0L &&
                        readySent.compareAndSet(false, true)) {
                        main.removeCallbacks(firstFrameDeadline)
                        emit("ready", mapOf("canControl" to canControl, "relativeMouse" to relativeMouse, "absoluteMouse" to absoluteMouse))
                    }
                }
            }
        }

        private fun fail(message: String, retryable: Boolean = false) {
            if (!stopped.get() && failed.compareAndSet(false, true)) {
                emit("error", mapOf("message" to message, "retryable" to ((established || knownServer) && retryable)))
                stop()
            }
        }

        fun sendInput(bytes: ByteArray): Boolean {
            if (stopped.get() || !canControl || bytes.size !in 16..4096) return false
            if (inputs.offer(bytes)) return true
            fail("控制指令拥塞，已释放操作并断开连接")
            return false
        }

        private fun emit(state: String, values: Map<String, Any> = emptyMap()) {
            main.post { if (active === this && (state == "error" || !stopped.get()))
                sink?.success(mapOf("sessionId" to id, "state" to state) + values) }
        }
        fun stop() {
            stopped.set(true); confirmation.complete(false)
            main.removeCallbacks(firstFrameDeadline)
            try { socket?.close() } catch (_: IOException) { }
            scheduler.shutdownNow()
            inputWriter.shutdownNow(); inputs.clear()
            pairingReader.shutdownNow()
        }
        private fun readJson(input: DataInputStream): JSONObject {
            val size = input.readInt()
            if (size !in 2..8192) throw IOException("Desktop handshake exceeds limit")
            val bytes = ByteArray(size); input.readFully(bytes)
            var depth = 0; var quoted = false; var escaped = false
            for (byte in bytes) {
                val c = byte.toInt().toChar()
                if (quoted) {
                    if (escaped) escaped = false else if (c == '\\') escaped = true else if (c == '"') quoted = false
                } else if (c == '"') quoted = true
                else if (c == '{' || c == '[') { depth++; if (depth > 8) throw IOException("Desktop handshake nesting exceeds limit") }
                else if (c == '}' || c == ']') depth--
            }
            return JSONObject(String(bytes, Charsets.UTF_8))
        }
        private fun writeJson(output: DataOutputStream, value: JSONObject) {
            val bytes = value.toString().toByteArray(Charsets.UTF_8)
            synchronized(output) { output.writeInt(bytes.size); output.write(bytes); output.flush() }
        }
        @Suppress("DEPRECATION")
        private fun displayRefreshRate(): Float = try {
            val value = if (Build.VERSION.SDK_INT >= 30) context.display?.refreshRate ?: 0f
                else (context.getSystemService(Context.WINDOW_SERVICE) as WindowManager).defaultDisplay.refreshRate
            if (value.isFinite() && value in 1f..1000f) value else 0f
        } catch (_: Exception) { 0f }
        fun run() {
            var decoder: DesktopVideoDecoder? = null
            var surface: Surface? = null
            var stage = "初始化手机桌面身份"
            try {
                val identity = DesktopClientIdentity(context)
                knownServer = identity.knowsServer(peer)
                stage = "连接电脑桌面服务"
                val raw = Socket(); socket = raw
                if (stopped.get()) return
                raw.connect(InetSocketAddress(host, port), 5000)
                val tls = identity.sslContext(peer).socketFactory.createSocket(raw, host, port, true) as SSLSocket
                socket = tls
                tls.enabledProtocols = tls.supportedProtocols.filter { it == "TLSv1.2" || it == "TLSv1.3" }.toTypedArray()
                tls.tcpNoDelay = true; tls.soTimeout = 10000
                if (stopped.get()) return
                stage = "TLS安全握手"
                tls.startHandshake()
                stage = "核对桌面身份"
                val server = tls.session.peerCertificates.first() as X509Certificate
                val code = DesktopClientIdentity.verificationCode(server.encoded, identity.certificate().encoded)
                val input = DataInputStream(tls.inputStream)
                val output = DataOutputStream(tls.outputStream)
                val known = knownServer
                tls.soTimeout = 65000
                writeJson(output, JSONObject().put("version", 1).put("deviceId", localId).put("name", name).put("verifyIdentity", !known).put("mediaHeartbeat", true))
                var reply = readJson(input)
                if (reply.optString("state") == "verify") {
                    knownServer = false
                    if (reply.getString("code") != code) throw IOException("两端桌面身份码不一致，连接已拒绝")
                    emit("verify", mapOf("code" to code))
                    val serverReply = CompletableFuture<JSONObject>()
                    pairingReader.execute {
                        try { serverReply.complete(readJson(input)) }
                        catch (error: Exception) { serverReply.completeExceptionally(error) }
                    }
                    CompletableFuture.anyOf(confirmation, serverReply).get(60, TimeUnit.SECONDS)
                    if (serverReply.isDone && !confirmation.isDone) {
                        try { serverReply.get() } catch (error: Exception) { throw IOException("电脑未允许或已结束桌面配对", error) }
                        throw IOException("电脑提前结束身份确认，连接已拒绝")
                    }
                    if (!confirmation.getNow(false) || stopped.get()) throw IOException("桌面配对已取消")
                    writeJson(output, JSONObject().put("action", "confirm"))
                    reply = serverReply.get(60, TimeUnit.SECONDS)
                } else if (!known) throw IOException("首次桌面连接缺少身份确认")
                if (reply.optString("state") != "ready" || stopped.get()) throw IOException("电脑未允许桌面共享")
                stage = "接收电脑桌面"
                identity.confirmServer(peer, DesktopClientIdentity.fingerprint(server.encoded))
                tls.soTimeout = if (reply.optBoolean("mediaHeartbeat", false)) 6500 else 0
                surface = Surface(texture.surfaceTexture())
                val video = DesktopVideoDecoder(surface, { width, height ->
                    texture.surfaceTexture().setDefaultBufferSize(width, height)
                    emit("format", mapOf("width" to width, "height" to height))
                }, { error -> fail(error.message ?: "视频解码失败") })
                decoder = video
                videoDecoder = video
                canControl = reply.optBoolean("canControl", false)
                relativeMouse = reply.optBoolean("relativeMouse", false)
                absoluteMouse = reply.optBoolean("absoluteMouse", false)
                established = true
                main.postDelayed(firstFrameDeadline, 8000)
                if (canControl) inputWriter.execute {
                    try {
                        while (!stopped.get()) {
                            val bytes = inputs.take()
                            writeJson(output, JSONObject().put("action", "input").put("data",
                                android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)))
                        }
                    } catch (_: InterruptedException) { }
                    catch (_: IOException) { fail("控制连接已断开", true) }
                    catch (_: Exception) { fail("控制发送失败") }
                }
                emit("connecting")
                scheduler.scheduleAtFixedRate({
                    if (!stopped.get()) {
                        try {
                            writeJson(output, JSONObject().put("action", "keepAlive"))
                            emit("stats", mapOf("received" to received.get(), "submitted" to video.submittedFrames,
                                "decoded" to video.decodedFrames, "presented" to video.presentedFrames.get(),
                                "pending" to video.pendingFrames, "decoderName" to video.decoderName, "decoderMode" to video.decoderMode,
                                "maxVideoGapMs" to video.maxPresentedGapNs.getAndSet(0) / 1_000_000.0,
                                "displayHz" to displayRefreshRate(),
                                "consumed" to textureStatistics.consumedFrames.get(),
                                "maxTextureGapMs" to textureStatistics.maxGapNs.getAndSet(0) / 1_000_000.0,
                                "textureSampling" to textureStatistics.available,
                                "timeMs" to android.os.SystemClock.elapsedRealtime()))
                        } catch (_: IOException) { fail("桌面连接保活失败", true) }
                        catch (_: Exception) { fail("桌面连接保活失败") }
                    }
                }, 0, 1, TimeUnit.SECONDS)
                while (!stopped.get()) {
                    val packet = DesktopVideoProtocol.read(input) ?: throw EOFException("电脑已停止桌面共享")
                    if (packet.flags == DesktopVideoProtocol.HEARTBEAT) continue
                    received.incrementAndGet()
                    video.submit(packet) { !stopped.get() }
                }
            } catch (error: Exception) {
                fail("$stage：${error.message ?: "连接失败"}",
                    error is EOFException || error is SocketException || error is SocketTimeoutException)
            } finally {
                stop()
                videoDecoder = null
                try { decoder?.close() } catch (_: Exception) { }
                surface?.release()
                main.post {
                    texture.setOnFrameConsumedListener(null)
                    texture.release()
                    if (active === this) active = null
                    sink?.success(mapOf("sessionId" to id, "state" to "stopped"))
                }
            }
        }
    }
}
