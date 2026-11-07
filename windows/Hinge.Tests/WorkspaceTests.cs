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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TrayMenu_RoundedBoundsKeepCenterAndClipCorners(int scale)
    {
        using var path = TrayMenu.Rounded(new System.Drawing.Rectangle(0, 0, 232 * scale, 136 * scale), 12 * scale);
        Assert.False(path.IsVisible(1, 1));
        Assert.True(path.IsVisible(116 * scale, 68 * scale));
        Assert.True(path.IsVisible(116 * scale, 1));
        Assert.False(path.IsVisible(233 * scale, 68 * scale));
    }

    [Fact]
    public void TrayMenu_LayoutKeepsWidthInsetsAndWhiteSurface()
    {
        using var menu = new TrayMenu();
        foreach (var label in new[] { "打开 Hinge", "传输文件", "退出" })
            menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem(label));
        menu.PrepareForDisplay();
        menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
        menu.PerformLayout();
        var scale = menu.DeviceDpi / 96d;
        Assert.InRange(menu.Width / scale, 80, 160);
        Assert.True(menu.Padding.Top >= Math.Round(8 * scale));
        Assert.True(menu.Padding.Bottom >= Math.Round(8 * scale));
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
        {
            Assert.True(item.Bounds.Left >= menu.Padding.Left, $"menu={menu.Size} padding={menu.Padding} item={item.Bounds} available={item.Available}");
            Assert.True(item.Bounds.Right <= menu.Width - menu.Padding.Right);
            Assert.True(item.Height >= Math.Round(32 * scale));
        }
        Assert.True(menu.Items[0].Bounds.Top >= menu.Padding.Top);
        Assert.True(menu.Items[^1].Bounds.Bottom <= menu.Height - menu.Padding.Bottom);
        if (!System.Windows.Forms.SystemInformation.HighContrast)
            Assert.Equal(System.Drawing.Color.White, menu.BackColor);
        menu.Items[1].Select();
        using var bitmap = new System.Drawing.Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, menu.Size));
        if (!System.Windows.Forms.SystemInformation.HighContrast)
        {
            var row = menu.Items[1].Bounds;
            Assert.Equal(System.Drawing.Color.White.ToArgb(), bitmap.GetPixel(1, row.Top + row.Height / 2).ToArgb());
            Assert.Equal(System.Drawing.Color.White.ToArgb(), bitmap.GetPixel(menu.Width - 2, row.Top + row.Height / 2).ToArgb());
            Assert.Equal(System.Drawing.Color.FromArgb(243, 244, 245).ToArgb(), bitmap.GetPixel(row.Right - 16, row.Top + row.Height / 2).ToArgb());
            var first = menu.Items[0].Bounds;
            var ink = new List<System.Drawing.Point>();
            for (int y = first.Top; y < first.Bottom; y++)
                for (int x = 0; x < menu.Width; x++)
                    if (bitmap.GetPixel(x, y).R < 128) ink.Add(new System.Drawing.Point(x, y));
            Assert.NotEmpty(ink);
            Assert.True(ink.Min(p => p.X) >= Math.Round(20 * scale));
            Assert.InRange((ink.Min(p => p.Y) + ink.Max(p => p.Y)) / 2d,
                first.Top + first.Height / 2d - 3 * scale, first.Top + first.Height / 2d + 3 * scale);
        }
        var preview = Environment.GetEnvironmentVariable("HINGE_TRAY_PREVIEW");
        if (!string.IsNullOrEmpty(preview)) bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
        var previousSize = menu.Size;
        menu.PrepareForDisplay();
        menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
        menu.PerformLayout();
        Assert.Equal(previousSize, menu.Size);
        menu.Items[0].Text = "打开 Hinge 并查看已连接设备";
        menu.PrepareForDisplay();
        menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
        Assert.True(menu.Width > previousSize.Width);
        menu.Items[0].Text = "打开 Hinge";
        menu.PrepareForDisplay();
        menu.Size = menu.GetPreferredSize(System.Drawing.Size.Empty);
        Assert.Equal(previousSize, menu.Size);
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
        bool transferFired = false;
        tray.OpenRequested += (s, e) => openFired = true;
        tray.ExitRequested += (s, e) => exitFired = true;
        tray.TransferRequested += (s, e) => transferFired = true;

        tray.RequestTransfer();
        Assert.True(transferFired);
        Assert.False(openFired);
        Assert.False(exitFired);
        tray.RequestOpen();
        tray.RequestExit();

        Assert.True(openFired);
        Assert.True(exitFired);

        tray.Dispose();
        Assert.False(tray.IsVisible);
    }
}
