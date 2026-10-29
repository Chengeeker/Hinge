using System.Buffers.Binary;
using Hinge.Core;

namespace Hinge.Tests;

public class DesktopVideoProtocolTests
{
    [Fact]
    public async Task HeartbeatHasBoundedDistinctPayloadAndCannotMixVideoFlags()
    {
        var heartbeat = new DesktopVideoPacket(DesktopVideoCodec.H264, DesktopVideoFlags.Heartbeat,
            7, 0, 1280, 720, [0]);
        using var stream = new MemoryStream();
        await DesktopVideoProtocol.WriteAsync(stream, heartbeat); stream.Position = 0;
        var parsed = await DesktopVideoProtocol.ReadAsync(stream);
        Assert.Equal(DesktopVideoFlags.Heartbeat, parsed!.Flags); Assert.Equal(new byte[] { 0 }, parsed.Data);
        Assert.Throws<InvalidDataException>(() => DesktopVideoProtocol.EncodeHeader(heartbeat with { Data = [1] }));
        Assert.Throws<InvalidDataException>(() => DesktopVideoProtocol.EncodeHeader(heartbeat with { Data = [0, 0] }));
        Assert.Throws<InvalidDataException>(() => DesktopVideoProtocol.EncodeHeader(heartbeat with { Flags = DesktopVideoFlags.Heartbeat | DesktopVideoFlags.KeyFrame }));
    }

    [Fact]
    public async Task FragmentedStreamPreservesMetadataAndAccessUnits()
    {
        using var stream = new FragmentedStream();
        var expected = new DesktopVideoPacket(DesktopVideoCodec.H264,
            DesktopVideoFlags.Configuration | DesktopVideoFlags.KeyFrame,
            uint.MaxValue, 123456789, 1920, 1080, [0, 0, 0, 1, 0x67, 2, 3]);
        await DesktopVideoProtocol.WriteAsync(stream, expected);
        stream.Position = 0;
        var actual = await DesktopVideoProtocol.ReadAsync(stream);
        Assert.NotNull(actual);
        Assert.Equal(expected with { Data = actual!.Data }, actual);
        Assert.Equal(expected.Data, actual.Data);
        Assert.Null(await DesktopVideoProtocol.ReadAsync(stream));
    }

    [Fact]
    public async Task UntrustedLengthsAndTruncationAreRejected()
    {
        var packet = new DesktopVideoPacket(DesktopVideoCodec.H264,
            DesktopVideoFlags.KeyFrame, 1, 0, 1280, 720, [1, 2, 3]);
        foreach (int length in new[] { -1, 0, DesktopVideoProtocol.MaxAccessUnitSize + 1 })
        {
            byte[] header = DesktopVideoProtocol.EncodeHeader(packet);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(28), length);
            using var invalid = new MemoryStream(header);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await DesktopVideoProtocol.ReadAsync(invalid));
        }
        using var truncated = new MemoryStream(DesktopVideoProtocol.EncodeHeader(packet));
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await DesktopVideoProtocol.ReadAsync(truncated));
    }

    private sealed class FragmentedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }
}
