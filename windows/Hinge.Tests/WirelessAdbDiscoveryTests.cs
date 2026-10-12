using Hinge.App;
using System.Diagnostics;

namespace Hinge.Tests;

public class WirelessAdbDiscoveryTests
{
    [Fact]
    public void EveryAdbCommandUsesHingesIsolatedServerAndOriginalUserKeyDirectory()
    {
        var startInfo = WirelessAdbClient.CreateStartInfo("devices", "-l");

        Assert.Equal(new[] { "-P", "5038", "devices", "-l" }, startInfo.ArgumentList);
        var expectedUserHome = Environment.GetEnvironmentVariable("ANDROID_USER_HOME");
        if (string.IsNullOrWhiteSpace(expectedUserHome))
            expectedUserHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android");
        Assert.Equal(expectedUserHome, startInfo.Environment["ANDROID_USER_HOME"]);
    }

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
    public void PairingAndConnectionServicesAreDiscoveredSeparately()
    {
        var output = "phone._adb-tls-pairing._tcp. _adb-tls-pairing._tcp. 192.168.1.4:42100\n" +
            "phone._adb-tls-connect._tcp. _adb-tls-connect._tcp. 192.168.1.4:42200\n";
        Assert.Equal(new[] { "192.168.1.4:42100" }, WirelessAdbClient.ParsePairingEndpoints(output));
        Assert.Equal(new[] { "192.168.1.4:42200" }, WirelessAdbClient.ParseMdnsEndpoints(output, "phone"));
        Assert.Empty(WirelessAdbClient.ParsePairingEndpoints(output, "192.168.1.5"));
    }

    [Fact]
    public void PairingGuidMdnSEndpointMatchesItsAdbIpTransport()
    {
        const string guid = "adb-phone-random";
        const string endpoint = "192.168.1.4:42200";
        var services = $"{guid} _adb-tls-connect._tcp {endpoint}\n";
        var devices = "List of devices attached\n" +
            $"{endpoint}\tdevice\n192.168.1.5:42201\tdevice\n";

        var verifiedCandidates = WirelessAdbClient.ParseMdnsEndpoints(services, guid);
        Assert.Equal(new[] { endpoint }, verifiedCandidates);
        Assert.DoesNotContain("192.168.1.5:42201", verifiedCandidates);
        Assert.Equal((endpoint, "device"), WirelessAdbClient.FindDeviceForCandidate(devices, endpoint, null, guid));
        Assert.Equal(("192.168.1.5:42201", "device"), WirelessAdbClient.FindDeviceForCandidate(
            devices, "192.168.1.5:42201", null, guid));
        Assert.Equal((guid + "-transport", "device"), WirelessAdbClient.FindDeviceForCandidate(
            $"{guid}-transport\tdevice\n", endpoint, null, guid));
        Assert.Equal((endpoint, "unauthorized"), WirelessAdbClient.FindDeviceForCandidate(
            $"{endpoint}\tunauthorized\n", endpoint, null, guid));
    }

    [Fact]
    public void IdentityMigrationPrefersStableIdentityAndKeepsPairingGuid()
    {
        Assert.Equal("serial:phone", WirelessAdbClient.ResolveStableIdentity(null, "serial:phone"));
        Assert.Equal("serial:new-phone", WirelessAdbClient.ResolveStableIdentity("serial:new-phone", "serial:old-phone"));
        Assert.Equal("serial:legacy", WirelessAdbClient.ResolveVerifiedIdentity(null, null, "serial:legacy"));
        Assert.Null(WirelessAdbClient.ResolveVerifiedIdentity(null, "adb-paired", "serial:legacy"));
        Assert.Equal("serial:current", WirelessAdbClient.ResolveVerifiedIdentity(
            "serial:current", "adb-paired", "serial:legacy"));
        Assert.Equal("adb-phone-random", WirelessAdbClient.ParsePairingGuid(
            "Successfully paired to 192.168.1.4:42100 [guid=adb-phone-random]"));
        Assert.Null(WirelessAdbClient.ParsePairingGuid("Successfully paired"));
        Assert.False(WirelessAdbClient.IsStableIdentityValue("unknown"));
        Assert.True(WirelessAdbClient.IsStableIdentityValue("serial-123"));
    }

    [Fact]
    public void PairingGuidIsNotUsedAsTheVerifiedAdbSerial()
    {
        const string pairingGuid = "adb-phone-random";
        Assert.Null(WirelessAdbClient.ResolveVerifiedIdentity(null, pairingGuid, "serial:previous-phone"));
        Assert.Equal("serial:verified-phone", WirelessAdbClient.ResolveVerifiedIdentity(
            "serial:verified-phone", pairingGuid, "serial:previous-phone"));

        var devices = "List of devices attached\n192.168.1.4:42200\tdevice product:phone model:phone\n";
        Assert.Equal(("192.168.1.4:42200", "device"), WirelessAdbClient.FindDeviceForCandidate(
            devices, "192.168.1.4:42200", null, pairingGuid));
        Assert.Equal(new[] { "192.168.1.4:42200" }, WirelessAdbClient.ParseConnectedTransports(devices));
    }

