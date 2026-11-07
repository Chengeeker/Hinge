package com.hinge.office

import java.io.File
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.util.Base64
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject

object RelayCrypto {
    const val PART = 8 * 1024 * 1024
    fun encode(bytes: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    fun decode(text: String): ByteArray = Base64.getUrlDecoder().decode(text.trim())
    fun key(text: String) = decode(text).also { require(it.size == 32) { "加密密钥必须是32字节Base64URL" } }
    fun sha(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)
    fun hmac(key: ByteArray, bytes: ByteArray): ByteArray = Mac.getInstance("HmacSHA256").run { init(SecretKeySpec(key, "HmacSHA256")); doFinal(bytes) }
    fun id(key: String, device: String): String = encode(hmac(key(key), "Hinge-Relay-Device-v1|${device.trim()}".toByteArray()))
    fun derive(secret: String, transfer: String, sender: String, receiver: String): ByteArray {
        val prk = hmac(sha(transfer.toByteArray()), key(secret))
        return hmac(prk, "Hinge-Cloud-Relay-v1|$sender|$receiver".toByteArray() + byteArrayOf(1))
    }
    fun nonce(transfer: String, part: Int): ByteArray = ByteBuffer.allocate(12).put(sha(transfer.toByteArray()), 0, 8).putInt(part).array()
    fun metadataNonce(transfer: String) = sha("Hinge-Cloud-Relay-v1|metadata|$transfer".toByteArray()).copyOf(12)
    fun aad(transfer: String, sender: String, receiver: String, part: Int, size: Int) = "Hinge-Cloud-Relay-v1|$transfer|$sender|$receiver|$part|$size".toByteArray()
    fun metadataAad(transfer: String, sender: String, receiver: String) = "Hinge-Cloud-Relay-v1|metadata|$transfer|$sender|$receiver".toByteArray()
    fun crypt(encrypt: Boolean, key: ByteArray, nonce: ByteArray, aad: ByteArray, data: ByteArray): ByteArray = Cipher.getInstance("AES/GCM/NoPadding").run {
        init(if (encrypt) Cipher.ENCRYPT_MODE else Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce)); updateAAD(aad); doFinal(data)
    }
    fun hash(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input -> val buffer = ByteArray(65536); while (true) { val n = input.read(buffer); if (n < 0) break; digest.update(buffer, 0, n) } }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
    fun partCount(size: Long): Int { require(size in 0..(PART.toLong() * 10000)); return maxOf(1, ((size + PART - 1) / PART).toInt()) }
}

