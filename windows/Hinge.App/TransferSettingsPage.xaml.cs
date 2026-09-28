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
    public TextBlock Status => TransferSettingsStatusText;

    public TransferSettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }
}
