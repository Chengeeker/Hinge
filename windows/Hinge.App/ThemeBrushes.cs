using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Hinge.App;

internal static class ThemeBrushes
{
    public static Brush Primary(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE4, 0x00, 0x00, 0x00));

    public static Brush Secondary(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9E, 0x00, 0x00, 0x00));

    public static Brush Muted(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x61, 0x00, 0x00, 0x00));

    public static Brush Hover(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00));

    public static Brush Pressed(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x00, 0x00, 0x00));

    public static Brush Border(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));

    public static Brush MutedSurface(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00));

    public static Brush AccentText(FrameworkElement owner) => Solid(
        IsDark(owner) ? Color.FromArgb(0xFF, 0x75, 0xB6, 0xFF) : Color.FromArgb(0xFF, 0x00, 0x5F, 0xB8));

    public static Brush StatusSurface(FrameworkElement owner, InfoBarSeverity severity) => Solid(
        (IsDark(owner), severity) switch
        {
            (true, InfoBarSeverity.Success) => Color.FromArgb(0xFF, 0x25, 0x3A, 0x2A),
            (true, InfoBarSeverity.Warning) => Color.FromArgb(0xFF, 0x49, 0x3B, 0x1D),
            (true, InfoBarSeverity.Error) => Color.FromArgb(0xFF, 0x44, 0x27, 0x26),
            (true, _) => Color.FromArgb(0xFF, 0x24, 0x36, 0x4A),
            (false, InfoBarSeverity.Success) => Color.FromArgb(0xFF, 0xDF, 0xF6, 0xDD),
            (false, InfoBarSeverity.Warning) => Color.FromArgb(0xFF, 0xFF, 0xF4, 0xCE),
            (false, InfoBarSeverity.Error) => Color.FromArgb(0xFF, 0xFD, 0xE7, 0xE9),
            (false, _) => Color.FromArgb(0xFF, 0xEF, 0xF6, 0xFC)
        });

    public static Brush StatusText(FrameworkElement owner, InfoBarSeverity severity) => Solid(
        (IsDark(owner), severity) switch
        {
            (true, InfoBarSeverity.Success) => Color.FromArgb(0xFF, 0xD6, 0xF5, 0xD8),
            (true, InfoBarSeverity.Warning) => Color.FromArgb(0xFF, 0xFF, 0xE7, 0xA0),
            (true, InfoBarSeverity.Error) => Color.FromArgb(0xFF, 0xFF, 0xDA, 0xD6),
            (true, _) => Color.FromArgb(0xFF, 0xD8, 0xEA, 0xFE),
            (false, InfoBarSeverity.Success) => Color.FromArgb(0xFF, 0x0F, 0x5F, 0x16),
            (false, InfoBarSeverity.Warning) => Color.FromArgb(0xFF, 0x6A, 0x4B, 0x00),
            (false, InfoBarSeverity.Error) => Color.FromArgb(0xFF, 0x8B, 0x1A, 0x1A),
            (false, _) => Color.FromArgb(0xFF, 0x0F, 0x4C, 0x75)
        });

    public static bool IsDark(FrameworkElement owner)
    {
        var rootTheme = (owner.XamlRoot?.Content as FrameworkElement)?.ActualTheme;
        var theme = rootTheme is ElementTheme.Dark or ElementTheme.Light
            ? rootTheme.Value
            : owner.ActualTheme;
        return theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
    }

    private static SolidColorBrush Solid(Color color) => new(color);
}
