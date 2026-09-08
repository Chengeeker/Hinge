using System.Buffers.Binary;

namespace Hinge.Core;

public enum MessageType : ushort
{
    DeviceDiscovery = 0x0001,
    PairRequest     = 0x0002,
    PairConfirm     = 0x0003,
    SessionInit     = 0x0010,
    SessionAck      = 0x0011,
    HeartbeatPing   = 0x0012,
    HeartbeatPong   = 0x0013,
    TextMessage     = 0x0020,
    FileOffer       = 0x0030,
    FileAccept      = 0x0031,
    FileReject      = 0x0032,
    FileChunk       = 0x0033,
    FileComplete    = 0x0034,
    SyncManifestRequest  = 0x0035,
    SyncManifestResponse = 0x0036,
    SyncPullRequest      = 0x0037,
    ClipboardEvent  = 0x0040,
    RemoteInput     = 0x0050,
    ScreenStream    = 0x0060,
    NotificationEvent  = 0x0070,
    NotificationAction = 0x0071,
    ToolCommand     = 0x0080,
    ToolResult      = 0x0081,
    CompressedControl = 0x0082,
}

public class ProtocolFrame
{
    public static readonly byte[] MagicBytes = new byte[] { 0x4F, 0x53, 0x50, 0x31 }; // "OSP1"
    public const int HeaderSize = 52;
    public const int MaxPayloadSize = 16 * 1024 * 1024;

    public ushort Version { get; set; } = 1;
    public MessageType Type { get; set; }
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public Guid SessionId { get; set; } = Guid.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        if (Payload.Length > MaxPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(Payload), $"Payload exceeds the {MaxPayloadSize} byte limit.");
        }

        byte[] buffer = new byte[HeaderSize + Payload.Length];
        Span<byte> span = buffer.AsSpan();

        MagicBytes.CopyTo(span.Slice(0, 4));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), Version);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), (ushort)Type);
        ProtocolUuid.WriteNetworkBytes(MessageId, span.Slice(8, 16));
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(24, 8), Timestamp);
        ProtocolUuid.WriteNetworkBytes(SessionId, span.Slice(32, 16));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(48, 4), (uint)Payload.Length);

        if (Payload.Length > 0)
        {
            Payload.CopyTo(span.Slice(HeaderSize, Payload.Length));
        }

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out ProtocolFrame? frame)
    {
        frame = null;
        if (data.Length < HeaderSize) return false;

        if (data[0] != MagicBytes[0] || data[1] != MagicBytes[1] ||
            data[2] != MagicBytes[2] || data[3] != MagicBytes[3])
        {
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
        MessageType type = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6, 2));
        Guid messageId = ProtocolUuid.ReadNetworkBytes(data.Slice(8, 16));
        long timestamp = BinaryPrimitives.ReadInt64BigEndian(data.Slice(24, 8));
        Guid sessionId = ProtocolUuid.ReadNetworkBytes(data.Slice(32, 16));
        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(48, 4));

        if (payloadLength > MaxPayloadSize || payloadLength > int.MaxValue) return false;
        long frameLength = HeaderSize + (long)payloadLength;
        if (data.Length < frameLength) return false;

        byte[] payload = data.Slice(HeaderSize, (int)payloadLength).ToArray();

        frame = new ProtocolFrame
        {
            Version = version,
            Type = type,
            MessageId = messageId,
            Timestamp = timestamp,
            SessionId = sessionId,
            Payload = payload
        };
        return true;
    }
}

public static class ProtocolUuid
{
    public static void WriteNetworkBytes(Guid value, Span<byte> destination)
    {
        if (destination.Length < 16) throw new ArgumentException("UUID destination must be 16 bytes.", nameof(destination));
        if (!value.TryWriteBytes(destination[..16], bigEndian: true, out int bytesWritten) || bytesWritten != 16)
        {
            throw new ArgumentException("UUID destination must be 16 bytes.", nameof(destination));
        }
    }

    public static Guid ReadNetworkBytes(ReadOnlySpan<byte> source)
    {
        if (source.Length < 16) throw new ArgumentException("UUID source must be 16 bytes.", nameof(source));
        return new Guid(source[..16], bigEndian: true);
    }
}
