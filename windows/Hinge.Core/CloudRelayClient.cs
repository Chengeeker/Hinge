using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public sealed class CloudRelayException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public CloudRelayException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

public sealed class CloudRelayClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public CloudRelayClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public async Task<bool> CheckHealthAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            BuildUri(endpoint, "/v1/health"),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<CloudRelayCredentials> RegisterAsync(
        string endpoint,
        string adminToken,
        string relayDeviceId,
        string deviceName,
        string platform,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(endpoint, "/v1/register"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken.Trim());
        request.Content = JsonContent.Create(new
        {
            relayDeviceId,
            name = deviceName,
            platform,
            clientVersion,
        }, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        string token = RequiredString(document.RootElement, "deviceToken");
        string returnedId = RequiredString(document.RootElement, "relayDeviceId");
        return new CloudRelayCredentials(returnedId, token);
    }

    public async Task<bool> IsPeerRegisteredAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string peerHingeDeviceId,
        CancellationToken cancellationToken = default)
    {
        string localRelay = CloudRelayCrypto.ComputeRelayDeviceId(
            settings.RelayEncryptionKey,
            localHingeDeviceId);
        string peerRelay = CloudRelayCrypto.ComputeRelayDeviceId(
            settings.RelayEncryptionKey,
            peerHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            settings,
            localRelay,
            $"/v1/devices/{Uri.EscapeDataString(peerRelay)}",
            null,
            cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        return document.RootElement.TryGetProperty("registered", out var registered) &&
            registered.ValueKind == JsonValueKind.True;
    }

    public async Task<CloudRelayUploadSession> CreateTransferAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string peerHingeDeviceId,
        string transferId,
        int partCount,
        long expiresAt,
        CancellationToken cancellationToken = default)
    {
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        string receiverRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, peerHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            settings,
            senderRelay,
            "/v1/transfers",
            JsonContent.Create(new
            {
                transferId,
                receiverRelayDeviceId = receiverRelay,
                partSize = CloudRelayConstants.PartSize,
                partCount,
                expiresAt,
            }, options: JsonOptions),
            cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        return new CloudRelayUploadSession(
            RequiredString(document.RootElement, "transferId"),
            RequiredString(document.RootElement, "uploadId"),
            document.RootElement.GetProperty("partSize").GetInt32(),
            document.RootElement.GetProperty("partCount").GetInt32());
    }

    public async Task<CloudRelayUploadedPart> UploadPartAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string transferId,
        string uploadId,
        int partNumber,
        ReadOnlyMemory<byte> encryptedPart,
        CancellationToken cancellationToken = default)
    {
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var content = new ByteArrayContent(encryptedPart.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Put,
            settings,
            senderRelay,
            $"/v1/transfers/{Uri.EscapeDataString(transferId)}/parts/{partNumber}?uploadId={Uri.EscapeDataString(uploadId)}",
            content,
            cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        return new CloudRelayUploadedPart(
            document.RootElement.GetProperty("partNumber").GetInt32(),
            RequiredString(document.RootElement, "etag"));
    }

    public async Task CompleteTransferAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        CloudRelayManifest manifest,
        string uploadId,
        IReadOnlyList<CloudRelayUploadedPart> parts,
        CancellationToken cancellationToken = default)
    {
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            settings,
            senderRelay,
            $"/v1/transfers/{Uri.EscapeDataString(manifest.TransferId)}/complete",
            JsonContent.Create(new
            {
                parts = parts.Select(part => new { partNumber = part.PartNumber, etag = part.Etag }),
                manifest,
            }, options: JsonOptions),
            cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<CloudRelayManifest>> GetInboxAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        CancellationToken cancellationToken = default)
    {
        string receiverRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            settings,
            receiverRelay,
            "/v1/inbox",
            null,
            cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        return document.RootElement.TryGetProperty("items", out var items)
            ? JsonSerializer.Deserialize<List<CloudRelayManifest>>(items.GetRawText(), JsonOptions) ?? []
            : [];
    }

    public async Task<byte[]> DownloadPartAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string transferId,
        int partNumber,
        CancellationToken cancellationToken = default)
    {
        string receiverRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            settings,
            receiverRelay,
            $"/v1/transfers/{Uri.EscapeDataString(transferId)}/parts/{partNumber}",
            null,
            cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task AcknowledgeAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string transferId,
        CancellationToken cancellationToken = default)
    {
        string receiverRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            settings,
            receiverRelay,
            $"/v1/transfers/{Uri.EscapeDataString(transferId)}/ack",
            JsonContent.Create(new { }, options: JsonOptions),
            cancellationToken);
        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<CloudRelayReceipt>> GetReceiptsAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        CancellationToken cancellationToken = default)
    {
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Get,
            settings,
            senderRelay,
            "/v1/receipts",
            null,
            cancellationToken);
        using var document = await ReadJsonOrThrowAsync(response);
        return document.RootElement.TryGetProperty("items", out var items)
            ? JsonSerializer.Deserialize<List<CloudRelayReceipt>>(items.GetRawText(), JsonOptions) ?? []
            : [];
    }

    public async Task DeleteReceiptAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string transferId,
        CancellationToken cancellationToken = default)
    {
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        using var response = await SendAuthenticatedAsync(
            HttpMethod.Delete,
            settings,
            senderRelay,
            $"/v1/receipts/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
        await EnsureSuccessAsync(response);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        CloudRelaySettings settings,
        string relayDeviceId,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured) throw new InvalidOperationException("Cloud Relay is not configured.");
        using var request = new HttpRequestMessage(method, BuildUri(settings.Endpoint, path))
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
        request.Headers.TryAddWithoutValidation("X-Hinge-Relay-Device", relayDeviceId);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        throw new CloudRelayException(response.StatusCode, ExtractError(detail));
    }

    private static async Task<JsonDocument> ReadJsonOrThrowAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync();
            throw new CloudRelayException(response.StatusCode, ExtractError(detail));
        }
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string ExtractError(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);
            if (document.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? "Cloud Relay request failed.";
        }
        catch
        {
            // Fall through to a bounded response body.
        }
        return string.IsNullOrWhiteSpace(detail)
            ? "Cloud Relay request failed."
            : detail.Trim()[..Math.Min(detail.Trim().Length, 400)];
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            string result = value.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        throw new CloudRelayException(HttpStatusCode.BadGateway, $"Cloud Relay response is missing {property}.");
    }

    private static Uri BuildUri(string endpoint, string path)
    {
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Cloud Relay endpoint must be an HTTP(S) URL.", nameof(endpoint));
        }
        return new Uri(baseUri, path.TrimStart('/'));
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public sealed class CloudRelayTransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CloudRelayClient _client;

    public CloudRelayTransferService(CloudRelayClient client)
    {
        _client = client;
    }

    public async Task<string> SendFileAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string peerHingeDeviceId,
        string filePath,
        string? fileName = null,
        string? mimeType = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("File not found.", filePath);
        if (!settings.IsConfigured) throw new InvalidOperationException("Cloud Relay is not configured.");

        var info = new FileInfo(filePath);
        string transferId = Guid.NewGuid().ToString();
        string senderRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, localHingeDeviceId);
        string receiverRelay = CloudRelayCrypto.ComputeRelayDeviceId(settings.RelayEncryptionKey, peerHingeDeviceId);
        int partCount = Math.Max(1, checked((int)((info.Length + CloudRelayConstants.PartSize - 1) / CloudRelayConstants.PartSize)));
        long createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expiresAt = createdAt + CloudRelayConstants.DefaultTtlHours * 3600L;
        string sha256 = await ComputeSha256Async(filePath, cancellationToken);
        byte[] transferKey = CloudRelayCrypto.DeriveTransferKey(
            settings.RelayEncryptionKey,
            transferId,
            senderRelay,
            receiverRelay);
        try
        {
            var metadata = new CloudRelayFileMetadata
            {
                FileName = string.IsNullOrWhiteSpace(fileName) ? info.Name : Path.GetFileName(fileName),
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
                FileSize = info.Length,
                ModifiedTime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                Sha256 = sha256,
            };
            var encryptedMetadata = CloudRelayCrypto.EncryptMetadata(
                metadata,
                transferKey,
                transferId,
                senderRelay,
                receiverRelay);
            var session = await _client.CreateTransferAsync(
                settings,
                localHingeDeviceId,
                peerHingeDeviceId,
                transferId,
                partCount,
                expiresAt,
                cancellationToken);

            var uploadedParts = new List<CloudRelayUploadedPart>(partCount);
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[CloudRelayConstants.PartSize];
            long transferred = 0;
            for (int partNumber = 1; partNumber <= partCount; partNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = 0;
                while (count < buffer.Length && transferred + count < info.Length)
                {
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(count, buffer.Length - count),
                        cancellationToken);
                    if (read == 0) break;
                    count += read;
                }

                byte[] encrypted = CloudRelayCrypto.EncryptPart(
                    buffer.AsSpan(0, count),
                    transferKey,
                    transferId,
                    senderRelay,
                    receiverRelay,
                    partNumber);
                uploadedParts.Add(await _client.UploadPartAsync(
                    settings,
                    localHingeDeviceId,
                    transferId,
                    session.UploadId,
                    partNumber,
                    encrypted,
                    cancellationToken));
                transferred += count;
                progress?.Report(new TransferProgress
                {
                    TransferId = transferId,
                    FileName = metadata.FileName,
                    BytesTransferred = transferred,
                    TotalBytes = info.Length,
                    State = TransferState.Transferring,
                });
            }

            var manifest = new CloudRelayManifest
            {
                TransferId = transferId,
                SenderRelayDeviceId = senderRelay,
                ReceiverRelayDeviceId = receiverRelay,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
                PartSize = CloudRelayConstants.PartSize,
                PartCount = partCount,
                CiphertextSize = checked(info.Length + (long)partCount * CloudRelayConstants.GcmTagSize),
                MetadataCiphertext = encryptedMetadata.Ciphertext,
                MetadataNonce = encryptedMetadata.Nonce,
                MetadataTag = encryptedMetadata.Tag,
            };
            await _client.CompleteTransferAsync(
                settings,
                localHingeDeviceId,
                manifest,
                session.UploadId,
                uploadedParts,
                cancellationToken);
            progress?.Report(new TransferProgress
            {
                TransferId = transferId,
                FileName = metadata.FileName,
                BytesTransferred = info.Length,
                TotalBytes = info.Length,
                State = TransferState.AwaitingPickup,
            });
            return transferId;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transferKey);
        }
    }

    public async Task<IReadOnlyList<CloudRelayReceiveResult>> ReceiveInboxAsync(
        CloudRelaySettings settings,
        string localHingeDeviceId,
        string downloadDirectory,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlySet<string>? allowedSenderRelayDeviceIds = null)
    {
        Directory.CreateDirectory(downloadDirectory);
        var received = new List<CloudRelayReceiveResult>();
        var manifests = await _client.GetInboxAsync(settings, localHingeDeviceId, cancellationToken);
        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (allowedSenderRelayDeviceIds != null &&
                !allowedSenderRelayDeviceIds.Contains(manifest.SenderRelayDeviceId))
            {
                continue;
            }
            byte[] transferKey = CloudRelayCrypto.DeriveTransferKey(
                settings.RelayEncryptionKey,
                manifest.TransferId,
                manifest.SenderRelayDeviceId,
                manifest.ReceiverRelayDeviceId);
            try
            {
                var metadata = CloudRelayCrypto.DecryptMetadata(manifest, settings.RelayEncryptionKey);
                if (metadata.FileSize < 0 || metadata.FileSize > long.MaxValue - CloudRelayConstants.GcmTagSize)
                {
                    throw new InvalidDataException("Cloud Relay file size is invalid.");
                }
                string safeName = Path.GetFileName(metadata.FileName);
                if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..") safeName = $"Hinge-{manifest.TransferId}.bin";
                string temporaryPath = Path.Combine(downloadDirectory, $".{manifest.TransferId}.part");
                string finalPath = GetUniquePath(Path.Combine(downloadDirectory, safeName));
                long written = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    for (int partNumber = 1; partNumber <= manifest.PartCount; partNumber++)
                    {
                        int plaintextLength = partNumber == manifest.PartCount
                            ? checked((int)(manifest.CiphertextSize - (long)(partNumber - 1) * (manifest.PartSize + CloudRelayConstants.GcmTagSize) - CloudRelayConstants.GcmTagSize))
                            : manifest.PartSize;
                        byte[] encrypted = await _client.DownloadPartAsync(
                            settings,
                            localHingeDeviceId,
                            manifest.TransferId,
                            partNumber,
                            cancellationToken);
                        byte[] plaintext = CloudRelayCrypto.DecryptPart(
                            encrypted,
                            transferKey,
                            manifest.TransferId,
                            manifest.SenderRelayDeviceId,
                            manifest.ReceiverRelayDeviceId,
                            partNumber,
                            plaintextLength);
                        await output.WriteAsync(plaintext, cancellationToken);
                        hash.AppendData(plaintext);
                        written += plaintext.Length;
                        progress?.Report(new TransferProgress
                        {
                            TransferId = manifest.TransferId,
                            FileName = safeName,
                            BytesTransferred = written,
                            TotalBytes = metadata.FileSize,
                            State = TransferState.Transferring,
                        });
                    }
                }

                string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (written != metadata.FileSize || !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(metadata.Sha256)))
                {
                    File.Delete(temporaryPath);
                    throw new CryptographicException("Cloud Relay plaintext SHA-256 verification failed.");
                }
                File.Move(temporaryPath, finalPath);
                await _client.AcknowledgeAsync(settings, localHingeDeviceId, manifest.TransferId, cancellationToken);
                received.Add(new CloudRelayReceiveResult(
                    manifest.TransferId,
                    manifest.SenderRelayDeviceId,
                    finalPath,
                    safeName,
                    written));
                progress?.Report(new TransferProgress
                {
                    TransferId = manifest.TransferId,
                    FileName = safeName,
                    BytesTransferred = written,
                    TotalBytes = metadata.FileSize,
                    State = TransferState.Completed,
                });
            }
            catch
            {
                string temporaryPath = Path.Combine(downloadDirectory, $".{manifest.TransferId}.part");
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(transferKey);
            }
        }
        return received;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int index = 2; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}
