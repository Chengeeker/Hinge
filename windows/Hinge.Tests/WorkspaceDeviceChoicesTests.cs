using Hinge.Core;

namespace Hinge.Tests;

public sealed class WorkspaceDeviceChoicesTests
{
    [Fact]
    public void HundredsOfOldTrustEntriesDoNotFloodActiveSelector()
    {
        var history = Enumerable.Range(0, 40)
            .Select(i => new PhoneDeviceTarget("old-" + i, "vivo X200 Pro mini", false))
            .Concat(Enumerable.Range(0, 9)
                .Select(i => new PhoneDeviceTarget("vivo-legacy-" + i, "vivo X300 Pro", false)))
            .Concat(new[]
            {
                new PhoneDeviceTarget("vivo-live", "vivo X300 Pro", true),
                new PhoneDeviceTarget("oppo-live", "OPPO Find X9s Pro", true)
            });
        var visible = WorkspaceDeviceChoices.ForSelector(history, "vivo-live");
        Assert.Equal(2, visible.Count);
        Assert.All(visible, phone => Assert.True(phone.IsConnected));
    }

    [Fact]
    public void OfflineExplicitSelectionRemainsSingleAndNeverBecomesAnotherDevice()
    {
        var sources = new[]
        {
            new PhoneDeviceTarget("a-old", "旧手机", false),
            new PhoneDeviceTarget("a-old", "旧手机", false),
            new PhoneDeviceTarget("new", "OPPO", true)
        };
        var visible = WorkspaceDeviceChoices.ForSelector(sources, "a-old");
        Assert.Equal(2, visible.Count);
        Assert.Equal("a-old", visible[1].DeviceId);
        Assert.False(visible[1].IsConnected);
    }

    [Fact]
    public void TrulyConnectedSameModelPhonesRemainDistinctByDeviceId()
    {
        var sources = new[]
        {
            new PhoneDeviceTarget("phone-1", "vivo X300 Pro", true),
            new PhoneDeviceTarget("phone-2", "vivo X300 Pro", true)
        };
        var visible = WorkspaceDeviceChoices.ForSelector(sources, "phone-1");
        Assert.Equal(2, visible.Count);
        Assert.NotEqual(visible[0].DeviceId, visible[1].DeviceId);
    }

    [Fact]
    public void NearbyLocalAliasIsVisualOnlyNotAndroidOrDiscovery()
    {
        var local = new Device { DeviceId = "desktop", Name = "Infinitycm" };
        var alias = new Device { DeviceId = "local-alias", Name = "local" };
        var phone = new Device { DeviceId = "android", Name = "local",
            Platform = DevicePlatform.Android };
        var result = NearbyDevicePresentation.Visible(new[] { local, alias, phone }, "desktop");
        Assert.Single(result);
        Assert.Same(phone, result[0]);
        Assert.Equal("192.168.3.56", NearbyDevicePresentation.DisplayAddresses(
            new[] { "local", "192.168.3.56" }));
    }
}
