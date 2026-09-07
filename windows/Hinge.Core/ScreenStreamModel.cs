using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public enum ScreenStreamSubtype : byte
{
    ControlRequest  = 0x01,
    ControlResponse = 0x02,
    FrameConfig     = 0x10,
    KeyFrame        = 0x11,
    InterFrame      = 0x12,
    JpegFrame       = 0x13
}

[Flags]
public enum ScreenStreamFlags : byte
{
    None        = 0x00,
    EndOfFrame  = 0x01,
    KeyFrame    = 0x02,
    ConfigFrame = 0x04
}

public class ScreenStreamConfig
{
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;
    public int Fps { get; set; } = 60;
    public int Bitrate { get; set; } = 8_000_000;
    public string Codec { get; set; } = "H264";
    public bool NativeResolution { get; set; }
}

public class ScreenStreamControlMessage
{
    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    [JsonPropertyName("streamId")]
    public ushort StreamId { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("fps")]
    public int Fps { get; set; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("codec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Codec { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    [JsonPropertyName("nativeResolution")]
    public bool NativeResolution { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ScreenStreamControlMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScreenStreamControlMessage>(json);
        }
        catch
        {
            return null;
        }
    }
}

public class ScreenStreamPacket
{
    public const int HeaderSize = 20;

    public ScreenStreamSubtype Subtype { get; set; }
    public ScreenStreamFlags Flags { get; set; }
    public ushort StreamId { get; set; }
    public uint SequenceNumber { get; set; }
    public uint PayloadLength { get; set; }
    public ulong TimestampUs { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        int totalSize = HeaderSize + (Payload?.Length ?? 0);
        byte[] buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        span[0] = (byte)Subtype;
        span[1] = (byte)Flags;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), StreamId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), SequenceNumber);
        uint actualPayloadLen = (uint)(Payload?.Length ?? 0);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), actualPayloadLen);
        BinaryPrimitives.WriteUInt64BigEndian(span.Slice(12, 8), TimestampUs);

        if (Payload != null && Payload.Length > 0)
        {
            Payload.CopyTo(span.Slice(HeaderSize, Payload.Length));
        }

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ScreenStreamPacket? packet)
    {
        packet = null;
        if (span.Length < HeaderSize) return false;

        var subtype = (ScreenStreamSubtype)span[0];
        var flags = (ScreenStreamFlags)span[1];
        ushort streamId = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4));
        uint payloadLen = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4));
        ulong timestampUs = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(12, 8));

        int availablePayload = span.Length - HeaderSize;
        if (availablePayload < payloadLen) return false;

        byte[] payloadBytes = new byte[payloadLen];
        if (payloadLen > 0)
        {
            span.Slice(HeaderSize, (int)payloadLen).CopyTo(payloadBytes);
        }

        packet = new ScreenStreamPacket
        {
            Subtype = subtype,
            Flags = flags,
            StreamId = streamId,
            SequenceNumber = seq,
            PayloadLength = payloadLen,
            TimestampUs = timestampUs,
            Payload = payloadBytes
        };
        return true;
    }
}
