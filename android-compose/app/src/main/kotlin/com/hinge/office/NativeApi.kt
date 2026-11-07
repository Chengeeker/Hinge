package com.hinge.office

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.io.Closeable
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

class NativeFailure(val code:String,message:String?,val details:Any?):IllegalStateException(message?:code)

data class NativeCall(val method: String, val arguments: Any?) {
    @Suppress("UNCHECKED_CAST") fun <T> argument(key: String): T? = (arguments as? Map<*, *>)?.get(key) as? T
}
interface NativeResult {
    fun success(value: Any?)
    fun error(code: String, message: String?, details: Any?)
    fun notImplemented()
}
fun interface NativeEventSink { fun success(value: Any?) }

/** Direct Kotlin calls into the preserved platform implementation; no engine or serialization. */
class NativeApi {
    private val handlers = mutableMapOf<String, (NativeCall, NativeResult) -> Unit>()
    private val listeners = mutableMapOf<String, MutableList<(Any?) -> Unit>>()
    fun register(name: String, handler: (NativeCall, NativeResult) -> Unit) { handlers[name] = handler }
    fun unregister(name: String) { handlers.remove(name) }
    fun sink(name: String) = NativeEventSink { value -> listeners[name]?.toList()?.forEach { it(value) } }
    fun listen(name: String, listener: (Any?) -> Unit): Closeable {
        listeners.getOrPut(name) { mutableListOf() }.add(listener)
        return Closeable { listeners[name]?.remove(listener) }
    }
    suspend fun call(method: String, args: Any? = null, channel: String = "hinge/platform"): Any? =
        withContext(Dispatchers.Main.immediate) {
            suspendCancellableCoroutine { continuation ->
                val result = object : NativeResult {
                    override fun success(value: Any?) { if (continuation.isActive) continuation.resume(value) }
                    override fun error(code: String, message: String?, details: Any?) {
                        if (continuation.isActive) continuation.resumeWithException(NativeFailure(code,message,details))
                    }
                    override fun notImplemented() = error("unsupported", "不支持的操作：$method", null)
                }
                try { handlers[channel]?.invoke(NativeCall(method, args), result) ?: result.notImplemented() }
                catch (e: Exception) { result.error("native", e.message, null) }
            }
        }
}
