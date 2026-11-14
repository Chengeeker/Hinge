using Hinge.App;

namespace Hinge.Tests;

public sealed class RecentVendorGeneratedPathsTests
{
    private const long TypicalSize = 512 * 1024;

    [Theory]
    [InlineData(".cache.bin", "/storage/emulated/0/oua_classifier/.cache.bin", "application/octet-stream")]
    [InlineData("aiboost_gpu_cache_123.bin", "oua_classifier/aiboost_gpu_cache_123.bin", "application/octet-stream")]
    [InlineData(".portal_background.png", "Download/.content_portal/.portal_background.png", "image/png")]
    [InlineData("OPLUSDRAG_abc.png", "Download/.OplusDragAndDrop/com.tencent.mm/OPLUSDRAG_abc.png", "image/png")]
    [InlineData(".1794467.jpg", "/storage/emulated/0/.MediaTrash/.mediaTrash/.1794467.jpg", "image/jpeg")]
    [InlineData("PictureUnlock_s_123_user0.pictureunlock.jpg", ".yuetu_unlock/PictureUnlock_s_123_user0.pictureunlock.jpg", "image/jpeg")]
    [InlineData("ad1022178739bc99e4cf751589e89c53.0", "/storage/emulated/0/.yuetu_unlock/vivo_ad/real/ad1022178739bc99e4cf751589e89c53.0", "application/octet-stream")]
    [InlineData("-1978127994.0", "/sdcard/.vivoBrowser/img/-1978127994.0", "application/octet-stream")]
    [InlineData("10003.png", "Download/.TempDragFile/com.bbk.theme/sticker/10003.png", "image/png")]
    [InlineData("log.txt", "Download/vivo办公套件/log.txt", "text/plain")]
    public void RealOppoAndVivoGeneratedFilesHideByPrecisePath(
        string fileName, string path, string mimeType)
    {
        Assert.True(RecentFileFilterRules.ShouldHide(fileName, path, mimeType, TypicalSize));
        Assert.True(RecentFileFilterRules.GetSuspicionScore(fileName, path, mimeType, TypicalSize)
            >= RecentFileFilterRules.HideThreshold);
    }

    [Theory]
    [InlineData("backup.zip", "Download/阅读备份/backup.zip", "application/zip")]
    [InlineData("YiYu_v8.8.1_auto.penny", "Documents/YiYuJiZhang/YiYu_v8.8.1_auto.penny", "application/octet-stream")]
    [InlineData("sample_0_20.webp", "Pictures/.2041841469/sample_0_20.webp", "image/webp")]
    [InlineData("privacy.db", ".系统文件，请勿删/privacy.db", "application/octet-stream")]
    [InlineData("log.txt", "Documents/Project/log.txt", "text/plain")]
    [InlineData("aiboost_gpu_cache_123.bin", "Download/aiboost_gpu_cache_123.bin", "application/octet-stream")]
    [InlineData("portrait.png", "Download/Album/portrait.png", "image/png")]
    [InlineData("OPLUSDRAG_saved.png", "Pictures/OPLUSDRAG_saved.png", "image/png")]
    public void UserBackupsAndAmbiguousFilesAreNotHidden(
        string fileName, string path, string mimeType)
    {
        Assert.False(RecentFileFilterRules.ShouldHide(fileName, path, mimeType, TypicalSize));
    }

    [Fact]
    public void WindowsSeparatorsAndCaseVariationsMatchTheDirectory()
    {
        Assert.True(RecentFileFilterRules.ShouldHide(
            "10003.png", @"STORAGE\EMULATED\0\DOWNLOAD\.TEMPDRAGFILE\10003.png", "image/png", TypicalSize));
        Assert.False(RecentFileFilterRules.ShouldHide(
            "10003.png", "Download/Folder/.TempDragFile/10003.png", "image/png", TypicalSize));
    }

    [Fact]
    public void SimilarFolderNamesDoNotCauseFalsePositives()
    {
        Assert.False(RecentFileFilterRules.ShouldHide(
            "photo.jpg", "Download/.content_portal_backup/photo.jpg", "image/jpeg", TypicalSize));
        Assert.False(RecentFileFilterRules.ShouldHide(
            "log.txt", "Download/vivo办公套件备份/log.txt", "text/plain", TypicalSize));
    }
}
