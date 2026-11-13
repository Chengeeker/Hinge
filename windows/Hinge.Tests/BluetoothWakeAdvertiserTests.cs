using Hinge.Core;
using Hinge.Platform;

namespace Hinge.Tests;

public class BluetoothWakeAdvertiserTests
{
    [Fact]
    public async Task InvalidDurationNeverReportsStarted()
    {
        using var advertiser = new BluetoothWakeAdvertiser();
        int starts = 0;
        Assert.False(await advertiser.PublishAsync(Guid.NewGuid().ToString(),
            WakeAdvertisementKind.Pairing, TimeSpan.Zero, CancellationToken.None, () => starts++));
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task CancelledReconnectNeverStartsPublisher()
    {
        using var advertiser = new BluetoothWakeAdvertiser();
        int starts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            advertiser.PublishAsync("windows-test-device", WakeAdvertisementKind.Reconnect,
                TimeSpan.FromSeconds(10), new CancellationToken(true), () => starts++, skipIfBusy: true));
        Assert.Equal(0, starts);
    }

}
