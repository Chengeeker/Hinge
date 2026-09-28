using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class CloudRelaySettingsPage : Page
{
    public Button BackToSettings => BackToSettingsButton;
    public ToggleSwitch Enabled => CloudRelayEnabledToggle;
    public TextBox Endpoint => CloudRelayEndpointTextBox;
    public TextBox Key => CloudRelayKeyTextBox;
    public PasswordBox AdminToken => CloudRelayAdminTokenTextBox;
    public Button GenerateKey => GenerateCloudRelayKeyButton;
    public Button CopyKey => CopyCloudRelayKeyButton;
    public Button Save => SaveCloudRelayButton;
    public Button Register => RegisterCloudRelayButton;
    public Button Test => TestCloudRelayButton;
    public TextBlock Status => CloudRelayStatusText;

    public CloudRelaySettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }
}
