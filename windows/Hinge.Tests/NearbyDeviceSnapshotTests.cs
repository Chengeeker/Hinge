using Hinge.Core;

namespace Hinge.Tests;

public sealed class NearbyDeviceSnapshotTests
{
    [Fact]
    public void BothPhonesRemainVisibleAfterAnotherDiscoveryUpdate()
    {
        var a = new Device { DeviceId = "phone-a", Name = "vivo X200 Pro mini", Platform = DevicePlatform.Android };
        var b = new Device { DeviceId = "phone-b", Name = "vivo X300 Pro", Platform = DevicePlatform.Android };
        var snapshot = NearbyDeviceSnapshot.Build(new[] { b, a }, Array.Empty<SessionPeerInfo>());
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(new[] { "phone-a", "phone-b" }, snapshot.Select(d => d.DeviceId));
        Assert.Same(a, snapshot[0]);
    }

    [Fact]
    public void ConnectedPhoneIsNotHiddenWhenUdpPacketIsMissing()
    {
        var a = new Device { DeviceId = "phone-a", Name = "Phone A", Platform = DevicePlatform.Android };
        var b = new SessionPeerInfo("phone-b", "Phone B", "android", "vivo", "X300 Pro");
        var snapshot = NearbyDeviceSnapshot.Build(new[] { a }, new[] { b });
        Assert.Equal(2, snapshot.Count);
        var connected = Assert.Single(snapshot.Where(d => d.DeviceId == "phone-b"));
        Assert.Equal(ConnectionState.Connected, connected.ConnectionState);
        Assert.Equal(DevicePlatform.Android, connected.Platform);
    }

    [Fact]
    public void SameNamedPhonesHaveSeparateRowsButDuplicateIdDoesNot()
    {
        var a = new Device { DeviceId = "phone-a", Name = "Phone", Platform = DevicePlatform.Android };
        var b = new Device { DeviceId = "phone-b", Name = "Phone", Platform = DevicePlatform.Android };
        var peer = new SessionPeerInfo("PHONE-B", "Phone", "android");
        var snapshot = NearbyDeviceSnapshot.Build(new[] { a, b }, new[] { peer });
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(new[] { "phone-a", "phone-b" }, snapshot.Select(d => d.DeviceId));
    }
}
