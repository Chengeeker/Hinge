using System.Buffers.Binary;
using System.IO.Compression;

namespace Hinge.Core;

/// <summary>
/// Optional compression for large JSON/control payloads.
///
/// The envelope is deliberately a new message type instead of a header flag so
/// that protocol v1 peers can continue to parse ordinary frames unchanged.
/// Compression is only enabled after the peer advertises the same capability
/// during the session identity exchange.
/// </summary>
public static class ProtocolCompression
{
    public const string Capability = "control-compression-zlib-v1";
    public const byte EnvelopeVersion = 1;
    public const int EnvelopeHeaderSize = 8;
    private const int MinimumPayloadSize = 2048;

    private static readonly MessageType[] CompressibleTypes =
    {
        MessageType.TextMessage,
        MessageType.SyncManifestRequest,
        MessageType.SyncManifestResponse,
        MessageType.SyncPullRequest,
        MessageType.ClipboardEvent,
        MessageType.NotificationEvent,
        MessageType.NotificationAction,
        MessageType.ToolCommand,
        MessageType.ToolResult
    };

    public static bool IsCompressible(MessageType type, int payloadLength) =>
        payloadLength >= MinimumPayloadSize &&
        Array.IndexOf(CompressibleTypes, type) >= 0;

    public static bool TryCompress(
        MessageType type,
        byte[] payload,
        out byte[] compressedPayload)
    {
        compressedPayload = Array.Empty<byte>();
        if (!IsCompressible(type, payload.Length) ||
            payload.Length > ProtocolFrame.MaxPayloadSize)
        {
            return false;
        }

        using var output = new MemoryStream();
        try
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(payload, 0, payload.Length);
            }
        }
        catch (Exception)
        {
            // Compression is an optimization. A runtime/provider failure must
            // never prevent the uncompressed control frame from being sent.
            return false;
        }

        byte[] compressed = output.ToArray();
        if (compressed.Length + EnvelopeHeaderSize >= payload.Length)
        {
            return false;
        }

        compressedPayload = new byte[EnvelopeHeaderSize + compressed.Length];
        compressedPayload[0] = EnvelopeVersion;
        compressedPayload[1] = 0; // Reserved flags; must remain zero for v1.
        BinaryPrimitives.WriteUInt16BigEndian(
            compressedPayload.AsSpan(2, 2),
            (ushort)type);
        BinaryPrimitives.WriteUInt32BigEndian(
            compressedPayload.AsSpan(4, 4),
            (uint)payload.Length);
        compressed.CopyTo(compressedPayload.AsSpan(EnvelopeHeaderSize));
        return true;
    }

    public static bool TryDecompress(
        byte[] envelope,
        out MessageType type,
        out byte[] payload)
    {
        type = MessageType.ToolResult;
        payload = Array.Empty<byte>();

        if (envelope.Length <= EnvelopeHeaderSize ||
            envelope[0] != EnvelopeVersion ||
            envelope[1] != 0)
        {
            return false;
        }

        type = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(
            envelope.AsSpan(2, 2));
        uint expectedLength = BinaryPrimitives.ReadUInt32BigEndian(
            envelope.AsSpan(4, 4));
        if (!IsCompressible(type, (int)Math.Min(expectedLength, int.MaxValue)) ||
            expectedLength > ProtocolFrame.MaxPayloadSize)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(
                envelope,
                EnvelopeHeaderSize,
                envelope.Length - EnvelopeHeaderSize,
                writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)expectedLength);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > expectedLength)
                {
                    return false;
                }
                output.Write(buffer, 0, read);
            }

            if (output.Length != expectedLength)
            {
                return false;
            }

            payload = output.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
