using System.Buffers.Binary;

namespace Hinge.Core;

public enum DesktopVideoCodec : byte { H264 = 1, H265 = 2 }

[Flags]
public enum DesktopVideoFlags : byte { None = 0, Configuration = 1, KeyFrame = 2, Heartbeat = 4 }

/// <summary>One complete Annex B access unit on the dedicated authenticated media stream.</summary>
public sealed record DesktopVideoPacket(DesktopVideoCodec Codec, DesktopVideoFlags Flags,
    uint Sequence, long PresentationTimeUs, int Width, int Height, byte[] Data);

public static class DesktopVideoProtocol
{
    public const int HeaderSize = 32;
    public const int MaxAccessUnitSize = 4 * 1024 * 1024;
    public const int MaxDimension = 8192;

    public static byte[] EncodeHeader(DesktopVideoPacket packet)
    {
        Validate(packet.Codec, packet.Flags, packet.PresentationTimeUs,
            packet.Width, packet.Height, packet.Data.Length);
        if (packet.Flags == DesktopVideoFlags.Heartbeat && packet.Data[0] != 0)
            throw new InvalidDataException("Invalid desktop heartbeat payload.");
        byte[] header = new byte[HeaderSize];
        "HDV1"u8.CopyTo(header);
        header[4] = (byte)packet.Codec;
        header[5] = (byte)packet.Flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), packet.Sequence);
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(12), packet.PresentationTimeUs);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), packet.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), packet.Height);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(28), packet.Data.Length);
        return header;
    }

    // A single writer owns a stream; callers must serialize complete packet writes.
    public static async ValueTask WriteAsync(Stream stream, DesktopVideoPacket packet,
        CancellationToken cancellationToken = default)
    {
        await stream.WriteAsync(EncodeHeader(packet), cancellationToken);
        await stream.WriteAsync(packet.Data, cancellationToken);
    }

    public static async ValueTask<DesktopVideoPacket?> ReadAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        if (await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        if (!header.AsSpan(0, 4).SequenceEqual("HDV1"u8) || header[6] != 0 || header[7] != 0)
            throw new InvalidDataException("Unsupported desktop video header.");
        var codec = (DesktopVideoCodec)header[4];
        var flags = (DesktopVideoFlags)header[5];
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        long pts = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(12));
        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24));
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(28));
        Validate(codec, flags, pts, width, height, length);
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data, cancellationToken);
        if (flags == DesktopVideoFlags.Heartbeat && data[0] != 0)
            throw new InvalidDataException("Invalid desktop heartbeat payload.");
        return new(codec, flags, sequence, pts, width, height, data);
    }

    private static void Validate(DesktopVideoCodec codec, DesktopVideoFlags flags,
        long pts, int width, int height, int length)
    {
        if (codec is not (DesktopVideoCodec.H264 or DesktopVideoCodec.H265) ||
            (flags & ~(DesktopVideoFlags.Configuration | DesktopVideoFlags.KeyFrame | DesktopVideoFlags.Heartbeat)) != 0 ||
            ((flags & DesktopVideoFlags.Heartbeat) != 0 && (flags != DesktopVideoFlags.Heartbeat || length != 1)) ||
            pts < 0 || width < 2 || height < 2 || width > MaxDimension || height > MaxDimension ||
            (width & 1) != 0 || (height & 1) != 0 || length < 1 || length > MaxAccessUnitSize)
            throw new InvalidDataException("Invalid desktop video packet metadata.");
    }
}
