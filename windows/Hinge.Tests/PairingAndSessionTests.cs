using System.Net;
using System.Text;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class PairingAndSessionTests
{
    [Fact]
    public void TrustStore_Add_IsTrusted_And_Revoke_Works()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"trust_{Guid.NewGuid()}.json");
        try
        {
            var store = new TrustStore(tempFile);
            Assert.False(store.IsTrusted("dev-001"));

            var trusted = new TrustedDevice
            {
                DeviceId = "dev-001",
                Name = "Android Tablet",
                TrustState = TrustState.Trusted
            };
            store.AddOrUpdate(trusted);

            Assert.True(store.IsTrusted("dev-001"));
            Assert.Equal("Android Tablet", store.GetDevice("dev-001")?.Name);

            // Re-load from same file to test persistence
            var store2 = new TrustStore(tempFile);
            Assert.True(store2.IsTrusted("dev-001"));

            // Revoke
            Assert.True(store2.Revoke("dev-001"));
            Assert.False(store2.IsTrusted("dev-001"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void PairingManager_Derives_Deterministic_6Digit_Pin()
    {
        string initId = "c85d7b5f-519b-4e12-8e10-3b0222a7f05a";
        string recvId = "1ab063eb-c033-46c7-b908-10763fd66229";
        string salt = "abc12345";

        int pin1 = PairingManager.DerivePin(initId, recvId, salt);
        int pin2 = PairingManager.DerivePin(initId, recvId, salt);

        Assert.Equal(pin1, pin2);
        Assert.InRange(pin1, 100000, 999999);
    }

    [Fact]
    public void PairingManager_Derives_Deterministic_6Digit_SasCode()
    {
        string secretStr = "shared-secret-key-12345";
        string nonceA = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        string nonceB = "09f8e7d6c5b4a39281706f5e4d3c2b1a";
        string pubKeyA = "pubkey_phone_alice";
        string pubKeyB = "pubkey_pc_bob";

        string sas1 = PairingManager.DeriveSasCode(secretStr, nonceA, nonceB, pubKeyA, pubKeyB);
        string sas2 = PairingManager.DeriveSasCode(secretStr, nonceA, nonceB, pubKeyA, pubKeyB);

        Assert.Equal(sas1, sas2);
        Assert.Equal(6, sas1.Length);
        Assert.True(int.TryParse(sas1, out int code));
        Assert.InRange(code, 100000, 999999);

        // Different nonce must yield different SAS code
        string nonceBAltered = "19f8e7d6c5b4a39281706f5e4d3c2b1b";
        string sasDifferent = PairingManager.DeriveSasCode(secretStr, nonceA, nonceBAltered, pubKeyA, pubKeyB);
        Assert.NotEqual(sas1, sasDifferent);
    }

    [Fact]
    public void PairingManager_Interactive_Pairing_Flow_Works()
    {
        var localId = new DeviceIdentity { DeviceId = "pc-bob", Name = "Bob PC" };
        var store = new TrustStore(Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid()}.json"));
        var mgr = new PairingManager(localId, store);

        var req = new PairRequestMessage
        {
            InitiatorDeviceId = "phone-alice",
            InitiatorName = "Alice Phone",
            InitiatorNonce = "a1b2c3d4e5f60718293a4b5c6d7e8f90",
            InitiatorPublicKey = "alice_pubkey_sample",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var confirm = mgr.CreatePairConfirm(req, accepted: true);
        Assert.True(confirm.Accepted);
        Assert.Equal(6, confirm.SasCode.Length);
        Assert.NotEmpty(confirm.ReceiverNonce);

        mgr.SaveTrustedPeer(req.InitiatorDeviceId, req.InitiatorName, req.InitiatorPublicKey);
        Assert.True(mgr.IsPeerTrusted("phone-alice"));
    }

    [Fact]
    public async Task SessionManager_Loopback_Connection_Transfers_Frame()
    {
        int port = 52860;
        var localId = new DeviceIdentity { DeviceId = "server-dev", Name = "Server" };
        var store = new TrustStore(Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid()}.json"));
        using var server = new SessionManager(localId, store, port);
        server.StartListener();

        var tcs = new TaskCompletionSource<ProtocolFrame>();
        SessionConnection? incomingConnection = null;
        server.ClientConnected += (_, connection) => incomingConnection = connection;
        server.MessageReceived += (s, e) =>
        {
            tcs.TrySetResult(e.Frame);
        };

        var clientId = new DeviceIdentity { DeviceId = "client-dev", Name = "Client" };
        using var clientSession = new SessionManager(clientId, store, port + 1);
        using var connection = await clientSession.ConnectToPeerAsync(IPAddress.Loopback, port);

        var handshakeDeadline = DateTime.UtcNow.AddSeconds(3);
        while ((incomingConnection?.RemoteDeviceId == null || connection.RemoteDeviceId == null) &&
               DateTime.UtcNow < handshakeDeadline)
        {
            await Task.Delay(20);
        }
        Assert.Equal("client-dev", incomingConnection?.RemoteDeviceId);
        Assert.Equal("server-dev", connection.RemoteDeviceId);

        byte[] payload = Encoding.UTF8.GetBytes("Hello Secure World");
        await connection.SendFrameAsync(MessageType.TextMessage, payload);

        var received = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Equal(tcs.Task, received);

        var frame = await tcs.Task;
        Assert.Equal(MessageType.TextMessage, frame.Type);
        Assert.Equal("Hello Secure World", Encoding.UTF8.GetString(frame.Payload));
    }
}
