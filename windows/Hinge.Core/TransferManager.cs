using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Hinge.Core;

public class IncomingFileContext
{
    public FileOfferMessage Offer { get; set; } = new();
    public string TempFilePath { get; set; } = string.Empty;
    public string FinalFilePath { get; set; } = string.Empty;
    public FileStream? FileStream { get; set; }
    public long BytesReceived { get; set; }
    public IncrementalHash? Hash { get; set; }
    public bool HashIsContiguous { get; set; } = true;
    public Stopwatch Timer { get; } = Stopwatch.StartNew();
}

public class TransferManager : IDisposable
{
    // Keep file frames large enough to avoid turning a bulk transfer into a
    // long sequence of small socket writes. The protocol still accepts the
    // older smaller chunks, so this is a sender-side performance improvement.
    private const int FileChunkSize = 2 * 1024 * 1024;
    private const int FileReadAheadDepth = 3;
    private string _downloadDirectory;
    private readonly ConcurrentDictionary<string, IncomingFileContext> _incomingTransfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly ConcurrentDictionary<string, string> _incomingDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _incomingFrameGate = new(1, 1);

    private sealed class PooledFileChunk
    {
        public byte[] Buffer { get; }
        public int Count { get; }
        public long Offset { get; }
        public uint ChunkIndex { get; }

        public PooledFileChunk(byte[] buffer, int count, long offset, uint chunkIndex)
        {
            Buffer = buffer;
            Count = count;
            Offset = offset;
            ChunkIndex = chunkIndex;
        }

        public void Return() => ArrayPool<byte>.Shared.Return(Buffer);
    }

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

