using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class HomePage : Page
{
    public TextBlock HeroDeviceNameText => HeroDeviceName;
    public TextBlock HeroDeviceDetailText => HeroDeviceDetail;
    public ContentControl HeroDeviceLogoControl => HeroDeviceLogo;
    public TextBlock LocalDeviceInfoText => LocalDeviceInfo;
    public TextBlock DeviceCountTextBlock => DeviceCountText;
    public ListView DeviceList => DeviceListView;
    public InfoBar DiscoveryStatus => ActivityInfoBar;
    public TextBlock ClipboardStatus => ClipboardStatusText;
    public TextBlock Status => StatusText;
    public Button Refresh => RefreshButton;
    public Button SendFile => SendFileButton;
    public Button SendText => SendTextButton;
    public Button ClipboardToggle => ClipboardButton;

    public HomePage()
    {
        InitializeComponent();
    }
}
