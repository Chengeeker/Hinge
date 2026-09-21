using Hinge.Core;

namespace Hinge.Tests;

public sealed class WakeProtocolTests
{
    [Fact]
    public void WakePayload_ContainsStableDeviceTagAndKind()
    {
        var first = HingeWakeProtocol.CreatePayload("windows-test-device", WakeAdvertisementKind.FileTransfer);
        var second = HingeWakeProtocol.CreatePayload("windows-test-device", WakeAdvertisementKind.FileTransfer);
        var pairing = HingeWakeProtocol.CreatePayload("windows-test-device", WakeAdvertisementKind.Pairing);

        Assert.Equal(first, second);
        Assert.NotEqual(first, pairing);
        Assert.True(HingeWakeProtocol.IsWakePayload(first));
        Assert.Equal(HingeWakeProtocol.DeviceTagLength + 5, first.Length);
    }

    [Fact]
    public void WakePayload_RejectsUnknownVersionOrKind()
    {
        var payload = HingeWakeProtocol.CreatePayload("windows-test-device", WakeAdvertisementKind.FileTransfer);

        payload[3] = 99;
        Assert.False(HingeWakeProtocol.IsWakePayload(payload));

        payload = HingeWakeProtocol.CreatePayload("windows-test-device", WakeAdvertisementKind.FileTransfer);
        payload[4] = 99;
        Assert.False(HingeWakeProtocol.IsWakePayload(payload));
    }
}
