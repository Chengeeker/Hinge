using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Hinge.App;

/// <summary>
/// A small dependency-free wrapping panel for page headers and filter toolbars.
/// It keeps the last control right-aligned while the line fits and wraps the
/// toolbar below the title when the window becomes narrow.
/// </summary>
public sealed class HingeWrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(HingeWrapPanel),
            new PropertyMetadata(8d));

    public static readonly DependencyProperty LineSpacingProperty =
        DependencyProperty.Register(
            nameof(LineSpacing),
            typeof(double),
            typeof(HingeWrapPanel),
            new PropertyMetadata(8d));

    public static readonly DependencyProperty JustifyLastLineProperty =
        DependencyProperty.Register(
            nameof(JustifyLastLine),
            typeof(bool),
            typeof(HingeWrapPanel),
            new PropertyMetadata(true));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public bool JustifyLastLine
    {
        get => (bool)GetValue(JustifyLastLineProperty);
        set => SetValue(JustifyLastLineProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var lines = BuildLines(availableSize.Width);
        var width = double.IsInfinity(availableSize.Width)
            ? lines.Count == 0 ? 0 : lines.Max(line => line.Width)
            : availableSize.Width;
        var height = lines.Sum(line => line.Height) + Math.Max(0, lines.Count - 1) * LineSpacing;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var lines = BuildLines(finalSize.Width);
        var y = 0d;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var x = 0d;
            var justify = JustifyLastLine && lines.Count == 1 && line.Children.Count > 1;
            for (var childIndex = 0; childIndex < line.Children.Count; childIndex++)
            {
                var child = line.Children[childIndex];
                var desired = child.DesiredSize;
                if (justify && childIndex == line.Children.Count - 1)
                {
                    x = Math.Max(x, finalSize.Width - desired.Width);
                }

                child.Arrange(new Rect(x, y, desired.Width, line.Height));
                x += desired.Width + Spacing;
            }

            y += line.Height + LineSpacing;
        }

        return finalSize;
    }

    private List<Line> BuildLines(double availableWidth)
    {
        var finiteWidth = !double.IsInfinity(availableWidth) && availableWidth > 0;
        var lines = new List<Line>();
        var current = new Line();

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = child.DesiredSize;
            var nextWidth = current.Children.Count == 0
                ? desired.Width
                : current.Width + Spacing + desired.Width;
            if (finiteWidth && current.Children.Count > 0 && nextWidth > availableWidth)
            {
                lines.Add(current);
                current = new Line();
                nextWidth = desired.Width;
            }

            current.Children.Add(child);
            current.Width = nextWidth;
            current.Height = Math.Max(current.Height, desired.Height);
        }

        if (current.Children.Count > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private sealed class Line
    {
        public List<UIElement> Children { get; } = [];
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