    [Fact]
    public void PairingIsRecordedOnlyWhenAdbConfirmsSuccess()
    {
        Assert.True(WirelessAdbClient.IsSuccessfulPairingResult(0,
            "Successfully paired to 192.168.1.4:42100 [guid=adb-phone-random]"));
        Assert.False(WirelessAdbClient.IsSuccessfulPairingResult(0, string.Empty));
        Assert.False(WirelessAdbClient.IsSuccessfulPairingResult(0, "error: pairing failed"));
        Assert.False(WirelessAdbClient.IsSuccessfulPairingResult(1, "Successfully paired"));
    }

    [Fact]
    public void MissingMdnsConnectServiceFallsBackToSavedConnectionPortAfterDiscoveryCandidates()
    {
        const string services = "phone._adb-tls-pairing._tcp. _adb-tls-pairing._tcp. 192.168.1.4:42100\n" +
            "other-phone._adb-tls-connect._tcp. _adb-tls-connect._tcp. 192.168.1.5:42300\n";
        var discovered = WirelessAdbClient.ParseMdnsEndpoints(services, "phone");
        var fallback = WirelessAdbClient.BuildConnectionCandidates(discovered, "192.168.1.4:42200");
        Assert.Equal(new[] { "192.168.1.4:42200" }, fallback);

        var matched = WirelessAdbClient.BuildConnectionCandidates(
            ["192.168.1.4:42300"], "192.168.1.4:42200");
        Assert.Equal(new[] { "192.168.1.4:42300", "192.168.1.4:42200" }, matched);
        Assert.DoesNotContain("192.168.1.4:42100", matched);
    }

    [Fact]
    public void ConnectionFailuresKeepDiscoveryTimeoutAuthorizationAndOfflineReasonsDistinct()
    {
        var discovery = WirelessAdbClient.ClassifyConnectionFailure(
            true, false, false, false, "ERROR: mdns daemon unavailable", "failed to connect");
        Assert.Equal(WirelessAdbConnectionState.DiscoveryFailed, discovery.State);
        Assert.Contains("mDNS 发现失败", discovery.Message);
        Assert.Contains("IP 与无线调试连接端口兜底", discovery.Message);

        Assert.Equal(WirelessAdbConnectionState.ConnectionTimedOut,
            WirelessAdbClient.ClassifyConnectionFailure(false, true, false, false, string.Empty, string.Empty).State);
        Assert.Equal(WirelessAdbConnectionState.AuthorizationInvalid,
            WirelessAdbClient.ClassifyConnectionFailure(false, false, true, false, string.Empty, string.Empty).State);
        Assert.Equal(WirelessAdbConnectionState.DeviceOffline,
            WirelessAdbClient.ClassifyConnectionFailure(false, false, false, true, string.Empty, string.Empty).State);
        Assert.True(WirelessAdbClient.IsAdbAuthorizationFailure("failed to authenticate to 192.168.1.4:42200", string.Empty));
        Assert.True(WirelessAdbClient.IsAdbOfflineResult("error: device offline", string.Empty));
    }

    [Fact]
    public void AdbWifi20RequiresNewToolingAndDeviceServiceMetadata()
    {
        Assert.True(WirelessAdbClient.SupportsAdbWifi20(
            "Android Debug Bridge version 1.0.41\nVersion 37.0.0", "mdns_service_version: \"2.0\""));
        Assert.False(WirelessAdbClient.SupportsAdbWifi20(
            "Android Debug Bridge version 1.0.41\nVersion 36.0.2", "mdns_service_version: \"2.0\""));
        Assert.False(WirelessAdbClient.SupportsAdbWifi20(
            "Android Debug Bridge version 1.0.41\nVersion 37.0.0", string.Empty));
        var runtime = WirelessAdbClient.ParseRuntimeInfo(
            "Android Debug Bridge version 1.0.41\nVersion 37.0.0",
            "version: \"37.0.0\" mdns_backend: LIBADBMDNS",
            "mdns daemon version [libadbmdns 0.0.1]",
            "mdns_service_version: \"2.0\"");
        Assert.Equal("37.0.0", runtime.PlatformToolsVersion);
        Assert.Equal("37.0.0", runtime.ServerVersion);
        Assert.Equal("LIBADBMDNS", runtime.MdnsBackend);
        Assert.True(runtime.SupportsAdbWifi20);
    }

