using Hinge.App;

namespace Hinge.Tests;

public sealed class WirelessAdbInventoryTests
{
    [Fact]
    public void PairUsesNativeNonInteractiveAdbArguments()
    {
        Assert.Equal(new[] { "pair", "192.168.3.8:42100", "123456" },
            WirelessAdbClient.BuildPairCommand("192.168.3.8", 42100, "123456"));
    }

    [Fact]
    public void GroupsTwoTransportsOfSamePhoneByConfirmedSerial()
    {
        var rows = WirelessAdbClient.BuildDeviceInventory(
        [
            new WirelessAdbDeviceObservation("192.168.3.56:43771", "device", "OPPO", "PME110", "3B16AA0115C00000"),
            new WirelessAdbDeviceObservation("adb-3B16AA0115C00000-xyz._adb-tls-connect._tcp", "device", "OPPO", "PME110", "3B16AA0115C00000"),
            new WirelessAdbDeviceObservation("adb-vivo._adb-tls-connect._tcp", "device", "vivo", "V2502A", "10AF9M0027002CR")
        ], Array.Empty<MdnsEndpoint>());

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Count(row => row.Connected));
        var oppo = Assert.Single(rows, row => row.Name == "OPPO PME110");
        Assert.Contains("192.168.3.56:43771", oppo.Details);
        Assert.Contains("adb-3B16AA0115C00000", oppo.Details);
    }

    [Fact]
    public void UnpairedAdvertisedDeviceIsNotMisreportedAsConnected()
    {
        var rows = WirelessAdbClient.BuildDeviceInventory(
            [new WirelessAdbDeviceObservation("adb-vivo._adb-tls-connect._tcp",
                "device", "vivo", "V2502A", "10AF9M0027002CR")],
            [new MdnsEndpoint("adb-400D4C02Z500000-DahqnR",
                "_adb-tls-connect._tcp", "192.168.3.8:46241", "")]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Count(row => row.Connected));
        var pending = Assert.Single(rows, row => !row.Connected);
        Assert.Contains("192.168.3.8:46241", pending.Details);
        Assert.Equal("已发现 · 未连接", pending.Status);
    }

    [Fact]
    public void MdnsServiceForAlreadyConnectedTlsDeviceDoesNotAddGhostRow()
    {
        var rows = WirelessAdbClient.BuildDeviceInventory(
            [new WirelessAdbDeviceObservation(
                "adb-abcd._adb-tls-connect._tcp", "device", "vivo", "V2502A", "abcd")],
            [new MdnsEndpoint("adb-abcd", "_adb-tls-connect._tcp", "192.168.3.9:43000", "")]);

        Assert.Single(rows);
        Assert.True(rows[0].Connected);
    }
}
