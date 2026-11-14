using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class NotificationHistoryPage
{
    private Func<IReadOnlyList<SessionConnection>>? _allSessionsProvider;
    private Func<string, string>? _deviceNameProvider;
    private readonly Dictionary<string, int> _multiOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _multiTotals = new(StringComparer.OrdinalIgnoreCase);
    private bool _updatingDeviceFilter;
    private string? _notificationDeviceFilterId;

    /// <summary>Null means every connected phone; never an implicit fallback.</summary>
    public string? SelectedPhoneFilterId => _notificationDeviceFilterId;

    private void DeviceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDeviceFilter || _allSessionsProvider == null) return;
        var id = (DeviceFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.Equals(id, _notificationDeviceFilterId, StringComparison.OrdinalIgnoreCase))
            return;
        _notificationDeviceFilterId = string.IsNullOrWhiteSpace(id) ? null : id;
        _ = LoadPageAsync(reset: true);
    }

    private void UpdateDeviceFilter(IReadOnlyList<SessionConnection> sessions)
    {
        var phones = sessions
            .Where(c => c.IsSessionReady && !string.IsNullOrWhiteSpace(c.RemoteDeviceId))
            .GroupBy(c => c.RemoteDeviceId!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => _deviceNameProvider?.Invoke(c.RemoteDeviceId!) ??
                c.PeerInfo?.Name ?? c.RemoteDeviceId!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _updatingDeviceFilter = true;
        try
        {
            DeviceFilter.Items.Clear();
            var all = new ComboBoxItem { Tag = string.Empty, Content = "全部已连接手机" };
            DeviceFilter.Items.Add(all);
            ComboBoxItem? chosen = null;
            foreach (var phone in phones)
            {
                var deviceId = phone.RemoteDeviceId!;
                var option = new ComboBoxItem
                {
                    Tag = deviceId,
                    Content = _deviceNameProvider?.Invoke(deviceId) ??
                        phone.PeerInfo?.Name ?? "Android 手机"
                };
                DeviceFilter.Items.Add(option);
                if (string.Equals(deviceId, _notificationDeviceFilterId,
                    StringComparison.OrdinalIgnoreCase))
                    chosen = option;
            }
            if (_notificationDeviceFilterId != null && chosen == null)
            {
                chosen = new ComboBoxItem
                {
                    Tag = _notificationDeviceFilterId,
                    Content = "当前筛选的手机（离线）"
                };
                DeviceFilter.Items.Add(chosen);
            }
            DeviceFilter.SelectedItem = chosen ?? all;
        }
        finally { _updatingDeviceFilter = false; }
    }

    private async Task LoadMultiDevicePageAsync(bool reset)
    {
        if (_loading)
        {
            if (reset)
            {
                ++_loadVersion;
                _pendingReset = true;
            }
            return;
        }

        var version = reset ? ++_loadVersion : _loadVersion;
        var all = _allSessionsProvider?.Invoke() ?? Array.Empty<SessionConnection>();
        UpdateDeviceFilter(all);
        var sessions = all
            .Where(c => c.IsSessionReady && !string.IsNullOrWhiteSpace(c.RemoteDeviceId) &&
                (_notificationDeviceFilterId == null ||
                    string.Equals(c.RemoteDeviceId, _notificationDeviceFilterId,
                        StringComparison.OrdinalIgnoreCase)))
            .GroupBy(c => c.RemoteDeviceId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (reset)
        {
            _multiOffsets.Clear();
            _multiTotals.Clear();
            _items.Clear();
            _itemIds.Clear();
            _nextOffset = 0;
            _total = 0;
            SummaryText.Text = string.Empty;
            LoadMoreButton.Visibility = Visibility.Collapsed;
            _historyScroll?.ChangeView(null, 0, null, true);
        }

        if (sessions.Length == 0 || _client == null)
        {
            ClearButton.IsEnabled = false;
            LoadMoreButton.Visibility = Visibility.Collapsed;
            SetStatus("当前手机未连接",
                _notificationDeviceFilterId == null ?
                    "请先连接至少一台 Android 手机。" :
                    "筛选的手机已离线；切换到全部设备或重新连接手机。",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
            return;
        }

        var pending = sessions.Where(c =>
            reset || !_multiOffsets.TryGetValue(c.RemoteDeviceId!, out var offset) ||
            !_multiTotals.TryGetValue(c.RemoteDeviceId!, out var total) || offset < total)
            .ToArray();
        if (pending.Length == 0) return;

        _loading = true;
        LoadingIndicator.IsActive = true;
        LoadingIndicator.Visibility = Visibility.Visible;
        ClearButton.IsEnabled = false;
        LoadMoreButton.IsEnabled = false;
        SetStatus("正在读取通知历史", $"正在查询 {pending.Length} 台手机的通知记录。",
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        try
        {
            var responses = await Task.WhenAll(pending.Select(async session =>
            {
                try
                {
                    var id = session.RemoteDeviceId!;
                    var offset = _multiOffsets.TryGetValue(id, out var current) ? current : 0;
                    var page = await _client.LoadNotificationHistoryAsync(session, offset,
                        PageSize, _ascending, _packageName);
                    return (Id: id, Page: page, Error: (Exception?)null);
                }
                catch (Exception exception)
                {
                    return (Id: session.RemoteDeviceId!,
                        Page: (RemoteNotificationHistoryPage?)null, Error: exception);
                }
            }));

            if (version != _loadVersion) return;
            var apps = new Dictionary<string, RemoteNotificationHistoryApplication>(
                StringComparer.OrdinalIgnoreCase);
            var errors = 0;
            var noPermission = 0;
            foreach (var reply in responses)
            {
                if (reply.Page == null)
                {
                    errors++;
                    continue;
                }
                var page = reply.Page;
                if (!page.AccessEnabled || !page.Enabled) noPermission++;
                var offset = _multiOffsets.TryGetValue(reply.Id, out var last) ? last : 0;
                _multiOffsets[reply.Id] = page.Items.Count == 0 ?
                    page.Total : offset + page.Items.Count;
                _multiTotals[reply.Id] = page.Total;
                foreach (var item in page.Items)
                {
                    item.SourceDeviceId = reply.Id;
                    item.SourceDeviceName = _deviceNameProvider?.Invoke(reply.Id) ?? reply.Id;
                    if (_itemIds.Add(DeviceWorkspaceManager.CacheKey(reply.Id, item.Id)))
                        _items.Add(item);
                }
                foreach (var app in page.Applications)
                {
                    if (!apps.TryGetValue(app.PackageName, out var existing))
                    {
                        existing = new RemoteNotificationHistoryApplication
                        {
                            PackageName = app.PackageName,
                            AppName = app.AppName,
                            IconBase64 = app.IconBase64
                        };
                        apps[app.PackageName] = existing;
                    }
                    existing.Count += app.Count;
                }
            }

            var sorted = _ascending
                ? _items.OrderBy(item => item.Timestamp).ToArray()
                : _items.OrderByDescending(item => item.Timestamp).ToArray();
            _items.Clear();
            foreach (var item in sorted) _items.Add(item);

            _total = _multiTotals.Values.Sum();
            _nextOffset = _multiOffsets.Values.Sum();
            RenderApplicationFilter(apps.Values.OrderBy(a => a.AppName).ToArray());
            RenderItems();
            SetStatus(errors > 0 ? "部分手机读取失败" :
                noPermission > 0 ? "部分手机未授予通知权限" : "通知历史已更新",
                $"已汇总 {sessions.Length} 台手机的 {_total} 条通知" +
                (errors > 0 ? $"，其中 {errors} 台读取失败，可重试" : string.Empty),
                errors > 0 || noPermission > 0
                    ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
                    : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
                SetStatus("通知历史获取失败", exception.Message,
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            _loading = false;
            LoadingIndicator.IsActive = false;
            LoadingIndicator.Visibility = Visibility.Collapsed;
            ClearButton.IsEnabled = _total > 0 && _clearItems != null;
            LoadMoreButton.IsEnabled = true;
            if (_pendingReset)
            {
                _pendingReset = false;
                await LoadMultiDevicePageAsync(reset: true);
            }
        }
    }
}
