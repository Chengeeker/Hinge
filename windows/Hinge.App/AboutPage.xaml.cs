using Microsoft.UI.Xaml.Controls;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"版本 v{Constants.AppVersion}";
    }
}
