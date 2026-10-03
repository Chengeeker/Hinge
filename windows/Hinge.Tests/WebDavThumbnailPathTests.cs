using Hinge.Core;

namespace Hinge.Tests;

public class WebDavThumbnailPathTests
{
    [Fact]
    public void MapsUnicodeNamesRelativeToUrlRootNotInitialDirectory()
    {
        var result = WebDavThumbnailPath.Resolve(new("https://nas.test/dav/"),
            new("https://nas.test/dav/Photos/%E4%B8%AD%E6%96%87%20a.jpg"), "Z:\\");
        Assert.Equal("Z:\\Photos\\中文 a.jpg", result);
    }

    [Theory]
    [InlineData("https://other.test/dav/a.jpg")]
    [InlineData("https://nas.test/dav-other/a.jpg")]
    [InlineData("https://nas.test/dav/a%2fb.jpg")]
    [InlineData("https://nas.test/dav/a%5cb.jpg")]
    [InlineData("https://nas.test/dav/a%3ab.jpg")]
    [InlineData("https://nas.test/dav/a.%20/b.jpg")]
    public void RejectsOtherEndpointsAndUnsafeWindowsNames(string uri)
    {
        Assert.Null(WebDavThumbnailPath.Resolve(new("https://nas.test/dav/"), new(uri), "Z:\\Cloud"));
    }

    [Fact]
    public void OldProfilesDefaultToNetworkPreview()
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize<WebDavProfile>("{\"Url\":\"https://nas.test/dav/\"}")!;
        Assert.Equal("", profile.ThumbnailMountPath);
        Assert.Null(WebDavThumbnailPath.Resolve(new(profile.Url), new(profile.Url + "a.jpg"), profile.ThumbnailMountPath));
    }

    [Fact]
    public void RejectsRelativeMountPath()
    {
        Assert.Throws<ArgumentException>(() => WebDavThumbnailPath.Resolve(new("https://nas.test/dav/"),
            new("https://nas.test/dav/a.jpg"), "Cloud"));
    }
}
