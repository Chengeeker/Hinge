using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

internal static class SettingsPageLayout
{
    public static void MatchViewportWidth(ScrollViewer scrollViewer, FrameworkElement contentRoot)
    {
        void UpdateWidth()
        {
            var width = scrollViewer.ViewportWidth;
            if (width > 0 && contentRoot.Width != width)
                contentRoot.Width = width;
        }

        scrollViewer.Loaded += (_, _) => UpdateWidth();
        scrollViewer.SizeChanged += (_, _) => UpdateWidth();
    }
}
