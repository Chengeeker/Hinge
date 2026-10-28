using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class GameRemoteInputTests
{
    [Fact]
    public void Cipher_MatchesAndroid_RejectsTamperReplayReflectionAndOldSession()
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] challenge = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        using var client = new RemoteInputCipher(key, challenge, false);
        using var server = new RemoteInputCipher(key, challenge, true);
        var plain = new byte[16]; plain[0] = 0x10; plain[1] = 87;
        var envelope = client.Encrypt(plain);
        Assert.Equal("010000000000000001ffb671f0197b2d3427a589b991b2f16f34c8715658af4f34410363b65e2ad465", Convert.ToHexString(envelope).ToLowerInvariant());
        var tampered = envelope.ToArray(); tampered[10] ^= 1;
        Assert.False(server.TryDecrypt(tampered, out _));
        Assert.True(server.TryDecrypt(envelope, out var decoded)); Assert.Equal(plain, decoded);
        Assert.False(server.TryDecrypt(envelope, out _));
        Assert.False(client.TryDecrypt(envelope, out _));
        using var next = new RemoteInputCipher(key, new byte[32], true);
        Assert.False(next.TryDecrypt(envelope, out _));
        Assert.True(client.TryDecrypt(server.Encrypt(plain), out decoded)); Assert.Equal(plain, decoded);
    }

    private sealed class Injector : IInputInjector
    {
        public System.Collections.Concurrent.ConcurrentQueue<(byte Key, bool Down)> Events = new();
        public void KeyDown(byte key) => Events.Enqueue((key, true));
        public void KeyUp(byte key) => Events.Enqueue((key, false));
        public void MoveMouse(int x, int y) { }
        public void MouseDown(RemoteMouseButton b) { }
        public void MouseUp(RemoteMouseButton b) { }
        public void MouseClick(RemoteMouseButton b) { }
        public void MouseDoubleClick() { }
        public void MouseScroll(int delta) { }
        public void SendText(string text) { }
        public void Dispose() { }
    }

    [Fact]
    public async Task SecureController_HoldsKeys_RejectsClearInput_AndReleasesOnStopDisableAndDisconnect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = await listener.AcceptTcpClientAsync();
        using var conn = new SessionConnection(accepted, new DeviceIdentity { DeviceId = "pc", Name = "PC" });
        var trustedPath = Path.Combine(Path.GetTempPath(), $"game-control-{Guid.NewGuid()}.json");
        try
        {
            var store = new TrustStore(trustedPath);
            store.AddOrUpdate(new TrustedDevice { DeviceId = "phone", Name = "Phone" });
            var injector = new Injector();
            using var manager = new RemoteInputManager(injector, store, true);
            byte[] key = RandomNumberGenerator.GetBytes(32); manager.SetControlKey(key);
            conn.Start();
            var identity = new ProtocolFrame { Type = MessageType.SessionInit, Payload = Encoding.UTF8.GetBytes("{\"deviceId\":\"phone\",\"name\":\"Phone\",\"platform\":\"android\"}") };
            await raw.GetStream().WriteAsync(identity.Serialize());
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (conn.State != SessionState.Connected && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(SessionState.Connected, conn.State);
            var request = new byte[16]; request[0] = 0x30;
            Assert.True(await manager.HandleIncomingFrameAsync(conn, new ProtocolFrame { Type = MessageType.RemoteInput, Payload = request }));
            byte[]? challenge = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var header = new byte[ProtocolFrame.HeaderSize];
            while (challenge == null)
            {
                await raw.GetStream().ReadExactlyAsync(header, timeout.Token);
                int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(48));
                var payload = new byte[length]; await raw.GetStream().ReadExactlyAsync(payload, timeout.Token);
                if (ProtocolFrame.TryCreate(header, payload, out var frame) && frame!.Type == MessageType.RemoteInput) challenge = payload.AsSpan(16).ToArray();
            }
            using var cipher = new RemoteInputCipher(key, challenge, false);
            Task<bool> Send(RemoteActionType action, byte button = 0) => manager.HandleIncomingFrameAsync(conn,
                new ProtocolFrame { Type = MessageType.SecureRemoteInput, Payload = cipher.Encrypt(new RemoteInputEvent { ActionType = action, ButtonOrKey = button }.Serialize()) });
            Assert.True(await Send(RemoteActionType.ControlStart));
            Assert.False(await manager.HandleIncomingFrameAsync(conn, new ProtocolFrame { Type = MessageType.RemoteInput, Payload = new RemoteInputEvent { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 87 }.Serialize() }));
            await Send(RemoteActionType.KeyDown, 87); await Send(RemoteActionType.KeyDown, 87); await Send(RemoteActionType.KeyDown, 68);
            Assert.Equal(new[] { ((byte)87, true), ((byte)68, true) }, injector.Events);
            await Send(RemoteActionType.ControlStop);
            Assert.Contains(((byte)87, false), injector.Events); Assert.Contains(((byte)68, false), injector.Events);
            injector.Events.Clear(); await Send(RemoteActionType.ControlStart); await Send(RemoteActionType.KeyDown, 32);
            await manager.SetEnabledAsync(false); Assert.Contains(((byte)32, false), injector.Events);
            injector.Events.Clear(); await manager.SetEnabledAsync(true); await Send(RemoteActionType.ControlStart); await Send(RemoteActionType.KeyDown, 65);
            conn.Dispose();
            deadline = DateTime.UtcNow.AddSeconds(3);
            while (!injector.Events.Contains(((byte)65, false)) && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Contains(((byte)65, false), injector.Events);
        }
        finally { if (File.Exists(trustedPath)) File.Delete(trustedPath); }
    }

    [Fact]
    public async Task OptInLanMode_AcceptsClearControlAndEndsWhenEncryptionIsReenabled()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var accepted = await listener.AcceptTcpClientAsync();
        using var conn = new SessionConnection(accepted, new DeviceIdentity { DeviceId = "pc", Name = "PC" });
        var trustedPath = Path.Combine(Path.GetTempPath(), $"game-control-lan-{Guid.NewGuid()}.json");
        try
        {
            var store = new TrustStore(trustedPath);
            store.AddOrUpdate(new TrustedDevice { DeviceId = "phone", Name = "Phone" });
            var injector = new Injector();
            using var manager = new RemoteInputManager(injector, store, enabled: true, encryptionRequired: false);
            conn.Start();
            var identity = new ProtocolFrame { Type = MessageType.SessionInit, Payload = Encoding.UTF8.GetBytes("{\"deviceId\":\"phone\",\"name\":\"Phone\",\"platform\":\"android\"}") };
            await raw.GetStream().WriteAsync(identity.Serialize());
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (conn.State != SessionState.Connected && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(SessionState.Connected, conn.State);

            Assert.True(await manager.HandleIncomingFrameAsync(conn, new ProtocolFrame
            {
                Type = MessageType.RemoteInput,
                Payload = new RemoteInputEvent { ActionType = RemoteActionType.ControlProbe }.Serialize()
            }));
            byte[]? acceptedPayload = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var stream = raw.GetStream();
            var header = new byte[ProtocolFrame.HeaderSize];
            while (acceptedPayload == null)
            {
                await stream.ReadExactlyAsync(header, timeout.Token);
                int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(48));
                var payload = new byte[length]; await stream.ReadExactlyAsync(payload, timeout.Token);
                if (ProtocolFrame.TryCreate(header, payload, out var frame) && frame!.Type == MessageType.RemoteInput)
                    acceptedPayload = payload;
            }
            Assert.True(RemoteInputEvent.TryParse(acceptedPayload, out var response));
            Assert.Equal(RemoteActionType.ControlAccepted, response!.ActionType);
            Assert.Equal(3, response.Flags);

            Assert.True(await manager.HandleIncomingFrameAsync(conn, new ProtocolFrame
            {
                Type = MessageType.RemoteInput,
                Payload = new RemoteInputEvent { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 87 }.Serialize()
            }));
            Assert.Contains(((byte)87, true), injector.Events);

            await manager.SetEncryptionRequiredAsync(true);
            Assert.Contains(((byte)87, false), injector.Events);
            Assert.False(await manager.HandleIncomingFrameAsync(conn, new ProtocolFrame
            {
                Type = MessageType.RemoteInput,
                Payload = new RemoteInputEvent { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 65 }.Serialize()
            }));
        }
        finally { if (File.Exists(trustedPath)) File.Delete(trustedPath); }
    }
}
