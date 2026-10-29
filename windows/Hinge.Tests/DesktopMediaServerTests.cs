using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Hinge.Core;

namespace Hinge.Tests;

public class DesktopMediaServerTests
{
    [Fact]
    public async Task HandshakeDisconnectReportsItsStageAndDoesNotBlockNextViewer()
    {
        using var certificate = CreateCertificate("server", "1.3.6.1.5.5.7.3.1");
        using var identity = CreateCertificate("client", "1.3.6.1.5.5.7.3.2");
        string path = Path.Combine(Path.GetTempPath(), "HingeHandshake-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string,string> { ["phone"] = DesktopPeerIdentity.Fingerprint(identity.RawData) }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new DesktopMediaServer(certificate, _ => true,
            (_, _) => Task.FromResult(false), token => Frames(() => {}, token), path);
        server.Error += (_, message) => error.TrySetResult(message);
        server.StatusChanged += (_, _) => { if (error.Task.IsCompleted && server.ActiveDeviceId == null) idle.TrySetResult(true); };
        try
        {
            server.Start();
            using (var aborted = new TcpClient())
                await aborted.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
            Assert.StartsWith("TLS安全握手：", await error.Task.WaitAsync(timeout.Token));
            await idle.Task.WaitAsync(timeout.Token);
            using var socket = new TcpClient();
            await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
            using var tls = await ConnectAsync(socket, identity, timeout.Token);
            await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Phone", mediaHeartbeat = true }, timeout.Token);
            using var ready = await ReadAsync(tls, timeout.Token);
            Assert.Equal("ready", ready.RootElement.GetProperty("state").GetString());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SlowViewerCannotBuildAnUnboundedVideoBacklog()
    {
        using var certificate = CreateCertificate("server", "1.3.6.1.5.5.7.3.1");
        using var identity = CreateCertificate("client", "1.3.6.1.5.5.7.3.2");
        string path = Path.Combine(Path.GetTempPath(), "HingeSlow-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string,string> { ["phone"] = DesktopPeerIdentity.Fingerprint(identity.RawData) }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new DesktopMediaServer(certificate, _ => true,
            (_, _) => Task.FromResult(false), LargeFrames, path);
        server.Error += (_, message) => error.TrySetResult(message);
        try
        {
            server.Start(); using var socket = new TcpClient { ReceiveBufferSize = 1024 };
            await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
            using var tls = await ConnectAsync(socket, identity, timeout.Token);
            await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Phone", mediaHeartbeat = true }, timeout.Token);
            using var ready = await ReadAsync(tls, timeout.Token);
            // Deliberately do not read pixels; the media write deadline must close this session.
            Assert.Contains("两秒", await error.Task.WaitAsync(timeout.Token));
        }
        finally { File.Delete(path); }
    }

    private static async IAsyncEnumerable<DesktopVideoPacket> LargeFrames([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] data = new byte[DesktopVideoProtocol.MaxAccessUnitSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return new(DesktopVideoCodec.H264, DesktopVideoFlags.KeyFrame, 0, 0, 1280, 720, data);
            await Task.Yield();
        }
    }

    [Fact]
    public async Task StaticVideoGetsNegotiatedHeartbeatAndStopCancelsPendingSource()
    {
        using var certificate = CreateCertificate("server", "1.3.6.1.5.5.7.3.1");
        using var identity = CreateCertificate("client", "1.3.6.1.5.5.7.3.2");
        string path = Path.Combine(Path.GetTempPath(), "HingeIdle-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Dictionary<string,string> { ["phone"] = DesktopPeerIdentity.Fingerprint(identity.RawData) }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new DesktopMediaServer(certificate, _ => true,
            (_, _) => throw new InvalidOperationException("Persisted peer must not request confirmation"), token => Frames(() => {}, token), path);
        try
        {
            server.Start(); using var socket = new TcpClient(); await socket.ConnectAsync(Socket.OSSupportsIPv6 ? "::1" : "127.0.0.1", server.Port, timeout.Token);
            using var tls = await ConnectAsync(socket, identity, timeout.Token);
            await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Phone", mediaHeartbeat = true }, timeout.Token);
            using var ready = await ReadAsync(tls, timeout.Token); Assert.True(ready.RootElement.GetProperty("mediaHeartbeat").GetBoolean());
            Assert.Equal(DesktopVideoFlags.KeyFrame, (await DesktopVideoProtocol.ReadAsync(tls, timeout.Token))!.Flags);
            var heartbeat = await DesktopVideoProtocol.ReadAsync(tls, timeout.Token);
            Assert.Equal(DesktopVideoFlags.Heartbeat, heartbeat!.Flags); Assert.Equal(1u, heartbeat.Sequence);
            await SendAsync(tls, new { action = "stop" }, timeout.Token);
            Assert.Equal(0, await tls.ReadAsync(new byte[1], timeout.Token));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LocalRefusalClosesBeforePhoneConfirmsWithoutStartingCapture()
    {
        using var certificate = CreateCertificate("server", "1.3.6.1.5.5.7.3.1");
        using var identity = CreateCertificate("client", "1.3.6.1.5.5.7.3.2");
        string path = Path.Combine(Path.GetTempPath(), "HingeRefused-" + Guid.NewGuid().ToString("N") + ".json");
        int captures = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new DesktopMediaServer(certificate, _ => true, (_, _) => Task.FromResult(false),
            token => Frames(() => Interlocked.Increment(ref captures), token), path);
        server.Start();
        using var socket = new TcpClient(); await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
        using var tls = await ConnectAsync(socket, identity, timeout.Token);
        await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Phone" }, timeout.Token);
        using var verify = await ReadAsync(tls, timeout.Token);
        Assert.Equal("verify", verify.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, await tls.ReadAsync(new byte[1], timeout.Token));
        Assert.Equal(0, captures); Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task MediaRequiresMutualConfirmationAndRejectsChangedClientIdentity()
    {
        using var certificate = CreateCertificate("server", "1.3.6.1.5.5.7.3.1");
        using var clientIdentity = CreateCertificate("client", "1.3.6.1.5.5.7.3.2");
        using var changedIdentity = CreateCertificate("changed", "1.3.6.1.5.5.7.3.2");
        string directory = Path.Combine(Path.GetTempPath(), "HingeDesktopTest-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pins.json");
        Directory.CreateDirectory(directory);
        string trustPath = Path.Combine(directory, "trust.json");
        var trust = new TrustStore(trustPath);
        trust.AddOrUpdate(new TrustedDevice { DeviceId = "phone", Name = "Test Phone" });
        var input = new MockInputInjector();
        using var manager = new RemoteInputManager(input, trust, enabled: true, encryptionRequired: true);
        int captures = 0, approvals = 0;
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new DesktopMediaServer(certificate, id => id == "phone",
            (_, _) => { Interlocked.Increment(ref approvals); return approval.Task; },
            token => Frames(() => Interlocked.Increment(ref captures), token), path, manager.BeginAuthenticatedControl);
        server.Start();
        server.Error += (_, message) => Console.WriteLine("Desktop server: " + message);
        try
        {
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
                using var tls = await ConnectAsync(socket, clientIdentity, timeout.Token);
                await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Test Phone" }, timeout.Token);
                using var verify = await ReadAsync(tls, timeout.Token);
                Assert.Equal("verify", verify.RootElement.GetProperty("state").GetString());
                Assert.Equal(DesktopPeerIdentity.VerificationCode(certificate.RawData, clientIdentity.RawData),
                    verify.RootElement.GetProperty("code").GetString());
                Assert.Equal(0, captures);
                Assert.False(File.Exists(path));
                approval.SetResult(true);
                await SendAsync(tls, new { action = "confirm" }, timeout.Token);
                using var ready = await ReadAsync(tls, timeout.Token);
                Assert.Equal("ready", ready.RootElement.GetProperty("state").GetString());
                Assert.True(ready.RootElement.GetProperty("canControl").GetBoolean());
                Assert.True(ready.RootElement.GetProperty("relativeMouse").GetBoolean());
                await SendAsync(tls, new { action = "input", data = Convert.ToBase64String(new RemoteInputEvent
                    { ActionType = RemoteActionType.MouseMove, DeltaX = 17, DeltaY = -9 }.Serialize()) }, timeout.Token);
                while (input.LastMoveX != 17) await Task.Delay(10, timeout.Token);
                Assert.Equal(-9, input.LastMoveY);
                var frame = await DesktopVideoProtocol.ReadAsync(tls, timeout.Token);
                Assert.Equal(new byte[] { 0, 0, 0, 1, 0x65 }, frame!.Data);
                Assert.True(File.Exists(path));
                await SendAsync(tls, new { action = "stop" }, timeout.Token);
                Assert.Equal(0, await tls.ReadAsync(new byte[1], timeout.Token));
            }
            Assert.Equal(1, approvals);
            Assert.Equal(1, captures);
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
                using var tls = await ConnectAsync(socket, clientIdentity, timeout.Token);
                await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Same Phone" }, timeout.Token);
                using var ready = await ReadAsync(tls, timeout.Token);
                Assert.Equal("ready", ready.RootElement.GetProperty("state").GetString());
                Assert.NotNull(await DesktopVideoProtocol.ReadAsync(tls, timeout.Token));
                await SendAsync(tls, new { action = "stop" }, timeout.Token);
                Assert.Equal(0, await tls.ReadAsync(new byte[1], timeout.Token));
            }
            Assert.Equal(1, approvals);
            Assert.Equal(2, captures);
            using (var socket = new TcpClient())
            {
                await socket.ConnectAsync("127.0.0.1", server.Port, timeout.Token);
                using var tls = await ConnectAsync(socket, changedIdentity, timeout.Token);
                await SendAsync(tls, new { version = 1, deviceId = "phone", name = "Changed Phone" }, timeout.Token);
                Assert.Equal(0, await tls.ReadAsync(new byte[1], timeout.Token));
            }
            Assert.Equal(1, approvals);
            Assert.Equal(2, captures);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(trustPath)) File.Delete(trustPath);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static async IAsyncEnumerable<DesktopVideoPacket> Frames(Action started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        started();
        yield return new(DesktopVideoCodec.H264, DesktopVideoFlags.KeyFrame, 0, 0, 1280, 720, [0, 0, 0, 1, 0x65]);
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static X509Certificate2 CreateCertificate(string name, string eku)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid(eku) }, false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        return new X509Certificate2(created.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.UserKeySet);
    }

    private static async Task<SslStream> ConnectAsync(TcpClient socket, X509Certificate2 identity, CancellationToken token)
    {
        var tls = new SslStream(socket.GetStream(), false, (_, _, _, _) => true);
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "Hinge Desktop",
                ClientCertificates = new X509CertificateCollection { identity },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => identity,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, token);
            return tls;
        }
        catch { tls.Dispose(); throw; }
    }

    private static async Task SendAsync(Stream stream, object value, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value), header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(payload, token);
    }
    private static async Task<JsonDocument> ReadAsync(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header); Assert.InRange(length, 2, 8192);
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, token); return JsonDocument.Parse(data);
    }
}
