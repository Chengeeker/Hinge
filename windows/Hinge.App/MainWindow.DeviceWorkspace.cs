using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace Hinge.App;

/// <summary>
/// Presentation-level device workspace selector. It never opens or tears down
/// network sessions. An explicitly chosen offline phone must NOT be replaced
/// with the next online phone during a pending edit or transfer.
/// </summary>
public sealed partial class MainWindow
{
    private const string WorkspaceDeviceSetting = "Workspace.SelectedPhoneDeviceId.v1";
    private readonly DeviceWorkspaceManager _deviceWorkspace = new();
    private bool _updatingWorkspaceSelector;
    private string _homeDeviceCardsFingerprint = string.Empty;
    private string _workspaceSelectorFingerprint = string.Empty;

    private void RestoreDeviceWorkspace()
    {
        var stored = ReadUserSetting(WorkspaceDeviceSetting) as string;
        _deviceWorkspace.RestoreSelection(stored);
    }

    private void WorkspaceDeviceSelector_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingWorkspaceSelector || WorkspaceDeviceSelector.SelectedItem is not ComboBoxItem
            { Tag: string id }) return;
        SelectWorkspaceDevice(id);
    }

    private void SelectWorkspaceDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        if (_deviceWorkspace.IsSelected(deviceId)) return;
        _deviceWorkspace.SelectDevice(deviceId);
        WriteUserSetting(WorkspaceDeviceSetting, deviceId);
        _activeConnection = _sessionManager.ConnectionForDevice(deviceId);
        _activeDevice = _registry.TryGetDevice(deviceId, out var device) ? device : null;
        _wirelessAdbClipboardSyncCancellation?.Cancel();
        RefreshWorkspaceSelectorAndCards();
        SetHeroDevice(_activeDevice);
        RefreshSelectedScreenStatus();
        RefreshScopedWirelessAdbSettings();
        _ = RebindClipboardToSelectedPhoneAsync();
        RefreshCurrentWorkspacePage(_activeConnection);
    }

    private SessionConnection? SelectedWorkspaceConnection()
    {
        var selected = _deviceWorkspace.SelectedDeviceId;
        if (string.IsNullOrWhiteSpace(selected))
        {
            var first = _sessionManager.ActiveConnections.FirstOrDefault(
                session => session.IsSessionReady && !string.IsNullOrWhiteSpace(session.RemoteDeviceId));
            if (first?.RemoteDeviceId is { } id)
            {
                _deviceWorkspace.AdoptFirstConnected(id);
                selected = _deviceWorkspace.SelectedDeviceId;
            }
        }
        return selected == null ? null : _sessionManager.ConnectionForDevice(selected);
    }

    private void RefreshWorkspaceSelectorAndCards()
    {
        if (_homePage == null || _registry == null || _sessionManager == null) return;
        var targets = GetPhoneTargets();
        foreach (var target in targets.Where(t => t.IsConnected))
            _deviceWorkspace.AdoptFirstConnected(target.DeviceId);

        var selected = _deviceWorkspace.SelectedDeviceId;
        var fingerprint = string.Join("|", targets.Select(t =>
            t.DeviceId + ":" + t.Name + ":" + t.IsConnected)) + "|" + selected;
        if (_workspaceSelectorFingerprint != fingerprint)
        {
            _workspaceSelectorFingerprint = fingerprint;
            _updatingWorkspaceSelector = true;
            try
            {
                WorkspaceDeviceSelector.Items.Clear();
                var items = targets.ToList();
                if (selected != null &&
                    !items.Any(t => string.Equals(t.DeviceId, selected,
                        StringComparison.OrdinalIgnoreCase)))
                    items.Insert(0, new PhoneDeviceTarget(selected, "上次选择的手机", false));
                ComboBoxItem? selectedItem = null;
                foreach (var phone in items)
                {
                    var option = new ComboBoxItem
                    {
                        Tag = phone.DeviceId,
                        Content = phone.Name + (phone.IsConnected ? " · 已连接" : " · 离线"),
                        MinWidth = 0
                    };
                    WorkspaceDeviceSelector.Items.Add(option);
                    if (string.Equals(phone.DeviceId, selected, StringComparison.OrdinalIgnoreCase))
                        selectedItem = option;
                }
                WorkspaceDeviceSelector.SelectedItem = selectedItem;
                WorkspaceDeviceSelector.IsEnabled = items.Count > 0;
            }
            finally { _updatingWorkspaceSelector = false; }
        }
        RefreshConnectedHomeCards(targets);
    }

    private void RefreshConnectedHomeCards(IReadOnlyList<PhoneDeviceTarget> targets)
    {
        if (_homePage == null) return;
        var connected = targets.Where(t => t.IsConnected).ToArray();
        var selected = _deviceWorkspace.SelectedDeviceId;
        var fingerprint = string.Join("|", connected.Select(t => t.DeviceId + ":" + t.Name)) +
            "|" + selected;
        if (_homeDeviceCardsFingerprint == fingerprint) return;
        _homeDeviceCardsFingerprint = fingerprint;

        var panel = _homePage.DeviceCardsPanel;
        panel.Children.Clear();
        _homePage.DeviceCardsContainer.Visibility =
            connected.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int start = 0; start < connected.Length; start += 2)
        {
            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            for (int column = 0; column < 2 && start + column < connected.Length; column++)
            {
                var phone = connected[start + column];
                var current = string.Equals(phone.DeviceId, selected,
                    StringComparison.OrdinalIgnoreCase);
                var card = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    BorderBrush = current ? ThemeBrushes.Accent(PageRoot) :
                        ThemeBrushes.Border(PageRoot),
                    Padding = new Thickness(12),
                    Background = ThemeBrushes.MutedSurface(PageRoot)
                };
                var stack = new StackPanel { Spacing = 7 };
                var heading = new TextBlock
                {
                    Text = phone.Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                stack.Children.Add(heading);
                stack.Children.Add(new TextBlock
                {
                    Text = current ? "已连接 · 当前工作设备" : "已连接 · 可切换",
                    Foreground = ThemeBrushes.Secondary(PageRoot),
                    FontSize = 12
                });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var use = new HandCursorButton
                {
                    Content = current ? "正在使用" : "设为当前",
                    IsEnabled = !current,
                    MinHeight = 30,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                use.Click += (_, _) => SelectWorkspaceDevice(phone.DeviceId);
                actions.Children.Add(use);
                var files = new HandCursorButton
                {
                    Content = "文件",
                    MinHeight = 30,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                files.Click += (_, _) =>
                {
                    SelectWorkspaceDevice(phone.DeviceId);
                    ShowFileManagement();
                    _filePage?.SelectPhone(phone.DeviceId);
                };
                actions.Children.Add(files);
                stack.Children.Add(actions);
                card.Child = stack;
                Grid.SetColumn(card, column);
                grid.Children.Add(card);
            }
            panel.Children.Add(grid);
        }
    }
}
