using System.Text.Json;
using Hinge.Core;

namespace Hinge.Tests;

public class DesktopVideoSettingsTests
{
    [Fact]
    public void SavedSettingsRoundTripAndOldEmptySettingsKeepDefaults()
    {
        var custom = new DesktopVideoSettings(FramesPerSecond: 90, LongestEdge: 2560, Bitrate: 16_000_000);
        var loaded = JsonSerializer.Deserialize<DesktopVideoSettings>(JsonSerializer.Serialize(custom));
        Assert.Equal(custom, loaded);
        loaded!.Validate();
        Assert.Equal(new DesktopVideoSettings(), JsonSerializer.Deserialize<DesktopVideoSettings>("{}"));
        new DesktopVideoSettings(15, 120, 3840, 50_000_000).Validate();
        new DesktopVideoSettings(0, 15, 640, 1_000_000).Validate();
    }

    [Theory]
    [InlineData(-1, 60, 1920, 8_000_000)]
    [InlineData(16, 60, 1920, 8_000_000)]
    [InlineData(0, 14, 1920, 8_000_000)]
    [InlineData(0, 121, 1920, 8_000_000)]
    [InlineData(0, 60, 639, 8_000_000)]
    [InlineData(0, 60, 3841, 8_000_000)]
    [InlineData(0, 60, 1920, 999_999)]
    [InlineData(0, 60, 1920, 50_000_001)]
    public void InvalidPersistedValuesAreRejected(int monitor, int fps, int edge, int bitrate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DesktopVideoSettings(monitor, fps, edge, bitrate).Validate());
}
