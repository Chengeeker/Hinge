using System.Net;
using Hinge.Core;

namespace Hinge.Tests;

public sealed class WebDavRedirectTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Redirect(int status, string location)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    [Theory]
    [InlineData(301, false)]
    [InlineData(302, false)]
    [InlineData(303, false)]
    [InlineData(307, false)]
    [InlineData(308, false)]
    [InlineData(302, true)]
    public async Task SignedCdnDownloadUsesSeparateAnonymousConnection(int status, bool thumbnail)
    {
        int authenticatedRequests = 0, anonymousRequests = 0;
        using var authenticated = new Handler(request =>
        {
            authenticatedRequests++;
            Assert.Equal("dav.example.test", request.RequestUri!.Host);
            return Redirect(status, "https://cdn.example.test/file.jpg?signature=test");
        });
        using var anonymous = new Handler(request =>
        {
            anonymousRequests++;
            Assert.Equal("cdn.example.test", request.RequestUri!.Host);
            Assert.Equal("?signature=test", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Equal("Hinge-WebDAV/1.0", request.Headers.UserAgent.ToString());
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        using var client = new WebDavBrowserClient(new WebDavProfile { Url = "https://dav.example.test/", UserAgent = "Private-UA", Username = "test" }, "test-password", authenticated, anonymous);
        var entry = new WebDavEntry("file.jpg", client.Child(client.Root, "file.jpg"), false, 3, null);
        var path = Path.Combine(Path.GetTempPath(), "hinge-redirect-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (thumbnail) await client.DownloadThumbnailSourceAsync(entry, path, 3, default);
            else await client.DownloadAsync(entry, path, null, default);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Equal(1, authenticatedRequests);
            Assert.Equal(1, anonymousRequests);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("http://cdn.example.test/file")]
    [InlineData("https://user:pass@cdn.example.test/file")]
    [InlineData("file:///C:/file")]
    [InlineData("/file.jpg")]
    public async Task UnsafeRedirectOrLoopDoesNotPublishFile(string location)
    {
        using var initial = new Handler(_ => Redirect(302, location));
        using var anonymous = new Handler(_ => throw new Exception("Unsafe target requested"));
        using var client = new WebDavBrowserClient(new WebDavProfile { Url = "https://dav.example.test/" }, "", initial, anonymous);
        var path = Path.Combine(Path.GetTempPath(), "hinge-redirect-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(
            new("file.jpg", client.Child(client.Root, "file.jpg"), false, 3, null), path, null, default));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RedirectChainHasFiniteLimit()
    {
        int calls = 0;
        using var handler = new Handler(_ => Redirect(302, "/file-" + ++calls));
        using var client = new WebDavBrowserClient(new WebDavProfile { Url = "https://dav.example.test/" }, "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-redirect-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(
            new("file.jpg", client.Child(client.Root, "file.jpg"), false, 3, null), path, null, default));
        Assert.Equal(6, calls);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RelativeRedirectWorksWithoutChangingDavDirectory()
    {
        using var handler = new Handler(request => request.RequestUri!.AbsolutePath == "/dav/file.jpg"
            ? Redirect(302, "resolved.jpg") : new(HttpStatusCode.OK) { Content = new ByteArrayContent([7]) });
        using var client = new WebDavBrowserClient(new WebDavProfile { Url = "https://dav.example.test/dav/" }, "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-redirect-" + Guid.NewGuid().ToString("N"));
        try
        {
            await client.DownloadAsync(new("file.jpg", client.Child(client.Root, "file.jpg"), false, 1, null), path, null, default);
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(path));
            Assert.Equal("/dav/", client.Root.AbsolutePath);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
