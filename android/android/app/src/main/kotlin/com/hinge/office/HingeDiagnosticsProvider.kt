package com.hinge.office

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import java.io.FileNotFoundException
import java.io.OutputStream
import java.nio.charset.StandardCharsets
import kotlin.concurrent.thread

class HingeDiagnosticsProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        if (mode != "r" || uri.lastPathSegment != "connection") {
            throw FileNotFoundException("Unsupported diagnostics path")
        }

        val appContext = context?.applicationContext
            ?: throw FileNotFoundException("Application context unavailable")
        val (reader, writer) = ParcelFileDescriptor.createPipe()
        thread(name = "hinge-diagnostics-export", isDaemon = true) {
            ParcelFileDescriptor.AutoCloseOutputStream(writer).use { output: OutputStream ->
                val log = HingeDiagnostics.from(appContext).read()
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
    ): Cursor? = null

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
