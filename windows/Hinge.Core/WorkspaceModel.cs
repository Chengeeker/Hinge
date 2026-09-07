using System.Text;

namespace Hinge.Core;

public class WorkspaceSnapshot
{
    public int DiscoveredDevicesCount { get; set; }
    public int TrustedDevicesCount { get; set; }
    public int ActiveConnectionsCount { get; set; }
    public int ActiveTransfersCount { get; set; }
    public int CompletedTransfersCount { get; set; }
    public int ClipboardEventsSynced { get; set; }
    public bool IsScreenMirroringActive { get; set; }
    public double MirrorFps { get; set; }
    public double MirrorBitrateBps { get; set; }
    public int NotificationsForwardedCount { get; set; }
    public TimeSpan Uptime { get; set; }

    public string FormatDashboard()
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                         HINGE — WORKSPACE DASHBOARD                           ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"  Uptime               : {Uptime.Hours:D2}h {Uptime.Minutes:D2}m {Uptime.Seconds:D2}s");
        sb.AppendLine($"  Discovered Devices   : {DiscoveredDevicesCount} total ({TrustedDevicesCount} trusted, {ActiveConnectionsCount} connected)");
        sb.AppendLine($"  File Transfers       : {ActiveTransfersCount} in-progress, {CompletedTransfersCount} completed");
        sb.AppendLine($"  Clipboard Sync       : {ClipboardEventsSynced} events synced (Anti-loop active)");
        string mirrorStatus = IsScreenMirroringActive
            ? $"ACTIVE ({MirrorFps:F1} FPS, {MirrorBitrateBps / 1_000_000:F2} Mbps)"
            : "INACTIVE";
        sb.AppendLine($"  Screen Mirroring     : {mirrorStatus}");
        sb.AppendLine($"  Notifications        : {NotificationsForwardedCount} relayed");
        sb.AppendLine("================================================================================");
        return sb.ToString();
    }
}

public class WorkspaceMonitor : IDisposable
{
    private readonly DeviceRegistry? _registry;
    private readonly TrustStore? _trustStore;
    private readonly SessionManager? _sessionManager;
    private readonly TransferManager? _transferManager;
    private readonly ClipboardManager? _clipboardManager;
    private readonly ScreenStreamReceiver? _screenReceiver;
    private readonly NotificationManager? _notificationManager;
    private readonly DateTime _startTime = DateTime.UtcNow;

    private int _clipboardSyncedCount;
    private int _completedTransfersCount;
    private int _notificationsCount;

    public event EventHandler<WorkspaceSnapshot>? SnapshotUpdated;

    public WorkspaceMonitor(
        DeviceRegistry? registry = null,
        TrustStore? trustStore = null,
        SessionManager? sessionManager = null,
        TransferManager? transferManager = null,
        ClipboardManager? clipboardManager = null,
        ScreenStreamReceiver? screenReceiver = null,
        NotificationManager? notificationManager = null)
    {
        _registry = registry;
        _trustStore = trustStore;
        _sessionManager = sessionManager;
        _transferManager = transferManager;
        _clipboardManager = clipboardManager;
        _screenReceiver = screenReceiver;
        _notificationManager = notificationManager;

        if (_clipboardManager != null)
        {
            _clipboardManager.ClipboardReceived += (_, _) =>
            {
                Interlocked.Increment(ref _clipboardSyncedCount);
                NotifyUpdate();
            };
        }

        if (_transferManager != null)
        {
            _transferManager.FileReceived += (_, _) =>
            {
                Interlocked.Increment(ref _completedTransfersCount);
                NotifyUpdate();
            };
        }

        if (_notificationManager != null)
        {
            _notificationManager.NotificationReceived += (_, _) =>
            {
                Interlocked.Increment(ref _notificationsCount);
                NotifyUpdate();
            };
        }

        if (_screenReceiver != null)
        {
            _screenReceiver.StreamStarted += (_, _) => NotifyUpdate();
            _screenReceiver.StreamStopped += (_, _) => NotifyUpdate();
        }
    }

    public WorkspaceSnapshot GetSnapshot()
    {
        var mirrorStats = _screenReceiver?.Statistics;
        bool isMirroring = _screenReceiver?.State == ScreenStreamState.Streaming;

        return new WorkspaceSnapshot
        {
            DiscoveredDevicesCount = _registry?.GetAllDevices().Count ?? 0,
            TrustedDevicesCount = _trustStore?.GetAllTrustedDevices().Count ?? 0,
            ActiveConnectionsCount = _sessionManager?.ActiveConnections.Count ?? 0,
            ActiveTransfersCount = _transferManager?.ActiveTransfersCount ?? 0,
            CompletedTransfersCount = _completedTransfersCount,
            ClipboardEventsSynced = _clipboardSyncedCount,
            IsScreenMirroringActive = isMirroring,
            MirrorFps = mirrorStats?.CurrentFps ?? 0,
            MirrorBitrateBps = mirrorStats?.CurrentBitrateBps ?? 0,
            NotificationsForwardedCount = _notificationsCount,
            Uptime = DateTime.UtcNow - _startTime
        };
    }

    private void NotifyUpdate()
    {
        SnapshotUpdated?.Invoke(this, GetSnapshot());
    }

    public void Dispose()
    {
    }
}
