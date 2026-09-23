using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Hinge.Core;

public enum SessionState
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Suspended,
    Reconnecting
}

public sealed record SessionPeerInfo(
    string DeviceId,
    string Name,
    string Platform,
    string Manufacturer = "",
    string Model = "",
    IReadOnlyList<string>? Capabilities = null,
    bool PairingRequired = false);

public class SessionMessageEventArgs : EventArgs
{
    public ProtocolFrame Frame { get; }
    public SessionConnection Connection { get; }

    public SessionMessageEventArgs(ProtocolFrame frame, SessionConnection connection)
    {
        Frame = frame;
        Connection = connection;
    }
}

public class SessionConnection : IDisposable
{
    // Keep enough in-flight data for the 2 MiB bulk frames on a Wi-Fi link.
    // The OS may clamp or round this value; socket tuning remains best effort.
    private const int BulkSocketBufferSize = 4 * 1024 * 1024;
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AndroidHeartbeatInterval = TimeSpan.FromSeconds(20);
    private const int MaxMissedHeartbeats = 6;
    private const int AndroidSuspendedAfterMissedHeartbeats = 3;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly DeviceIdentity _localIdentity;
    private readonly string _localPairingCode;
    private readonly string _remotePairingCode;
    private readonly string _localPairingChallenge = PairingManager.CreateChallenge();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _missedHeartbeats;
    private bool _disposed;
    private bool _started;
    private bool _peerPairingRequired;
    private bool _peerPairingVerified;
    private bool _pairingProofSent;
    private bool _peerIdentifiedRaised;
    private string _peerPairingChallenge = string.Empty;

    public Guid SessionId { get; } = Guid.NewGuid();
    public SessionState State { get; private set; } = SessionState.Connecting;
    public bool IsSessionReady => State is SessionState.Connected or SessionState.Suspended;
    public bool IsOutbound { get; }
    public SessionPeerInfo? PeerInfo { get; private set; }
    public string? RemoteDeviceId => PeerInfo?.DeviceId;
    public IPAddress? RemoteAddress => (_client.Client.RemoteEndPoint as IPEndPoint)?.Address;
    public bool PeerRequiresPairing => _peerPairingRequired;
    public bool IsPairingAuthenticated => _peerPairingVerified &&
        (!_peerPairingRequired || _pairingProofSent);
    public string? PairingError { get; private set; }

    public event EventHandler<ProtocolFrame>? FrameReceived;
    public event EventHandler<SessionState>? StateChanged;
    public event EventHandler<SessionPeerInfo>? PeerIdentified;

    public SessionConnection(
        TcpClient client,
        DeviceIdentity localIdentity,
        bool isOutbound = false,
        string? localPairingCode = null,
        string? remotePairingCode = null)
    {
        _client = client;
        _localIdentity = localIdentity;
        IsOutbound = isOutbound;
        _localPairingCode = PairingManager.NormalizePairingCode(localPairingCode);
        _remotePairingCode = PairingManager.NormalizePairingCode(remotePairingCode);
        try
        {
            _client.NoDelay = true;
            _client.SendBufferSize = BulkSocketBufferSize;
            _client.ReceiveBufferSize = BulkSocketBufferSize;
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch
        {
            // Socket tuning is best effort; the session can still operate.
        }
        _stream = client.GetStream();
    }

    // Start only after SessionManager has attached all lifecycle and identity
    // handlers. Starting the read loop in the constructor allowed a fast peer
    // to send SessionInit before PeerIdentified was subscribed, leaving a
    // healthy socket with a UI that still believed it was waiting for a device.
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        _ = CompleteBackgroundSendAsync(SendSessionIdentityAsync(MessageType.SessionInit));
        _ = AuthenticationTimeoutAsync(_cts.Token);
    }

