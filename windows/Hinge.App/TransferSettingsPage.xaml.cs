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
    public Button CleanDeviceHistory => CleanDeviceHistoryButton;
    public TextBox WirelessAdbHost => WirelessAdbHostTextBox;
    public TextBox WirelessAdbPairingPort => WirelessAdbPairingPortTextBox;
    public TextBox WirelessAdbConnectPort => WirelessAdbConnectPortTextBox;
    public PasswordBox WirelessAdbPairingCode => WirelessAdbPairingCodeTextBox;
    public ToggleSwitch WirelessAdbClipboardSync => WirelessAdbClipboardSyncToggle;
    public Button WirelessAdbPair => WirelessAdbPairButton;
    public Button WirelessAdbConnect => WirelessAdbConnectButton;
    public Button WirelessAdbPullLogs => WirelessAdbPullLogsButton;
    public TextBlock WirelessAdbStatus => WirelessAdbStatusText;
    public TextBlock WirelessAdbClipboardStatus => WirelessAdbClipboardStatusText;
    public TextBlock Status => TransferSettingsStatusText;

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
