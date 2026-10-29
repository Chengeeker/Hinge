using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Hinge.Core;

public sealed record DesktopPairingRequest(string DeviceId, string Name, string VerificationCode);

/// <summary>Dedicated mutual-TLS video listener. A new peer cannot receive pixels before local confirmation.</summary>
public sealed class DesktopMediaServer : IAsyncDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly Func<string, bool> _isConnectedTrustedPeer;
    private readonly Func<DesktopPairingRequest, CancellationToken, Task<bool>> _approve;
    private readonly Func<CancellationToken, IAsyncEnumerable<DesktopVideoPacket>> _source;
    private readonly string _pinsPath;
    private readonly Func<string, RemoteInputManager.AuthenticatedControlLease?>? _beginControl;
    private readonly Dictionary<string, string> _pins;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _viewer = new(1, 1);
    private Task? _run;
    private Task? _activeTask;
    private string? _activeDevice;
    private int _port, _disposed;
    public int Port => Volatile.Read(ref _port);
    public string? ActiveDeviceId => Volatile.Read(ref _activeDevice);
    public event EventHandler? StatusChanged;
    public event EventHandler<string>? Error;

    public DesktopMediaServer(X509Certificate2 certificate, Func<string, bool> isConnectedTrustedPeer,
        Func<DesktopPairingRequest, CancellationToken, Task<bool>> approve,
        Func<CancellationToken, IAsyncEnumerable<DesktopVideoPacket>> source, string pinsPath,
        Func<string, RemoteInputManager.AuthenticatedControlLease?>? beginControl = null)
    {
        if (!certificate.HasPrivateKey) throw new ArgumentException("Desktop server certificate needs a private key.");
        _certificate = certificate;
        _isConnectedTrustedPeer = isConnectedTrustedPeer;
        _approve = approve;
        _source = source;
        _pinsPath = pinsPath;
        _beginControl = beginControl;
        if (File.Exists(pinsPath) && new FileInfo(pinsPath).Length > 1024 * 1024)
            throw new InvalidDataException("Desktop peer trust file exceeds limit.");
        _pins = File.Exists(pinsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(pinsPath))
                ?? throw new InvalidDataException("Invalid desktop peer trust file.")
            : new();
        if (_pins.Count > 256 || _pins.Any(pair => !ValidId(pair.Key) || !ValidPin(pair.Value)))
            throw new InvalidDataException("Invalid desktop peer trust entries.");
        _listener = new TcpListener(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        if (Socket.OSSupportsIPv6) _listener.Server.DualMode = true;
    }

    public void Start()
    {
        if (_run != null) throw new InvalidOperationException("Desktop listener already started.");
        _stop.Token.ThrowIfCancellationRequested();
        _listener.Start(4);
        Volatile.Write(ref _port, ((IPEndPoint)_listener.LocalEndpoint).Port);
        _run = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (!_viewer.Wait(0)) { client.Dispose(); continue; }
                _activeTask = ServeAsync(client);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var session = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            string stage = "TLS安全握手";
            try
            {
                client.NoDelay = true;
                client.SendBufferSize = 64 * 1024;
                using var tls = new SslStream(client.GetStream(), false,
                    (_, certificate, _, _) => certificate != null);
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                {
                    handshake.CancelAfter(TimeSpan.FromSeconds(10));
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate, ClientCertificateRequired = true,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    }, handshake.Token);
                }
                stage = "读取手机桌面请求";
                using var peer = new X509Certificate2(tls.RemoteCertificate
                    ?? throw new AuthenticationException("Missing desktop client identity."));
                if (peer.NotAfter.ToUniversalTime() <= DateTime.UtcNow || peer.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                    throw new AuthenticationException("Desktop client identity expired.");
                using var pairing = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                pairing.CancelAfter(TimeSpan.FromSeconds(60));
                using var hello = await ReadJsonAsync(tls, pairing.Token);
                if (hello.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported desktop version.");
                string id = hello.RootElement.GetProperty("deviceId").GetString() ?? "";
                string name = hello.RootElement.GetProperty("name").GetString() ?? "Android 手机";
                if (!ValidId(id) || name.Length is < 1 or > 128 || name.Any(char.IsControl) || !_isConnectedTrustedPeer(id))
                    throw new AuthenticationException("Desktop peer is not an allowed connected Hinge device.");
                string pin = DesktopPeerIdentity.Fingerprint(peer.RawData);
                bool known = _pins.TryGetValue(id, out string? knownPin);
                if (known && knownPin != pin) throw new AuthenticationException("Desktop peer identity changed. Explicitly reset pairing first.");
                bool verifyAgain = hello.RootElement.TryGetProperty("verifyIdentity", out var verifyProperty) && verifyProperty.GetBoolean();
                if (!known || verifyAgain)
                {
                    stage = "核对桌面身份";
                    if (!known && _pins.Count >= 256) throw new AuthenticationException("Desktop peer trust limit reached.");
                    string code = DesktopPeerIdentity.VerificationCode(_certificate.RawData, peer.RawData);
                    await WriteJsonAsync(tls, new { state = "verify", code }, pairing.Token);
                    Task<bool> local = _approve(new(id, name, code), pairing.Token);
                    Task<JsonDocument> confirmationTask = ReadJsonAsync(tls, pairing.Token);
                    try
                    {
                        var first = await Task.WhenAny(local, confirmationTask).WaitAsync(pairing.Token);
                        if (first == local && !await local.WaitAsync(pairing.Token))
                            throw new AuthenticationException("Desktop pairing was refused locally.");
                        using var confirmation = await confirmationTask;
                        if (confirmation.RootElement.GetProperty("action").GetString() != "confirm" ||
                            !await local.WaitAsync(pairing.Token)) throw new AuthenticationException("Desktop pairing was not confirmed.");
                    }
                    finally
                    {
                        pairing.Cancel();
                        try { using var remaining = await confirmationTask; }
                        catch (Exception error) when (error is OperationCanceledException or IOException or JsonException) { }
                    }
                    _pins[id] = pin;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_pinsPath))!);
                        await File.WriteAllTextAsync(_pinsPath + ".tmp", JsonSerializer.Serialize(_pins), session.Token);
                        File.Move(_pinsPath + ".tmp", _pinsPath, true);
                    }
                    catch { if (known) _pins[id] = knownPin!; else _pins.Remove(id); throw; }
                }
                if (!_isConnectedTrustedPeer(id)) throw new AuthenticationException("Hinge peer disconnected during pairing.");
                stage = "桌面视频传输";
                using var control = _beginControl?.Invoke(id);
                bool mediaHeartbeat = hello.RootElement.TryGetProperty("mediaHeartbeat", out var heartbeatProperty) && heartbeatProperty.GetBoolean();
                await WriteJsonAsync(tls, new { state = "ready", version = 1, canControl = control != null,
                    relativeMouse = control != null, absoluteMouse = control != null, mediaHeartbeat }, session.Token);
                Volatile.Write(ref _activeDevice, id); StatusChanged?.Invoke(this, EventArgs.Empty);
                Task video = PumpAsync(tls, mediaHeartbeat, session.Token);
                Task heartbeat = WatchViewerAsync(tls, id, control, session.Token);
                try { await await Task.WhenAny(video, heartbeat); }
                finally
                {
                    session.Cancel();
                    try { await Task.WhenAll(video, heartbeat); }
                    catch (Exception exception) when (exception is OperationCanceledException or IOException or AuthenticationException or TimeoutException) { }
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or
                OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or TimeoutException)
            {
                if (!_stop.IsCancellationRequested && exception is not OperationCanceledException)
                    Error?.Invoke(this, stage + "：" + exception.Message);
            }
            finally
            {
                session.Cancel(); Volatile.Write(ref _activeDevice, null);
                _viewer.Release(); StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task PumpAsync(Stream stream, bool mediaHeartbeat, CancellationToken cancellationToken)
    {
        using var sourceStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var source = _source(sourceStop.Token).GetAsyncEnumerator(sourceStop.Token);
        Task<bool>? pending = null;
        uint sequence = 0;
        int width = 2, height = 2;
        try
        {
            while (true)
            {
                pending ??= source.MoveNextAsync().AsTask();
                DesktopVideoPacket packet;
                try
                {
                    bool next = mediaHeartbeat
                        ? await pending.WaitAsync(TimeSpan.FromSeconds(1), sourceStop.Token)
                        : await pending;
                    if (!next) return;
                    packet = source.Current with { Sequence = sequence++ };
                    width = packet.Width; height = packet.Height;
                    pending = null;
                }
                catch (TimeoutException) when (pending is { IsFaulted: false, IsCanceled: false })
                {
                    packet = new(DesktopVideoCodec.H264, DesktopVideoFlags.Heartbeat, sequence++, 0, width, height, [0]);
                }
                using var write = CancellationTokenSource.CreateLinkedTokenSource(sourceStop.Token);
                write.CancelAfter(TimeSpan.FromSeconds(2));
                try { await DesktopVideoProtocol.WriteAsync(stream, packet, write.Token); }
                catch (OperationCanceledException) when (!sourceStop.IsCancellationRequested)
                {
                    throw new IOException("桌面画面发送超过两秒，请降低清晰度或码率后重连。");
                }
            }
        }
        finally
        {
            sourceStop.Cancel();
            if (pending != null)
            {
                try { await pending; }
                catch (Exception error) when (error is OperationCanceledException or IOException) { }
            }
        }
    }

    private async Task WatchViewerAsync(Stream stream, string id, RemoteInputManager.AuthenticatedControlLease? control, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var message = await ReadJsonAsync(stream, timeout.Token);
            string? action = message.RootElement.GetProperty("action").GetString();
            if (!_isConnectedTrustedPeer(id) || action == "stop") return;
            if (action == "input")
            {
                byte[] bytes = message.RootElement.GetProperty("data").GetBytesFromBase64();
                if (bytes.Length is < 16 or > 4096 || !RemoteInputEvent.TryParse(bytes, out var input) || input == null)
                    throw new InvalidDataException("Invalid desktop input payload.");
                if (input.ActionType == RemoteActionType.ControlStop) return;
                if (control == null || !control.Send(input)) throw new AuthenticationException("Desktop input permission ended.");
            }
            else if (action == "keepAlive")
            {
                if (control != null && !control.Send(new RemoteInputEvent { ActionType = RemoteActionType.ControlKeepAlive })) return;
            }
            else
                throw new InvalidDataException("Unexpected desktop viewer message.");
        }
    }

    internal static async Task<JsonDocument> ReadJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > 8192) throw new InvalidDataException("Desktop handshake exceeds limit.");
        byte[] payload = new byte[length]; await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
    }

    private static async Task WriteJsonAsync(Stream stream, object value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken); await stream.WriteAsync(payload, cancellationToken);
    }
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl);
    private static bool ValidPin(string? pin) => pin?.Length == 64 && pin.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _port, 0);
        _stop.Cancel(); _listener.Stop();
        if (_run != null) await _run;
        if (_activeTask != null) await _activeTask;
        _stop.Dispose(); _viewer.Dispose();
    }
}
