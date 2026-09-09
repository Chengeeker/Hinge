using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class SettingsPage : Page
{
    public Button Personalization => PersonalizationButton;
    public Button Storage => StorageButton;
    public TextBox StoragePath => StoragePathTextBox;
    public TextBlock NotificationStatus => NotificationStatusText;
    public Button OpenNotificationSettings => OpenNotificationSettingsButton;
    public Button About => AboutButton;
    public TextBlock Status => SettingsStatusText;
    public ToggleSwitch MinimizeToTray => MinimizeToTrayToggle;
    public ToggleSwitch StartWithWindows => StartWithWindowsToggle;
    public ToggleSwitch SilentStartup => SilentStartupToggle;

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }
}
