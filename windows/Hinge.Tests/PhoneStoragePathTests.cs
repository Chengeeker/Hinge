using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public sealed class PhoneStoragePathTests
{
    [Theory]
    [InlineData("Download/notes/a.txt", "/storage/emulated/0/Download/notes/a.txt")]
    [InlineData("DCIM/Camera/test.jpg", "/storage/emulated/0/DCIM/Camera/test.jpg")]
    [InlineData("/storage/emulated/0/Documents/file.pdf", "/storage/emulated/0/Documents/file.pdf")]
    public void RelativePathIsFullWhenDirectoryIsKnown(string relative, string expected)
    {
        Assert.Equal(expected, PhoneStoragePath.Resolve(relative));
    }

    [Fact]
    public void RealMediaStoreDataOverridesGuessedRelativePath()
    {
        Assert.Equal("/storage/1C10-130F/private/photo.jpg",
            PhoneStoragePath.Resolve("DCIM/photo.jpg", "/storage/1C10-130F/private/photo.jpg"));
    }

    [Fact]
    public void OrphanedNameOrTraversalMustNeverBePresentedAsVerifiedPath()
    {
        Assert.Null(PhoneStoragePath.Resolve("photo.jpg"));
        Assert.Null(PhoneStoragePath.Resolve("Download/../private/file.jpg"));
        Assert.Null(PhoneStoragePath.Resolve("", ""));
        Assert.Equal("/storage/emulated/0/Download",
            PhoneStoragePath.Directory("Download/photo.jpg"));
    }
}