    public async Task SendFrameAsync(MessageType type, byte[] payload)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionConnection));

        var outgoingType = type;
        var outgoingPayload = payload;
        if (PeerInfo?.Capabilities?.Contains(ProtocolCompression.Capability) == true &&
            ProtocolCompression.TryCompress(type, payload, out var compressedPayload))
        {
            outgoingType = MessageType.CompressedControl;
            outgoingPayload = compressedPayload;
        }

        var frame = new ProtocolFrame
        {
            Version = 1,
            Type = outgoingType,
            SessionId = SessionId,
            Payload = outgoingPayload
        };
        byte[] data = frame.Serialize();

        await _sendLock.WaitAsync(_cts.Token);
        try
        {
            await _stream.WriteAsync(data, _cts.Token);
            // NetworkStream writes are not buffered like a file stream. A
            // flush after every 2 MiB FILE_CHUNK only adds an await without
            // improving delivery; retain the flush for small control frames.
            if (type != MessageType.FileChunk)
            {
                await _stream.FlushAsync(_cts.Token);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a FILE_CHUNK without first creating a separate chunk payload.
    /// The complete frame is rented from the shared array pool and returned
    /// only after NetworkStream.WriteAsync has finished using it.
    /// </summary>
    public async Task SendFileChunkAsync(
        Guid transferId,
        uint chunkIndex,
        long offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default,
        Action<long, long, long>? timingCallback = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionConnection));

        long frameStart = timingCallback == null ? 0 : Stopwatch.GetTimestamp();
        int payloadLength = checked(28 + data.Length);
        int frameLength = checked(ProtocolFrame.HeaderSize + payloadLength);
        byte[] frame = ArrayPool<byte>.Shared.Rent(frameLength);
        try
        {
            ProtocolFrame.WriteFileChunkFrame(
                frame,
                SessionId,
                transferId,
                chunkIndex,
                offset,
                data);

            long lockStart = timingCallback == null ? 0 : Stopwatch.GetTimestamp();
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                long writeStart = timingCallback == null ? 0 : Stopwatch.GetTimestamp();
                await _stream.WriteAsync(frame.AsMemory(0, frameLength), cancellationToken);
                timingCallback?.Invoke(lockStart - frameStart, writeStart - lockStart,
                    Stopwatch.GetTimestamp() - writeStart);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    public async Task SendJsonAsync<T>(MessageType type, T obj)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        await SendFrameAsync(type, bytes);
    }

    private Task SendSessionIdentityAsync(MessageType type) => SendJsonAsync(type, new
    {
        deviceId = _localIdentity.DeviceId,
        name = _localIdentity.Name,
        manufacturer = string.Empty,
        model = string.Empty,
        platform = "windows",
        capabilities = new[]
        {
            ProtocolCompression.Capability,
            ProtocolCompression.StreamingFileHashCapability
        },
        pairingRequired = _localPairingCode.Length > 0,
        pairingChallenge = _localPairingChallenge,
        pairingProof = string.Empty
    });

    private async Task SendSessionAckAsync()
    {
        string proof = PairingManager.CreateProof(_remotePairingCode, _peerPairingChallenge);
        if (_peerPairingRequired && proof.Length > 0) _pairingProofSent = true;

        await SendJsonAsync(MessageType.SessionAck, new
        {
            deviceId = _localIdentity.DeviceId,
            name = _localIdentity.Name,
            manufacturer = string.Empty,
            model = string.Empty,
            platform = "windows",
            capabilities = new[]
            {
                ProtocolCompression.Capability,
                ProtocolCompression.StreamingFileHashCapability
            },
            pairingRequired = _localPairingCode.Length > 0,
            pairingChallenge = _localPairingChallenge,
            pairingProof = proof
        });
        TryCompleteAuthentication();
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        byte[] headerBuffer = new byte[ProtocolFrame.HeaderSize];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int headerRead = 0;
                while (headerRead < ProtocolFrame.HeaderSize)
                {
                    int read = await _stream.ReadAsync(
                        headerBuffer.AsMemory(headerRead, ProtocolFrame.HeaderSize - headerRead), token);
                    if (read == 0) return;
                    headerRead += read;
                }

                uint payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.AsSpan(48, 4));
                if (payloadLength > ProtocolFrame.MaxPayloadSize) return;

                int payloadLengthInt = (int)payloadLength;
                byte[] payload = new byte[payloadLengthInt];
                int payloadRead = 0;
                while (payloadRead < payloadLengthInt)
                {
                    int read = await _stream.ReadAsync(
                        payload.AsMemory(payloadRead, payloadLengthInt - payloadRead), token);
                    if (read == 0) return;
                    payloadRead += read;
                }

                if (ProtocolFrame.TryCreate(headerBuffer, payload, out var frame) && frame != null)
                {
                    HandleIncomingFrame(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch
        {
            // A broken socket is surfaced through the state transition below.
        }
        finally
        {
            UpdateState(SessionState.Disconnected);
        }
    }

    private void HandleIncomingFrame(ProtocolFrame frame)
    {
        Interlocked.Exchange(ref _missedHeartbeats, 0);
        if (State == SessionState.Suspended)
        {
            UpdateState(SessionState.Connected);
        }

        if (frame.Type is MessageType.SessionInit or MessageType.SessionAck)
        {
            AcceptPeerIdentity(frame.Payload);
            if (frame.Type == MessageType.SessionInit)
            {
                _ = CompleteBackgroundSendAsync(SendSessionAckAsync());
            }
            return;
        }

        if (frame.Type == MessageType.HeartbeatPing)
        {
            _ = CompleteBackgroundSendAsync(
                SendFrameAsync(MessageType.HeartbeatPong, Array.Empty<byte>()));
            return;
        }

        if (frame.Type == MessageType.HeartbeatPong)
        {
            Interlocked.Exchange(ref _missedHeartbeats, 0);
            return;
        }

        if (frame.Type == MessageType.CompressedControl)
        {
            if (!ProtocolCompression.TryDecompress(
                    frame.Payload,
                    out var innerType,
                    out var innerPayload))
            {
                return;
            }

            HandleIncomingFrame(new ProtocolFrame
            {
                Version = frame.Version,
                Type = innerType,
                MessageId = frame.MessageId,
                Timestamp = frame.Timestamp,
                SessionId = frame.SessionId,
                Payload = innerPayload
            });
            return;
        }

        FrameReceived?.Invoke(this, frame);
    }

    private void AcceptPeerIdentity(byte[] payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            string deviceId = root.TryGetProperty("deviceId", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId == _localIdentity.DeviceId) return;

            bool pairingRequired = root.TryGetProperty("pairingRequired", out var requiredValue) &&
                requiredValue.ValueKind == JsonValueKind.True;
            string pairingChallenge = root.TryGetProperty("pairingChallenge", out var challengeValue)
                ? challengeValue.GetString() ?? string.Empty
                : string.Empty;
            string pairingProof = root.TryGetProperty("pairingProof", out var proofValue)
                ? proofValue.GetString() ?? string.Empty
                : string.Empty;

            var peer = new SessionPeerInfo(
                deviceId,
                root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "未命名设备" : "未命名设备",
                root.TryGetProperty("platform", out var platformValue) ? platformValue.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("manufacturer", out var manufacturerValue) ? manufacturerValue.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() ?? string.Empty : string.Empty,
                ReadCapabilities(root),
                pairingRequired);

            _peerPairingRequired = pairingRequired;
            if (!string.IsNullOrWhiteSpace(pairingChallenge))
            {
                _peerPairingChallenge = pairingChallenge.Trim().ToLowerInvariant();
            }
            _peerPairingVerified = _localPairingCode.Length == 0 ||
                PairingManager.VerifyProof(_localPairingCode, _localPairingChallenge, pairingProof);
            if (_localPairingCode.Length > 0 && !_peerPairingVerified)
            {
                PairingError = "本机已设置配对码，但对方未提供正确配对码。";
            }
            else if (_peerPairingRequired && _remotePairingCode.Length == 0)
            {
                PairingError = "目标设备需要输入 6 位配对码。";
            }

            if (PeerInfo?.DeviceId == peer.DeviceId)
            {
                // A duplicate SessionInit/SessionAck is normal when both
                // sides start together. Keep the session usable even if the
                // first identity frame arrived before the UI subscribed.
                PeerInfo = peer;
                TryCompleteAuthentication();
                return;
            }
            PeerInfo = peer;
            TryCompleteAuthentication();
        }
        catch (JsonException)
        {
            // Ignore malformed identity frames without dropping a healthy socket.
        }
    }

    private void TryCompleteAuthentication()
    {
        if (PeerInfo == null || _disposed || State == SessionState.Connected) return;

        if (_localPairingCode.Length > 0 && !_peerPairingVerified)
        {
            return;
        }

        if (_peerPairingRequired && !_pairingProofSent)
        {
            PairingError ??= "目标设备需要输入 6 位配对码。";
            return;
        }

        PairingError = null;
        UpdateState(SessionState.Connected);
        if (!_peerIdentifiedRaised)
        {
            _peerIdentifiedRaised = true;
            PeerIdentified?.Invoke(this, PeerInfo);
        }
    }

    private async Task AuthenticationTimeoutAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            if (State != SessionState.Connected)
            {
                PairingError ??= "设备身份或配对码验证超时。";
                Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private static IReadOnlyList<string> ReadCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan interval = string.Equals(
                        PeerInfo?.Platform,
                        "android",
                        StringComparison.OrdinalIgnoreCase)
                    ? AndroidHeartbeatInterval
                    : DefaultHeartbeatInterval;
                await Task.Delay(interval, token);
                int missed = Interlocked.Increment(ref _missedHeartbeats);
                if (string.Equals(
                        PeerInfo?.Platform,
                        "android",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Android may be in Doze while its foreground service and
                    // TCP socket remain alive. Keep the socket and let a real
                    // read/write error or FIN/RST decide disconnection.
                    if (missed >= AndroidSuspendedAfterMissedHeartbeats &&
                        State == SessionState.Connected)
                    {
                        UpdateState(SessionState.Suspended);
                    }

                    await SendFrameAsync(MessageType.HeartbeatPing, Array.Empty<byte>());
                    continue;
                }

                if (missed > MaxMissedHeartbeats)
                {
                    UpdateState(SessionState.Reconnecting);
                    Dispose();
                    return;
                }
                await SendFrameAsync(MessageType.HeartbeatPing, Array.Empty<byte>());
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch
        {
            UpdateState(SessionState.Disconnected);
            Dispose();
        }
    }

    private static async Task CompleteBackgroundSendAsync(Task sendTask)
    {
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The connection closed while a protocol acknowledgement was queued.
        }
        catch (ObjectDisposedException)
        {
            // A state check and the send itself are not atomic; disconnect wins.
        }
        catch (SocketException)
        {
            // The read/heartbeat loops publish the disconnected state.
        }
        catch (IOException)
        {
            // Socket shutdown races are expected for best-effort control frames.
        }
    }

    private void UpdateState(SessionState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _client.Close(); } catch { }
        UpdateState(SessionState.Disconnected);
    }
}

