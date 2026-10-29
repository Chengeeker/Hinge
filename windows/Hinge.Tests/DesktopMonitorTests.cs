using Hinge.Core;
using Hinge.Platform;
using System.Text.Json;

namespace Hinge.Tests;

public class DesktopMonitorTests
{
    [Fact]
    public void MonitorListPreservesNativeOrderAndUprightDimensions()
    {
        var monitors = NativeDesktopVideoSource.ParseMonitorList("0\t\\\\.\\DISPLAY1\t1920\t1080\t0\r\n1\t\\\\.\\DISPLAY4\t1080\t1920\t1\n");
        Assert.Equal(2, monitors.Count);
        Assert.Equal(new DesktopMonitor(1, @"\\.\DISPLAY4", 1080, 1920, 1), monitors[1]);
        var settings = new DesktopVideoSettings(Monitor: 1, MonitorDeviceName: monitors[1].DeviceName);
        Assert.Equal(settings, JsonSerializer.Deserialize<DesktopVideoSettings>(JsonSerializer.Serialize(settings)));
        Assert.Empty(NativeDesktopVideoSource.ParseMonitorList(""));
    }

    [Theory]
    [InlineData("1\t\\\\.\\DISPLAY1\t1920\t1080\t0")]
    [InlineData("0\tinvalid\t1920\t1080\t0")]
    [InlineData("0\t\\\\.\\DISPLAY1\t0\t1080\t0")]
    [InlineData("0\t\\\\.\\DISPLAY1\t1920\t1080\t4")]
    [InlineData("0\t\\\\.\\DISPLAY1\t1920\t1080\t0\n1\t\\\\.\\DISPLAY1\t1920\t1080\t0")]
    public void MalformedMonitorListCannotSelectAnotherScreen(string text) =>
        Assert.Throws<InvalidDataException>(() => NativeDesktopVideoSource.ParseMonitorList(text));

    [Fact]
    public void ExcessiveMonitorOutputIsRejected() =>
        Assert.Throws<InvalidDataException>(() => NativeDesktopVideoSource.ParseMonitorList(new string('x', 8193)));
}
