using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hinge.App;

public sealed partial class PersonalizationPage : Page
{
    private bool _updatingFontOptions;
    private IReadOnlyList<CustomFontEntry> _fonts = [];
    public RadioButtons ThemeOptionsControl => ThemeOptions;
    public ComboBox PaletteOptionsControl => PaletteOptions;
    public ComboBox MaterialOptionsControl => MaterialOptions;
    public ComboBox FontOptionsControl => FontOptions;
    public ComboBox FontWeightOptionsControl => FontWeightOptions;
    public Button ImportFont => ImportFontButton;
    public Button DeleteFont => DeleteFontButton;
    public bool IsUpdatingFontOptions => _updatingFontOptions;
    public string SelectedFontFileName => FontWeightOptions.SelectedValue as string ?? string.Empty;
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
        SettingsPageLayout.MatchViewportWidth(PersonalizationScrollViewer, PersonalizationPageContentRoot);
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

    internal void SetFonts(IReadOnlyList<CustomFontEntry> fonts, string? selectedFileName, string? selectedFontFamilySource)
    {
        _fonts = fonts;
        _updatingFontOptions = true;
        try
        {
            FontOptions.Items.Clear();
            FontOptions.Items.Add(new ComboBoxItem { Content = "HarmonyOS Sans SC（内置）", Tag = string.Empty });
            foreach (var family in fonts.Select(font => font.FamilyName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
                FontOptions.Items.Add(new ComboBoxItem { Content = $"{family}（导入）", Tag = family });

            var selectedFont = fonts.FirstOrDefault(font => font.FileName == selectedFileName);
            FontOptions.SelectedValue = selectedFont?.FamilyName ?? string.Empty;
            SetAvailableFontWeights(selectedFont?.FamilyName ?? string.Empty, selectedFileName);
            DeleteFontButton.IsEnabled = !string.IsNullOrEmpty(selectedFileName);
            SetFontPreview(selectedFontFamilySource);
        }
        finally
        {
            _updatingFontOptions = false;
        }
    }

    internal CustomFontEntry? FindFont(string fileName) => _fonts.FirstOrDefault(font => font.FileName == fileName);

    internal void SetAvailableFontWeights(string familyName, string? selectedFileName = null)
    {
        bool wasUpdating = _updatingFontOptions;
        _updatingFontOptions = true;
        try
        {
            FontWeightOptions.Items.Clear();
            if (string.IsNullOrEmpty(familyName))
                FontWeightOptions.Items.Add(new ComboBoxItem { Content = "默认（内置）", Tag = string.Empty });
            var familyFonts = _fonts.Where(font => string.Equals(font.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var duplicateStyles = familyFonts.GroupBy(font => font.StyleName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var font in familyFonts)
            {
                FontWeightOptions.Items.Add(new ComboBoxItem
                {
                    Content = duplicateStyles.Contains(font.StyleName)
                        ? $"{font.StyleName} · {font.SourceName} ({font.VersionName})"
                        : font.StyleName,
                    Tag = font.FileName
                });
            }

            var preferred = familyFonts.FirstOrDefault(font => string.Equals(font.StyleName, "Regular", StringComparison.OrdinalIgnoreCase))
                ?? familyFonts.FirstOrDefault(font => string.Equals(font.StyleName, "Normal", StringComparison.OrdinalIgnoreCase))
                ?? familyFonts.FirstOrDefault();
            FontWeightOptions.SelectedValue = string.IsNullOrEmpty(familyName)
                ? string.Empty
                : selectedFileName ?? preferred?.FileName ?? string.Empty;
            DeleteFontButton.IsEnabled = !string.IsNullOrEmpty(FontWeightOptions.SelectedValue as string);
        }
        finally
        {
            _updatingFontOptions = wasUpdating;
        }
    }

    public void SetFontPreview(string? fontFamilySource) =>
        FontPreviewText.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
            fontFamilySource ?? "ms-appx:///Assets/Fonts/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans SC");

    internal void SetFontImportStatus(string message)
    {
        FontImportStatusText.Text = message;
        FontImportStatusText.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private async void ImportFontButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetFontImportStatus("正在打开字体选择器…");
        if ((Application.Current as App)?.MainWindow is { } window)
        {
            await window.ImportCustomFontsAsync(this);
        }
        else
        {
            SetFontImportStatus("窗口尚未就绪，请返回后重试。");
        }
    }

    private async void DeleteFontButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((Application.Current as App)?.MainWindow is { } window)
        {
            await window.DeleteCustomFontAsync(this);
        }
    }
}
