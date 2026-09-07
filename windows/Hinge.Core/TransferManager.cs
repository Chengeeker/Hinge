using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hinge.Core;

public class IncomingFileContext
{
    public FileOfferMessage Offer { get; set; } = new();
    public string TempFilePath { get; set; } = string.Empty;
    public string FinalFilePath { get; set; } = string.Empty;
    public FileStream? FileStream { get; set; }
    public long BytesReceived { get; set; }
}

public class TransferManager : IDisposable
{
    private const int FileChunkSize = 512 * 1024;
    private string _downloadDirectory;
    private readonly ConcurrentDictionary<string, IncomingFileContext> _incomingTransfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly ConcurrentDictionary<string, string> _incomingDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _incomingFrameGate = new(1, 1);

    public event EventHandler<TextTransferMessage>? TextReceived;
    public event EventHandler<TransferProgress>? TransferProgressChanged;
    public event EventHandler<string>? FileReceived;
    public event EventHandler<TransferFailure>? TransferFailed;

    public int ActiveTransfersCount => _incomingTransfers.Count;

    /// <summary>
    /// Routes the next incoming file with this name to a temporary directory.
    /// This is used by Windows previews; ordinary incoming transfers continue
    /// to use the user's Hinge download directory.
    /// </summary>
    public IDisposable RegisterIncomingDirectory(string fileName, string directory)
    {
        var safeName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException("文件名无效。", nameof(fileName));
        }

