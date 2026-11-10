package com.hinge.office

import java.io.BufferedInputStream
import java.io.InputStream
import java.util.Locale

/**
 * Small, dependency-free MIME detector for the file manager.
 *
 * MediaProvider metadata is not consistent across OEM devices. The detector
 * therefore uses the filename for normal listings and only inspects a small
 * header when a caller explicitly asks for metadata. It never reads the whole
 * file while building a directory listing.
 */
object HingeMimeDetector {
    private val documentExtensions = setOf(
        "pdf", "doc", "docx", "docm", "dot", "dotx", "odt",
        "xls", "xlsx", "xlsm", "xlt", "xltx", "ods",
        "ppt", "pptx", "pptm", "pps", "ppsx", "odp",
        "txt", "md", "csv", "tsv", "rtf", "json", "xml", "html", "htm",
    )

    private val knownMimeTypes = mapOf(
        "pdf" to "application/pdf",
        "doc" to "application/msword",
        "docx" to "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "docm" to "application/vnd.ms-word.document.macroenabled.12",
        "dot" to "application/msword",
        "dotx" to "application/vnd.openxmlformats-officedocument.wordprocessingml.template",
        "xls" to "application/vnd.ms-excel",
        "xlsx" to "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "xlsm" to "application/vnd.ms-excel.sheet.macroenabled.12",
        "xlt" to "application/vnd.ms-excel",
        "xltx" to "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
        "ppt" to "application/vnd.ms-powerpoint",
        "pptx" to "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "pptm" to "application/vnd.ms-powerpoint.presentation.macroenabled.12",
        "pps" to "application/vnd.ms-powerpoint",
        "ppsx" to "application/vnd.openxmlformats-officedocument.presentationml.slideshow",
        "zip" to "application/zip",
        "rar" to "application/vnd.rar",
        "7z" to "application/x-7z-compressed",
        "gz" to "application/gzip",
        "apk" to "application/vnd.android.package-archive",
        "aab" to "application/octet-stream",
        "mp3" to "audio/mpeg",
        "m4a" to "audio/mp4",
        "flac" to "audio/flac",
        "wav" to "audio/wav",
        "ogg" to "audio/ogg",
        "oga" to "audio/ogg",
        "mp4" to "video/mp4",
        "m4v" to "video/x-m4v",
        "3gp" to "video/3gpp",
        "mov" to "video/quicktime",
        "mkv" to "video/x-matroska",
        "avi" to "video/x-msvideo",
        "webm" to "video/webm",
        "jpg" to "image/jpeg",
        "jpeg" to "image/jpeg",
        "png" to "image/png",
        "gif" to "image/gif",
        "webp" to "image/webp",
        "heic" to "image/heic",
        "heif" to "image/heif",
        "bmp" to "image/bmp",
        "tif" to "image/tiff",
        "tiff" to "image/tiff",
        "svg" to "image/svg+xml",
        "txt" to "text/plain",
        "md" to "text/markdown",
        "csv" to "text/csv",
        "tsv" to "text/tab-separated-values",
        "json" to "application/json",
        "xml" to "application/xml",
        "html" to "text/html",
        "htm" to "text/html",
        "rtf" to "application/rtf",
    )

    fun fromName(name: String): String {
        val extension = extensionOf(name)
        knownMimeTypes[extension]?.let { return it }
        return android.webkit.MimeTypeMap.getSingleton()
            .getMimeTypeFromExtension(extension)
            ?: "application/octet-stream"
    }

    fun resolve(name: String, reportedMimeType: String?): String {
        val reported = reportedMimeType?.trim()?.lowercase(Locale.ROOT).orEmpty()
        if (reported.isNotBlank() &&
            reported != "application/octet-stream" &&
            reported != "binary/octet-stream") {
            return reported
        }
        return fromName(name)
    }

