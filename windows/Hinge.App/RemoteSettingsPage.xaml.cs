using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class RemoteSettingsPage : Page
{
    public Button Back => BackButton;
    public Button PhoneScreen => PhoneScreenButton;
    public Button RemoteInput => RemoteInputButton;
    public TextBlock PhoneStatus => PhoneSummaryText;
    public TextBlock RemoteStatus => RemoteSummaryText;

    public RemoteSettingsPage()
    {
        InitializeComponent();
        SettingsPageLayout.MatchViewportWidth(RemoteScrollViewer, RemotePageRoot);
    }
}