public class SessionManager : IDisposable
{
    private readonly DeviceIdentity _localIdentity;
    private readonly TrustStore _trustStore;
    private readonly int _listenPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<SessionConnection> _connections = new();
    private readonly object _lock = new();

    public bool IsListening { get; private set; }
    public string? LastError { get; private set; }
    public int ListeningPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _listenPort;

    public event EventHandler<SessionConnection>? ClientConnected;
    public event EventHandler<SessionMessageEventArgs>? MessageReceived;

    public IReadOnlyList<SessionConnection> ActiveConnections
    {
        get { lock (_lock) return _connections.Where(connection => connection.State != SessionState.Disconnected).ToList(); }
    }

    public SessionManager(
        DeviceIdentity localIdentity,
        TrustStore trustStore,
        int listenPort = Constants.SessionTcpPort,
        string? localPairingCode = null)
    {
        _localIdentity = localIdentity;
        _trustStore = trustStore;
        _listenPort = listenPort;
        _localPairingCode = PairingManager.NormalizePairingCode(localPairingCode);
    }

    private string _localPairingCode;

    public string LocalPairingCode
    {
        get => _localPairingCode;
        set => _localPairingCode = PairingManager.NormalizePairingCode(value);
    }

    public void StartListener()
    {
        if (_listener != null || IsListening) return;

        var cancellation = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Any, _listenPort);
        try
        {
            listener.Start();
            _cts = cancellation;
            _listener = listener;
            IsListening = true;
            LastError = null;
            _ = Task.Run(() => AcceptLoopAsync(cancellation.Token), cancellation.Token);
        }
        catch (Exception exception)
        {
            try { listener.Stop(); } catch { }
            cancellation.Dispose();
            _listener = null;
            _cts = null;

            // A stale Hinge process or another local service may still own
            // the well-known port. Keep the device reachable by binding an
            // ephemeral port and advertising that actual port over UDP.
            var fallbackCancellation = new CancellationTokenSource();
            var fallbackListener = new TcpListener(IPAddress.Any, 0);
            try
            {
                fallbackListener.Start();
                _cts = fallbackCancellation;
                _listener = fallbackListener;
                IsListening = true;
                LastError = $"标准端口 {_listenPort} 被占用，已切换到临时端口 {ListeningPort}";
                _ = Task.Run(() => AcceptLoopAsync(fallbackCancellation.Token), fallbackCancellation.Token);
            }
            catch (Exception fallbackException)
            {
                try { fallbackListener.Stop(); } catch { }
                fallbackCancellation.Dispose();
                _listener = null;
                _cts = null;
                IsListening = false;
                LastError = $"无法监听 TCP {_listenPort}：{exception.Message}；临时端口也不可用：{fallbackException.Message}";
            }
        }
    }

    public async Task<SessionConnection> ConnectToPeerAsync(
        IPAddress remoteIp,
        int port = Constants.SessionTcpPort,
        string? remotePairingCode = null,
        CancellationToken cancellationToken = default)
    {
        var matchingIp = NetworkInterfaceHelper.FindMatchingLocalPhysicalAddress(remoteIp);
        if (matchingIp != null)
        {
            var boundClient = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            try
            {
                boundClient.Client.Bind(new IPEndPoint(matchingIp, 0));
                await ConnectWithTimeoutAsync(boundClient, remoteIp, port, cancellationToken);
                return RegisterConnection(boundClient, isOutbound: true, remotePairingCode: remotePairingCode);
            }
            catch
            {
                boundClient.Dispose();
                // Fall back to unbound connection below
            }
        }

        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await ConnectWithTimeoutAsync(client, remoteIp, port, cancellationToken);
            return RegisterConnection(client, isOutbound: true, remotePairingCode: remotePairingCode);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ConnectWithTimeoutAsync(
        TcpClient client,
        IPAddress remoteIp,
        int port,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(remoteIp, port, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"连接 {remoteIp}:{port} 超时。");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                RegisterConnection(client, isOutbound: false, remotePairingCode: null);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private SessionConnection RegisterConnection(
        TcpClient client,
        bool isOutbound,
        string? remotePairingCode)
    {
        var connection = new SessionConnection(
            client,
            _localIdentity,
            isOutbound,
            _localPairingCode,
            remotePairingCode);
        lock (_lock) _connections.Add(connection);

        connection.FrameReceived += (_, frame) =>
            MessageReceived?.Invoke(this, new SessionMessageEventArgs(frame, connection));
        connection.PeerIdentified += (_, _) => RemoveDuplicateConnections(connection);
        connection.StateChanged += (_, state) =>
        {
            if (state != SessionState.Disconnected) return;
            lock (_lock) _connections.Remove(connection);
            connection.Dispose();
        };

        if (connection.PeerInfo != null) RemoveDuplicateConnections(connection);

        ClientConnected?.Invoke(this, connection);
        connection.Start();
        return connection;
    }

    private void RemoveDuplicateConnections(SessionConnection identifiedConnection)
    {
        string? remoteId = identifiedConnection.RemoteDeviceId;
        if (string.IsNullOrWhiteSpace(remoteId)) return;

        List<SessionConnection> duplicates;
        lock (_lock)
        {
            duplicates = _connections
                .Where(connection => connection.IsSessionReady && connection.RemoteDeviceId == remoteId)
                .ToList();
        }
        if (duplicates.Count <= 1) return;

        bool preferOutbound = string.CompareOrdinal(_localIdentity.DeviceId, remoteId) < 0;
        SessionConnection keep = duplicates.FirstOrDefault(connection => connection.IsOutbound == preferOutbound)
            ?? identifiedConnection;
        foreach (var duplicate in duplicates.Where(connection => !ReferenceEquals(connection, keep)))
        {
            duplicate.Dispose();
        }
    }

    public SessionConnection? ConnectionForDevice(string deviceId)
    {
        lock (_lock)
        {
            return _connections.FirstOrDefault(connection =>
                connection.IsSessionReady && connection.RemoteDeviceId == deviceId);
        }
    }

    public void StopListener()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        IsListening = false;

        SessionConnection[] connections;
        lock (_lock)
        {
            connections = _connections.ToArray();
            _connections.Clear();
        }
        foreach (var connection in connections) connection.Dispose();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => StopListener();
}
