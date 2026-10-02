using System.Net;
using System.Net.Http.Headers;
using WebDav;

namespace Hinge.Core;

public sealed record WebDavEntry(string Name, Uri Uri, bool IsDirectory, long Size, DateTime? Modified)
{
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    public string SizeLabel => IsDirectory ? "文件夹" : $"{Size / 1048576d:F2} MB";
    public string FileSizeLabel => IsDirectory ? "—" : $"{Size / 1048576d:F2} MB";
    public string TypeLabel => IsDirectory ? "文件夹" : string.IsNullOrEmpty(Path.GetExtension(Name)) ? "文件" : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() + " 文件";
    public string ModifiedLabel => Modified?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "—";
}
public static class WebDavEntrySorting
{
    public static IReadOnlyList<WebDavEntry> Sort(IEnumerable<WebDavEntry> entries, string mode)
    {
        var groups = entries.OrderByDescending(entry => entry.IsDirectory);
        return (mode switch
        {
            "type" => groups.ThenBy(entry => entry.TypeLabel, StringComparer.OrdinalIgnoreCase),
            "sizeAsc" => groups.ThenBy(entry => entry.Size),
            "sizeDesc" => groups.ThenByDescending(entry => entry.Size),
            "timeAsc" => groups.ThenBy(entry => entry.Modified ?? DateTime.MaxValue),
            "timeDesc" => groups.ThenByDescending(entry => entry.Modified ?? DateTime.MinValue),
            _ => groups.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        }).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
public sealed record WebDavTransferProgress(long Completed, long Total);

/// <summary>Endpoint-scoped WebDAV session; never shares credentials or TLS exceptions with LAN/Relay.</summary>
public sealed class WebDavBrowserClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _anonymousDownload;
    private readonly WebDavClient _dav;
    public Uri Root { get; }
    public Uri InitialDirectory { get; }
    public bool ParallelTransfers { get; }

    public WebDavBrowserClient(WebDavProfile profile, string password, HttpMessageHandler? testHandler = null,
        HttpMessageHandler? redirectTestHandler = null)
    {
        Root = ValidateEndpoint(profile.Url);
        InitialDirectory = BuildDirectory(profile.InitialPath);
        if (profile.UserAgent.Contains('\r') || profile.UserAgent.Contains('\n'))
            throw new ArgumentException("UA 不能包含换行符。");
        var handler = testHandler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            Credentials = new NetworkCredential(profile.Username, password),
            PreAuthenticate = true,
            AutomaticDecompression = DecompressionMethods.None,
            ServerCertificateCustomValidationCallback = profile.TrustAllCertificates
                ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator : null
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            string.IsNullOrWhiteSpace(profile.UserAgent) ? "Hinge-WebDAV/1.0" : profile.UserAgent);
        // CDN/signed-link GETs must not inherit DAV credentials, cookies, UA or TLS exceptions.
        _anonymousDownload = new HttpClient(redirectTestHandler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = TimeSpan.FromMinutes(10) };
        _anonymousDownload.DefaultRequestHeaders.UserAgent.ParseAdd("Hinge-WebDAV/1.0");
        _dav = new WebDavClient(_http);
        ParallelTransfers = profile.ParallelTransfers;
    }

