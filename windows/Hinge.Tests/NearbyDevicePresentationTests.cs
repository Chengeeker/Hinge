using Hinge.Core;

namespace Hinge.Tests;

public sealed class NearbyDevicePresentationTests
{
    [Fact]
    public void ReinstalledVivoSuppressesOnlyDisconnectedDuplicateRow()
    {
        var retired = Phone("old-id", "vivo X300 Pro", ConnectionState.Disconnected);
        var fresh = Phone("current-id", "vivo X300 Pro", ConnectionState.Discovered);
        fresh.NetworkAddresses.Add("192.168.3.43");
        var other = Phone("oppo-id", "OPPO Find X9s Pro", ConnectionState.Connected);
        other.Manufacturer = "OPPO";
        other.Model = "Find X9s Pro";

        var input = new[] { retired, fresh, other };
        var visible = NearbyDevicePresentation.Visible(input, "windows-pc");
        Assert.Equal(2, visible.Count);
        Assert.DoesNotContain(retired, visible);
        Assert.Contains(fresh, visible);
        Assert.Contains(other, visible);
        // Presentation must not delete or modify the discovery registry.
        Assert.Equal(3, input.Length);
        Assert.Equal(ConnectionState.Disconnected, retired.ConnectionState);
    }

    [Fact]
    public void TwoLiveIdenticalModelsKeepSeparateDeviceIds()
    {
        var a = Phone("vivo-1", "vivo X300 Pro", ConnectionState.Connected);
        var b = Phone("vivo-2", "vivo X300 Pro", ConnectionState.Discovered);
        var visible = NearbyDevicePresentation.Visible(new[] { a, b }, "windows-pc");
        Assert.Equal(2, visible.Count);
        Assert.Contains(a, visible);
        Assert.Contains(b, visible);
    }

    [Fact]
    public void OfflineDeviceReturnsWhenNoMatchingLiveDeviceRemains()
    {
        var retired = Phone("old-id", "vivo X300 Pro", ConnectionState.Disconnected);
        var newlyOffline = Phone("new-id", "vivo X300 Pro", ConnectionState.Disconnected);
        Assert.Equal(2, NearbyDevicePresentation.Visible(
            new[] { retired, newlyOffline }, "windows-pc").Count);
    }

    [Fact]
    public void DifferentModelsNeverHideEachOther()
    {
        var old = Phone("old", "vivo X300 Pro", ConnectionState.Disconnected);
        var active = Phone("active", "vivo X200 Pro mini", ConnectionState.Connected);
        Assert.Equal(2, NearbyDevicePresentation.Visible(
            new[] { old, active }, "windows-pc").Count);
    }

    [Fact]
    public void ConflictingManufacturerOrModelMustNotBeMerged()
    {
        var retired = Phone("old", "Phone", ConnectionState.Disconnected);
        var present = Phone("new", "Phone", ConnectionState.Discovered);
        present.Manufacturer = "another";
        Assert.Equal(2, NearbyDevicePresentation.Visible(
            new[] { retired, present }, "windows-pc").Count);
        present.Manufacturer = "vivo";
        present.Model = "X200 Pro";
        Assert.Equal(2, NearbyDevicePresentation.Visible(
            new[] { retired, present }, "windows-pc").Count);
    }

    [Fact]
    public void LocalAliasesAreFilteredWithoutChangingAndroidDeviceDiscovery()
    {
        var desktop = new Device { DeviceId = "windows-pc", Name = "Windows",
            Platform = DevicePlatform.Windows };
        var alias = new Device { DeviceId = "alias", Name = "local",
            Platform = DevicePlatform.Windows };
        var android = Phone("android", "local", ConnectionState.Discovered);
        var visible = NearbyDevicePresentation.Visible(new[] { desktop, alias, android },
            "windows-pc");
        Assert.Single(visible);
        Assert.Same(android, visible[0]);
    }

    private static Device Phone(string id, string name, ConnectionState state) =>
        new()
        {
            DeviceId = id,
            Name = name,
            Manufacturer = "vivo",
            Model = name.StartsWith("vivo X300", StringComparison.OrdinalIgnoreCase)
                ? "X300 Pro" : name.StartsWith("vivo X200", StringComparison.OrdinalIgnoreCase)
                    ? "X200 Pro mini" : "Unknown",
            Platform = DevicePlatform.Android,
            ConnectionState = state
        };
}
