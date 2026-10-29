using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class RemoteInputSettingsPage : Page
{
    public TextBox ControlToken => ControlTokenBox;
    public Button RotateToken => RotateTokenButton;
    public Button BackToSettings => BackButton;
    public ToggleSwitch AllowRemoteInput => AllowRemoteInputToggle;
    public ToggleSwitch RequireEncryption => RequireEncryptionToggle;
    public ToggleSwitch ShareDesktop => ShareDesktopToggle;
    public TextBlock DesktopStatus => DesktopStatusText;
    public TextBlock DesktopEncoderStatus => DesktopEncoderStatusText;
    public Button StopDesktop => StopDesktopButton;
    public Button ResetDesktopPeers => ResetDesktopPeersButton;
    public Slider DesktopFps => DesktopFpsSlider;
    public Slider DesktopEdge => DesktopEdgeSlider;
    public Slider DesktopBitrate => DesktopBitrateSlider;
    public TextBlock DesktopFpsCaption => DesktopFpsLabel;
    public TextBlock DesktopEdgeCaption => DesktopEdgeLabel;
    public TextBlock DesktopBitrateCaption => DesktopBitrateLabel;
    public Button ApplyDesktopVideo => ApplyDesktopVideoButton;
    public ComboBox DesktopMonitor => DesktopMonitorCombo;
    public Button RefreshDesktopMonitors => RefreshDesktopMonitorsButton;
    public Button GameLayoutSettings => GameLayoutSettingsButton;
    public Border TokenPanel => RemoteInputTokenPanel;
    public TextBlock Status => RemoteInputStatusText;
    public TextBlock StatusDetails => RemoteInputStatusDetailsText;

    public RemoteInputSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        SettingsPageLayout.MatchViewportWidth(RemoteInputScrollViewer, RemoteInputPageRoot);
    }
}
