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
    internal static event Action? PaletteChanged;

    private sealed record PaletteRoles(IReadOnlyDictionary<string, Windows.UI.Color> Light, IReadOnlyDictionary<string, Windows.UI.Color> Dark);

    private static readonly string[] SchemeRoles =
    [
        "Primary", "OnPrimary", "PrimaryContainer", "OnPrimaryContainer",
        "Secondary", "OnSecondary", "SecondaryContainer", "OnSecondaryContainer",
        "Tertiary", "OnTertiary", "TertiaryContainer", "OnTertiaryContainer",
        "Error", "OnError", "ErrorContainer", "OnErrorContainer",
        "Surface", "OnSurface", "OnSurfaceVariant", "Outline", "OutlineVariant",
        "InverseSurface", "OnInverseSurface", "InversePrimary", "SurfaceTint", "Shadow", "Scrim",
        "SurfaceDim", "SurfaceBright", "SurfaceContainerLowest", "SurfaceContainerLow",
        "SurfaceContainer", "SurfaceContainerHigh", "SurfaceContainerHighest"
    ];

    // ARGB roles exported from the Android ColorScheme.fromSeed tonalSpot path; neutral uses its neutral variant.
    private static readonly IReadOnlyDictionary<string, PaletteRoles> Palettes = new Dictionary<string, PaletteRoles>
    {
        ["sage"] = CreatePalette(
            "3E6837,FFFFFF,BEF0B2,265022,3E6837,FFFFFF,BEF0B2,265022,3E6837,FFFFFF,BEF0B2,265022,BA1A1A,FFFFFF,FFDAD6,93000A,F7FBF1,191D17,42493F,73796E,C2C8BC,2E322B,EFF2E8,A3D397,3E6837,000000,000000,D8DBD2,F7FBF1,FFFFFF,F2F5EB,ECEFE5,E6E9E0,E0E4DA",
            "A3D397,0E380D,265022,BEF0B2,A3D397,0E380D,265022,BEF0B2,A3D397,0E380D,265022,BEF0B2,FFB4AB,690005,93000A,FFDAD6,11140F,E0E4DA,C2C8BC,8C9388,42493F,E0E4DA,2E322B,3E6837,A3D397,000000,000000,11140F,363A34,0B0F0A,191D17,1D211B,272B25,323630"),
        ["blue"] = CreatePalette(
            "39608F,FFFFFF,D3E4FF,1E4876,39608F,FFFFFF,D3E4FF,1E4876,39608F,FFFFFF,D3E4FF,1E4876,BA1A1A,FFFFFF,FFDAD6,93000A,F8F9FF,191C20,43474E,73777F,C3C6CF,2E3035,EFF0F7,A3C9FE,39608F,000000,000000,D8DAE0,F8F9FF,FFFFFF,F2F3FA,ECEDF4,E7E8EE,E1E2E8",
            "A3C9FE,00315B,1E4876,D3E4FF,A3C9FE,00315B,1E4876,D3E4FF,A3C9FE,00315B,1E4876,D3E4FF,FFB4AB,690005,93000A,FFDAD6,111418,E1E2E8,C3C6CF,8D9199,43474E,E1E2E8,2E3035,39608F,A3C9FE,000000,000000,111418,37393E,0C0E13,191C20,1D2024,272A2F,32353A"),
        ["purple"] = CreatePalette(
            "6750A4,FFFFFF,EADDFF,21005D,6750A4,FFFFFF,EADDFF,21005D,6750A4,FFFFFF,EADDFF,21005D,B3261E,FFFFFF,F9DEDC,410E0B,FEF7FF,1D1B20,49454F,79747E,CAC4D0,322F35,F5EFF7,D0BCFF,65558F,000000,000000,DED8E0,FDF7FF,FFFFFF,F7F2FA,F3EDF7,ECE6F0,E6E0E9",
            "CFBDFE,36275D,4D3D75,E9DDFF,CFBDFE,36275D,4D3D75,E9DDFF,CFBDFE,36275D,4D3D75,E9DDFF,FFB4AB,690005,93000A,FFDAD6,141218,E6E0E9,CAC4CF,948F99,49454E,E6E0E9,322F35,65558F,CFBDFE,000000,000000,141218,3B383E,0F0D13,1D1B20,211F24,2B292F,36343A"),
        ["teal"] = CreatePalette(
            "006A60,FFFFFF,9EF2E4,005048,006A60,FFFFFF,9EF2E4,005048,006A60,FFFFFF,9EF2E4,005048,BA1A1A,FFFFFF,FFDAD6,93000A,F4FBF8,161D1B,3F4947,6F7977,BEC9C6,2B3230,ECF2EF,82D5C8,006A60,000000,000000,D5DBD9,F4FBF8,FFFFFF,EFF5F2,E9EFED,E3EAE7,DDE4E1",
            "82D5C8,003731,005048,9EF2E4,82D5C8,003731,005048,9EF2E4,82D5C8,003731,005048,9EF2E4,FFB4AB,690005,93000A,FFDAD6,0E1513,DDE4E1,BEC9C6,899390,3F4947,DDE4E1,2B3230,006A60,82D5C8,000000,000000,0E1513,343B39,090F0E,161D1B,1A211F,252B2A,303635"),
        ["orange"] = CreatePalette(
            "855318,FFFFFF,FFDCBE,693C00,855318,FFFFFF,FFDCBE,693C00,855318,FFFFFF,FFDCBE,693C00,BA1A1A,FFFFFF,FFDAD6,93000A,FFF8F5,211A14,51453A,837468,D5C3B5,372F28,FDEEE3,FDB975,855318,000000,000000,E6D7CD,FFF8F5,FFFFFF,FFF1E7,FAEBE0,F4E5DB,EFE0D5",
            "FDB975,4A2800,693C00,FFDCBE,FDB975,4A2800,693C00,FFDCBE,FDB975,4A2800,693C00,FFDCBE,FFB4AB,690005,93000A,FFDAD6,19120C,EFE0D5,D5C3B5,9D8E81,51453A,EFE0D5,372F28,855318,FDB975,000000,000000,19120C,403830,130D07,211A14,261E18,302822,3C332C"),
        ["neutral"] = CreatePalette(
            "516164,FFFFFF,D4E6E9,39494C,516164,FFFFFF,D4E6E9,39494C,516164,FFFFFF,D4E6E9,39494C,BA1A1A,FFFFFF,FFDAD6,93000A,FAF9F9,1B1C1C,464747,777777,C7C6C6,2F3031,F2F0F0,B8CACD,516164,000000,000000,DBDAD9,FAF9F9,FFFFFF,F4F3F3,EFEDED,E9E8E8,E3E2E2",
            "B8CACD,233336,39494C,D4E6E9,B8CACD,233336,39494C,D4E6E9,B8CACD,233336,39494C,D4E6E9,FFB4AB,690005,93000A,FFDAD6,121414,E3E2E2,C7C6C6,919191,464747,E3E2E2,2F3031,516164,B8CACD,000000,000000,121414,383939,0D0E0F,1B1C1C,1F2020,292A2A,343535")
    };

    static ThemeBrushes()
    {
        Settings.ColorValuesChanged += (_, _) => RefreshPalette();
    }

    public static Brush Primary(FrameworkElement owner) => Resource(owner, "HingeM3OnSurfaceBrush");
    public static Brush Secondary(FrameworkElement owner) => Resource(owner, "HingeM3OnSurfaceVariantBrush");
    public static Brush Muted(FrameworkElement owner) => Resource(owner, "HingeM3OnSurfaceVariantBrush");
    public static Brush Hover(FrameworkElement owner) => Resource(owner, "HingeM3StateHoverBrush");
    public static Brush Pressed(FrameworkElement owner) => Resource(owner, "HingeM3StatePressedBrush");
    public static Brush Border(FrameworkElement owner) => Resource(owner, "HingeM3OutlineVariantBrush");
    public static Brush MutedSurface(FrameworkElement owner) => Resource(owner, "HingeM3SurfaceContainerLowBrush");
    public static Brush AccentText(FrameworkElement owner) => Resource(owner, "HingeM3PrimaryBrush");
    public static Brush Accent(FrameworkElement owner) => Resource(owner, "HingeM3PrimaryBrush");
    public static Brush OnAccent(FrameworkElement owner) => Resource(owner, "HingeM3OnPrimaryBrush");
    public static Brush Error(FrameworkElement owner) => Resource(owner, "HingeM3ErrorBrush");

    public static string NormalizePaletteName(string? paletteName) => paletteName switch
    {
        "tonalSpot" => "purple",
        "sage" or "blue" or "purple" or "teal" or "orange" or "neutral" => paletteName,
        _ => "sage"
    };

    public static void ApplyPalette(string paletteName)
    {
        var roles = Palettes[NormalizePaletteName(paletteName)];
        ApplyRoleMap((ResourceDictionary)M3Dictionary().ThemeDictionaries["Light"], roles.Light);
        ApplyRoleMap((ResourceDictionary)M3Dictionary().ThemeDictionaries["Dark"], roles.Dark);

        foreach (var (key, brush) in Brushes)
            brush.Color = ReadColor(key.Theme, key.Key);
        PaletteChanged?.Invoke();
    }

    private static PaletteRoles CreatePalette(string light, string dark) =>
        new(CreateRoleMap(light), CreateRoleMap(dark));

    private static IReadOnlyDictionary<string, Windows.UI.Color> CreateRoleMap(string values)
    {
        var colors = values.Split(',');
        if (colors.Length != SchemeRoles.Length)
            throw new InvalidDataException($"An M3 palette has {colors.Length} roles; expected {SchemeRoles.Length}.");

        return SchemeRoles.Select((role, index) => new KeyValuePair<string, Windows.UI.Color>(role,
                ParseColor(colors[index])))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static Windows.UI.Color ParseColor(string value)
    {
        var rgb = Convert.ToUInt32(value, 16);
        return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static void ApplyRoleMap(ResourceDictionary dictionary, IReadOnlyDictionary<string, Windows.UI.Color> roles)
    {
        foreach (var (role, color) in roles)
            SetBrushColor(dictionary, $"HingeM3{role}Brush", color);

        var onSurface = roles["OnSurface"];
        var primaryContainer = roles["PrimaryContainer"];
        var onPrimaryContainer = roles["OnPrimaryContainer"];
        SetBrushColor(dictionary, "HingeM3StateHoverBrush", onSurface);
        SetBrushColor(dictionary, "HingeM3StatePressedBrush", onSurface);
        SetBrushColor(dictionary, "HingeM3DisabledContentBrush", onSurface);
        SetBrushColor(dictionary, "NavigationViewDefaultPaneBackground", roles["SurfaceContainer"]);
        SetBrushColor(dictionary, "NavigationViewExpandedPaneBackground", roles["SurfaceContainer"]);
        SetBrushColor(dictionary, "NavigationViewItemForeground", onSurface);
        SetBrushColor(dictionary, "NavigationViewItemBackgroundPointerOver", WithAlpha(onSurface, 20));
        SetBrushColor(dictionary, "NavigationViewItemForegroundPointerOver", onSurface);
        SetBrushColor(dictionary, "NavigationViewItemBackgroundPressed", WithAlpha(onSurface, 31));
        SetBrushColor(dictionary, "NavigationViewItemForegroundPressed", onSurface);
        SetBrushColor(dictionary, "NavigationViewItemBackgroundSelected", primaryContainer);
        SetBrushColor(dictionary, "NavigationViewItemForegroundSelected", onPrimaryContainer);
        SetBrushColor(dictionary, "NavigationViewItemBackgroundSelectedPointerOver", primaryContainer);
        SetBrushColor(dictionary, "NavigationViewItemForegroundSelectedPointerOver", onPrimaryContainer);
        SetBrushColor(dictionary, "NavigationViewSelectionIndicatorForeground", Microsoft.UI.Colors.Transparent);
    }

    private static void SetBrushColor(ResourceDictionary dictionary, string key, Windows.UI.Color color)
    {
        if (!dictionary.ContainsKey(key) || dictionary[key] is not SolidColorBrush brush)
            throw new InvalidOperationException($"The M3 theme dictionary is missing brush '{key}'.");
        brush.Color = color;
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color color, byte alpha) =>
        Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);

    private static ResourceDictionary M3Dictionary() => Application.Current.Resources.MergedDictionaries
        .First(dictionary => dictionary.Source?.OriginalString.EndsWith("HingeM3.xaml", StringComparison.OrdinalIgnoreCase) == true);

    public static Windows.UI.Color Color(FrameworkElement owner, string key)
    {
        var brush = (SolidColorBrush)Resource(owner, key);
        var color = brush.Color;
        return Windows.UI.Color.FromArgb(
            (byte)Math.Round(color.A * brush.Opacity), color.R, color.G, color.B);
    }
    public static Windows.UI.Color Color(ElementTheme theme, string key)
    {
        var themeKey = Accessibility.HighContrast
            ? "HighContrast"
            : theme == ElementTheme.Dark ? "Dark" : "Light";
        var palette = (ResourceDictionary)M3Dictionary().ThemeDictionaries[themeKey];
        return ((SolidColorBrush)palette[key]).Color;
    }
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

    private static void RefreshPalette() => _dispatcher?.TryEnqueue(RefreshPaletteNow);

    private static void RefreshPaletteNow()
    {
        foreach (var (key, brush) in Brushes)
            brush.Color = ReadColor(key.Theme, key.Key);
        PaletteChanged?.Invoke();
    }

    public static TextBlock Text(TextBlock text, string typographyStyle = "HingeM3BodyStyle")
    {
        text.Style ??= (Style)Application.Current.Resources[typographyStyle];
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
