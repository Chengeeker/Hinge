using Hinge.App;
using System.Diagnostics;

namespace Hinge.Tests;

public class WirelessAdbDiscoveryTests
{
    [Fact]
    public void ConnectedTransportsExcludeOfflineAndUnauthorizedPhones()
    {
        var output = "List of devices attached\r\n192.168.1.4:40000\tdevice\r\n" +
            "other\tunauthorized\nold\toffline\nadb-phone._adb-tls-connect._tcp\tdevice\n";
        Assert.Equal(new[] { "192.168.1.4:40000", "adb-phone._adb-tls-connect._tcp" },
            WirelessAdbClient.ParseConnectedTransports(output));
    }

    [Fact]
    public void MdnsMatchesExactRememberedIdentityAndConnectionService()
    {
        var output = "List of discovered mdns services\n" +
            "adb-phone _adb-tls-connect._tcp 192.168.1.4:43210\n" +
            "adb-phone2 _adb-tls-connect._tcp 192.168.1.5:43211\n" +
            "adb-phone _adb-tls-pairing._tcp 192.168.1.4:12345\n" +
            "adb-phone._adb-tls-connect._tcp. _adb-tls-connect._tcp. [fe80::1]:45678\n" +
            "adb-phone _adb-tls-connect._tcp host.example:40000\n" +
            "adb-phone _adb-tls-connect._tcp 192.168.1.4:0\n";
        Assert.Equal(new[] { "192.168.1.4:43210", "[fe80::1]:45678" },
            WirelessAdbClient.ParseMdnsEndpoints(output, "adb-phone"));
        Assert.Empty(WirelessAdbClient.ParseMdnsEndpoints(output, "unknown"));
    }

    [Fact]
    public void SerialFallbackMatchesServiceSuffixWithoutMatchingAnotherPhone()
    {
        var output = "adb-phone-random _adb-tls-connect._tcp 192.168.1.4:43210\n" +
            "adb-phone2-random _adb-tls-connect._tcp 192.168.1.5:43211\n";
        Assert.Equal(new[] { "192.168.1.4:43210" },
            WirelessAdbClient.ParseMdnsEndpoints(output, "serial:phone"));
    }

    [Fact]
    public async Task EarlyBridgeExitReportsMissingClassWithoutStderrContents()
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("echo ClassNotFoundException dummy-private-text 1>&2 & exit /b 7");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WirelessAdbClipboardBridge.StartAsync(start, _ => { }, CancellationToken.None));
        Assert.Contains("退出码 7", error.Message);
        Assert.Contains("缺少剪贴板桥", error.Message);
        Assert.DoesNotContain("dummy-private-text", error.Message);
    }
}