    fun isDocument(name: String, reportedMimeType: String?): Boolean {
        val reported = reportedMimeType?.trim()?.lowercase(Locale.ROOT).orEmpty()
        if (reported.startsWith("image/") ||
            reported.startsWith("video/") ||
            reported.startsWith("audio/")) {
            return false
        }
        if (reported.startsWith("text/") ||
            reported == "application/pdf" ||
            reported.contains("msword") ||
            reported.contains("ms-excel") ||
            reported.contains("ms-powerpoint") ||
            reported.contains("opendocument") ||
            reported.contains("officedocument") ||
            reported == "application/rtf") {
            return true
        }
        return extensionOf(name) in documentExtensions
    }

    /** Detects only the leading bytes and leaves the stream open. */
    fun sniff(name: String, source: InputStream): String {
        val input = if (source is BufferedInputStream) source else BufferedInputStream(source)
        input.mark(64)
        val header = ByteArray(64)
        val count = input.read(header)
        input.reset()
        if (count <= 0) return fromName(name)

        fun byteAt(index: Int): Int = if (index < count) header[index].toInt() and 0xFF else -1
        fun asciiAt(index: Int, value: String): Boolean {
            if (index < 0 || index + value.length > count) return false
            return value.indices.all { header[index + it].toInt() and 0xFF == value[it].code }
        }

        if (byteAt(0) == 0xFF && byteAt(1) == 0xD8 && byteAt(2) == 0xFF) return "image/jpeg"
        if (asciiAt(0, "\u0089PNG")) return "image/png"
        if (asciiAt(0, "GIF87a") || asciiAt(0, "GIF89a")) return "image/gif"
        if (asciiAt(0, "BM")) return "image/bmp"
        if (asciiAt(0, "RIFF") && asciiAt(8, "WEBP")) return "image/webp"
        if (asciiAt(0, "\u001F\u008B")) return "application/gzip"
        if (asciiAt(0, "fLaC")) return "audio/flac"
        if (asciiAt(0, "OggS")) return "audio/ogg"
        if (asciiAt(0, "RIFF") && asciiAt(8, "WAVE")) return "audio/wav"
        if (asciiAt(0, "RIFF") && asciiAt(8, "AVI ")) return "video/x-msvideo"
        if (asciiAt(0, "%PDF-")) return "application/pdf"
        if (byteAt(0) == 0x50 && byteAt(1) == 0x4B &&
            byteAt(2) in setOf(0x03, 0x05, 0x07) &&
            byteAt(3) in setOf(0x04, 0x06, 0x08)) {
            return fromName(name).takeUnless { it == "application/octet-stream" } ?: "application/zip"
        }
        if (asciiAt(0, "Rar!\u001A\u0007")) return "application/vnd.rar"
        if (byteAt(0) == 0x37 && byteAt(1) == 0x7A && byteAt(2) == 0xBC &&
            byteAt(3) == 0xAF && byteAt(4) == 0x27 && byteAt(5) == 0x1C) {
            return "application/x-7z-compressed"
        }
        if (byteAt(0) == 0x1A && byteAt(1) == 0x45 &&
            byteAt(2) == 0xDF && byteAt(3) == 0xA3) {
            return "video/x-matroska"
        }
        if (asciiAt(0, "ID3") ||
            (byteAt(0) == 0xFF && byteAt(1) in setOf(0xF1, 0xF2, 0xF3, 0xFA, 0xFB))) {
            return "audio/mpeg"
        }
        if (asciiAt(4, "ftyp")) {
            val brand = if (count >= 12) {
                String(header, 8, 4, Charsets.US_ASCII).lowercase(Locale.ROOT)
            } else {
                ""
            }
            return if (brand == "m4a " || brand == "m4b ") "audio/mp4"
            else if (brand == "qt  ") "video/quicktime"
            else if (brand.startsWith("3gp")) "video/3gpp"
            else "video/mp4"
        }
        return fromName(name)
    }

    private fun extensionOf(name: String): String {
        val cleanName = name.substringBefore('?').substringBefore('#')
        return cleanName.substringAfterLast('.', "").lowercase(Locale.ROOT)
    }
}
