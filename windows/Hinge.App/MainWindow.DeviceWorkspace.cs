using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hinge.App;

/// <summary>
/// Presentation-level workspace selection, independent from each TCP session.
/// Selecting a phone changes the default data source only; a card's Send action
/// targets its own authenticated DeviceId without modifying the workspace.
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

    private void UpdateWorkspaceDeviceFooterLayout()
    {
        if (AppNavigation == null || ExpandedWorkspaceDeviceSelector == null ||
            CompactWorkspaceDeviceButton == null) return;

        // NavigationView's compact pane is only 72 px wide. Do not let the
        // full device label/ComboBox participate in its layout measurements.
        var compact = !AppNavigation.IsPaneOpen;
        ExpandedWorkspaceDeviceSelector.Visibility = compact
            ? Visibility.Collapsed : Visibility.Visible;
        CompactWorkspaceDeviceButton.Visibility = compact
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompactWorkspaceDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        // Re-query live connections on every click rather than reusing a stale
        // flyout. Two phones with the same model still have distinct DeviceIds.
        var targets = GetPhoneTargets();
        var selected = _deviceWorkspace.SelectedDeviceId;
        var choices = WorkspaceDeviceChoices.ForSelector(targets, selected);
        var menu = new MenuFlyout();
        if (choices.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "暂无可选择的手机",
                IsEnabled = false
            });
        }
        foreach (var phone in choices)
        {
            var deviceId = phone.DeviceId;
            var current = string.Equals(deviceId, selected,
                StringComparison.OrdinalIgnoreCase);
            var item = new MenuFlyoutItem
            {
                Text = phone.Name + (phone.IsConnected ? " · 已连接" : " · 离线"),
                Icon = current ? new SymbolIcon { Symbol = Symbol.Accept } : null
            };
            item.Click += (_, _) => SelectWorkspaceDevice(deviceId);
            menu.Items.Add(item);
        }
        menu.ShowAt(CompactWorkspaceDeviceButton);
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
                session => session.IsSessionReady &&
                    string.Equals(session.PeerInfo?.Platform, "android",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(session.RemoteDeviceId));
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
        // Trusted history includes old installs with changed Android identities.
        // Do not show those 40+ offline ghosts in the active-workspace dropdown.
        var choices = WorkspaceDeviceChoices.ForSelector(targets, selected);
        var currentChoice = choices.FirstOrDefault(phone =>
            string.Equals(phone.DeviceId, selected, StringComparison.OrdinalIgnoreCase));
        var compactLabel = currentChoice == null
            ? "暂无当前工作设备"
            : "当前工作设备：" + currentChoice.Name +
              (currentChoice.IsConnected ? "（已连接）" : "（离线）");
        CompactWorkspaceDeviceButton.IsEnabled = choices.Count > 0;
        ToolTipService.SetToolTip(CompactWorkspaceDeviceButton, compactLabel);
        AutomationProperties.SetName(CompactWorkspaceDeviceButton, compactLabel + "，点击切换");

        var fingerprint = string.Join("|", choices.Select(t =>
            t.DeviceId + ":" + t.Name + ":" + t.IsConnected)) + "|" + selected;
        if (_workspaceSelectorFingerprint != fingerprint)
        {
            _workspaceSelectorFingerprint = fingerprint;
            _updatingWorkspaceSelector = true;
            try
            {
                WorkspaceDeviceSelector.Items.Clear();
                ComboBoxItem? selectedItem = null;
                foreach (var phone in choices)
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
                WorkspaceDeviceSelector.IsEnabled = choices.Count > 0;
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
        var fingerprint = string.Join("|", connected.Select(t => t.DeviceId + ":" + t.Name))
            + "|" + selected;
        if (_homeDeviceCardsFingerprint == fingerprint) return;
        _homeDeviceCardsFingerprint = fingerprint;

        var panel = _homePage.DeviceCardsPanel;
        panel.Children.Clear();
        _homePage.DeviceCardsContainer.Visibility = Visibility.Visible;
        _homePage.ConnectedDeviceCountLabel.Text = connected.Length == 0
            ? "设备连接后，可直接向对应手机发送文件"
            : $"已连接 {connected.Length} 台 · 每台设备均可独立操作";
        _homePage.EmptyConnectedDeviceHint.Visibility =
            connected.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var phone in connected)
        {
            var current = string.Equals(phone.DeviceId, selected,
                StringComparison.OrdinalIgnoreCase);
            var radius = (CornerRadius)Application.Current.Resources["HingeCornerRadius"];
            var card = new Border
            {
                CornerRadius = radius,
                // M3 tonal selection: keep the surface light and use a quiet
                // outline instead of the previous dark primary-color frame.
                BorderThickness = current ? new Thickness(1) : new Thickness(0),
                BorderBrush = ThemeBrushes.Border(PageRoot),
                Padding = new Thickness(16, 14, 16, 14),
                Background = current
                    ? ThemeBrushes.MutedSurface(PageRoot)
                    : ThemeBrushes.SurfaceContainer(PageRoot)
            };

            var content = new StackPanel { Spacing = 14 };
            var heading = new Grid { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _registry.TryGetDevice(phone.DeviceId, out var discovered);
            var model = discovered?.Model ?? phone.Name;
            var manufacturer = discovered?.Manufacturer ?? string.Empty;
            var logo = BuildOfficialBrandLogo(manufacturer, model + " " + phone.Name, 38, 30);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.VerticalAlignment = VerticalAlignment.Center;
            var logoSurface = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = radius,
                Background = ThemeBrushes.SurfaceContainerLowest(PageRoot),
                Child = logo
            };
            Grid.SetColumn(logoSurface, 0);
            heading.Children.Add(logoSurface);

            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock
            {
                Text = phone.Name,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            labels.Children.Add(new TextBlock
            {
                Text = "已连接 · 局域网会话可用",
                FontSize = 12,
                Foreground = ThemeBrushes.Secondary(PageRoot)
            });
            Grid.SetColumn(labels, 1);
            heading.Children.Add(labels);

            if (current)
            {
                var selectedLabel = new Border
                {
                    CornerRadius = radius,
                    Padding = new Thickness(10, 5, 10, 5),
                    Background = ThemeBrushes.PrimaryContainer(PageRoot),
                    Child = new TextBlock
                    {
                        Text = "当前工作设备",
                        FontSize = 12,
                        Foreground = ThemeBrushes.OnPrimaryContainer(PageRoot)
                    },
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(selectedLabel, 2);
                heading.Children.Add(selectedLabel);
            }
            content.Children.Add(heading);

            // Action destinations are explicit: sending to phone B never needs
            // the global selector to switch away from phone A.
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };
            var send = new HandCursorButton
            {
                Content = "发送文件",
                MinHeight = 36,
                Padding = new Thickness(16, 7, 16, 7),
                Style = (Style)Application.Current.Resources["HingeM3FilledButtonStyle"]
            };
            AutomationProperties.SetName(send, $"发送文件到 {phone.Name}");
            send.Click += async (_, _) => await SendFileToPhoneAsync(phone.DeviceId);
            actions.Children.Add(send);

            var files = new HandCursorButton
            {
                Content = "文件管理",
                MinHeight = 36,
                Padding = new Thickness(12, 7, 12, 7),
                Style = (Style)Application.Current.Resources["HingeM3OutlinedButtonStyle"]
            };
            AutomationProperties.SetName(files, $"浏览 {phone.Name} 的文件");
            files.Click += (_, _) =>
            {
                // Navigate through the real sidebar selection, not by opening
                // a file view over Home while the Home item stays selected.
                // Keep the clicked phone as a page-local file-management target.
                var targetDeviceId = phone.DeviceId;
                DispatcherQueue.TryEnqueue(() => OpenPhoneFilesFromHome(targetDeviceId));
            };
            actions.Children.Add(files);

            if (!current)
            {
                var use = new HandCursorButton
                {
                    Content = "设为当前",
                    MinHeight = 36,
                    Padding = new Thickness(10, 7, 10, 7),
                    Style = (Style)Application.Current.Resources["HingeM3TextButtonStyle"]
                };
                AutomationProperties.SetName(use, $"设 {phone.Name} 为当前工作设备");
                // Do not remove a clicked WinUI element during its own pointer event.
                use.Click += (_, _) => DispatcherQueue.TryEnqueue(() => SelectWorkspaceDevice(phone.DeviceId));
                actions.Children.Add(use);
            }
            content.Children.Add(actions);
            card.Child = content;
            panel.Children.Add(card);
        }
    }

    private void OpenPhoneFilesFromHome(string deviceId)
    {
        // Treat a home-card shortcut exactly like choosing Files in the sidebar.
        // Never change the global working phone just to browse another phone.
        var filesNavItem = AppNavigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "files",
                StringComparison.Ordinal));
        if (filesNavItem == null) return;

        if (!ReferenceEquals(AppNavigation.SelectedItem, filesNavItem))
            AppNavigation.SelectedItem = filesNavItem;

        // Let NavigationView.SelectionChanged complete its normal navigation
        // before selecting the source tab inside the file-management page.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ContentFrame.Content is not FileManagementPage)
                NavigateTo("文件管理", 0);
            RefreshFilePhoneSources();
            _filePage?.SelectPhone(deviceId);
        });
    }

    private async Task SendFileToPhoneAsync(string deviceId)
    {
        if (_sessionManager.ConnectionForDevice(deviceId) is not
            { IsSessionReady: true, State: SessionState.Connected })
        {
            await ShowDialogAsync("无法发送文件", "所选手机已经断开连接，请先重新连接。", false);
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            // Re-resolve by the captured immutable DeviceId after the picker.
            // Never fall back to the global selected device on disconnect.
            var target = _sessionManager.ConnectionForDevice(deviceId);
            if (target is not { IsSessionReady: true, State: SessionState.Connected })
            {
                await ShowDialogAsync("设备已断开", "选择文件期间目标手机已离线；文件未发送到其他手机。", false);
                return;
            }

            var result = await SendFilesToConnectionAsync(target,
                new[] { file.Path }, string.Empty, CancellationToken.None,
                showFailureDialog: true);
            if (result is { Completed: > 0 })
                StatusText.Text = $"已向 {target.PeerInfo?.Name ?? "目标手机"} 发送 {file.Name}";
        }
        catch (Exception error)
        {
            await ShowDialogAsync("发送文件失败", error.Message, false);
        }
    }
}
