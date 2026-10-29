package com.hinge.office

import java.security.GeneralSecurityException
import java.security.PrivateKey
import java.security.PublicKey
import java.security.Signature

internal object DesktopTlsSigning {
    fun canSign(privateKey: PrivateKey, publicKey: PublicKey): Boolean = try {
        // Conscrypt supplies the digest itself for AndroidKeyStore TLS keys.
        val digest = ByteArray(32) { it.toByte() }
        val signer = Signature.getInstance("NONEwithECDSA")
        signer.initSign(privateKey)
        signer.update(digest)
        val signature = signer.sign()
        val verifier = Signature.getInstance("NONEwithECDSA")
        verifier.initVerify(publicKey)
        verifier.update(digest)
        verifier.verify(signature)
    } catch (_: GeneralSecurityException) { false }
}
