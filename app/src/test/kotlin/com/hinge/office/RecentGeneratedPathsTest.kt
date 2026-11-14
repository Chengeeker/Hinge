package com.hinge.office

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RecentGeneratedPathsTest {
    @Test fun confirmedOppoGeneratedDirectoriesAreExcluded() {
        val cases = listOf(
            ".cache.bin" to "/storage/emulated/0/oua_classifier/.cache.bin",
            "aiboost_gpu_cache_123.bin" to "oua_classifier/aiboost_gpu_cache_123.bin",
            ".portal_background.png" to "Download/.content_portal/.portal_background.png",
            "OPLUSDRAG_abc.png" to "Download/.OplusDragAndDrop/com.tencent.mm/OPLUSDRAG_abc.png",
            ".1794467.jpg" to "/storage/emulated/0/.MediaTrash/.mediaTrash/.1794467.jpg"
        )
        for ((name, path) in cases) assertTrue(path, RecentGeneratedPaths.isGenerated(name, path))
    }

    @Test fun confirmedVivoGeneratedDirectoriesAreExcluded() {
        val cases = listOf(
            "PictureUnlock_s_123_user0.pictureunlock.jpg" to ".yuetu_unlock/PictureUnlock_s_123_user0.pictureunlock.jpg",
            "ad1022178739bc99e4cf751589e89c53.0" to "/storage/emulated/0/.yuetu_unlock/vivo_ad/real/ad1022178739bc99e4cf751589e89c53.0",
            "-1978127994.0" to "/sdcard/.vivoBrowser/img/-1978127994.0",
            "10003.png" to "Download/.TempDragFile/com.bbk.theme/sticker/10003.png",
            "log.txt" to "Download/vivo办公套件/log.txt"
        )
        for ((name, path) in cases) assertTrue(path, RecentGeneratedPaths.isGenerated(name, path))
    }

    @Test fun doNotHideUserBackupsOrAmbiguousPaths() {
        val cases = listOf(
            "backup.zip" to "Download/阅读备份/backup.zip",
            "YiYu_v8.8.1_auto.penny" to "Documents/YiYuJiZhang/YiYu_v8.8.1_auto.penny",
            "sample_0_20.webp" to "Pictures/.2041841469/sample_0_20.webp",
            "privacy.db" to ".系统文件，请勿删/privacy.db",
            "log.txt" to "Documents/Project/log.txt",
            "aiboost_gpu_cache_123.bin" to "Download/aiboost_gpu_cache_123.bin",
            "portrait.png" to "Download/Album/portrait.png",
            "OPLUSDRAG_saved.png" to "Pictures/OPLUSDRAG_saved.png"
        )
        for ((name, path) in cases) assertFalse(path, RecentGeneratedPaths.isGenerated(name, path))
    }

    @Test fun folderBoundariesAndWindowsSeparatorsAreHandled() {
        assertTrue(RecentGeneratedPaths.isGenerated(
            "10003.png", "STORAGE\\EMULATED\\0\\DOWNLOAD\\.TEMPDRAGFILE\\10003.png"))
        assertFalse(RecentGeneratedPaths.isGenerated(
            "10003.png", "Download/Folder/.TempDragFile/10003.png"))
        assertFalse(RecentGeneratedPaths.isGenerated(
            "photo.jpg", "Download/.content_portal_backup/photo.jpg"))
    }
}
