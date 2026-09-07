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
    private readonly string _downloadDirectory;
    private readonly ConcurrentDictionary<string, IncomingFileContext> _incomingTransfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

    public event EventHandler<TextTransferMessage>? TextReceived;
    public event EventHandler<TransferProgress>? TransferProgressChanged;
    public event EventHandler<string>? FileReceived;

    public int ActiveTransfersCount => _incomingTransfers.Count;

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

    private async Task HandleFileOfferAsync(SessionConnection conn, FileOfferMessage offer)
    {
        // Sanitize filename to prevent directory traversal
        string safeName = Path.GetFileName(offer.FileName);
        string finalPath = Path.Combine(_downloadDirectory, safeName);
        string tempPath = finalPath + ".part";

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

        await context.FileStream.FlushAsync();
        context.FileStream.Close();
        context.FileStream.Dispose();
        context.FileStream = null;

        // Verify SHA-256
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
            // Corrupt file
            File.Delete(context.TempFilePath);
            var prog = new TransferProgress
            {
                TransferId = complete.TransferId,
                FileName = context.Offer.FileName,
                BytesTransferred = 0,
                TotalBytes = context.Offer.FileSize,
                State = TransferState.Failed
            };
            TransferProgressChanged?.Invoke(this, prog);
        }
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
    }
}
