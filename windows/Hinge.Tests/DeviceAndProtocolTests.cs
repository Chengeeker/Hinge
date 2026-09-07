using System.Buffers.Binary;
using System.Text.Json;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class DeviceModelTests
{
    [Fact]
    public void Device_Serialization_RoundTrip_PreservesProperties()
    {
        var device = new Device
        {
            DeviceId = "win-guid-001",
            Name = "Windows Workstation",
            Platform = DevicePlatform.Windows,
            AppVersion = "0.1.0",
            ProtocolVersion = "0.1",
            Capabilities = new List<string> { "file_transfer", "screen_mirror", "remote_control" },
            NetworkAddresses = new List<string> { "192.168.1.120" },
            ConnectionState = ConnectionState.Connected,
            TrustState = TrustState.Trusted
        };

        string json = JsonSerializer.Serialize(device);
        var deserialized = JsonSerializer.Deserialize<Device>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("win-guid-001", deserialized.DeviceId);
        Assert.Equal(DevicePlatform.Windows, deserialized.Platform);
        Assert.Equal(TrustState.Trusted, deserialized.TrustState);
        Assert.Contains("file_transfer", deserialized.Capabilities);
    }
}

public class ProtocolFramingTests
{
    [Fact]
    public void ProtocolFrame_Serialize_And_Parse_Succeeds()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("Hello Hinge");
        var frame = new ProtocolFrame
        {
            Version = 1,
            Type = MessageType.TextMessage,
            Payload = payload
        };

        byte[] serialized = frame.Serialize();
        bool success = ProtocolFrame.TryParse(serialized, out var parsed);

        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(MessageType.TextMessage, parsed.Type);
        Assert.Equal(payload.Length, parsed.Payload.Length);
        Assert.Equal("Hello Hinge", System.Text.Encoding.UTF8.GetString(parsed.Payload));
    }

    [Fact]
    public void ProtocolFrame_InvalidMagic_FailsParse()
    {
        byte[] corrupted = new byte[60];
        corrupted[0] = 0x00; // Invalid magic
        bool success = ProtocolFrame.TryParse(corrupted, out var parsed);

        Assert.False(success);
        Assert.Null(parsed);
    }

    [Fact]
    public void ProtocolFrame_Uses_Rfc4122_NetworkByteOrder()
    {
        var frame = new ProtocolFrame
        {
            Type = MessageType.TextMessage,
            MessageId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            SessionId = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f")
        };

        byte[] serialized = frame.Serialize();

        Assert.Equal("00112233445566778899AABBCCDDEEFF", Convert.ToHexString(serialized.AsSpan(8, 16)));
        Assert.Equal("000102030405060708090A0B0C0D0E0F", Convert.ToHexString(serialized.AsSpan(32, 16)));
        Assert.True(ProtocolFrame.TryParse(serialized, out var parsed));
        Assert.Equal(frame.MessageId, parsed!.MessageId);
        Assert.Equal(frame.SessionId, parsed.SessionId);
    }

    [Fact]
    public void ProtocolFrame_Rejects_UnsupportedPayloadLength()
    {
        var serialized = new ProtocolFrame { Type = MessageType.TextMessage }.Serialize();
        BinaryPrimitives.WriteUInt32BigEndian(serialized.AsSpan(48, 4), uint.MaxValue);

        Assert.False(ProtocolFrame.TryParse(serialized, out var parsed));
        Assert.Null(parsed);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProtocolFrame
            {
                Type = MessageType.TextMessage,
                Payload = new byte[ProtocolFrame.MaxPayloadSize + 1]
            }.Serialize());
    }
}
