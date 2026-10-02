using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Hinge.Core;

namespace Hinge.Tests;

public sealed class WebDavTests
{
    [Fact]
    public void RuntimeBrowserTemplatesHaveValidXmlAndDeclaredPrefixes()
    {
        using var stream = typeof(WebDavTests).Assembly.GetManifestResourceStream("WebDavBrowserView.Source.cs")!;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var templates = System.Text.RegularExpressions.Regex.Matches(source, "XamlReader\\.Load\\(\"\"\"(?<xaml>[\\s\\S]*?)\"\"\"\\)");
        Assert.True(templates.Count >= 3, "Runtime template scan must not silently skip the browser templates.");
        foreach (System.Text.RegularExpressions.Match match in templates)
        {
            var document = System.Xml.Linq.XDocument.Parse(match.Groups["xaml"].Value);
            Assert.NotNull(document.Root);
        }
        Assert.Contains("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThumbnailDownloadEnforcesLimitAndPreservesExistingFile(bool contentLengthKnown)
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
        { Content = contentLengthKnown ? new ByteArrayContent(new byte[32]) : new UnknownLengthContent(new byte[32]) });
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-thumb-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAsync<IOException>(() => client.DownloadThumbnailSourceAsync(new("pic.jpg", client.Child(client.Root, "pic.jpg"), false, 0, null), path, 16, default));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.part"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ThumbnailDownloadUsesBoundedAtomicFileAndRejectsOtherHosts()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-thumb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new WebDavEntry("pic.jpg", client.Child(client.Root, "pic.jpg"), false, 3, null);
            await client.DownloadThumbnailSourceAsync(entry, path, 3, default);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAsync<ArgumentException>(() => client.DownloadThumbnailSourceAsync(entry with { Size = 4 }, path, 3, default));
            await Assert.ThrowsAsync<ArgumentException>(() => client.DownloadThumbnailSourceAsync(entry with { Uri = new Uri("https://other.example.test/pic.jpg") }, path, 3, default));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    [Theory]
    [InlineData("name", "a.bin", "b.txt", "c.txt")]
    [InlineData("type", "a.bin", "b.txt", "c.txt")]
    [InlineData("sizeAsc", "b.txt", "c.txt", "a.bin")]
    [InlineData("sizeDesc", "a.bin", "c.txt", "b.txt")]
    [InlineData("timeAsc", "c.txt", "b.txt", "a.bin")]
    [InlineData("timeDesc", "b.txt", "c.txt", "a.bin")]
    public void BrowserSortKeepsDirectoriesFirstAndUnknownDatesLast(string mode, string first, string second, string third)
    {
        var root = new Uri("https://dav.example.test/");
        WebDavEntry Entry(string name, long size, DateTime? date, bool folder = false) => new(name, new Uri(root, name), folder, size, date);
        var entries = new[] { Entry("b.txt", 1, new DateTime(2026, 10, 2)), Entry("z-folder", 0, null, true), Entry("a.bin", 3, null), Entry("c.txt", 2, new DateTime(2026, 10, 1)) };
        Assert.Equal(new[] { "z-folder", first, second, third }, WebDavEntrySorting.Sort(entries, mode).Select(entry => entry.Name));
        Assert.Equal("b.txt", entries[0].Name);
    }

    [Fact]
    public void BrowserMetadataHasSeparateDateTypeAndSizeLabels()
    {
        var file = new WebDavEntry("example.APK", new Uri("https://dav.example.test/example.APK"), false, 1048576, null);
        Assert.Equal("APK 文件", file.TypeLabel);
        Assert.Equal("—", file.ModifiedLabel);
        Assert.Contains("1", file.FileSizeLabel);
        var date = new DateTime(2026, 10, 2, 12, 34, 0, DateTimeKind.Utc);
        Assert.Equal(date.ToLocalTime().ToString("yyyy/MM/dd HH:mm"), (file with { Modified = date }).ModifiedLabel);
        Assert.Equal("文件夹", (file with { IsDirectory = true }).TypeLabel);
        Assert.Equal("—", (file with { IsDirectory = true }).FileSizeLabel);
        Assert.Equal("文件", (file with { Name = "README" }).TypeLabel);
    }

    [Fact]
    public void MultipleAddressesRetainIndependentSettingsAndStableIds()
    {
        var first = Profile() with { Id = "first", Remark = "NAS A", Username = "a", ProtectedPassword = "cipher-a", UserAgent = "agent-a", InitialPath = "one", ParallelTransfers = true };
        var second = Profile() with { Id = "second", Remark = "NAS B", Username = "b", ProtectedPassword = "cipher-b", UserAgent = "agent-b", InitialPath = "two", TrustAllCertificates = true };
        var state = new Hinge.App.WebDavSettingsState { Profiles = [first, second], TabOrder = ["second", "phone", "first"] };
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Hinge.App.WebDavSettingsState>(json)!;
        Assert.Equal(first, restored.Profiles[0]);
        Assert.Equal(second, restored.Profiles[1]);
        Assert.Equal(state.TabOrder, restored.TabOrder);
        var edited = restored.Profiles[0] with { Remark = "renamed", InitialPath = "changed" };
        restored.Profiles[0] = edited;
        Assert.Equal("first", edited.Id);
        Assert.Equal(second, restored.Profiles[1]);
        var fresh = new WebDavProfile();
        Assert.NotEqual(first.Id, fresh.Id);
        Assert.NotEqual(second.Id, fresh.Id);
        Assert.Empty(fresh.Username);
        Assert.Empty(fresh.ProtectedPassword);
        Assert.False(fresh.TrustAllCertificates);
        Assert.False(fresh.ParallelTransfers);
    }

    private static WebDavProfile Profile(bool parallel = false) => new()
    { Url = "https://dav.example.test/webdav/", ParallelTransfers = parallel };

    [Theory]
    [InlineData("ftp://host/dav/")]
    [InlineData("https://user:password@host/dav/")]
    [InlineData("https://host/dav/?token=secret")]
    [InlineData("https://host/dav/#fragment")]
    public void InvalidEndpointIsRejected(string url) => Assert.Throws<ArgumentException>(() => WebDavBrowserClient.ValidateEndpoint(url));

    [Fact]
    public void UnicodeAndReservedCharactersAreEncodedOnce()
    {
        using var client = new WebDavBrowserClient(Profile() with { InitialPath = "/资料/工作 空间/" }, "");
        Assert.Equal("https://dav.example.test/webdav/%E8%B5%84%E6%96%99/%E5%B7%A5%E4%BD%9C%20%E7%A9%BA%E9%97%B4/", client.InitialDirectory.AbsoluteUri);
        Assert.EndsWith("a%23b%25c.txt", client.Child(client.InitialDirectory, "a#b%c.txt").AbsoluteUri);
        Assert.Null(client.Parent(client.Root));
        Assert.Equal(client.Root, client.Parent(client.BuildDirectory("a")));
    }

    [Fact]
    public void TraversalAndUserAgentInjectionAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new WebDavBrowserClient(Profile() with { InitialPath = "../escape" }, ""));
        Assert.Throws<ArgumentException>(() => new WebDavBrowserClient(Profile() with { UserAgent = "test\r\nX-Injected: 1" }, ""));
        using var client = new WebDavBrowserClient(Profile(), "");
        Assert.Throws<ArgumentException>(() => client.Child(client.Root, "../escape"));
        Assert.False(client.Contains(new Uri("https://other.example.test/webdav/file")));
        Assert.False(client.Contains(new Uri("https://dav.example.test/webdav-other/file")));
        Assert.True(client.Contains(new Uri("https://dav.example.test/webdav")));
    }

    [Fact]
    public void DisplayNameUsesRemarkAndFallsBackToHost()
    {
        Assert.Equal("dav.example.test", Profile().DisplayName);
        Assert.Equal("家庭 NAS", (Profile() with { Remark = "家庭 NAS" }).DisplayName);
        Assert.False(Profile().TrustAllCertificates);
        Assert.False(Profile().ParallelTransfers);
    }

    [Fact]
    public async Task ListingSkipsSelfAndNestedResources()
    {
        var xml = """
        <d:multistatus xmlns:d="DAV:">
          <d:response><d:href>/webdav/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
          <d:response><d:href>/webdav/folder/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
          <d:response><d:href>/webdav/file%20one.txt</d:href><d:propstat><d:prop><d:resourcetype/><d:getcontentlength>12</d:getcontentlength></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
          <d:response><d:href>/webdav/folder/nested.txt</d:href><d:propstat><d:prop><d:resourcetype/></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
        </d:multistatus>
        """;
        using var handler = new Handler(request =>
        {
            Assert.Equal("PROPFIND", request.Method.Method);
            Assert.Equal("1", request.Headers.GetValues("Depth").Single());
            return new((HttpStatusCode)207) { Content = new StringContent(xml) };
        });
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        var entries = await client.ListAsync(client.Root);
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("file one.txt", entries[1].Name);
        Assert.Equal(12, entries[1].Size);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadsPreserveBytesAndFallbackWhenRangesAreUnsupported(bool supportsRange)
    {
        var bytes = new byte[16 * 1024 * 1024 + 19];
        RandomNumberGenerator.Fill(bytes);
        int requests = 0;
        using var handler = new Handler(request =>
        {
            Interlocked.Increment(ref requests);
            var range = request.Headers.Range?.Ranges.Single();
            if (supportsRange && range != null)
            {
                long start = range.From!.Value, end = range.To!.Value;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(bytes.AsSpan((int)start, (int)(end - start + 1)).ToArray()) };
                response.Headers.ETag = new EntityTagHeaderValue("\"version-1\"");
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, bytes.Length);
                if (end > 0) Assert.Equal("\"version-1\"", request.Headers.IfMatch.Single().ToString());
                return response;
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var client = new WebDavBrowserClient(Profile(true), "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await client.DownloadAsync(new("file.bin", client.Child(client.Root, "file.bin"), false, bytes.Length, null), path, null, default);
            Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Equal(supportsRange ? 5 : 2, requests);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".hinge-*.part"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task FailedDownloadDoesNotReplaceExistingFile()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.Forbidden));
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(
                new("file", client.Child(client.Root, "file"), false, 10, null), path, null, default));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DownloadOutsideEndpointMakesNoRequest()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Must not send request"));
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(
            new("file", new Uri("https://other.test/file"), false, 10, null), "unused", null, default));
    }

    [Fact]
    public async Task CancelledDownloadDoesNotPublishPartialFile()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-test-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(
            new("file", client.Child(client.Root, "file"), false, 3, null), path, null, cancellation.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task UploadStreamsBytesWithCustomUserAgentAndEscapedName()
    {
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-" + Guid.NewGuid().ToString("N") + " #中文.txt");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            using var handler = new AsyncHandler(async request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Contains("%23", request.RequestUri!.AbsoluteUri);
                Assert.Equal("Custom-UA/1.0", request.Headers.GetValues("User-Agent").Single());
                Assert.Equal(bytes, await request.Content!.ReadAsByteArrayAsync());
                return new(HttpStatusCode.Created);
            });
            using var client = new WebDavBrowserClient(Profile() with { UserAgent = "Custom-UA/1.0" }, "", handler);
            await client.UploadAsync(client.Root, path, null, default);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task DirectoryCreationAndDeletionUseScopedWebDavMethods()
    {
        var methods = new List<string>();
        using var handler = new Handler(request =>
        {
            methods.Add(request.Method.Method);
            Assert.StartsWith("https://dav.example.test/webdav/", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.NoContent);
        });
        using var client = new WebDavBrowserClient(Profile(), "", handler);
        await client.CreateDirectoryAsync(client.Root, "工作", default);
        await client.DeleteAsync(new("file", client.Child(client.Root, "file"), false, 3, null), default);
        Assert.Equal(new[] { "MKCOL", "DELETE" }, methods);
    }

    [Fact]
    public async Task ChangedEntityDuringParallelDownloadIsNotPublished()
    {
        using var handler = new Handler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            { Content = new ByteArrayContent([0]) };
            response.Headers.ETag = new EntityTagHeaderValue(range.To == 0 ? "\"v1\"" : "\"v2\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, 16777216);
            return response;
        });
        using var client = new WebDavBrowserClient(Profile(true), "", handler);
        var path = Path.Combine(Path.GetTempPath(), "hinge-dav-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAnyAsync<Exception>(() => client.DownloadAsync(
                new("file", client.Child(client.Root, "file"), false, 16777216, null), path, null, default));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".hinge-*.part"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PasswordProtectionRoundTripsWithoutPlaintextInSerializedProfile()
    {
        const string password = "dummy-test-only-密码-123456";
        var protectedPassword = await Hinge.App.WebDavSettingsStore.ProtectAsync(password);
        Assert.NotEqual(password, protectedPassword);
        Assert.Equal(password, await Hinge.App.WebDavSettingsStore.UnprotectAsync(protectedPassword));
        var json = System.Text.Json.JsonSerializer.Serialize(Profile() with { ProtectedPassword = protectedPassword });
        Assert.DoesNotContain(password, json);
    }

    [Fact]
    public async Task EmptyPasswordDoesNotNeedAnEncryptedBlob()
    {
        Assert.Equal("", await Hinge.App.WebDavSettingsStore.ProtectAsync(""));
        Assert.Equal("", await Hinge.App.WebDavSettingsStore.UnprotectAsync(""));
    }

    [Fact]
    public async Task CorruptedProtectedPasswordIsNotSilentlyReplacedWithEmptyPassword()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Hinge.App.WebDavSettingsStore.UnprotectAsync("AQIDBA=="));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return respond(request); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
