package com.hinge.office

/**
 * The Android side of the small out-of-band BLE wake advertisement.
 *
 * The manufacturer data is only a wake hint. It contains no file data,
 * pairing code, or LAN credential; the existing authenticated TCP session is
 * still the trust boundary.
 */
object HingeWakeProtocol {
    const val COMPANY_ID = 0xFFFE
    private const val KIND_INDEX = 4
    private const val PAYLOAD_LENGTH = 13

    val FILTER_DATA = byteArrayOf(
        0x48, // H
        0x47, // G
        0x57, // W
        0x01, // protocol version
    )

    val FILTER_MASK = byteArrayOf(
        0xFF.toByte(),
        0xFF.toByte(),
        0xFF.toByte(),
        0xFF.toByte(),
    )

    internal val HOST_FILTER_MASK = ByteArray(PAYLOAD_LENGTH) { index ->
        if (index == KIND_INDEX) 0 else 0xFF.toByte()
    }

    internal fun isWakePayload(payload: ByteArray): Boolean =
        payload.size == PAYLOAD_LENGTH &&
            FILTER_DATA.indices.all { payload[it] == FILTER_DATA[it] } &&
            payload[KIND_INDEX] in 1..3

    internal fun matchesHost(boundPayload: ByteArray, received: ByteArray): Boolean =
        isWakePayload(boundPayload) && isWakePayload(received) && boundPayload.indices.all { index ->
            index == KIND_INDEX || boundPayload[index] == received[index]
        }
}
