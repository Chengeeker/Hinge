using Hinge.App;

namespace Hinge.Tests;

public sealed class RecentFileFilterRulesTests
{
    [Theory]
    [InlineData("report.jpg", "DCIM/Camera/report.jpg", "image/jpeg", 1024 * 1024)]
    [InlineData("report.pdf", "Download/report.pdf", "application/pdf", 10 * 1024)]
    [InlineData("notes.txt", "Documents/notes.txt", "text/plain", 32 * 1024)]
    public void WeakSignalsDoNotHideLegitimateUserFiles(
        string name,
        string path,
        string mimeType,
        long sizeBytes)
    {
        Assert.False(RecentFileFilterRules.ShouldHide(name, path, mimeType, sizeBytes));
    }

    [Theory]
    [InlineData("preview.jpg", "/storage/emulated/0/Android/data/com.example/cache/preview.jpg", "image/jpeg", 1024 * 1024)]
    [InlineData(".nomedia", "DCIM/.nomedia", "application/octet-stream", 0)]
    [InlineData("thumbdata3-176350", "/DCIM/.thumbnails/thumbdata3-176350", "application/octet-stream", 12 * 1024)]
    public void StrongCacheEvidenceHidesRegardlessOfMimeOrPublicParent(
        string name,
        string path,
        string mimeType,
        long sizeBytes)
    {
        Assert.True(RecentFileFilterRules.ShouldHide(name, path, mimeType, sizeBytes));
    }

    [Theory]
    [InlineData("cache_123456789012345678901234.jpg", "Android/data/com.example/files", "application/octet-stream", 1024)]
    [InlineData("temporary.tmp", "cache/logs", "application/octet-stream", 4096)]
    [InlineData("8f14e45fceea167a5a36dedd4bea2543", "Android/data/com.example/cache", "application/octet-stream", 32)]
    public void CombinedWeakSignalsCanReachHideThreshold(
        string name,
        string path,
        string mimeType,
        long sizeBytes)
    {
        Assert.True(RecentFileFilterRules.ShouldHide(name, path, mimeType, sizeBytes));
        Assert.True(RecentFileFilterRules.GetSuspicionScore(name, path, mimeType, sizeBytes) >=
            RecentFileFilterRules.HideThreshold);
    }

    [Fact]
    public void ExtensionlessNameIsHardFilteredRegardlessOfSize()
    {
        Assert.Equal(10, RecentFileFilterRules.GetSuspicionScore(
            "meeting-notes", "Misc/meeting-notes", "application/octet-stream", 128 * 1024));
        Assert.True(RecentFileFilterRules.ShouldHide(
            "meeting-notes", "Misc/meeting-notes", "application/octet-stream", 128 * 1024));
    }

    [Fact]
    public void FilesSmallerThanTenKiBAreHardFilteredRegardlessOfExtension()
    {
        Assert.Equal(10, RecentFileFilterRules.GetSuspicionScore(
            "tiny.bin", "Misc/tiny.bin", "application/octet-stream", 1024));
        Assert.True(RecentFileFilterRules.ShouldHide(
            "tiny.bin", "Misc/tiny.bin", "application/octet-stream", 1024));
        Assert.True(RecentFileFilterRules.ShouldHide(
            "tiny.jpg", "Pictures/tiny.jpg", "image/jpeg", 1));
    }

    [Fact]
    public void ExactlyTenKiBIsNotHiddenByTheSizeRule()
    {
        Assert.False(RecentFileFilterRules.ShouldHide(
            "report.pdf", "Documents/report.pdf", "application/pdf", 10 * 1024));
    }

    [Fact]
    public void CacheExtensionAloneDoesNotOverrideRecognizedMimeAndUserFolder()
    {
        Assert.Equal(10, RecentFileFilterRules.GetSuspicionScore(
            "draft.tmp", "Documents/draft.tmp", "text/plain", 100 * 1024));
        Assert.False(RecentFileFilterRules.ShouldHide(
            "draft.tmp", "Documents/draft.tmp", "text/plain", 100 * 1024));
    }

    [Theory]
    [InlineData("photo_temp_123.jpg")]
    [InlineData("PhotoTemp123.jpg")]
    [InlineData("temporary-Photo.jpg")]
    public void TempNameMarkersHideEvenWithKnownMimeAndPublicFolder(string name)
    {
        Assert.True(RecentFileFilterRules.ShouldHide(
            name, $"Downloads/{name}", "image/jpeg", 128 * 1024));
    }

    [Theory]
    [InlineData("template.docx")]
    [InlineData("attempt-report.pdf")]
    public void TempInsideAnotherWordDoesNotHideUserFiles(string name)
    {
        Assert.False(RecentFileFilterRules.ShouldHide(
            name, $"Documents/{name}", "application/pdf", 128 * 1024));
    }
}
