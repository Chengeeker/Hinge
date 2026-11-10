package com.hinge.office

import java.io.DataInputStream
import java.io.IOException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal data class DesktopVideoPacket(
    val codec: Int, val flags: Int, val sequence: Long,
    val presentationTimeUs: Long, val width: Int, val height: Int, val data: ByteArray,
)

/** Dedicated media stream framing; payloads never cross Flutter's event channel. */
internal object DesktopVideoProtocol {
    const val HEADER_SIZE = 32
    const val MAX_ACCESS_UNIT_SIZE = 4 * 1024 * 1024
    const val CONFIGURATION = 1
    const val KEY_FRAME = 2
    const val HEARTBEAT = 4

    fun read(input: InputStream): DesktopVideoPacket? {
        val header = ByteArray(HEADER_SIZE)
        val first = input.read()
        if (first == -1) return null
        header[0] = first.toByte()
        DataInputStream(input).readFully(header, 1, HEADER_SIZE - 1)
        val buffer = ByteBuffer.wrap(header).order(ByteOrder.BIG_ENDIAN)
        if (buffer.int != 0x48445631) throw IOException("Unsupported desktop video magic")
        val codec = buffer.get().toInt() and 0xff
        val flags = buffer.get().toInt() and 0xff
        if (buffer.short.toInt() != 0) throw IOException("Unsupported desktop video version")
        val sequence = buffer.int.toLong() and 0xffffffffL
        val pts = buffer.long
        val width = buffer.int
        val height = buffer.int
        val length = buffer.int
        if (codec !in 1..2 || flags and 7 != flags || pts < 0 ||
            (flags and HEARTBEAT != 0 && (flags != HEARTBEAT || length != 1)) ||
            width !in 2..8192 || height !in 2..8192 ||
            width % 2 != 0 || height % 2 != 0 || length !in 1..MAX_ACCESS_UNIT_SIZE) {
            throw IOException("Invalid desktop video packet metadata")
        }
        val data = ByteArray(length)
        DataInputStream(input).readFully(data)
        if (flags == HEARTBEAT && data[0].toInt() != 0) throw IOException("Invalid desktop heartbeat")
        return DesktopVideoPacket(codec, flags, sequence, pts, width, height, data)
    }
}
