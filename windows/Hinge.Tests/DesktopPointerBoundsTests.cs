using Hinge.Core;

namespace Hinge.Tests;

public class DesktopPointerBoundsTests
{
    [Fact]
    public void AbsoluteTouchMapsToSelectedNegativeOriginDisplayAndRejectsInvalidCoordinates()
    {
        var display = new DesktopPointerBounds(-1080, -300, 1080, 1920);
        Assert.Equal((-1080, -300), display.FromNormalized(0, 0));
        Assert.Equal((-1, 1619), display.FromNormalized(65535, 65535));
        Assert.Throws<ArgumentOutOfRangeException>(() => display.FromNormalized(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => display.FromNormalized(0, 65536));
    }
    [Fact]
    public void OutsideCursorStartsInSelectedDisplayAndExtremeDeltasStayInside()
    {
        var display = new DesktopPointerBounds(-1080, -300, 1080, 1920);
        Assert.Equal((-540, 660), display.Move(100, 200, 0, 0));
        Assert.Equal((-1080, 1619), display.Move(-500, 200, int.MinValue, int.MaxValue));
        Assert.Equal((-1, -300), display.Move(-500, 200, int.MaxValue, int.MinValue));
        Assert.Equal((-505, 215), display.Move(-500, 200, -5, 15));
    }

    [Theory]
    [InlineData(-1920, -1080, 5760, 2160)]
    [InlineData(-1080, -1920, 1080, 1920)]
    [InlineData(0, 0, 3840, 2160)]
    [InlineData(0, 0, 65536, 2)]
    public void NormalizedPixelCentersRoundTripWithoutCrossingDisplayEdges(int left, int top, int width, int height)
    {
        var desktop = new DesktopPointerBounds(left, top, width, height);
        for (int x = 0; x < width; x++) {
            var point = desktop.Normalize(left + x, top);
            Assert.InRange(point.X, 0, 65535);
            Assert.Equal(x, (int)((long)point.X * width / 65536));
        }
        for (int y = 0; y < height; y++) {
            var point = desktop.Normalize(left, top + y);
            Assert.Equal(y, (int)((long)point.Y * height / 65536));
        }
    }

    [Theory]
    [InlineData(0, 0, 1, 1080)]
    [InlineData(0, 0, 1920, 65537)]
    [InlineData(2147483647, 0, 1920, 1080)]
    public void InvalidGeometryCannotWrapIntoAnotherScreen(int left, int top, int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopPointerBounds(left, top, width, height).Move(0, 0, 0, 0));
}