    public static Uri ValidateEndpoint(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("URL 必须是 HTTP/HTTPS WebDAV 地址，不能包含账号、查询参数或片段。");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public Uri BuildDirectory(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part == "." || part == ".."))
            throw new ArgumentException("初始路径不能包含 . 或 ..。");
        return new Uri(Root, string.Join("/", parts.Select(Uri.EscapeDataString)) + (parts.Length > 0 ? "/" : ""));
    }

    public bool Contains(Uri uri) => uri.Scheme == Root.Scheme && uri.Authority == Root.Authority &&
        uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 &&
        (uri.AbsolutePath.StartsWith(Root.AbsolutePath, StringComparison.Ordinal) ||
         uri.AbsolutePath.TrimEnd('/') == Root.AbsolutePath.TrimEnd('/'));

    private void EnsureScope(Uri uri)
    {
        if (!Contains(uri)) throw new InvalidOperationException("服务器返回了配置地址之外的路径，已拒绝访问。");
        var decoded = Uri.UnescapeDataString(uri.AbsolutePath).Replace('\\', '/');
        if (decoded.Split('/').Any(part => part == "." || part == ".."))
            throw new InvalidOperationException("服务器返回了不安全的路径。");
    }

    public Uri Child(Uri directory, string name, bool isDirectory = false)
    {
        EnsureScope(directory);
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\r', '\n']) >= 0)
            throw new ArgumentException("文件名不能为空或包含路径分隔符。");
        return new Uri(new Uri(directory.AbsoluteUri.TrimEnd('/') + "/"),
            Uri.EscapeDataString(name) + (isDirectory ? "/" : ""));
    }

    public Uri? Parent(Uri directory)
    {
        EnsureScope(directory);
        if (directory.AbsolutePath.TrimEnd('/') == Root.AbsolutePath.TrimEnd('/')) return null;
        var parent = new Uri(directory, "../");
        return Contains(parent) ? parent : Root;
    }

    public async Task<IReadOnlyList<WebDavEntry>> ListAsync(Uri directory, CancellationToken token = default)
    {
        EnsureScope(directory);
        var response = await _dav.Propfind(directory, new PropfindParameters { CancellationToken = token });
        if (!response.IsSuccessful) throw new HttpRequestException($"读取目录失败：HTTP {response.StatusCode}。");
        var entries = new List<WebDavEntry>();
        foreach (var resource in response.Resources)
        {
            if (!Uri.TryCreate(directory, resource.Uri, out var uri)) continue;
            EnsureScope(uri);
            if (uri.AbsolutePath.TrimEnd('/') == directory.AbsolutePath.TrimEnd('/')) continue;
            var parent = new Uri(new Uri(uri.AbsoluteUri.TrimEnd('/') + (resource.IsCollection ? "/" : "")), resource.IsCollection ? "../" : ".");
            if (parent.AbsolutePath.TrimEnd('/') != directory.AbsolutePath.TrimEnd('/')) continue;
            var name = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').Last());
            entries.Add(new WebDavEntry(name, uri, resource.IsCollection, resource.ContentLength ?? 0, resource.LastModifiedDate));
        }
        return entries.OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task CreateDirectoryAsync(Uri directory, string name, CancellationToken token)
    {
        var response = await _dav.Mkcol(Child(directory, name, true), new MkColParameters { CancellationToken = token });
        if (!response.IsSuccessful) throw new HttpRequestException($"创建目录失败：HTTP {response.StatusCode}。");
    }

    public async Task DeleteAsync(WebDavEntry entry, CancellationToken token)
    {
        EnsureScope(entry.Uri);
        var response = await _dav.Delete(entry.Uri, new DeleteParameters { CancellationToken = token });
        if (!response.IsSuccessful) throw new HttpRequestException($"删除失败：HTTP {response.StatusCode}。");
    }

    public async Task UploadAsync(Uri directory, string localPath, IProgress<WebDavTransferProgress>? progress, CancellationToken token)
    {
        await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using var content = new TransferContent(source, progress);
        var response = await _dav.PutFile(Child(directory, Path.GetFileName(localPath)), content,
            new PutFileParameters { CancellationToken = token });
        if (!response.IsSuccessful) throw new HttpRequestException($"上传失败：HTTP {response.StatusCode}。");
        progress?.Report(new(source.Length, source.Length));
    }

    public async Task DownloadAsync(WebDavEntry entry, string destination, IProgress<WebDavTransferProgress>? progress, CancellationToken token)
    {
        EnsureScope(entry.Uri);
        var temporary = destination + ".hinge-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            bool ranged = false;
            if (ParallelTransfers && entry.Size >= 16 * 1024 * 1024)
            {
                using var response = await GetDownloadResponseAsync(entry.Uri, new RangeHeaderValue(0, 0), null, token);
                // Parallel requests must observe the same file version. Without a strong ETag use one stream.
                if (response.StatusCode == HttpStatusCode.PartialContent && response.Headers.ETag is { IsWeak: false } etag &&
                    response.Content.Headers.ContentRange is { From: 0, To: 0 } range && range.Length == entry.Size &&
                    !response.Content.Headers.ContentEncoding.Any())
                {
                    await DownloadRangesAsync(entry, temporary, etag, progress, token);
                    ranged = true;
                }
                else if (!response.IsSuccessStatusCode) response.EnsureSuccessStatusCode();
            }
            if (!ranged) await DownloadSingleAsync(entry.Uri, temporary, progress, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task DownloadThumbnailSourceAsync(WebDavEntry entry, string destination, long maxBytes, CancellationToken token)
    {
        if (entry.IsDirectory || !Contains(entry.Uri) || maxBytes <= 0 || entry.Size > maxBytes)
            throw new ArgumentException("文件不满足缩略图下载范围或大小限制。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await DownloadSingleAsync(entry.Uri, temporary, null, token, maxBytes);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task DownloadSingleAsync(Uri uri, string target, IProgress<WebDavTransferProgress>? progress, CancellationToken token, long maxBytes = long.MaxValue)
    {
        using var response = await GetDownloadResponseAsync(uri, null, null, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new IOException("媒体超过自动缩略图大小限制。");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        var total = response.Content.Headers.ContentLength ?? 0;
        long completed = 0;
        var buffer = new byte[131072];
        while (true)
        {
            int read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            if (completed > maxBytes - read) throw new IOException("媒体超过自动缩略图大小限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            completed += read;
            progress?.Report(new(completed, total));
        }
        if (total != 0 && completed != total) throw new IOException("文件下载不完整，未保存到目标位置。");
        progress?.Report(new(completed, total));
    }

    private async Task DownloadRangesAsync(WebDavEntry entry, string target, EntityTagHeaderValue etag,
        IProgress<WebDavTransferProgress>? progress, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var output = File.OpenHandle(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);
        RandomAccess.SetLength(output, entry.Size);
        long completed = 0;
        const int count = 4;
        long segment = (entry.Size + count - 1) / count;
        var tasks = Enumerable.Range(0, count).Select(async index =>
        {
            try
            {
                long start = segment * index, end = Math.Min(entry.Size - 1, start + segment - 1);
                using var response = await GetDownloadResponseAsync(entry.Uri, new RangeHeaderValue(start, end), etag, linked.Token);
                var range = response.Content.Headers.ContentRange;
                if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start || range.To != end ||
                    range.Length != entry.Size || response.Headers.ETag?.ToString() != etag.ToString() ||
                    response.Content.Headers.ContentEncoding.Any())
                    throw new IOException("服务器未返回一致的文件分段，请关闭多线程传输后重试。");
                await using var source = await response.Content.ReadAsStreamAsync(linked.Token);
                var buffer = new byte[131072];
                long offset = start;
                while (offset <= end)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, end - offset + 1)), linked.Token);
                    if (read == 0) throw new IOException("下载分段不完整。");
                    await RandomAccess.WriteAsync(output, buffer.AsMemory(0, read), offset, linked.Token);
                    offset += read;
                    progress?.Report(new(Interlocked.Add(ref completed, read), entry.Size));
                }
            }
            catch { linked.Cancel(); throw; }
        }).ToArray();
        await Task.WhenAll(tasks);
        progress?.Report(new(entry.Size, entry.Size));
    }

    private async Task<HttpResponseMessage> GetDownloadResponseAsync(Uri uri, RangeHeaderValue? range,
        EntityTagHeaderValue? etag, CancellationToken token)
    {
        EnsureScope(uri);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool anonymous = false;
        for (int hop = 0; hop <= 5; hop++)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(uri.AbsoluteUri)) throw new HttpRequestException("文件下载重定向形成循环。");
            anonymous |= !Contains(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = range;
            if (etag != null) request.Headers.IfMatch.Add(etag);
            var response = await (anonymous ? _anonymousDownload : _http)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            Uri? next;
            try
            {
                var location = response.Headers.Location;
                if (hop == 5 || location == null || !Uri.TryCreate(uri, location, out next))
                    throw new HttpRequestException("文件下载重定向次数过多或缺少有效 Location。");
                if (next.Scheme is not ("http" or "https") || next.UserInfo.Length != 0 ||
                    next.Fragment.Length != 0 || (uri.Scheme == "https" && next.Scheme != "https"))
                    throw new HttpRequestException("已拒绝不安全的文件下载重定向。");
            }
            finally { response.Dispose(); }
            uri = next;
        }
        throw new HttpRequestException("文件下载重定向次数过多。");
    }

    public void Dispose() { _dav.Dispose(); _http.Dispose(); _anonymousDownload.Dispose(); }

    private sealed class TransferContent(Stream source, IProgress<WebDavTransferProgress>? progress) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = source.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream output, TransportContext? context) => CopyAsync(output, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream output, TransportContext? context, CancellationToken token) => CopyAsync(output, token);
        private async Task CopyAsync(Stream output, CancellationToken token)
        {
            // Auth challenges may replay a PUT; reset the source instead of sending only its remainder.
            source.Position = 0;
            var buffer = new byte[131072];
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                progress?.Report(new(source.Position, source.Length));
            }
        }
    }
}
