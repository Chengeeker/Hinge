using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Hinge.App;

internal static class ThemeBrushes
{
    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly UISettings Settings = new();
    private static readonly Dictionary<(ElementTheme Theme, string Key), SolidColorBrush> Brushes = new();
    private static DispatcherQueue? _dispatcher;
    private static event Action? PaletteChanged;

    static ThemeBrushes()
    {
        Settings.ColorValuesChanged += (_, _) => RefreshPalette();
    }

    public static Brush Primary(FrameworkElement owner) => Resource(owner, "TextFillColorPrimaryBrush");
    public static Brush Secondary(FrameworkElement owner) => Resource(owner, "TextFillColorSecondaryBrush");
    public static Brush Muted(FrameworkElement owner) => Resource(owner, "TextFillColorTertiaryBrush");
    public static Brush Hover(FrameworkElement owner) => Resource(owner, "SubtleFillColorSecondaryBrush");
    public static Brush Pressed(FrameworkElement owner) => Resource(owner, "SubtleFillColorTertiaryBrush");
    public static Brush Border(FrameworkElement owner) => Resource(owner, "CardStrokeColorDefaultBrush");
    public static Brush MutedSurface(FrameworkElement owner) => Resource(owner, "CardBackgroundFillColorSecondaryBrush");
    public static Brush AccentText(FrameworkElement owner) => Resource(owner, "AccentTextFillColorPrimaryBrush");
    public static Brush Accent(FrameworkElement owner) => Resource(owner, "AccentFillColorDefaultBrush");
    public static Brush OnAccent(FrameworkElement owner) => Resource(owner, "TextOnAccentFillColorPrimaryBrush");
    public static Brush StatusSurface(FrameworkElement owner, InfoBarSeverity severity) => Resource(owner,
        $"SystemFillColor{SeverityName(severity)}BackgroundBrush");
    public static Brush StatusText(FrameworkElement owner, InfoBarSeverity severity) => Primary(owner);

    private static string SeverityName(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Success => "Success",
        InfoBarSeverity.Warning => "Caution",
        InfoBarSeverity.Error => "Critical",
        _ => "Attention"
    };

    public static bool IsDark(FrameworkElement owner)
    {
        var rootTheme = (owner.XamlRoot?.Content as FrameworkElement)?.ActualTheme;
        var theme = rootTheme is ElementTheme.Dark or ElementTheme.Light ? rootTheme.Value : owner.ActualTheme;
        return theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
    }

    private static Brush Resource(FrameworkElement owner, string key)
    {
        _dispatcher ??= owner.DispatcherQueue;
        var theme = IsDark(owner) ? ElementTheme.Dark : ElementTheme.Light;
        if (!Brushes.TryGetValue((theme, key), out var brush))
        {
            brush = new SolidColorBrush(ReadColor(theme, key));
            Brushes.Add((theme, key), brush);
        }
        return brush;
    }

    private static Windows.UI.Color ReadColor(ElementTheme theme, string key)
    {
        // Resolve against installed WinUI resources, including the user's contrast palette.
        var probe = (Border)XamlReader.Load($"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" RequestedTheme=\"{theme}\" Background=\"{{ThemeResource {key}}}\" />");
        return ((SolidColorBrush)probe.Background).Color;
    }

    private static void RefreshPalette() => _dispatcher?.TryEnqueue(() =>
    {
        foreach (var (key, brush) in Brushes)
            brush.Color = ReadColor(key.Theme, key.Key);
        PaletteChanged?.Invoke();
    });

    public static TextBlock Text(TextBlock text)
    {
        var foreground = text.Foreground;
        var resourceKey = Brushes.FirstOrDefault(pair => ReferenceEquals(pair.Value, foreground)).Key.Key;
        void ApplyContrast()
        {
            if (resourceKey != null) foreground = Resource(text, resourceKey);
            var parent = VisualTreeHelper.GetParent(text);
            while (parent != null && parent is not ListViewItem && parent is not GridViewItem && parent is not Button)
                parent = VisualTreeHelper.GetParent(parent);
            if (Accessibility.HighContrast && parent != null)
                text.ClearValue(TextBlock.ForegroundProperty);
            else
                text.Foreground = foreground;
        }
        void ContrastChanged() => ApplyContrast();
        text.Loaded += (_, _) =>
        {
            ApplyContrast();
            PaletteChanged += ContrastChanged;
        };
        text.Unloaded += (_, _) => PaletteChanged -= ContrastChanged;
        text.ActualThemeChanged += (_, _) => ApplyContrast();
        return text;
    }
}
