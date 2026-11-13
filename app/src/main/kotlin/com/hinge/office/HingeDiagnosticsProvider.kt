package com.hinge.office

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.Binder
import android.os.Bundle
import android.os.ParcelFileDescriptor
import java.io.FileNotFoundException
import java.io.OutputStream
import java.nio.charset.StandardCharsets
import kotlin.concurrent.thread

class HingeDiagnosticsProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun call(method: String, arg: String?, extras: Bundle?): Bundle? {
        if (method != "adb_wake") return super.call(method, arg, extras)
        val appContext = requireNotNull(context)
        if (Binder.getCallingUid() != 2000) throw SecurityException("ADB shell required")
        appContext.enforceCallingPermission(android.Manifest.permission.DUMP, "ADB shell required")
        val ids = arg.orEmpty().split(':', limit = 2)
        val result = when {
            ids.size != 2 || ids.any { it.isBlank() } -> "rejected"
            else -> HingeForegroundService.current?.requestAdbWake(ids[0], ids[1]) ?: "not_running"
        }
        return Bundle().apply { putString("hinge_wake", result) }
    }

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        if (mode != "r" || uri.lastPathSegment != "connection") {
            throw FileNotFoundException("Unsupported diagnostics path")
        }

        val appContext = context?.applicationContext
            ?: throw FileNotFoundException("Application context unavailable")
        val (reader, writer) = ParcelFileDescriptor.createPipe()
        thread(name = "hinge-diagnostics-export", isDaemon = true) {
            ParcelFileDescriptor.AutoCloseOutputStream(writer).use { output: OutputStream ->
                val log = HingeForegroundService.current?.connectionBroker()?.readDiagnostics()
                    ?: HingeDiagnostics.from(appContext).read()
                output.write(log.toByteArray(StandardCharsets.UTF_8))
            }
        }
        return reader
    }

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?,
    ): Cursor? {
        if (uri != ClipboardSyncSettings.URI) return null
        val appContext = context ?: return null
        return android.database.MatrixCursor(arrayOf("enabled")).apply {
            addRow(arrayOf(if (ClipboardSyncSettings.enabled(appContext)) 1 else 0))
        }
    }

    override fun getType(uri: Uri): String? = "text/plain"

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<out String>?,
    ): Int = 0
}
