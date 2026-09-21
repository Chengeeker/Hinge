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
}
