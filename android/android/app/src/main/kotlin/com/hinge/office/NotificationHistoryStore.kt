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
        val isVerificationCode: Boolean = false,
        val verificationCode: String? = null,
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
                $COLUMN_NOTIFICATION_KEY TEXT NOT NULL,
                $COLUMN_IS_VERIFICATION_CODE INTEGER NOT NULL DEFAULT 0,
                $COLUMN_VERIFICATION_CODE TEXT
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
        if (oldVersion < 2) {
            // Some 1.x builds shipped the v2 columns while the database
            // version was still incorrectly left at 1. Check each column so
            // upgrading either schema is safe and repeatable.
            if (!hasColumn(db, COLUMN_IS_VERIFICATION_CODE)) {
                db.execSQL(
                    "ALTER TABLE $TABLE_NOTIFICATIONS " +
                        "ADD COLUMN $COLUMN_IS_VERIFICATION_CODE INTEGER NOT NULL DEFAULT 0",
                )
            }
            if (!hasColumn(db, COLUMN_VERIFICATION_CODE)) {
                db.execSQL(
                    "ALTER TABLE $TABLE_NOTIFICATIONS " +
                        "ADD COLUMN $COLUMN_VERIFICATION_CODE TEXT",
                )
            }
        }
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
            put(COLUMN_IS_VERIFICATION_CODE, if (record.isVerificationCode) 1 else 0)
            if (record.verificationCode.isNullOrBlank()) {
                putNull(COLUMN_VERIFICATION_CODE)
            } else {
                put(COLUMN_VERIFICATION_CODE, record.verificationCode)
            }
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
            val isVerificationCode = cursor.getColumnIndexOrThrow(COLUMN_IS_VERIFICATION_CODE)
            val verificationCode = cursor.getColumnIndexOrThrow(COLUMN_VERIFICATION_CODE)
            while (cursor.moveToNext()) {
                val packageValue = cursor.getString(packageIndex)
                val contentValue = cursor.getString(content)
                val storedCode = cursor.getString(verificationCode)?.trim().orEmpty()
                // Records written before verification fields were persisted
                // can still be recovered. The extractor itself is strict:
                // it requires a verification keyword and a standalone code,
                // so ordinary notification numbers do not gain a copy action.
                val recoveredCode = if (storedCode.isNotEmpty()) {
                    storedCode
                } else {
                    VerificationCodeExtractor.find(contentValue).orEmpty()
                }
                result += Record(
                    id = cursor.getString(id),
                    packageName = packageValue,
                    appName = cursor.getString(appName),
                    title = cursor.getString(title),
                    content = contentValue,
                    timestamp = cursor.getLong(timestamp),
                    category = cursor.getString(category),
                    ongoing = cursor.getInt(ongoing) != 0,
                    notificationKey = cursor.getString(notificationKey),
                    isVerificationCode = cursor.getInt(isVerificationCode) != 0 || recoveredCode.isNotEmpty(),
                    verificationCode = recoveredCode.ifEmpty { null },
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
        private const val DATABASE_VERSION = 2
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
        private const val COLUMN_IS_VERIFICATION_CODE = "is_verification_code"
        private const val COLUMN_VERIFICATION_CODE = "verification_code"

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
            COLUMN_IS_VERIFICATION_CODE,
            COLUMN_VERIFICATION_CODE,
        )

        private fun hasColumn(db: SQLiteDatabase, column: String): Boolean {
            db.rawQuery("PRAGMA table_info($TABLE_NOTIFICATIONS)", null).use { cursor ->
                val nameIndex = cursor.getColumnIndex("name")
                if (nameIndex < 0) return false
                while (cursor.moveToNext()) {
                    if (cursor.getString(nameIndex) == column) return true
                }
            }
            return false
        }
    }
}