        Directory.CreateDirectory(directory);
        _incomingDirectories[safeName] = directory;
        return new IncomingDirectoryRegistration(
            () => _incomingDirectories.TryRemove(safeName, out _));
    }

    public TransferManager(string? downloadDirectory = null)
    {
        if (string.IsNullOrEmpty(downloadDirectory))
        {
            string userDownloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "Hinge"
            );
            _downloadDirectory = userDownloads;
        }
        else
        {
            _downloadDirectory = downloadDirectory;
        }
        Directory.CreateDirectory(_downloadDirectory);
    }

    /// <summary>
    /// Changes the default directory used by unsolicited incoming files while
    /// keeping the manager and its active connections alive.
    /// </summary>
    public void SetDownloadDirectory(string downloadDirectory)
    {
        if (string.IsNullOrWhiteSpace(downloadDirectory))
        {
            throw new ArgumentException("接收目录不能为空。", nameof(downloadDirectory));
        }

        Directory.CreateDirectory(downloadDirectory);
        _downloadDirectory = downloadDirectory;
    }

    public async Task SendTextAsync(SessionConnection conn, string content, string type = "text")
    {
        var msg = new TextTransferMessage
        {
            Type = type,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await conn.SendJsonAsync(MessageType.TextMessage, msg);
    }

    public async Task<string> SendFileAsync(
        SessionConnection conn,
        string filePath,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool precomputeHash = true,
        string? destinationPath = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        string transferId = Guid.NewGuid().ToString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellations[transferId] = cts;

        // Calculate SHA-256
        // Media preview can opt out of a complete pre-scan. The receiver will
        // verify the digest carried by FILE_COMPLETE after the stream ends.
        string sha256 = precomputeHash
            ? await ComputeSha256Async(filePath, cts.Token)
            : string.Empty;

        var offer = new FileOfferMessage
        {
            TransferId = transferId,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            Sha256 = sha256,
            DestinationPath = NormalizeDestinationPath(destinationPath)
        };

        // Send FILE_OFFER
        var acceptTcs = new TaskCompletionSource<FileAcceptMessage>();

        void OnFrame(object? s, ProtocolFrame frame)
        {
            if (frame.Type == MessageType.FileAccept)
            {
                var accept = JsonSerializer.Deserialize<FileAcceptMessage>(Encoding.UTF8.GetString(frame.Payload));
                if (accept != null && accept.TransferId == transferId)
                {
                    acceptTcs.TrySetResult(accept);
                }
            }
            else if (frame.Type == MessageType.FileReject)
            {
                acceptTcs.TrySetException(new InvalidOperationException("File transfer rejected by peer"));
            }
        }

        conn.FrameReceived += OnFrame;

        try
        {
            await conn.SendJsonAsync(MessageType.FileOffer, offer);

            // Wait for acceptance
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            using (linked.Token.Register(() => acceptTcs.TrySetCanceled()))
            {
                var accept = await acceptTcs.Task;
                if (!accept.Accepted)
                {
                    throw new InvalidOperationException($"File transfer rejected: {accept.Reason}");
                }

                // Begin chunk stream from accept.Offset
                long offset = accept.Offset;
                const int chunkSize = FileChunkSize;
                byte[] chunkBuffer = new byte[chunkSize];
                if (string.IsNullOrEmpty(sha256))
                {
                    // Resumed transfers need the complete digest before the
                    // remainder can be verified safely.
                    sha256 = await ComputeSha256Async(filePath, cts.Token);
                }

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (offset > 0 && offset < fs.Length)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                    }

                    uint chunkIndex = 0;
                    long bytesSent = offset;

                    while (bytesSent < fs.Length)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        int bytesRead = await fs.ReadAsync(chunkBuffer.AsMemory(0, chunkSize), cts.Token);
                        if (bytesRead == 0) break;

                        // Build chunk payload: 16B transferId + 4B chunkIndex + 8B offset + data
                        byte[] payload = new byte[28 + bytesRead];
                        ProtocolUuid.WriteNetworkBytes(Guid.Parse(transferId), payload.AsSpan(0, 16));
                        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16, 4), chunkIndex);
                        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(20, 8), bytesSent);
                        Buffer.BlockCopy(chunkBuffer, 0, payload, 28, bytesRead);

                        await conn.SendFrameAsync(MessageType.FileChunk, payload);

                        bytesSent += bytesRead;
                        chunkIndex++;

                        var prog = new TransferProgress
                        {
                            TransferId = transferId,
                            FileName = fileInfo.Name,
                            BytesTransferred = bytesSent,
                            TotalBytes = fs.Length,
                            State = TransferState.Transferring
                        };
                        progress?.Report(prog);
                        TransferProgressChanged?.Invoke(this, prog);
                    }
                }

                // Send FILE_COMPLETE
                var completeMsg = new FileCompleteMessage
                {
                    TransferId = transferId,
                    Sha256 = sha256,
                    Success = true
                };
                await conn.SendJsonAsync(MessageType.FileComplete, completeMsg);

                var finalProg = new TransferProgress
                {
                    TransferId = transferId,
                    FileName = fileInfo.Name,
                    BytesTransferred = fileInfo.Length,
                    TotalBytes = fileInfo.Length,
                    State = TransferState.Completed
                };
                progress?.Report(finalProg);
                TransferProgressChanged?.Invoke(this, finalProg);

                return transferId;
            }
        }
        finally
        {
            conn.FrameReceived -= OnFrame;
            _cancellations.TryRemove(transferId, out _);
        }
    }

    public async Task HandleIncomingFrameAsync(SessionConnection conn, ProtocolFrame frame)
    {
        await _incomingFrameGate.WaitAsync();
        try
        {
            if (frame.Type == MessageType.TextMessage)
            {
                string json = Encoding.UTF8.GetString(frame.Payload);
                var textMsg = JsonSerializer.Deserialize<TextTransferMessage>(json);
                if (textMsg != null)
                {
                    TextReceived?.Invoke(this, textMsg);
                }
            }
            else if (frame.Type == MessageType.FileOffer)
            {
                string json = Encoding.UTF8.GetString(frame.Payload);
                var offer = JsonSerializer.Deserialize<FileOfferMessage>(json);
                if (offer != null)
                {
                    await HandleFileOfferAsync(conn, offer);
                }
            }
            else if (frame.Type == MessageType.FileChunk)
            {
                await HandleFileChunkAsync(frame.Payload);
            }
            else if (frame.Type == MessageType.FileComplete)
            {
                string json = Encoding.UTF8.GetString(frame.Payload);
                var complete = JsonSerializer.Deserialize<FileCompleteMessage>(json);
                if (complete != null)
                {
                    await HandleFileCompleteAsync(conn, complete);
                }
            }
        }
        catch (Exception exception)
        {
            await HandleIncomingFrameFailureAsync(conn, frame, exception);
        }
        finally
        {
            _incomingFrameGate.Release();
        }
    }

    private async Task HandleFileOfferAsync(SessionConnection conn, FileOfferMessage offer)
    {
        // Sanitize filename to prevent directory traversal and invalid Windows paths.
        string safeName = SanitizeFileName(offer.FileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new InvalidOperationException("收到的文件名无效。");
        }

        var directory = _incomingDirectories.TryRemove(safeName, out var registeredDirectory)
            ? registeredDirectory
            : _downloadDirectory;
        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, safeName);
        string transferSuffix = SanitizeTransferId(offer.TransferId);
        string tempPath = Path.Combine(
            directory,
            $".{safeName}.{transferSuffix}.part");

        long existingBytes = 0;
        if (File.Exists(tempPath))
        {
            existingBytes = new FileInfo(tempPath).Length;
            if (existingBytes > offer.FileSize)
            {
                // Invalidate partial file if larger than target
                File.Delete(tempPath);
                existingBytes = 0;
            }
        }

        var stream = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        if (existingBytes > 0)
        {
            stream.Seek(existingBytes, SeekOrigin.Begin);
        }

        var context = new IncomingFileContext
        {
            Offer = offer,
            TempFilePath = tempPath,
            FinalFilePath = finalPath,
            FileStream = stream,
            BytesReceived = existingBytes
        };
        _incomingTransfers[offer.TransferId] = context;

        var accept = new FileAcceptMessage
        {
            TransferId = offer.TransferId,
            Accepted = true,
            Offset = existingBytes
        };
        await conn.SendJsonAsync(MessageType.FileAccept, accept);
    }

    private async Task HandleFileChunkAsync(byte[] payload)
    {
        if (payload.Length < 28) return;

        Guid transferId = ProtocolUuid.ReadNetworkBytes(payload.AsSpan(0, 16));
        string transferIdStr = transferId.ToString();
        if (!_incomingTransfers.TryGetValue(transferIdStr, out var context) || context.FileStream == null)
        {
            return;
        }

        long chunkOffset = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(20, 8));
        int dataLength = payload.Length - 28;

        if (context.FileStream.Position != chunkOffset)
        {
            context.FileStream.Seek(chunkOffset, SeekOrigin.Begin);
        }

        await context.FileStream.WriteAsync(payload.AsMemory(28, dataLength));
        context.BytesReceived += dataLength;

        var prog = new TransferProgress
        {
            TransferId = transferIdStr,
            FileName = context.Offer.FileName,
            BytesTransferred = context.BytesReceived,
            TotalBytes = context.Offer.FileSize,
            State = TransferState.Transferring
        };
        TransferProgressChanged?.Invoke(this, prog);
    }

    private async Task HandleFileCompleteAsync(SessionConnection conn, FileCompleteMessage complete)
    {
        if (!_incomingTransfers.TryRemove(complete.TransferId, out var context) || context.FileStream == null)
        {
            return;
        }

        try
        {
            await context.FileStream.FlushAsync();
            await context.FileStream.DisposeAsync();
            context.FileStream = null;

            // Verify SHA-256 only after all queued chunks have been serialized.
            string localHash = await ComputeSha256Async(context.TempFilePath, CancellationToken.None);
            string expectedHash = string.IsNullOrWhiteSpace(complete.Sha256)
                ? context.Offer.Sha256
                : complete.Sha256;
            if (!string.IsNullOrWhiteSpace(expectedHash) &&
                string.Equals(localHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(context.FinalFilePath))
                {
                    File.Delete(context.FinalFilePath);
                }
                File.Move(context.TempFilePath, context.FinalFilePath);

                var prog = new TransferProgress
                {
                    TransferId = complete.TransferId,
                    FileName = context.Offer.FileName,
                    BytesTransferred = context.Offer.FileSize,
                    TotalBytes = context.Offer.FileSize,
                    State = TransferState.Completed
                };
                TransferProgressChanged?.Invoke(this, prog);
                FileReceived?.Invoke(this, context.FinalFilePath);
            }
            else
            {
                DeleteFileIfExists(context.TempFilePath);
                ReportTransferFailure(
                    context,
                    "文件校验失败，已丢弃不完整文件。",
                    complete.TransferId);
            }
        }
        catch (Exception exception)
        {
            context.FileStream?.Dispose();
            context.FileStream = null;
            DeleteFileIfExists(context.TempFilePath);
            ReportTransferFailure(context, exception.Message, complete.TransferId);
        }
    }

    private async Task HandleIncomingFrameFailureAsync(
        SessionConnection conn,
        ProtocolFrame frame,
        Exception exception)
    {
        if (frame.Type == MessageType.FileOffer)
        {
            try
            {
                var offer = JsonSerializer.Deserialize<FileOfferMessage>(
                    Encoding.UTF8.GetString(frame.Payload));
                if (offer != null)
                {
                    await conn.SendJsonAsync(
                        MessageType.FileAccept,
                        new FileAcceptMessage
                        {
                            TransferId = offer.TransferId,
                            Accepted = false,
                            Reason = exception.Message
                        });
                }
            }
            catch
            {
                // The peer may already have disconnected; there is nothing else
                // to do, but this must never escape the network callback.
            }
        }
        else if (frame.Type == MessageType.FileChunk && frame.Payload.Length >= 16)
        {
            var transferId = ProtocolUuid.ReadNetworkBytes(frame.Payload.AsSpan(0, 16)).ToString();
            DiscardIncomingTransfer(transferId, exception.Message);
        }
        else if (frame.Type == MessageType.FileComplete)
        {
            try
            {
                var complete = JsonSerializer.Deserialize<FileCompleteMessage>(
                    Encoding.UTF8.GetString(frame.Payload));
                if (complete != null)
                {
                    DiscardIncomingTransfer(complete.TransferId, exception.Message);
                }
            }
            catch
            {
                // Ignore malformed completion frames.
            }
        }
    }

    private void DiscardIncomingTransfer(string transferId, string error)
    {
        if (!_incomingTransfers.TryRemove(transferId, out var context)) return;
        context.FileStream?.Dispose();
        context.FileStream = null;
        DeleteFileIfExists(context.TempFilePath);
        ReportTransferFailure(context, error, transferId);
    }

    private void ReportTransferFailure(
        IncomingFileContext context,
        string error,
        string transferId)
    {
        var prog = new TransferProgress
        {
            TransferId = transferId,
            FileName = context.Offer.FileName,
            BytesTransferred = 0,
            TotalBytes = context.Offer.FileSize,
            State = TransferState.Failed
        };
        try { TransferProgressChanged?.Invoke(this, prog); } catch { }
        try
        {
            TransferFailed?.Invoke(this, new TransferFailure
            {
                TransferId = transferId,
                FileName = context.Offer.FileName,
                Error = error
            });
        }
        catch { }
    }

    private static string SanitizeFileName(string? fileName)
    {
        var value = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        value = builder.ToString().Trim().TrimEnd('.');
        if (value.Length > 180) value = value[..180].TrimEnd('.', ' ');
        return value;
    }

    private static string SanitizeTransferId(string? transferId)
    {
        var value = new string((transferId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (value.Length == 0) value = Guid.NewGuid().ToString("N");
        return value.Length > 32 ? value[..32] : value;
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public void CancelTransfer(string transferId)
    {
        if (_cancellations.TryRemove(transferId, out var cts))
        {
            cts.Cancel();
        }
    }

    private static string NormalizeDestinationPath(string? destinationPath)
    {
        var value = destinationPath?.Trim() ?? string.Empty;
        if (value.Length == 0) return string.Empty;

        // The receiver interprets this as a path below shared storage. Keep
        // the protocol value portable and reject traversal instead of ever
        // sending an absolute Windows path to the phone.
        value = value.Replace('\\', '/').Trim('/');
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".." || part.Contains(':')))
        {
            throw new ArgumentException("目标保存目录无效。", nameof(destinationPath));
        }

        return string.Join('/', parts);
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken token)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await sha.ComputeHashAsync(fs, token);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var ctx in _incomingTransfers.Values)
        {
            try { ctx.FileStream?.Dispose(); } catch { }
        }
        _incomingTransfers.Clear();
        _incomingDirectories.Clear();
        _incomingFrameGate.Dispose();
    }

    private sealed class IncomingDirectoryRegistration : IDisposable
    {
        private Action? _release;

        public IncomingDirectoryRegistration(Action release) => _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
