package com.hinge.office

import org.json.JSONArray
import org.json.JSONObject
import java.nio.ByteBuffer
import java.util.zip.Inflater
import java.io.ByteArrayOutputStream

fun Any?.asMap(): Map<String, Any?> = (this as? Map<*, *>)?.entries?.associate { it.key.toString() to it.value } ?: emptyMap()
fun Any?.asRows(): List<Map<String, Any?>> = (this as? List<*>)?.map { it.asMap() } ?: emptyList()
fun Map<String, Any?>.text(key: String, default: String = "") = this[key]?.toString() ?: default
fun Map<String, Any?>.number(key: String, default: Long = 0) = (this[key] as? Number)?.toLong() ?: default
fun JSONObject.toMap(): Map<String, Any?> = keys().asSequence().associateWith { jsonValue(opt(it)) }
fun jsonValue(value: Any?): Any? = when (value) {
    JSONObject.NULL -> null
    is JSONObject -> value.toMap()
    is JSONArray -> (0 until value.length()).map { jsonValue(value.opt(it)) }
    else -> value
}
object Wire {
    private val compressible = setOf(0x20, 0x35, 0x36, 0x37, 0x70, 0x71, 0x80, 0x81)
    fun unwrap(type: Int, payload: ByteArray): Pair<Int, ByteArray> {
        if (type != 0x82) return type to payload
        require(payload.size > 8 && payload[0] == 1.toByte() && payload[1] == 0.toByte()) { "无效压缩帧" }
        val header = ByteBuffer.wrap(payload)
        val inner = header.getShort(2).toInt() and 0xffff
        val length = header.getInt(4)
        require(inner in compressible && length in 0..(32 * 1024 * 1024 + 28)) { "压缩帧超出限制" }
        val inflater = Inflater()
        try {
            inflater.setInput(payload, 8, payload.size - 8)
            val output = ByteArrayOutputStream(minOf(length, 65536))
            val buffer = ByteArray(8192)
            while (!inflater.finished()) {
                val count = inflater.inflate(buffer)
                require(count > 0 && output.size() + count <= length) { "无效或超限的压缩数据" }
                output.write(buffer, 0, count)
            }
            require(output.size() == length && inflater.remaining == 0) { "压缩帧长度错误" }
            return inner to output.toByteArray()
        } finally { inflater.end() }
    }
    fun input(action: Int, key: Int = 0, flags: Int = 0, x: Int = 0, y: Int = 0, wheel: Int = 0, text: String = ""): ByteArray {
        val bytes = text.toByteArray(Charsets.UTF_8)
        require(bytes.size <= 16000)
        return ByteBuffer.allocate(16 + bytes.size).put(action.toByte()).put(key.toByte()).putShort(flags.toShort())
            .putInt(x).putInt(y).putInt(wheel).put(bytes).array()
    }
}
