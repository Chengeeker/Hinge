using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hinge.App;

public sealed partial class PersonalizationPage : Page
{
    public RadioButtons ThemeOptionsControl => ThemeOptions;
    public ComboBox MaterialOptionsControl => MaterialOptions;
    public Button ImportBackground => ImportBackgroundButton;
    public Button ClearBackground => ClearBackgroundButton;
    public Button BackToSettings => BackToSettingsButton;
    public TextBlock BackgroundPath => BackgroundPathText;
    public Image BackgroundImage => BackgroundPreview;
    public Border BackgroundPreviewContainer => BackgroundPreviewBorder;
    public PersonalizationPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    public void SetPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            BackgroundPathText.Text = "未设置背景图片";
            BackgroundPreview.Source = null;
            BackgroundPreviewBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            return;
        }

        BackgroundPathText.Text = path;
        try
        {
            BackgroundPreview.Source = new BitmapImage(new Uri(path));
            BackgroundPreviewBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch
        {
            BackgroundPreview.Source = null;
            BackgroundPreviewBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }
}