    [Fact]
    public void ProtoMdnsOutputKeepsPerDeviceVersionAndEndpoint()
    {
        var output = "tls { service { instance: \"adb-phone-random\" service: \"_adb-tls-connect._tcp\" " +
            "ipv4: \"192.168.1.4\" port: 42200 serial: \"phone\" mdns_service_version: \"2.0\" } }";
        var service = Assert.Single(WirelessAdbClient.ParseMdnsEndpointEntries(output));
        Assert.Equal("192.168.1.4:42200", service.Endpoint);
        Assert.Equal("2.0", service.ServiceVersion);
        Assert.Equal(new[] { "192.168.1.4:42200" }, WirelessAdbClient.ParseMdnsEndpoints(output, "serial:phone"));
    }

    [Fact]
    public async Task ReconnectManagerWakesOnNetworkChangeAndSerializesAttempts()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var active = 0;
        var maxActive = 0;
        var manager = new WirelessAdbConnectionManager(async cancellationToken =>
        {
            var inFlight = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maxActive, inFlight);
            try
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    started.TrySetResult();
                    await releaseFirstAttempt.Task.WaitAsync(cancellationToken);
                    throw new WirelessAdbException(WirelessAdbConnectionState.NetworkUnavailable, "network changed");
                }
                return "192.168.1.4:42200";
            }
            finally { Interlocked.Decrement(ref active); }
        }, listenForNetworkChanges: false, getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.StatusChanged += status =>
        {
            if (status.State == WirelessAdbConnectionState.Connected) connected.TrySetResult(status.Transport!);
        };

        manager.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        manager.NotifyNetworkChanged();
        manager.NotifyNetworkChanged();
        releaseFirstAttempt.TrySetResult();
        Assert.Equal("192.168.1.4:42200", await connected.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, maxActive);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task NewManagerAfterComputerRestartReusesSavedAdbAuthorization()
    {
        var connectAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new WirelessAdbConnectionManager(
            _ =>
            {
                connectAttempted.TrySetResult();
                return Task.FromResult("192.168.1.4:42200");
            },
            listenForNetworkChanges: false,
            getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.Start();
        Assert.Equal("192.168.1.4:42200", await manager.WaitForTransportAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3)));
        await connectAttempted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task PhoneRestartIpPortAndVpnChangesReconnectToDiscoveredEndpoint()
    {
        var firstConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoveredConnection = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var phoneRestarted = 0;
        var manager = new WirelessAdbConnectionManager(
            _ => Task.FromResult(Volatile.Read(ref phoneRestarted) == 0
                ? "192.168.1.4:42200"
                : "10.8.0.7:50999"),
            listenForNetworkChanges: false,
            getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.StatusChanged += status =>
        {
            if (status.Transport == "192.168.1.4:42200") firstConnection.TrySetResult();
            if (status.Transport == "10.8.0.7:50999") recoveredConnection.TrySetResult(status.Transport);
        };
        manager.Start();
        await firstConnection.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Interlocked.Exchange(ref phoneRestarted, 1);
        manager.NotifyNetworkChanged();
        Assert.Equal("10.8.0.7:50999", await recoveredConnection.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task ReconnectManagerSurfacesWirelessDebuggingDisabledSeparately()
    {
        var disabled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new WirelessAdbConnectionManager(
            _ => throw new WirelessAdbException(WirelessAdbConnectionState.WirelessDebuggingUnavailable, "wireless debugging disabled"),
            listenForNetworkChanges: false,
            getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.StatusChanged += status =>
        {
            if (status.State == WirelessAdbConnectionState.WirelessDebuggingUnavailable) disabled.TrySetResult();
        };
        manager.Start();
        await disabled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task ReconnectManagerSurfacesRevokedAdbAuthorization()
    {
        var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new WirelessAdbConnectionManager(
            _ => throw new WirelessAdbException(WirelessAdbConnectionState.AuthorizationInvalid, "authorization revoked"),
            listenForNetworkChanges: false,
            getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.StatusChanged += status =>
        {
            if (status.State == WirelessAdbConnectionState.AuthorizationInvalid) revoked.TrySetResult();
        };
        manager.Start();
        await revoked.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task ClipboardBridgeFailureDoesNotStopAdbConnectionManager()
    {
        var manager = new WirelessAdbConnectionManager(
            _ => Task.FromResult("192.168.1.4:42200"),
            listenForNetworkChanges: false,
            getRuntimeInfo: _ => Task.FromResult(new WirelessAdbRuntimeInfo("37.0.0", "37.0.0", "libadbmdns", false, true)));
        manager.Start();
        Assert.Equal("192.168.1.4:42200", await manager.WaitForTransportAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(3)));
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("exit /b 7");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WirelessAdbClipboardBridge.StartAsync(start, _ => { }, CancellationToken.None));
        Assert.Equal("192.168.1.4:42200", manager.CurrentTransport);
        await manager.DisposeAsync();
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

internal static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref location)) &&
               Interlocked.CompareExchange(ref location, value, current) != current) { }
    }
}
