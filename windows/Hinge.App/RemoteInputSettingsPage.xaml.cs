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
