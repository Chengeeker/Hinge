package com.hinge.office

import android.app.Activity
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.graphics.BitmapFactory
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import android.util.AtomicFile
import io.flutter.plugin.common.MethodChannel
import org.json.JSONObject
import java.io.File
import java.util.UUID
import java.security.KeyStore
import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import java.util.concurrent.Executors

class GameProfileBridge(private val activity: Activity, messenger: io.flutter.plugin.common.BinaryMessenger) {
    private val worker = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())
    private var pending: MethodChannel.Result? = null
    private val directory = File(activity.filesDir, "game-controls").apply { mkdirs() }
    private val file = AtomicFile(File(directory, "presets.json"))
    init {
        MethodChannel(messenger, "hinge/game_controls").setMethodCallHandler { call, result ->
            when (call.method) {
                "load" -> run(result) {
                    if (file.baseFile.exists() && file.baseFile.length() > 1024 * 1024) throw java.io.IOException()
                    if (file.baseFile.exists() || File(file.baseFile.path + ".bak").exists()) {
                        file.openRead().bufferedReader().use { it.readText().also { json -> if (json.toByteArray().size > 1024 * 1024) throw java.io.IOException() } }
                    } else null
                }
                "save" -> {
                    val json = call.arguments as? String
                    if (json == null || json.toByteArray().size > 1024 * 1024) result.error("invalid", "预设数据过大", null)
                    else run(result) {
                        val document = JSONObject(json)
                        var stream: java.io.FileOutputStream? = null
                        try { stream = file.startWrite(); stream.write(json.toByteArray()); file.finishWrite(stream) }
                        catch (e: Exception) { if (stream != null) file.failWrite(stream); throw e }
                        // Leave fresh draft icons alone; remove only old, unreferenced private icons.
                        runCatching {
                            val keep = mutableSetOf<String>()
                            val presets = document.getJSONArray("presets")
                            for (i in 0 until presets.length()) {
                                val controls = presets.getJSONObject(i).getJSONArray("controls")
                                for (j in 0 until controls.length()) keep.add(controls.getJSONObject(j).optString("image"))
                            }
                            directory.listFiles()?.filter { it.isFile && it.extension == "png" && it.absolutePath !in keep &&
                                System.currentTimeMillis() - it.lastModified() > 86400000L }?.forEach { it.delete() }
                        }
                        true
                    }
                }
                "loadRemoteControlKey" -> run(result) {
                    loadRemoteControlKey(call.argument<String>("peerId") ?: throw IllegalArgumentException())
                }
                "saveRemoteControlKey" -> run(result) {
                    val peerId = call.argument<String>("peerId") ?: throw IllegalArgumentException()
                    val key = call.argument<ByteArray>("key") ?: throw IllegalArgumentException()
                    if (key.size != 32) throw IllegalArgumentException()
                    saveRemoteControlKey(peerId, key)
                    true
                }
                "deleteRemoteControlKey" -> run(result) {
                    val peerId = call.argument<String>("peerId") ?: throw IllegalArgumentException()
                    deleteRemoteControlKey(peerId)
                    true
                }
                "icon" -> {
                    if (pending != null) result.error("busy", "图片选择器已打开", null)
                    else {
                        pending = result
                        try { activity.startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply { type = "image/*"; addCategory(Intent.CATEGORY_OPENABLE) }, REQUEST) }
                        catch (e: Exception) { pending = null; result.error("picker", "无法打开图片选择器", null) }
                    }
                }
                else -> result.notImplemented()
            }
        }
    }
    private fun run(result: MethodChannel.Result, action: () -> Any?) {
        worker.execute {
            try { val value = action(); main.post { result.success(value) } }
            catch (e: Exception) { main.post { result.error("storage", "无法读取或保存本机数据", null) } }
        }
    }
    private fun remoteKeyIdentity(peerId: String): String {
        require(peerId.isNotBlank() && peerId.length <= 512)
        val digest = MessageDigest.getInstance("SHA-256").digest(peerId.toByteArray(Charsets.UTF_8))
        return digest.joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }
    private fun remoteKeyStore() = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
    private fun remoteSecret(alias: String, create: Boolean): SecretKey? {
        val store = remoteKeyStore()
        (store.getKey(alias, null) as? SecretKey)?.let { return it }
        if (!create) return null
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build())
        return generator.generateKey()
    }
    private fun loadRemoteControlKey(peerId: String): ByteArray? {
        val identity = remoteKeyIdentity(peerId)
        val encoded = activity.getSharedPreferences("remote_control_secrets", Activity.MODE_PRIVATE).getString(identity, null) ?: return null
        val stored = Base64.decode(encoded, Base64.NO_WRAP)
        if (stored.size < 12 + 32 + 16) throw java.io.IOException()
        val key = remoteSecret("hinge_remote_$identity", false) ?: run {
            activity.getSharedPreferences("remote_control_secrets", Activity.MODE_PRIVATE).edit().remove(identity).commit()
            return null
        }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, stored.copyOfRange(0, 12)))
        cipher.updateAAD(peerId.toByteArray(Charsets.UTF_8))
        return cipher.doFinal(stored, 12, stored.size - 12).also { if (it.size != 32) throw java.io.IOException() }
    }
    private fun saveRemoteControlKey(peerId: String, controlKey: ByteArray) {
        val identity = remoteKeyIdentity(peerId)
        val key = remoteSecret("hinge_remote_$identity", true) ?: throw java.io.IOException()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key)
        cipher.updateAAD(peerId.toByteArray(Charsets.UTF_8))
        val iv = cipher.iv
        val encrypted = cipher.doFinal(controlKey)
        val stored = iv + encrypted
        check(activity.getSharedPreferences("remote_control_secrets", Activity.MODE_PRIVATE)
            .edit().putString(identity, Base64.encodeToString(stored, Base64.NO_WRAP)).commit())
    }
    private fun deleteRemoteControlKey(peerId: String) {
        val identity = remoteKeyIdentity(peerId)
        activity.getSharedPreferences("remote_control_secrets", Activity.MODE_PRIVATE).edit().remove(identity).commit()
        remoteKeyStore().deleteEntry("hinge_remote_$identity")
    }
    fun onResult(request: Int, code: Int, data: Intent?): Boolean {
        if (request != REQUEST) return false
        val result = pending ?: return true
        pending = null
        val uri = data?.data
        if (code != Activity.RESULT_OK || uri == null) { result.success(null); return true }
        run(result) {
            if ((directory.listFiles()?.sumOf { it.length() } ?: 0L) > 64L * 1024 * 1024) throw java.io.IOException()
            val bitmap = if (Build.VERSION.SDK_INT >= 28) ImageDecoder.decodeBitmap(ImageDecoder.createSource(activity.contentResolver, uri)) { decoder, info, _ ->
                val scale = 256.0 / maxOf(info.size.width, info.size.height).coerceAtLeast(256)
                decoder.setTargetSize(maxOf(1, (info.size.width * scale).toInt()), maxOf(1, (info.size.height * scale).toInt()))
                decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
            } else {
                val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                activity.contentResolver.openInputStream(uri).use { BitmapFactory.decodeStream(it, null, options) }
                if (options.outWidth <= 0 || options.outHeight <= 0) throw java.io.IOException()
                options.inSampleSize = 1
                while (maxOf(options.outWidth, options.outHeight) / options.inSampleSize > 256) options.inSampleSize *= 2
                options.inJustDecodeBounds = false
                activity.contentResolver.openInputStream(uri).use { BitmapFactory.decodeStream(it, null, options) } ?: throw java.io.IOException()
            }
            val icon = File(directory, UUID.randomUUID().toString() + ".png")
            try { icon.outputStream().use { if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) throw java.io.IOException() } }
            finally { bitmap.recycle() }
            icon.absolutePath
        }
        return true
    }
    fun close() { pending?.error("closed", "页面已关闭", null); pending = null; worker.shutdown() }
    companion object { const val REQUEST = 27401 }
}
