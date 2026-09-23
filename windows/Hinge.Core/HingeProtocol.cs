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
        WriteHeader(
            buffer.AsSpan(0, HeaderSize),
            Version,
            Type,
            MessageId,
            Timestamp,
            SessionId,
            Payload.Length);

        if (Payload.Length > 0)
        {
            Payload.CopyTo(buffer.AsSpan(HeaderSize, Payload.Length));
        }

        return buffer;
    }

    /// <summary>
    /// Writes only the fixed-size protocol header. Bulk senders use this to
    /// compose a pooled frame directly, avoiding a temporary FILE_CHUNK
    /// payload followed by a second full-frame allocation.
    /// </summary>
    public static void WriteHeader(
        Span<byte> destination,
        ushort version,
        MessageType type,
        Guid messageId,
        long timestamp,
        Guid sessionId,
        int payloadLength)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException($"Protocol header must be at least {HeaderSize} bytes.", nameof(destination));
        }
        if (payloadLength < 0 || payloadLength > MaxPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        }

        Span<byte> span = destination[..HeaderSize];
        MagicBytes.CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), version);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), (ushort)type);
        ProtocolUuid.WriteNetworkBytes(messageId, span.Slice(8, 16));
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(24, 8), timestamp);
        ProtocolUuid.WriteNetworkBytes(sessionId, span.Slice(32, 16));
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(48, 4), (uint)payloadLength);
    }

    /// <summary>
    /// Builds a FILE_CHUNK frame in one allocation. The caller owns the
    /// returned frame and can send it without first allocating a 28-byte
    /// transfer header plus a second frame-sized array.
    /// </summary>
    public static byte[] SerializeFileChunk(
        Guid sessionId,
        Guid transferId,
        uint chunkIndex,
        long offset,
        ReadOnlySpan<byte> data)
    {
        int payloadLength = checked(28 + data.Length);
        if (payloadLength > MaxPayloadSize)
        {
            throw new ArgumentOutOfRangeException(nameof(data), $"Payload exceeds the {MaxPayloadSize} byte limit.");
        }

        byte[] frame = new byte[HeaderSize + payloadLength];
        WriteFileChunkFrameCore(frame, sessionId, transferId, chunkIndex, offset, data);
        return frame;
    }

    /// <summary>
    /// Fills a caller-owned frame buffer with a FILE_CHUNK. Keeping the span
    /// work in this synchronous helper lets async send methods remain on the
    /// stable C# language surface supported by the project.
    /// </summary>
    public static void WriteFileChunkFrame(
        byte[] frame,
        Guid sessionId,
        Guid transferId,
        uint chunkIndex,
        long offset,
        ReadOnlyMemory<byte> data)
    {
        WriteFileChunkFrameCore(frame, sessionId, transferId, chunkIndex, offset, data.Span);
    }

    private static void WriteFileChunkFrameCore(
        byte[] frame,
        Guid sessionId,
        Guid transferId,
        uint chunkIndex,
        long offset,
        ReadOnlySpan<byte> data)
    {
        int payloadLength = checked(28 + data.Length);
        int frameLength = checked(HeaderSize + payloadLength);
        if (frame.Length < frameLength)
        {
            throw new ArgumentException("Destination frame is too small.", nameof(frame));
        }
        WriteHeader(
            frame.AsSpan(0, HeaderSize),
            version: 1,
            type: MessageType.FileChunk,
            messageId: Guid.NewGuid(),
            timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            sessionId: sessionId,
            payloadLength: payloadLength);

        Span<byte> payload = frame.AsSpan(HeaderSize, payloadLength);
        ProtocolUuid.WriteNetworkBytes(transferId, payload[..16]);
        BinaryPrimitives.WriteUInt32BigEndian(payload.Slice(16, 4), chunkIndex);
        BinaryPrimitives.WriteInt64BigEndian(payload.Slice(20, 8), offset);
        data.CopyTo(payload[28..]);
    }

    /// <summary>
    /// Creates a frame from separately read header and payload buffers. The
    /// payload is intentionally reused; the socket read loop owns it until
    /// all frame subscribers finish processing, so no 2 MiB clone is needed.
    /// </summary>
    public static bool TryCreate(
        ReadOnlySpan<byte> header,
        byte[] payload,
        out ProtocolFrame? frame)
    {
        frame = null;
        if (header.Length < HeaderSize) return false;
        if (header[0] != MagicBytes[0] || header[1] != MagicBytes[1] ||
            header[2] != MagicBytes[2] || header[3] != MagicBytes[3])
        {
            return false;
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(48, 4));
        if (payloadLength > MaxPayloadSize || payloadLength != payload.Length)
        {
            return false;
        }

        frame = new ProtocolFrame
        {
            Version = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(4, 2)),
            Type = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(header.Slice(6, 2)),
            MessageId = ProtocolUuid.ReadNetworkBytes(header.Slice(8, 16)),
            Timestamp = BinaryPrimitives.ReadInt64BigEndian(header.Slice(24, 8)),
            SessionId = ProtocolUuid.ReadNetworkBytes(header.Slice(32, 16)),
            Payload = payload
        };
        return true;
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
