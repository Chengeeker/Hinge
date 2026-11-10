package com.hinge.office

import android.content.ContentResolver
import android.content.Context
import android.net.Uri

internal object ClipboardSyncSettings {
    const val KEY = "clipboard_sync_enabled"
    val URI: Uri = Uri.parse("content://com.hinge.office.diagnostics/clipboard-sync")

    // Preserve existing Windows-authorized syncing when upgrading; either end can pause it.
    fun enabled(context: Context): Boolean =
        context.getSharedPreferences("app_settings", Context.MODE_PRIVATE).getBoolean(KEY, true)

    fun allowedByPhone(resolver: ContentResolver): Boolean = try {
        resolver.query(URI, arrayOf("enabled"), null, null, null)?.use {
            it.moveToFirst() && it.getInt(it.getColumnIndexOrThrow("enabled")) == 1
        } ?: false
    } catch (_: Exception) {
        false
    }
}
