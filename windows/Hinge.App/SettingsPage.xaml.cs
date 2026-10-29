using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class SettingsPage : Page
{
    public Button Personalization => PersonalizationButton;
    public Button WindowsSettings => WindowsSettingsButton;
    public Button TransferSettings => TransferSettingsButton;
    public Button ScreenControl => ScreenControlSettingsButton;
    public Button CloudRelaySettings => CloudRelaySettingsButton;
    public Button WebDavSettings => WebDavSettingsButton;

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        SettingsPageLayout.MatchViewportWidth(SettingsScrollViewer, SettingsPageContentRoot);
    }
}
