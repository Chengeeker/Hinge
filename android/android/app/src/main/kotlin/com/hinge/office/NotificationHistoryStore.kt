package com.hinge.office

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper

/**
 * Local notification history. The database is deliberately private to Hinge;
 * notification text is not written to shared storage or logs.
 */
class NotificationHistoryStore(context: Context) :
    SQLiteOpenHelper(context.applicationContext, DATABASE_NAME, null, DATABASE_VERSION) {

    data class Record(
        val id: String,
        val packageName: String,
        val appName: String,
        val title: String,
        val content: String,
        val timestamp: Long,
        val category: String,
        val ongoing: Boolean,
        val notificationKey: String,
    )

    data class ApplicationSummary(
        val packageName: String,
        val appName: String,
        val count: Int,
    )

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL(
            """
            CREATE TABLE $TABLE_NOTIFICATIONS (
                $COLUMN_ID TEXT PRIMARY KEY NOT NULL,
                $COLUMN_PACKAGE_NAME TEXT NOT NULL,
                $COLUMN_APP_NAME TEXT NOT NULL,
                $COLUMN_TITLE TEXT NOT NULL,
                $COLUMN_CONTENT TEXT NOT NULL,
                $COLUMN_TIMESTAMP INTEGER NOT NULL,
                $COLUMN_CATEGORY TEXT NOT NULL,
                $COLUMN_ONGOING INTEGER NOT NULL,
                $COLUMN_NOTIFICATION_KEY TEXT NOT NULL
            )
            """.trimIndent(),
        )
        db.execSQL(
            "CREATE INDEX notification_history_timestamp_idx " +
                "ON $TABLE_NOTIFICATIONS ($COLUMN_TIMESTAMP)",
        )
        db.execSQL(
            "CREATE INDEX notification_history_package_idx " +
                "ON $TABLE_NOTIFICATIONS ($COLUMN_PACKAGE_NAME)",
        )
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        // Version 1 is the initial schema. Keep this method explicit so a
        // future schema change does not silently delete a user's history.
    }

    fun upsert(record: Record): Boolean {
        val values = ContentValues().apply {
            put(COLUMN_ID, record.id)
            put(COLUMN_PACKAGE_NAME, record.packageName)
            put(COLUMN_APP_NAME, record.appName)
            put(COLUMN_TITLE, record.title)
            put(COLUMN_CONTENT, record.content)
            put(COLUMN_TIMESTAMP, record.timestamp)
            put(COLUMN_CATEGORY, record.category)
            put(COLUMN_ONGOING, if (record.ongoing) 1 else 0)
            put(COLUMN_NOTIFICATION_KEY, record.notificationKey)
        }
        return writableDatabase.insertWithOnConflict(
            TABLE_NOTIFICATIONS,
            null,
            values,
            SQLiteDatabase.CONFLICT_REPLACE,
        ) != -1L
    }

    fun count(packageName: String? = null): Int {
        val selection = packageName?.takeIf { it.isNotBlank() }?.let { "$COLUMN_PACKAGE_NAME = ?" }
        val args = packageName?.takeIf { it.isNotBlank() }?.let { arrayOf(it) }
        readableDatabase.rawQuery(
            "SELECT COUNT(*) FROM $TABLE_NOTIFICATIONS" +
                if (selection == null) "" else " WHERE $selection",
            args,
        ).use { cursor ->
            return if (cursor.moveToFirst()) cursor.getInt(0) else 0
        }
    }

    fun query(
        offset: Int,
        limit: Int,
        ascending: Boolean,
        packageName: String? = null,
    ): List<Record> {
        val safeOffset = offset.coerceAtLeast(0)
        val safeLimit = limit.coerceIn(1, 200)
        val selection = packageName?.takeIf { it.isNotBlank() }?.let { "$COLUMN_PACKAGE_NAME = ?" }
        val args = packageName?.takeIf { it.isNotBlank() }?.let { arrayOf(it) }
        val order = if (ascending) "ASC" else "DESC"
        val result = ArrayList<Record>(safeLimit)
        readableDatabase.query(
            TABLE_NOTIFICATIONS,
            COLUMNS,
            selection,
            args,
            null,
            null,
            "$COLUMN_TIMESTAMP $order, $COLUMN_ID $order",
            "$safeLimit OFFSET $safeOffset",
        ).use { cursor ->
            val id = cursor.getColumnIndexOrThrow(COLUMN_ID)
            val packageIndex = cursor.getColumnIndexOrThrow(COLUMN_PACKAGE_NAME)
            val appName = cursor.getColumnIndexOrThrow(COLUMN_APP_NAME)
            val title = cursor.getColumnIndexOrThrow(COLUMN_TITLE)
            val content = cursor.getColumnIndexOrThrow(COLUMN_CONTENT)
            val timestamp = cursor.getColumnIndexOrThrow(COLUMN_TIMESTAMP)
            val category = cursor.getColumnIndexOrThrow(COLUMN_CATEGORY)
            val ongoing = cursor.getColumnIndexOrThrow(COLUMN_ONGOING)
            val notificationKey = cursor.getColumnIndexOrThrow(COLUMN_NOTIFICATION_KEY)
            while (cursor.moveToNext()) {
                result += Record(
                    id = cursor.getString(id),
                    packageName = cursor.getString(packageIndex),
                    appName = cursor.getString(appName),
                    title = cursor.getString(title),
                    content = cursor.getString(content),
                    timestamp = cursor.getLong(timestamp),
                    category = cursor.getString(category),
                    ongoing = cursor.getInt(ongoing) != 0,
                    notificationKey = cursor.getString(notificationKey),
                )
            }
        }
        return result
    }

    fun applications(): List<ApplicationSummary> {
        val result = ArrayList<ApplicationSummary>()
        readableDatabase.query(
            TABLE_NOTIFICATIONS,
            arrayOf(COLUMN_PACKAGE_NAME, COLUMN_APP_NAME, "COUNT(*) AS item_count"),
            null,
            null,
            "$COLUMN_PACKAGE_NAME, $COLUMN_APP_NAME",
            null,
            "$COLUMN_APP_NAME COLLATE NOCASE ASC",
        ).use { cursor ->
            val packageIndex = cursor.getColumnIndexOrThrow(COLUMN_PACKAGE_NAME)
            val appName = cursor.getColumnIndexOrThrow(COLUMN_APP_NAME)
            val count = cursor.getColumnIndexOrThrow("item_count")
            while (cursor.moveToNext()) {
                result += ApplicationSummary(
                    packageName = cursor.getString(packageIndex),
                    appName = cursor.getString(appName),
                    count = cursor.getInt(count),
                )
            }
        }
        return result
    }

    fun delete(id: String): Boolean {
        val normalizedId = id.trim()
        if (normalizedId.isEmpty()) return false
        return writableDatabase.delete(
            TABLE_NOTIFICATIONS,
            "$COLUMN_ID = ?",
            arrayOf(normalizedId),
        ) > 0
    }

    fun clear(): Int = writableDatabase.delete(TABLE_NOTIFICATIONS, null, null)

    companion object {
        private const val DATABASE_NAME = "notification_history.db"
        private const val DATABASE_VERSION = 1
        private const val TABLE_NOTIFICATIONS = "notifications"
        private const val COLUMN_ID = "id"
        private const val COLUMN_PACKAGE_NAME = "package_name"
        private const val COLUMN_APP_NAME = "app_name"
        private const val COLUMN_TITLE = "title"
        private const val COLUMN_CONTENT = "content"
        private const val COLUMN_TIMESTAMP = "timestamp"
        private const val COLUMN_CATEGORY = "category"
        private const val COLUMN_ONGOING = "ongoing"
        private const val COLUMN_NOTIFICATION_KEY = "notification_key"

        private val COLUMNS = arrayOf(
            COLUMN_ID,
            COLUMN_PACKAGE_NAME,
            COLUMN_APP_NAME,
            COLUMN_TITLE,
            COLUMN_CONTENT,
            COLUMN_TIMESTAMP,
            COLUMN_CATEGORY,
            COLUMN_ONGOING,
            COLUMN_NOTIFICATION_KEY,
        )
    }
}
