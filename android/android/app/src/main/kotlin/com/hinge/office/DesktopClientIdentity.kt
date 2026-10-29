package com.hinge.office

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.Principal
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.security.spec.ECGenParameterSpec
import java.net.Socket
import java.util.Calendar
import javax.net.ssl.SSLContext
import javax.net.ssl.X509KeyManager
import javax.net.ssl.X509TrustManager
import javax.security.auth.x500.X500Principal

/** Persistent app-private TLS identity. First use still requires verification on both screens. */
internal class DesktopClientIdentity(context: Context) {
    private val preferences = context.getSharedPreferences("desktop_peer_pins", Context.MODE_PRIVATE)
    private val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
    private val alias: String

    init {
        synchronized(DesktopClientIdentity::class.java) {
            val legacy = "hinge_desktop_tls_v1"
            val upgraded = "hinge_desktop_tls_v2"
            alias = when {
                store.containsAlias(upgraded) -> upgraded
                store.containsAlias(legacy) && canSign(legacy) -> legacy
                else -> upgraded
            }
            if (!store.containsAlias(alias)) {
                val expires = Calendar.getInstance().apply { add(Calendar.YEAR, 10) }.time
                val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore")
                generator.initialize(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY)
                    .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                    .setDigests(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256,
                        KeyProperties.DIGEST_SHA384, KeyProperties.DIGEST_SHA512)
                    .setCertificateSubject(X500Principal("CN=Hinge Android Desktop Client"))
                    .setCertificateNotAfter(expires).build())
                generator.generateKeyPair()
            }
            check(canSign(alias)) { "手机桌面身份无法完成TLS签名，请解锁手机后重试；仍失败时请反馈此提示" }
        }
    }

    private fun canSign(selected: String): Boolean = DesktopTlsSigning.canSign(
        store.getKey(selected, null) as PrivateKey, store.getCertificate(selected).publicKey)

    fun certificate(): X509Certificate = store.getCertificate(alias) as X509Certificate
    fun knowsServer(peerId: String): Boolean = preferences.contains(pinKey(peerId))
    fun forgetServer(peerId: String) { check(preferences.edit().remove(pinKey(peerId)).commit()) }
    private fun pinKey(peerId: String): String {
        require(peerId.isNotBlank() && peerId.length <= 256)
        return fingerprint(peerId.toByteArray(Charsets.UTF_8))
    }

    fun confirmServer(peerId: String, pin: String) {
        require(pin.matches(Regex("[0-9A-F]{64}")))
        val existing = preferences.getString(pinKey(peerId), null)
        check(existing == null || existing == pin) { "电脑桌面身份变化，请显式取消旧配对后重试" }
        check(preferences.edit().putString(pinKey(peerId), pin).commit()) { "无法保存电脑桌面身份" }
    }

    fun sslContext(peerId: String): SSLContext {
        val knownPin = preferences.getString(pinKey(peerId), null)
        val keys = object : X509KeyManager {
            override fun getClientAliases(type: String?, issuers: Array<out Principal>?) = arrayOf(alias)
            override fun chooseClientAlias(types: Array<out String>?, issuers: Array<out Principal>?, socket: Socket?) = alias
            override fun getServerAliases(type: String?, issuers: Array<out Principal>?) = null
            override fun chooseServerAlias(type: String?, issuers: Array<out Principal>?, socket: Socket?) = null
            override fun getCertificateChain(selected: String?) = if (selected == alias) arrayOf(certificate()) else null
            override fun getPrivateKey(selected: String?): PrivateKey? = if (selected == alias) store.getKey(alias, null) as PrivateKey else null
        }
        val trust = object : X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, type: String?) = throw CertificateException("Client trust not supported")
            override fun checkServerTrusted(chain: Array<out X509Certificate>?, type: String?) {
                val leaf = chain?.firstOrNull() ?: throw CertificateException("Missing desktop server certificate")
                leaf.checkValidity()
                if (knownPin != null && fingerprint(leaf.encoded) != knownPin)
                    throw CertificateException("电脑桌面身份与保存的配对不一致")
                // An unknown peer is only a candidate: no pixels/input before explicit code confirmation.
            }
        }
        return SSLContext.getInstance("TLS").apply { init(arrayOf(keys), arrayOf(trust), SecureRandom()) }
    }

    companion object {
        fun fingerprint(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes)
            .joinToString("") { "%02X".format(it.toInt() and 0xff) }

        fun verificationCode(server: ByteArray, client: ByteArray): String {
            require(server.size in 32..16384 && client.size in 32..16384)
            val transcript = "Hinge Desktop Pairing v1\u0000" + fingerprint(server) + fingerprint(client)
            return fingerprint(transcript.toByteArray(Charsets.US_ASCII)).take(16).chunked(4).joinToString(" ")
        }
    }
}
