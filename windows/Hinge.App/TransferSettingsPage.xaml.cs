using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class TransferSettingsPage : Page
{
    public Button BackToSettings => BackToSettingsButton;
    public TextBox StoragePath => StoragePathTextBox;
    public Button Storage => StorageButton;
    public TextBox PairingCode => PairingCodeTextBox;
    public Button SavePairingCode => SavePairingCodeButton;
    public Button ClearPairingCode => ClearPairingCodeButton;
    public TextBlock PairingCodeStatus => PairingCodeStatusText;
    public Button StartBlePairing => StartBlePairingButton;
    public TextBlock BlePairingStatus => BlePairingStatusText;
    public Button CleanDeviceHistory => CleanDeviceHistoryButton;
    public TextBox WirelessAdbHost => WirelessAdbHostTextBox;
    public TextBox WirelessAdbPairingPort => WirelessAdbPairingPortTextBox;
    public TextBox WirelessAdbConnectPort => WirelessAdbConnectPortTextBox;
    public PasswordBox WirelessAdbPairingCode => WirelessAdbPairingCodeTextBox;
    public ToggleSwitch WirelessAdbClipboardSync => WirelessAdbClipboardSyncToggle;
    public Button WirelessAdbRecovery => WirelessAdbRecoveryButton;
    public TextBlock WirelessAdbRecoveryStatus => WirelessAdbRecoveryStatusText;
    public Button WirelessAdbPair => WirelessAdbPairButton;
    public Button WirelessAdbQrPair => WirelessAdbQrPairButton;
    public Button WirelessAdbConnect => WirelessAdbConnectButton;
    public Button WirelessAdbRefreshDevices => WirelessAdbRefreshDevicesButton;
    public Button WirelessAdbPullLogs => WirelessAdbPullLogsButton;
    public TextBox WirelessAdbCommand => WirelessAdbCommandTextBox;
    public Button WirelessAdbCommandRun => WirelessAdbCommandRunButton;
    public Button WirelessAdbCommandStop => WirelessAdbCommandStopButton;
    public TextBlock WirelessAdbCommandStatus => WirelessAdbCommandStatusText;
    public TextBox WirelessAdbCommandOutput => WirelessAdbCommandOutputTextBox;
    public TextBlock WirelessAdbStatus => WirelessAdbStatusText;
    public TextBlock WirelessAdbClipboardStatus => WirelessAdbClipboardStatusText;
    public TextBlock Status => TransferSettingsStatusText;
    public TextBlock LocalServiceInfo => LocalServiceInfoText;

    public TransferSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        SettingsPageLayout.MatchViewportWidth(TransferSettingsScrollViewer, TransferSettingsPageContentRoot);
        ActualThemeChanged += (_, _) => ApplyWirelessAdbStatusStyle();
        Loaded += (_, _) =>
        {
            ThemeBrushes.PaletteChanged += ApplyWirelessAdbStatusStyle;
            ApplyWirelessAdbStatusStyle();
        };
        Unloaded += (_, _) => ThemeBrushes.PaletteChanged -= ApplyWirelessAdbStatusStyle;
    }

    public void SetWirelessAdbStatus(string message)
    {
        WirelessAdbStatusDetailsText.Text = message;
        WirelessAdbStatusText.Text = SummarizeWirelessAdbStatus(message);
        WirelessAdbStatusInfoBar.IsOpen = !string.IsNullOrWhiteSpace(message);
        ApplyWirelessAdbStatusStyle();
    }

    public void SetWirelessAdbClipboardStatus(string message) => WirelessAdbClipboardStatusText.Text = message;
    public void SetWirelessAdbPairResult(string message) => WirelessAdbPairResultText.Text = message;

    public void SetWirelessAdbDevicesError(string message) => WirelessAdbDevicesSummaryText.Text = message;

    internal void SetWirelessAdbDevices(IReadOnlyList<WirelessAdbDeviceInfo> devices)
    {
        var connected = devices.Count(device => device.Connected);
        var pending = devices.Count - connected;
        WirelessAdbDevicesSummaryText.Text =
            $"{connected} 台已连接" + (pending == 0 ? "" : $" · {pending} 条待连接 / 离线") +
            " · Hinge 专用 ADB 端口 5038 · 自动刷新";
        WirelessAdbDevicesPanel.Children.Clear();
        if (devices.Count == 0)
        {
            WirelessAdbDevicesPanel.Children.Add(new TextBlock
            {
                Text = "暂无设备。打开手机或平板的无线调试后，点击刷新。",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            });
            return;
        }

        foreach (var device in devices)
        {
            var heading = new Grid { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
            var name = new TextBlock
            {
                Text = device.Name,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            var status = new TextBlock
            {
                Text = device.Status,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            Grid.SetColumn(status, 1);
            heading.Children.Add(name);
            heading.Children.Add(status);
            var details = new TextBlock
            {
                Text = device.Details,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 12
            };
            var contents = new StackPanel { Spacing = 5 };
            contents.Children.Add(heading);
            contents.Children.Add(details);
            WirelessAdbDevicesPanel.Children.Add(new Border
            {
                Padding = new Microsoft.UI.Xaml.Thickness(12, 10, 12, 10),
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(12),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)ResourcesOrTheme("HingeM3OutlineVariantBrush"),
                Child = contents
            });
        }
    }

    private object ResourcesOrTheme(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var brush)
            ? brush : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void ApplyWirelessAdbStatusStyle()
    {
        var message = WirelessAdbStatusDetailsText.Text;
        var severity = message.Contains("失败", StringComparison.Ordinal)
            ? InfoBarSeverity.Error
            : message.Contains("授权已失效", StringComparison.Ordinal) ||
              message.Contains("缺少可验证", StringComparison.Ordinal) ||
              message.Contains("无线调试服务", StringComparison.Ordinal) ||
              message.Contains("没有可用网络", StringComparison.Ordinal)
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Informational;
        WirelessAdbStatusInfoBar.Severity = severity;
        WirelessAdbStatusInfoBar.Background = ThemeBrushes.StatusSurface(this, severity);
        WirelessAdbStatusInfoBar.Foreground = ThemeBrushes.StatusText(this, severity);
    }

    private static string SummarizeWirelessAdbStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未连接";
        if (message.Contains("授权已失效", StringComparison.Ordinal) || message.Contains("缺少可验证", StringComparison.Ordinal)) return "ADB 授权失效";
        if (message.Contains("无线调试服务", StringComparison.Ordinal)) return "无线调试关闭或未发现";
        if (message.Contains("没有可用网络", StringComparison.Ordinal)) return "网络不可达";
        if (message.Contains("等待已配对手机", StringComparison.Ordinal)) return "等待设备";
        if (message.Contains("正在重试", StringComparison.Ordinal)) return "正在重连";
        if (message.Contains("失败", StringComparison.Ordinal)) return "操作失败";
        if (message.Contains("已连接", StringComparison.Ordinal)) return "已连接";
        if (message.Contains("已配对", StringComparison.Ordinal)) return "已配对";
        if (message.Contains("正在", StringComparison.Ordinal)) return "正在处理";
        if (message.Contains("已保存", StringComparison.Ordinal)) return "已保存";
        if (message.Contains("已关闭", StringComparison.Ordinal)) return "已关闭";
        return message.Length > 18 ? string.Concat(message.AsSpan(0, 18), "…") : message;
    }
}