class Relay(private val settings: Map<String, Any?>, private val device: Map<String, Any?>) {
    private val secret get() = settings.text("relayEncryptionKey")
    private val local get() = RelayCrypto.id(secret, device.text("deviceId"))
    suspend fun request(method: String, path: String, body: ByteArray? = null, token: String = settings.text("deviceToken"), identify: Boolean = true, binary: Boolean = false, limit: Int = 1024 * 1024): ByteArray = withContext(Dispatchers.IO) {
        val endpoint = URI(settings.text("endpoint").trim().trimEnd('/'))
        require(endpoint.scheme in listOf("https", "http") && !endpoint.host.isNullOrEmpty() && endpoint.userInfo == null && endpoint.query == null && endpoint.fragment == null) { "请输入有效的 Worker HTTP(S) 地址" }
        val url = URI(endpoint.toString() + path).toURL()
        val connection = url.openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.instanceFollowRedirects = false
            connection.connectTimeout = 15000; connection.readTimeout = 60000
            connection.setRequestProperty("Accept", "application/json")
            if (token.isNotBlank()) connection.setRequestProperty("Authorization", "Bearer ${token.trim()}")
            if (identify) connection.setRequestProperty("X-Hinge-Relay-Device", local)
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", if (binary) "application/octet-stream" else "application/json")
                connection.setFixedLengthStreamingMode(body.size)
                connection.outputStream.use { it.write(body) }
            }
            val code = connection.responseCode
            val stream = if (code in 200..299) connection.inputStream else connection.errorStream
            val output = ByteArrayOutputStream()
            stream?.use { input -> val buffer = ByteArray(65536); while (true) { val n = input.read(buffer); if (n < 0) break; require(output.size() + n <= limit) { "Relay 响应超过容量限制" }; output.write(buffer, 0, n) } }
            val bytes = output.toByteArray()
            check(code in 200..299) { "Relay HTTP $code：${String(bytes).take(256)}" }
            bytes
        } finally { connection.disconnect() }
    }
    private suspend fun json(method: String, path: String, body: Map<String, Any?>? = null) = JSONObject(String(request(method, path, body?.let { JSONObject(it).toString().toByteArray() }))).toMap()
    suspend fun receipts()=json("GET","/v1/receipts")["items"].asRows()
    suspend fun deleteReceipt(id:String){require(id.matches(Regex("[A-Za-z0-9-]{1,128}")));request("DELETE","/v1/receipts/$id")}
    suspend fun register(admin: String): String {
        require(admin.isNotBlank()) { "请输入管理员 Token" }
        val data = mapOf("relayDeviceId" to local, "name" to device.text("name"), "platform" to "android", "clientVersion" to BuildConfig.VERSION_NAME)
        return JSONObject(String(request("POST", "/v1/register", JSONObject(data).toString().toByteArray(), admin, false))).getString("deviceToken")
    }
    suspend fun send(file: File, name: String, mime: String, peer: String, progress: suspend (String, Long, Long, String) -> Unit): String = withContext(Dispatchers.IO) {
        require(file.isFile)
        val sender = local; val receiver = RelayCrypto.id(secret, peer)
        check(json("GET", "/v1/devices/$receiver")["registered"] == true) { "对方尚未注册此 Relay" }
        val id = UUID.randomUUID().toString()
        val size = file.length(); val count = RelayCrypto.partCount(size)
        try {
        progress(id, 0, size, "preparing")
        val hash = RelayCrypto.hash(file)
        val key = RelayCrypto.derive(secret, id, sender, receiver)
        val metadata = JSONObject(mapOf("fileName" to name, "mimeType" to mime, "fileSize" to size, "modifiedTime" to file.lastModified() / 1000, "sha256" to hash)).toString().toByteArray()
        val nonce = RelayCrypto.metadataNonce(id)
        val encryptedMetadata = RelayCrypto.crypt(true, key, nonce, RelayCrypto.metadataAad(id, sender, receiver), metadata)
        val created = System.currentTimeMillis() / 1000; val expires = created + 168 * 3600
        val uploadId = json("POST", "/v1/transfers", mapOf("transferId" to id, "receiverRelayDeviceId" to receiver, "partSize" to RelayCrypto.PART, "partCount" to count, "expiresAt" to expires)).text("uploadId")
        require(uploadId.isNotEmpty())
        val parts = mutableListOf<Map<String, Any?>>()
        var sent = 0L
        file.inputStream().use { input ->
            for (part in 1..count) {
                val length = minOf(RelayCrypto.PART.toLong(), size - sent).toInt()
                val plain = ByteArray(length)
                var offset = 0
                while (offset < length) { val n = input.read(plain, offset, length - offset); check(n > 0) { "发送期间文件被修改" }; offset += n }
                val encrypted = RelayCrypto.crypt(true, key, RelayCrypto.nonce(id, part), RelayCrypto.aad(id, sender, receiver, part, length), plain)
                val etag = JSONObject(String(request("PUT", "/v1/transfers/$id/parts/$part?uploadId=${java.net.URLEncoder.encode(uploadId, "UTF-8")}", encrypted, binary = true))).getString("etag")
                parts += mapOf("partNumber" to part, "etag" to etag)
                sent += length; progress(id, sent, size, "uploading")
            }
            check(input.read() == -1 && file.length() == size && RelayCrypto.hash(file) == hash) { "发送期间文件被修改，请重新发送" }
        }
        val manifest = mapOf("version" to 1, "transferId" to id, "senderRelayDeviceId" to sender, "receiverRelayDeviceId" to receiver,
            "createdAt" to created, "expiresAt" to expires, "partSize" to RelayCrypto.PART, "partCount" to count, "ciphertextSize" to size + count * 16L,
            "encryption" to "AES-256-GCM", "metadataCiphertext" to RelayCrypto.encode(encryptedMetadata.copyOfRange(0, encryptedMetadata.size - 16)),
            "metadataNonce" to RelayCrypto.encode(nonce), "metadataTag" to RelayCrypto.encode(encryptedMetadata.takeLast(16).toByteArray()))
        progress(id, size, size, "publishing")
        request("POST", "/v1/transfers/$id/complete", JSONObject(mapOf("uploadId" to uploadId, "parts" to parts, "manifest" to manifest)).toString().toByteArray())
        id
        } catch(e:Exception){if(e is kotlinx.coroutines.CancellationException)throw e;progress(id,0,size,"failed");throw e}
    }
    suspend fun receive(directory: File, trustedIds: Set<String>, completed:Map<String,String>,fileReady:suspend(String,File)->Unit,progress: suspend (String, String, Long, Long, String) -> Unit): List<File> = withContext(Dispatchers.IO) {
        val allowed = trustedIds.map { RelayCrypto.id(secret, it) }.toSet()
        val received = mutableListOf<File>()
        for (manifest in json("GET", "/v1/inbox")["items"].asRows().take(100)) {
            val id = manifest.text("transferId"); val sender = manifest.text("senderRelayDeviceId"); val receiver = manifest.text("receiverRelayDeviceId")
            if (sender !in allowed || receiver != local) continue
            require(id.matches(Regex("[A-Za-z0-9-]{1,128}")) && manifest.number("version") == 1L && manifest.number("partSize") == RelayCrypto.PART.toLong())
            require(manifest.text("encryption")=="AES-256-GCM"&&manifest.number("partCount") in 1..10000)
            if(id in completed){request("POST","/v1/transfers/$id/ack","{}".toByteArray());continue}
            require(manifest.text("metadataNonce").length<=32&&manifest.text("metadataTag").length<=32&&manifest.text("metadataCiphertext").length<=65536)
            require(RelayCrypto.decode(manifest.text("metadataNonce")).size==12&&RelayCrypto.decode(manifest.text("metadataTag")).size==16)
            val key = RelayCrypto.derive(secret, id, sender, receiver)
            val metadata = JSONObject(String(RelayCrypto.crypt(false, key, RelayCrypto.decode(manifest.text("metadataNonce")), RelayCrypto.metadataAad(id, sender, receiver), RelayCrypto.decode(manifest.text("metadataCiphertext")) + RelayCrypto.decode(manifest.text("metadataTag"))))).toMap()
            val size = metadata.number("fileSize", -1); val count = RelayCrypto.partCount(size)
            require(manifest.number("partCount") == count.toLong() && manifest.number("ciphertextSize") == size + 16L * count)
            require(metadata.text("sha256").matches(Regex("[0-9a-fA-F]{64}")))
            val name = metadata.text("fileName").replace('\\', '/').substringAfterLast('/').filter { it >= ' ' && it != '\u007f' }.take(160).takeUnless { it.isBlank() || it == "." || it == ".." } ?: "Hinge-file"
            check(directory.isDirectory || directory.mkdirs()) { "无法创建接收目录" }
            check(directory.usableSpace >= size) { "存储空间不足" }
            val temporary = File(directory, ".$id.part")
            var written = 0L
            var finalized=false
            try {
                temporary.outputStream().use { output ->
                    for (part in 1..count) {
                        val length = minOf(RelayCrypto.PART.toLong(), size - written).toInt()
                        val encrypted = request("GET", "/v1/transfers/$id/parts/$part", limit = RelayCrypto.PART + 16)
                        require(encrypted.size == length + 16)
                        val bytes = RelayCrypto.crypt(false, key, RelayCrypto.nonce(id, part), RelayCrypto.aad(id, sender, receiver, part, length), encrypted)
                        output.write(bytes); written += bytes.size
                        progress(id, name, written, size, "downloading")
                    }
                    output.fd.sync()
                }
                progress(id, name, written, size, "verifying")
                check(written == size && RelayCrypto.hash(temporary).equals(metadata.text("sha256"), true)) { "文件完整性校验失败" }
                var destination = File(directory, name); var suffix = 1
                while (destination.exists()) destination = File(directory, "${File(name).nameWithoutExtension} (${suffix++})${File(name).extension.takeIf { it.isNotEmpty() }?.let { ".$it" } ?: ""}")
                check(temporary.renameTo(destination)) { "无法保存接收文件" }
                fileReady(id,destination)
                finalized=true
                destination.setLastModified(metadata.number("modifiedTime")*1000)
                received += destination
                progress(id, name, size, size, "completed")
                request("POST", "/v1/transfers/$id/ack", "{}".toByteArray())
            } catch(e:Exception){if(e is kotlinx.coroutines.CancellationException)throw e;if(!finalized)progress(id,name,written,size,"failed");throw e} finally { if (temporary.exists()) temporary.delete() }
        }
        received
    }
}