        // New peers can verify the digest carried by FILE_COMPLETE, allowing
        // the sender to hash bytes while they are read for transmission. An
        // older peer does not advertise this capability, so retain the
        // pre-scan required by the original protocol behavior.
        bool supportsStreamingHash = conn.PeerInfo?.Capabilities?.Contains(
            ProtocolCompression.StreamingFileHashCapability,
            StringComparer.Ordinal) == true;
#if HINGE_TRANSFER_DIAGNOSTICS
        var timing = new TransferSendTiming(transferId, fileInfo.Length, supportsStreamingHash);
        long phaseStart = Stopwatch.GetTimestamp();
#endif
        string sha256 = supportsStreamingHash
            ? string.Empty
            : await ComputeSha256Async(filePath, cts.Token);
#if HINGE_TRANSFER_DIAGNOSTICS
        timing.PrehashTicks += Stopwatch.GetTimestamp() - phaseStart;
#endif

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

#if HINGE_TRANSFER_DIAGNOSTICS
        bool sendCompleted = false;
#endif
        try
        {
#if HINGE_TRANSFER_DIAGNOSTICS
            phaseStart = Stopwatch.GetTimestamp();
#endif
            await conn.SendJsonAsync(MessageType.FileOffer, offer);
#if HINGE_TRANSFER_DIAGNOSTICS
            timing.OfferTicks += Stopwatch.GetTimestamp() - phaseStart;
            timing.State = "waiting_accept";
            phaseStart = Stopwatch.GetTimestamp();
#endif

            // Wait for acceptance
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeoutCts.Token);
            using (linked.Token.Register(() => acceptTcs.TrySetCanceled()))
            {
                var accept = await acceptTcs.Task;
#if HINGE_TRANSFER_DIAGNOSTICS
                timing.AcceptWaitTicks += Stopwatch.GetTimestamp() - phaseStart;
#endif
                if (!accept.Accepted)
                {
                    throw new InvalidOperationException($"File transfer rejected: {accept.Reason}");
                }

                // Begin chunk stream from accept.Offset
                long offset = Math.Clamp(accept.Offset, 0, fileInfo.Length);
#if HINGE_TRANSFER_DIAGNOSTICS
                timing.StartOffset = offset;
                timing.BytesSent = offset;
                timing.State = "sending";
#endif
                if (string.IsNullOrEmpty(sha256))
                {
                    // Resumed transfers need the complete digest before the
                    // remainder can be verified safely.
                    if (offset > 0)
                    {
#if HINGE_TRANSFER_DIAGNOSTICS
                        phaseStart = Stopwatch.GetTimestamp();
#endif
                        sha256 = await ComputeSha256Async(filePath, cts.Token);
#if HINGE_TRANSFER_DIAGNOSTICS
                        timing.PrehashTicks += Stopwatch.GetTimestamp() - phaseStart;
#endif
                    }
                }

                Guid transferGuid = Guid.Parse(transferId);
                var transferTimer = Stopwatch.StartNew();
                IncrementalHash? streamingHash = string.IsNullOrEmpty(sha256)
                    ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                    : null;
                string? completedStreamingHash = null;
                using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                var channel = Channel.CreateBounded<PooledFileChunk>(
                    new BoundedChannelOptions(FileReadAheadDepth)
                    {
                        SingleWriter = true,
                        SingleReader = true,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                async Task ProduceChunksAsync()
                {
                    Exception? failure = null;
                    try
                    {
                        await using var fs = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            FileChunkSize,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);

                        uint chunkIndex = 0;
                        long readOffset = offset;
                        while (readOffset < fileInfo.Length)
                        {
                            pipelineCts.Token.ThrowIfCancellationRequested();
                            byte[]? buffer = ArrayPool<byte>.Shared.Rent(FileChunkSize);
                            try
                            {
#if HINGE_TRANSFER_DIAGNOSTICS
                                long readStart = Stopwatch.GetTimestamp();
#endif
                                int bytesRead = await fs.ReadAsync(
                                    buffer.AsMemory(0, FileChunkSize),
                                    pipelineCts.Token);
#if HINGE_TRANSFER_DIAGNOSTICS
                                Interlocked.Add(ref timing.ReadTicks,
                                    Stopwatch.GetTimestamp() - readStart);
#endif
                                if (bytesRead == 0) break;

#if HINGE_TRANSFER_DIAGNOSTICS
                                long hashStart = Stopwatch.GetTimestamp();
#endif
                                streamingHash?.AppendData(buffer.AsSpan(0, bytesRead));
#if HINGE_TRANSFER_DIAGNOSTICS
                                Interlocked.Add(ref timing.HashTicks,
                                    Stopwatch.GetTimestamp() - hashStart);
#endif
                                var chunk = new PooledFileChunk(
                                    buffer,
                                    bytesRead,
                                    readOffset,
                                    chunkIndex++);
                                buffer = null;
#if HINGE_TRANSFER_DIAGNOSTICS
                                long queueStart = Stopwatch.GetTimestamp();
#endif
                                await channel.Writer.WriteAsync(chunk, pipelineCts.Token);
#if HINGE_TRANSFER_DIAGNOSTICS
                                Interlocked.Add(ref timing.QueueWaitTicks,
                                    Stopwatch.GetTimestamp() - queueStart);
#endif
                                readOffset += bytesRead;
                            }
                            finally
                            {
                                if (buffer != null)
                                {
                                    ArrayPool<byte>.Shared.Return(buffer);
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        throw;
                    }
                    finally
                    {
                        channel.Writer.TryComplete(failure);
                    }
                }

                var producerTask = ProduceChunksAsync();
                long bytesSent = offset;
                try
                {
                    await foreach (var chunk in channel.Reader.ReadAllAsync(pipelineCts.Token))
                    {
                        try
                        {
                            pipelineCts.Token.ThrowIfCancellationRequested();
                            await conn.SendFileChunkAsync(
                                transferGuid,
                                chunk.ChunkIndex,
                                chunk.Offset,
                                chunk.Buffer.AsMemory(0, chunk.Count),
                                pipelineCts.Token
#if HINGE_TRANSFER_DIAGNOSTICS
                                , timing.AddSend
#endif
                                );
                            bytesSent += chunk.Count;
#if HINGE_TRANSFER_DIAGNOSTICS
                            timing.BytesSent = bytesSent;
                            timing.Checkpoint();
#endif

                            var prog = new TransferProgress
                            {
                                TransferId = transferId,
                                FileName = fileInfo.Name,
                                BytesTransferred = bytesSent,
                                TotalBytes = fileInfo.Length,
                                BytesPerSecond = bytesSent / Math.Max(transferTimer.Elapsed.TotalSeconds, 0.001),
                                State = TransferState.Transferring
                            };
                            progress?.Report(prog);
                            TransferProgressChanged?.Invoke(this, prog);
                        }
                        finally
                        {
                            chunk.Return();
                        }
                    }

                    await producerTask;
                }
                catch
                {
                    pipelineCts.Cancel();
                    try { await producerTask; } catch { }
                    throw;
                }
                finally
                {
                    pipelineCts.Cancel();
                    channel.Writer.TryComplete();
                    while (channel.Reader.TryRead(out var pendingChunk))
                    {
                        pendingChunk.Return();
                    }
                    if (streamingHash != null)
                    {
                        completedStreamingHash = Convert.ToHexString(
                            streamingHash.GetHashAndReset()).ToLowerInvariant();
                        streamingHash.Dispose();
                    }
                }

                if (string.IsNullOrEmpty(sha256) && completedStreamingHash != null)
                {
                    sha256 = completedStreamingHash;
                }

                // Send FILE_COMPLETE
#if HINGE_TRANSFER_DIAGNOSTICS
                phaseStart = Stopwatch.GetTimestamp();
#endif
                var completeMsg = new FileCompleteMessage
                {
                    TransferId = transferId,
                    Sha256 = sha256,
                    Success = true
                };
                await conn.SendJsonAsync(MessageType.FileComplete, completeMsg);
#if HINGE_TRANSFER_DIAGNOSTICS
                timing.CompleteTicks += Stopwatch.GetTimestamp() - phaseStart;
#endif

                var finalProg = new TransferProgress
                {
                    TransferId = transferId,
                    FileName = fileInfo.Name,
                    BytesTransferred = fileInfo.Length,
                    TotalBytes = fileInfo.Length,
                    BytesPerSecond = fileInfo.Length / Math.Max(transferTimer.Elapsed.TotalSeconds, 0.001),
                    State = TransferState.Completed
                };
                progress?.Report(finalProg);
                TransferProgressChanged?.Invoke(this, finalProg);

#if HINGE_TRANSFER_DIAGNOSTICS
                sendCompleted = true;
#endif
                return transferId;
            }
        }
        finally
        {
#if HINGE_TRANSFER_DIAGNOSTICS
            timing.State = sendCompleted ? "completed" :
                cts.IsCancellationRequested ? "cancelled" : "interrupted";
            timing.Write("final");
#endif
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
            BytesReceived = existingBytes,
            // A new transfer can be verified incrementally as chunks arrive.
            // Resumed partial files keep the conservative full-file fallback
            // because the existing prefix has not been fed into this digest.
            Hash = existingBytes == 0
                ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                : null
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
            context.HashIsContiguous = false;
        }

        await context.FileStream.WriteAsync(payload.AsMemory(28, dataLength));
        if (context.Hash != null && context.HashIsContiguous)
        {
            context.Hash.AppendData(payload.AsSpan(28, dataLength));
        }
        context.BytesReceived += dataLength;

        var prog = new TransferProgress
        {
            TransferId = transferIdStr,
            FileName = context.Offer.FileName,
            BytesTransferred = context.BytesReceived,
            TotalBytes = context.Offer.FileSize,
            BytesPerSecond = context.BytesReceived / Math.Max(context.Timer.Elapsed.TotalSeconds, 0.001),
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

            // Verify SHA-256 without reading the completed file a second time
            // when chunks arrived contiguously. Resumed or out-of-order files
            // retain the full-file fallback for correctness.
            string localHash;
            if (context.Hash != null && context.HashIsContiguous)
            {
                localHash = Convert.ToHexString(context.Hash.GetHashAndReset()).ToLowerInvariant();
            }
            else
            {
                localHash = await ComputeSha256Async(context.TempFilePath, CancellationToken.None);
            }
            context.Hash?.Dispose();
            context.Hash = null;
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
                    BytesPerSecond = context.Offer.FileSize / Math.Max(context.Timer.Elapsed.TotalSeconds, 0.001),
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
            context.Hash?.Dispose();
            context.Hash = null;
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
        context.Hash?.Dispose();
        context.Hash = null;
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
            try { ctx.Hash?.Dispose(); } catch { }
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
