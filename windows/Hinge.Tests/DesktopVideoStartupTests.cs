using Hinge.Core;
using Hinge.Platform;

namespace Hinge.Tests;

public class DesktopVideoStartupTests
{
    private sealed class StallingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private static byte[] Packet(DesktopVideoFlags flags)
    {
        var packet = new DesktopVideoPacket(DesktopVideoCodec.H264, flags, 0, 0, 640, 480, [0]);
        return [.. DesktopVideoProtocol.EncodeHeader(packet), .. packet.Data];
    }

    [Theory]
    [InlineData(DesktopVideoFlags.Configuration)]
    [InlineData(DesktopVideoFlags.Heartbeat)]
    public async Task ConfigurationOrHeartbeatCannotKeepStartupAlive(DesktopVideoFlags flags)
    {
        using var stream = new StallingStream(Packet(flags));
        await using var packets = NativeDesktopVideoSource.ReadPacketsAsync(stream, TimeSpan.FromMilliseconds(100)).GetAsyncEnumerator();
        Assert.True(await packets.MoveNextAsync());
        await Assert.ThrowsAsync<TimeoutException>(async () => await packets.MoveNextAsync());
    }

    [Fact]
    public async Task MissingFirstPacketTimesOut()
    {
        using var stream = new StallingStream([]);
        await using var packets = NativeDesktopVideoSource.ReadPacketsAsync(stream, TimeSpan.FromMilliseconds(100)).GetAsyncEnumerator();
        await Assert.ThrowsAsync<TimeoutException>(async () => await packets.MoveNextAsync());
    }

    [Theory]
    [InlineData(DesktopVideoFlags.KeyFrame)]
    [InlineData(DesktopVideoFlags.Configuration | DesktopVideoFlags.KeyFrame)]
    public async Task StaticDesktopAfterVideoUsesCallerCancellation(DesktopVideoFlags flags)
    {
        using var stream = new StallingStream(Packet(flags));
        using var stop = new CancellationTokenSource();
        await using var packets = NativeDesktopVideoSource.ReadPacketsAsync(stream, TimeSpan.FromMilliseconds(100), stop.Token).GetAsyncEnumerator();
        Assert.True(await packets.MoveNextAsync());
        var pending = packets.MoveNextAsync().AsTask();
        await Task.Delay(180);
        Assert.False(pending.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsStartupFailure()
    {
        using var stream = new StallingStream([]);
        using var stop = new CancellationTokenSource();
        await using var packets = NativeDesktopVideoSource.ReadPacketsAsync(stream, TimeSpan.FromSeconds(8), stop.Token).GetAsyncEnumerator();
        var pending = packets.MoveNextAsync().AsTask();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }
}
