using Hinge.Core;
using Hinge.Platform;
using Xunit;

namespace Hinge.Tests;

public class WorkspaceTests
{
    [Fact]
    public void WorkspaceSnapshot_FormatDashboard_ContainsExpectedSections()
    {
        var snapshot = new WorkspaceSnapshot
        {
            DiscoveredDevicesCount = 3,
            TrustedDevicesCount = 2,
            ActiveConnectionsCount = 1,
            ActiveTransfersCount = 0,
            CompletedTransfersCount = 5,
            ClipboardEventsSynced = 12,
            IsScreenMirroringActive = true,
            MirrorFps = 30.0,
            MirrorBitrateBps = 2_500_000,
            NotificationsForwardedCount = 4,
            Uptime = TimeSpan.FromMinutes(42)
        };

        string dashboard = snapshot.FormatDashboard();

        Assert.Contains("WORKSPACE DASHBOARD", dashboard);
        Assert.Contains("42m", dashboard);
        Assert.Contains("3 total", dashboard);
        Assert.Contains("2 trusted", dashboard);
        Assert.Contains("12 events synced", dashboard);
        Assert.Contains("ACTIVE (30.0 FPS", dashboard);
        Assert.Contains("4 relayed", dashboard);
    }

    [Fact]
    public void WorkspaceMonitor_AggregatesMetricsFromSubsystems()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trust_ws_{Guid.NewGuid():N}.json");
        try
        {
            var registry = new DeviceRegistry();
            registry.UpsertDevice(new DiscoveryMessage { DeviceId = "dev-1", Name = "Device 1" }, "192.168.1.10");
            registry.UpsertDevice(new DiscoveryMessage { DeviceId = "dev-2", Name = "Device 2" }, "192.168.1.11");

            var trustStore = new TrustStore(tempFile);
            trustStore.AddOrUpdate(new TrustedDevice { DeviceId = "dev-1", Name = "Device 1" });

            using var renderer = new MockScreenRenderer();
            using var screenReceiver = new ScreenStreamReceiver(renderer, trustStore);

            using var monitor = new WorkspaceMonitor(
                registry: registry,
                trustStore: trustStore,
                screenReceiver: screenReceiver
            );

            var snapshot = monitor.GetSnapshot();
            Assert.Equal(2, snapshot.DiscoveredDevicesCount);
            Assert.Equal(1, snapshot.TrustedDevicesCount);
            Assert.False(snapshot.IsScreenMirroringActive);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Win32TrayManager_LifecycleAndEvents()
    {
        using var tray = new Win32TrayManager();
        Assert.False(tray.IsVisible);

        tray.Initialize("Hinge", "Initial Tooltip");
        Assert.True(tray.IsVisible);
        Assert.Equal("Initial Tooltip", tray.CurrentTooltip);

        // Reinitialization must replace the native icon instead of adding one.
        tray.Initialize("Hinge", "Reinitialized Tooltip");
        Assert.True(tray.IsVisible);
        Assert.Equal("Reinitialized Tooltip", tray.CurrentTooltip);

        tray.UpdateTooltip("Updated Tooltip");
        Assert.Equal("Updated Tooltip", tray.CurrentTooltip);

        bool openFired = false;
        bool exitFired = false;
        tray.OpenRequested += (s, e) => openFired = true;
        tray.ExitRequested += (s, e) => exitFired = true;

        tray.RequestOpen();
        tray.RequestExit();

        Assert.True(openFired);
        Assert.True(exitFired);

        tray.Dispose();
        Assert.False(tray.IsVisible);
    }
}
