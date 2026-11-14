package com.hinge.office

import java.util.Locale

/**
 * Real-device-verified OPPO / vivo generated file directories.
 *
 * Used exclusively by the "recent" file listing. Browsing phone storage
 * must not use this filter, and no file is ever deleted by these rules.
 */
internal object RecentGeneratedPaths {
    fun isGenerated(name: String, path: String): Boolean {
        val fileName = name.trim().replace('\\', '/').substringAfterLast('/')
            .lowercase(Locale.ROOT)
        val segments = path.replace('\\', '/')
            .split('/')
            .filter { it.isNotBlank() }
            .map { it.trim().lowercase(Locale.ROOT) }
            .toMutableList()
        if (segments.lastOrNull() == fileName) segments.removeAt(segments.lastIndex)

        if (segments.any { it in setOf("oua_classifier", ".yuetu_unlock", ".mediatrash") }) {
            return true
        }
        if (adjacent(segments, ".vivobrowser", "img")) return true

        for (download in listOf("download", "downloads")) {
            if (adjacent(segments, download, ".content_portal") ||
                adjacent(segments, download, ".oplusdraganddrop") ||
                adjacent(segments, download, ".tempdragfile")) {
                return true
            }
            // A specific vivo office-suite diagnostic log, not arbitrary
            // user logs or backups in Download.
            if (fileName == "log.txt" && adjacent(segments, download, "vivo办公套件")) {
                return true
            }
        }
        return false
    }

    private fun adjacent(segments: List<String>, parent: String, child: String): Boolean =
        segments.windowed(size = 2).any { it[0] == parent && it[1] == child }
}
