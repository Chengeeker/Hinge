namespace Hinge.App;

public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _adbInventoryTimer;
    private CancellationTokenSource? _adbInventoryCancellation;
    private bool _adbInventoryRefreshing;

    private void ConfigureWirelessAdbInventory(TransferSettingsPage page)
    {
        page.WirelessAdbRefreshDevices.Click += (_, _) => _ = RefreshWirelessAdbInventoryAsync();
        page.Loaded += (_, _) => StartWirelessAdbInventory();
        page.Unloaded += (_, _) => StopWirelessAdbInventory();
        if (ReferenceEquals(ContentFrame.Content, page)) StartWirelessAdbInventory();
    }

    private void StartWirelessAdbInventory()
    {
        if (AdbProcessGate.Shared.SessionEnding) return;
        _adbInventoryCancellation ??= new CancellationTokenSource();
        if (_adbInventoryTimer == null)
        {
            _adbInventoryTimer = DispatcherQueue.CreateTimer();
            _adbInventoryTimer.Interval = TimeSpan.FromSeconds(8);
            _adbInventoryTimer.Tick += (_, _) => _ = RefreshWirelessAdbInventoryAsync();
        }
        _adbInventoryTimer.Start();
        _ = RefreshWirelessAdbInventoryAsync();
    }

    private void StopWirelessAdbInventory()
    {
        _adbInventoryTimer?.Stop();
        _adbInventoryCancellation?.Cancel();
        _adbInventoryCancellation = null;
    }

    private async Task RefreshWirelessAdbInventoryAsync()
    {
        if (_adbInventoryRefreshing || AdbProcessGate.Shared.SessionEnding) return;
        if (ContentFrame.Content is not TransferSettingsPage page) return;
        var cancellation = _adbInventoryCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested) return;
        _adbInventoryRefreshing = true;
        try
        {
            var devices = await WirelessAdbClient.ReadDeviceInventoryAsync(cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(ContentFrame.Content, page))
                page.SetWirelessAdbDevices(devices);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(ContentFrame.Content, page))
                page.SetWirelessAdbDevicesError("ADB 连接列表读取失败：" + exception.Message);
        }
        finally
        {
            _adbInventoryRefreshing = false;
        }
    }
}
