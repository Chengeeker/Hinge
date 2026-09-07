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
    Reconnecting
}

public sealed record SessionPeerInfo(
    string DeviceId,
    string Name,
    string Platform,
    string Manufacturer = "",
    string Model = "");

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
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly DeviceIdentity _localIdentity;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _missedHeartbeats;
    private bool _disposed;
    private bool _started;

    public Guid SessionId { get; } = Guid.NewGuid();
    public SessionState State { get; private set; } = SessionState.Connecting;
    public bool IsSessionReady => State == SessionState.Connected ||
        (State != SessionState.Disconnected && PeerInfo != null);
    public bool IsOutbound { get; }
    public SessionPeerInfo? PeerInfo { get; private set; }
    public string? RemoteDeviceId => PeerInfo?.DeviceId;
    public IPAddress? RemoteAddress => (_client.Client.RemoteEndPoint as IPEndPoint)?.Address;

    public event EventHandler<ProtocolFrame>? FrameReceived;
    public event EventHandler<SessionState>? StateChanged;
    public event EventHandler<SessionPeerInfo>? PeerIdentified;

    public SessionConnection(TcpClient client, DeviceIdentity localIdentity, bool isOutbound = false)
    {
        _client = client;
        _localIdentity = localIdentity;
        IsOutbound = isOutbound;
        try
        {
            _client.NoDelay = true;
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
        _ = SendSessionIdentityAsync(MessageType.SessionInit);
    }

    public async Task SendFrameAsync(MessageType type, byte[] payload)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionConnection));

        var frame = new ProtocolFrame
        {
            Version = 1,
            Type = type,
            SessionId = SessionId,
            Payload = payload
        };
        byte[] data = frame.Serialize();

        await _sendLock.WaitAsync(_cts.Token);
        try
        {
            await _stream.WriteAsync(data, _cts.Token);
            await _stream.FlushAsync(_cts.Token);
        }
        finally
        {
            _sendLock.Release();
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
        platform = "windows"
    });

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
                byte[] fullBuffer = new byte[ProtocolFrame.HeaderSize + payloadLengthInt];
                Buffer.BlockCopy(headerBuffer, 0, fullBuffer, 0, ProtocolFrame.HeaderSize);

                int payloadRead = 0;
                while (payloadRead < payloadLengthInt)
                {
                    int read = await _stream.ReadAsync(
                        fullBuffer.AsMemory(ProtocolFrame.HeaderSize + payloadRead, payloadLengthInt - payloadRead), token);
                    if (read == 0) return;
                    payloadRead += read;
                }

                if (ProtocolFrame.TryParse(fullBuffer, out var frame) && frame != null)
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
        if (frame.Type is MessageType.SessionInit or MessageType.SessionAck)
        {
            AcceptPeerIdentity(frame.Payload);
            if (frame.Type == MessageType.SessionInit)
            {
                _ = SendSessionIdentityAsync(MessageType.SessionAck);
            }
            return;
        }

        if (frame.Type == MessageType.HeartbeatPing)
        {
            _ = SendFrameAsync(MessageType.HeartbeatPong, Array.Empty<byte>());
            return;
        }

        if (frame.Type == MessageType.HeartbeatPong)
        {
            Interlocked.Exchange(ref _missedHeartbeats, 0);
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

            var peer = new SessionPeerInfo(
                deviceId,
                root.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "未命名设备" : "未命名设备",
                root.TryGetProperty("platform", out var platformValue) ? platformValue.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("manufacturer", out var manufacturerValue) ? manufacturerValue.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() ?? string.Empty : string.Empty);

            if (PeerInfo?.DeviceId == peer.DeviceId)
            {
                // A duplicate SessionInit/SessionAck is normal when both
                // sides start together. Keep the session usable even if the
                // first identity frame arrived before the UI subscribed.
                UpdateState(SessionState.Connected);
                return;
            }
            PeerInfo = peer;
            UpdateState(SessionState.Connected);
            PeerIdentified?.Invoke(this, peer);
        }
        catch (JsonException)
        {
            // Ignore malformed identity frames without dropping a healthy socket.
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                int missed = Interlocked.Increment(ref _missedHeartbeats);
                if (missed > 3)
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

    public SessionManager(DeviceIdentity localIdentity, TrustStore trustStore, int listenPort = Constants.SessionTcpPort)
    {
        _localIdentity = localIdentity;
        _trustStore = trustStore;
        _listenPort = listenPort;
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

    public async Task<SessionConnection> ConnectToPeerAsync(IPAddress remoteIp, int port = Constants.SessionTcpPort)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await client.ConnectAsync(remoteIp, port).WaitAsync(TimeSpan.FromSeconds(3));
            return RegisterConnection(client, isOutbound: true);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                RegisterConnection(client, isOutbound: false);
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

    private SessionConnection RegisterConnection(TcpClient client, bool isOutbound)
    {
        var connection = new SessionConnection(client, _localIdentity, isOutbound);
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
                .Where(connection => connection.State == SessionState.Connected && connection.RemoteDeviceId == remoteId)
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
                connection.State == SessionState.Connected && connection.RemoteDeviceId == deviceId);
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
