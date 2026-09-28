using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed partial class WindowsSettingsPage : Page
{
    public Button BackToSettings => BackToSettingsButton;
    public TextBlock NotificationStatus => NotificationStatusText;
    public Button OpenNotificationSettings => OpenNotificationSettingsButton;
    public ToggleSwitch StartWithWindows => StartWithWindowsToggle;
    public ToggleSwitch SilentStartup => SilentStartupToggle;
    public ToggleSwitch MinimizeToTray => MinimizeToTrayToggle;
    public ToggleSwitch ShowTrayBackgroundNotice => ShowTrayBackgroundNoticeToggle;
    public TextBlock Status => SettingsStatusText;

    public WindowsSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }
}
