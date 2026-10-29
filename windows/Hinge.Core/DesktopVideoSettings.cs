namespace Hinge.Core;

public sealed record DesktopVideoSettings(int Monitor = 0, int FramesPerSecond = 60,
    int LongestEdge = 1920, int Bitrate = 8_000_000, bool SoftwareEncoding = false, string? MonitorDeviceName = null)
{
    public void Validate()
    {
        if (Monitor is < 0 or > 15 || FramesPerSecond is < 15 or > 120 ||
            LongestEdge is < 640 or > 3840 || Bitrate is < 1_000_000 or > 50_000_000 ||
            (MonitorDeviceName != null && (MonitorDeviceName.Length is < 1 or > 32 ||
                !MonitorDeviceName.StartsWith(@"\\.\DISPLAY", StringComparison.Ordinal))))
            throw new ArgumentOutOfRangeException(nameof(DesktopVideoSettings), "Desktop video settings exceed supported bounds.");
    }
}
