package com.hinge.office

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.ContextWrapper
import android.content.AttributionSource
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.Base64
import java.io.BufferedReader
import java.io.InputStreamReader
import java.nio.charset.StandardCharsets
import kotlin.concurrent.thread

object AdbClipboardBridge {
    private const val MAX_TEXT_BYTES = 1_048_576
    private const val MAX_COMMAND_CHARS = 1_500_000
    private var stage = "context"

    @JvmStatic
    fun main(args: Array<String>) {
        try {
            runBridge()
        } catch (error: Throwable) {
            // Only exception types leave this process, never clipboard text or exception messages.
            var cause = error
            repeat(8) {
                (cause as? java.lang.reflect.InvocationTargetException)?.targetException?.let { cause = it }
            }
            if (cause is SecurityException) {
                val message = cause.message ?: ""
                val reason = when {
                    message.contains("does not belong") || message.contains("does not match") -> "package_identity"
                    message.contains("AttributionSource") -> "attribution"
                    message.contains("INTERACT_ACROSS_USERS") -> "cross_user"
                    else -> Regex("android\\.permission\\.[A-Z_]+").find(message)?.value ?: "permission"
                }
                System.out.println("ERROR\tclipboard_permission\t$stage\t$reason")
            } else {
                System.out.println("ERROR\tbridge\t${cause.javaClass.simpleName}")
            }
            System.out.flush()
        }
    }

    private fun runBridge() {
        if (Looper.myLooper() == null) Looper.prepareMainLooper()

        val activityThreadClass = Class.forName("android.app.ActivityThread")
        val activityThread = activityThreadClass.getMethod("systemMain").invoke(null)
        val systemContext = activityThreadClass.getMethod("getSystemContext").invoke(activityThread) as Context
        val packageContext = systemContext.createPackageContext(
            "com.android.shell",
            Context.CONTEXT_IGNORE_SECURITY,
        )
        val shellContext = if (Build.VERSION.SDK_INT >= 31) ShellClipboardContext31(packageContext)
            else ShellClipboardContext(packageContext)
        stage = "clipboard_manager"
        val handler = Handler(Looper.getMainLooper())
        // ContextWrapper.getSystemService delegates to ContextImpl and loses our shell attribution.
        val clipboard = ClipboardManager::class.java.getDeclaredConstructor(Context::class.java, Handler::class.java)
            .newInstance(shellContext, handler)
        val access = ShellClipboardAccess(clipboard)
        stage = "initial_read"
        var lastText = readText(access)
        val outputLock = Any()
        val listener = ClipboardManager.OnPrimaryClipChangedListener {
            val text = readText(access)
            if (text != lastText) {
                lastText = text
                if (text != null) {
                    val encoded = Base64.encodeToString(text.toByteArray(StandardCharsets.UTF_8), Base64.NO_WRAP)
                    synchronized(outputLock) {
                        System.out.println("CLIP\t$encoded")
                        System.out.flush()
                    }
                }
            }
        }
        stage = "listener"
        clipboard.addPrimaryClipChangedListener(listener)
        System.out.println("READY")
        System.out.flush()

        thread(name = "hinge-adb-clipboard-input", isDaemon = true) {
            try {
                BufferedReader(InputStreamReader(System.`in`, StandardCharsets.UTF_8)).use { input ->
                    while (true) {
                        val command = readBoundedLine(input) ?: break
                        if (!command.startsWith("SET\t")) continue
                        val bytes = try { Base64.decode(command.substring(4), Base64.DEFAULT) }
                            catch (_: IllegalArgumentException) { continue }
                        if (bytes.size > MAX_TEXT_BYTES) continue
                        val text = String(bytes, StandardCharsets.UTF_8)
                        handler.post {
                            if (text != readText(access)) {
                                lastText = text
                                access.setClip(ClipData.newPlainText("Hinge", text))
                            }
                        }
                    }
                }
            } finally {
                handler.post {
                    clipboard.removePrimaryClipChangedListener(listener)
                    // Android's main looper cannot be quit; this standalone shell process can exit.
                    kotlin.system.exitProcess(0)
                }
            }
        }

        Looper.loop()
    }

    private fun readText(access: ShellClipboardAccess): String? {
        val item = access.getClip()?.takeIf { it.itemCount > 0 }?.getItemAt(0) ?: return null
        return item.text?.toString()?.takeIf { it.toByteArray(StandardCharsets.UTF_8).size <= MAX_TEXT_BYTES }
    }

    private fun readBoundedLine(input: BufferedReader): String? {
        val line = StringBuilder()
        while (true) {
            val next = input.read()
            if (next < 0) return if (line.isEmpty()) null else line.toString()
            if (next == '\n'.code) return line.toString().removeSuffix("\r")
            if (line.length >= MAX_COMMAND_CHARS) {
                while (input.read().let { it >= 0 && it != '\n'.code }) Unit
                return ""
            }
            line.append(next.toChar())
        }
    }
}

private class ShellClipboardAccess(manager: ClipboardManager) {
    private val service = ClipboardManager::class.java.getDeclaredField("mService")
        .apply { isAccessible = true }.get(manager)
    private val stringType = String::class.java
    private val intType = Int::class.javaPrimitiveType!!
    private val booleanType = Boolean::class.javaPrimitiveType!!
    private val clipType = ClipData::class.java
    private val getMethod = findMethod("getPrimaryClip", listOf(
        listOf(stringType, intType),
        listOf(stringType, stringType, intType),
        listOf(stringType, stringType, intType, intType),
        listOf(stringType, stringType, stringType, stringType, intType, intType, booleanType),
    ))
    private val setMethod = findMethod("setPrimaryClip", listOf(
        listOf(clipType, stringType, intType),
        listOf(clipType, stringType, stringType, intType),
        listOf(clipType, stringType, stringType, intType, intType),
        listOf(clipType, stringType, stringType, intType, intType, booleanType),
    ))

    // Some OEM managers query a provider with system attribution before reading; use the shell Binder directly.
    fun getClip(): ClipData? = getMethod.invoke(service, *arguments(getMethod, null)) as ClipData?
    fun setClip(clip: ClipData) { setMethod.invoke(service, *arguments(setMethod, clip)) }

    private fun findMethod(name: String, signatures: List<List<Class<*>>>) = service.javaClass.methods
        .singleOrNull { it.name == name && it.parameterTypes.toList() in signatures }
        ?: throw UnsupportedOperationException("Unsupported clipboard Binder signature")

    private fun arguments(method: java.lang.reflect.Method, clip: ClipData?): Array<Any?> {
        var packageAssigned = false
        return method.parameterTypes.map { type ->
            when (type) {
                clipType -> clip
                stringType -> if (!packageAssigned) { packageAssigned = true; "com.android.shell" } else null
                intType -> 0
                booleanType -> false
                else -> error("Unsupported clipboard Binder parameter")
            }
        }.toTypedArray()
    }
}

private open class ShellClipboardContext(base: Context) : ContextWrapper(base) {
    override fun getPackageName() = "com.android.shell"
    override fun getOpPackageName() = "com.android.shell"
    override fun getAttributionTag(): String? = null
    override fun getDeviceId() = 0
}

@android.annotation.TargetApi(31)
private class ShellClipboardContext31(base: Context) : ShellClipboardContext(base) {
    override fun getAttributionSource(): AttributionSource = AttributionSource.Builder(android.os.Process.myUid())
        .setPackageName("com.android.shell").build()
}
