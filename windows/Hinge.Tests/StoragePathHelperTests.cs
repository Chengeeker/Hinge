using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class StoragePathHelperTests
{
    [Theory]
    [InlineData(null, "Download/Hinge")]
    [InlineData("", "Download/Hinge")]
    [InlineData("   ", "Download/Hinge")]
    [InlineData("Download", "Download")]
    [InlineData("/Download/", "Download")]
    [InlineData("\\Download\\", "Download")]
    [InlineData("Download/Test", "Download/Test")]
    [InlineData("/Download/Test/", "Download/Test")]
    [InlineData("Download\\SubFolder\\Nested", "Download/SubFolder/Nested")]
    [InlineData("/storage/emulated/0/", "Download/Hinge")]
    [InlineData("/storage/emulated/0/Download", "Download")]
    [InlineData("/storage/emulated/0/Download/Test/", "Download/Test")]
    [InlineData("storage/emulated/0/DCIM/Camera", "DCIM/Camera")]
    public void NormalizeFolderPath_HandlesVariousFormats(string? input, string expected)
    {
        var result = StoragePathHelper.NormalizeFolderPath(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Download", "Download")]
    [InlineData("Download/Test", "Test")]
    [InlineData("Download/Test/Sub", "Sub")]
    [InlineData("/storage/emulated/0/Documents/Work/", "Work")]
    [InlineData("", "Hinge")]
    public void GetDisplayFolderName_ReturnsLastSegment(string input, string expected)
    {
        var result = StoragePathHelper.GetDisplayFolderName(input);
        Assert.Equal(expected, result);
    }
}
