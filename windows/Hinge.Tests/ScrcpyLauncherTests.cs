using System.Diagnostics;
using Hinge.App;

namespace Hinge.Tests;

public class ScrcpyLauncherTests
{
    [Fact]
    public void MissingSettingsKeepCompatibleDefaults() =>
        Assert.Equal(new ScreenControlOptions(), System.Text.Json.JsonSerializer.Deserialize<ScreenControlOptions>("{}"));

    [Fact]
    public void SoftwareDecodingIsAnExplicitSavedCompatibilityChoice()
    {
        var options = new ScreenControlOptions(PreferHardwareDecoding: false);
        var info = ScrcpyLauncher.CreateStartInfo("scrcpy.exe", "192.168.1.20:43210", true, options);
        Assert.Contains("--hwdec=disabled", info.ArgumentList);
        Assert.Contains("--video-buffer=0", info.ArgumentList);
        Assert.DoesNotContain("--hwdec=auto", info.ArgumentList);
        Assert.Equal(options, System.Text.Json.JsonSerializer.Deserialize<ScreenControlOptions>(
            System.Text.Json.JsonSerializer.Serialize(options)));
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("h265")]
    [InlineData("av1")]
    public void CustomQualityParametersReachEncoderWithoutChangingTransport(string codec)
    {
        var options = new ScreenControlOptions(codec, 120, 16, 2560, true, 50);
        var info = ScrcpyLauncher.CreateStartInfo("scrcpy.exe", "192.168.1.20:43210", false, options);
        Assert.Contains("--video-codec=" + codec, info.ArgumentList);
        Assert.Contains("--max-fps=120", info.ArgumentList);
        Assert.Contains("--video-bit-rate=16M", info.ArgumentList);
        Assert.Contains("--max-size=2560", info.ArgumentList);
        Assert.Contains("--hwdec=auto", info.ArgumentList);
        Assert.Contains("--video-buffer=50", info.ArgumentList);
        Assert.Contains("--print-fps", info.ArgumentList);
        Assert.Contains("--no-control", info.ArgumentList);
        Assert.Equal("tcp:127.0.0.1:5038", info.Environment["ADB_SERVER_SOCKET"]);
        Assert.Equal(options, System.Text.Json.JsonSerializer.Deserialize<ScreenControlOptions>(
            System.Text.Json.JsonSerializer.Serialize(options)));
    }

    [Theory]
    [InlineData("h265 --record=private.mp4", 60, 8, 1920)]
    [InlineData("h264", 0, 8, 1920)]
    [InlineData("h264", 241, 8, 1920)]
    [InlineData("h264", 60, 0, 1920)]
    [InlineData("h264", 60, 201, 1920)]
    [InlineData("h264", 60, 8, 15)]
    [InlineData("h264", 60, 8, -16)]
    [InlineData("h264", 60, 8, 7681)]
    public void InvalidQualityDoesNotLaunch(string codec, int fps, int bitRate, int size) =>
        Assert.Throws<ArgumentException>(() => ScrcpyLauncher.CreateStartInfo("scrcpy.exe",
            "192.168.1.20:43210", true, new ScreenControlOptions(codec, fps, bitRate, size)));

    [Theory]
    [InlineData(-1)]
    [InlineData(201)]
    public void InvalidVideoBufferDoesNotLaunch(int videoBufferMs) =>
        Assert.Throws<ArgumentException>(() => ScrcpyLauncher.CreateStartInfo("scrcpy.exe",
            "192.168.1.20:43210", true, new ScreenControlOptions(VideoBufferMs: videoBufferMs)));

    [Fact]
    public void ExistingSavedSettingsDefaultVideoBufferToZero() =>
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<ScreenControlOptions>(
            "{\"VideoCodec\":\"h265\",\"MaxFps\":60,\"BitRateMbps\":8,\"MaxSize\":1920,\"PreferHardwareDecoding\":true}")!.VideoBufferMs);

    [Fact]
    public void OriginalResolutionIsExplicitlySupported() =>
        Assert.Contains("--max-size=0", ScrcpyLauncher.CreateStartInfo("scrcpy.exe",
            "192.168.1.20:43210", true, new ScreenControlOptions(MaxSize: 0)).ArgumentList);

    [Theory]
    [InlineData("INFO: 60 fps", 60, 0)]
    [InlineData("INFO: 59 fps (+2 frames skipped)", 59, 2)]
    [InlineData("0 fps", 0, 0)]
    public void ActualFrameRateIncludesIdleAndSkippedFrames(string line, int fps, int skipped) =>
        Assert.Equal(new ScreenFrameRate(fps, skipped), ScrcpyLauncher.ParseFrameRate(line));

    [Theory]
    [InlineData("INFO: FPS counter started")]
    [InlineData("--max-fps=60")]
    [InlineData("INFO: 999999999999 fps")]
    [InlineData("INFO: ١٢ fps")]
    [InlineData("INFO: ERROR 60 fps")]
    public void NonMeasurementsAreNotReportedAsFrameRate(string line) =>
        Assert.Null(ScrcpyLauncher.ParseFrameRate(line));

    [Fact]
    public async Task FrameMeasurementsSurvivePipeChunkBoundariesAndOversizedLogLines()
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-Command",
                     "[Console]::Error.WriteLine(('x' * 4096)); [Console]::Error.Write('INFO: '); Start-Sleep -Milliseconds 30; [Console]::Error.WriteLine('59 fps (+2 frames skipped)')" })
            info.ArgumentList.Add(argument);
        var samples = new System.Collections.Concurrent.ConcurrentQueue<ScreenFrameRate>();
        await ScrcpyLauncher.RunAsync(info, () => { }, CancellationToken.None, samples.Enqueue);
        Assert.Equal(new ScreenFrameRate(59, 2), Assert.Single(samples));
    }

    [Theory]
    [InlineData("adb-test._adb-tls-connect._tcp", "43210", true)]
    [InlineData("192.168.1.20:43210", "43210", true)]
    [InlineData("[fe80::1]:43210", "43210", true)]
    [InlineData("192.168.1.20:5555", "43210", false)]
    [InlineData("192.168.1.20:5555", "0", false)]
    [InlineData("usb-serial", "43210", false)]
    [InlineData("adb-test._adb-tls-connect._tcp", "", true)]
    [InlineData("adb-test._adb-tls-connect._tcp", "-1", true)]
    [InlineData("adb-test._adb._tcp", "", false)]
    public void ScreenRequiresSecureWirelessDebugging(string transport, string tlsPort, bool expected) =>
        Assert.Equal(expected, WirelessAdbClient.IsTlsScreenTransport(transport, tlsPort));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchPinsDeviceServerAndHostKeyWithoutShell(bool control)
    {
        var transport = "192.168.1.20:43210";
        var info = ScrcpyLauncher.CreateStartInfo(@"C:\Program Files\scrcpy\scrcpy.exe", transport, control);
        var adb = WirelessAdbClient.CreateStartInfo();
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal("tcp:127.0.0.1:5038", info.Environment["ADB_SERVER_SOCKET"]);
        Assert.Equal("5038", info.Environment["ANDROID_ADB_SERVER_PORT"]);
        Assert.Equal(adb.Environment["ANDROID_USER_HOME"], info.Environment["ANDROID_USER_HOME"]);
        Assert.Equal(Path.GetFullPath(adb.FileName), info.Environment["ADB"]);
        Assert.Contains("--serial=" + transport, info.ArgumentList);
        Assert.Contains("--no-clipboard-autosync", info.ArgumentList);
        Assert.Contains("--video-codec=h264", info.ArgumentList);
        Assert.Equal(!control, info.ArgumentList.Contains("--no-control"));
        Assert.DoesNotContain(info.ArgumentList, value => value.Contains("tcpip") || value.Contains("kill-server"));
        Assert.False(info.Environment.ContainsKey("SCRCPY_SERVER_PATH"));
        Assert.False(info.Environment.ContainsKey("ANDROID_SERIAL"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-e")]
    [InlineData("unverified-usb-serial")]
    [InlineData("192.168.1.20:43210 --tcpip")]
    public void InvalidTransportsAreRejected(string transport) =>
        Assert.Throws<ArgumentException>(() => ScrcpyLauncher.CreateStartInfo("scrcpy.exe", transport, true));

    [Fact]
    public void MissingBundledRuntimeExplainsHowToRepair() =>
        Assert.Contains("重新安装", Assert.Throws<FileNotFoundException>(() =>
            ScrcpyLauncher.FindExecutable(@"C:\missing-hinge-289a")).Message);

    [Fact]
    public void PartialBundleIsRejectedUntilAllDependenciesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "hinge-screen-test-" + Guid.NewGuid());
        var directory = Path.Combine(root, "tools", "scrcpy");
        Directory.CreateDirectory(directory);
        var files = new[] { "scrcpy.exe", "scrcpy-server", "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll",
            "SDL3.dll", "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll", "swresample-7.dll", "libusb-1.0.dll" };
        try
        {
            File.WriteAllText(Path.Combine(directory, files[0]), "test");
            Assert.Throws<FileNotFoundException>(() => ScrcpyLauncher.FindExecutable(root));
            foreach (var file in files.Skip(1)) File.WriteAllText(Path.Combine(directory, file), "test");
            Assert.Equal(Path.Combine(directory, "scrcpy.exe"), ScrcpyLauncher.FindExecutable(root));
        }
        finally
        {
            foreach (var file in files) File.Delete(Path.Combine(directory, file));
            Directory.Delete(directory);
            Directory.Delete(Path.GetDirectoryName(directory)!);
            Directory.Delete(root);
        }
    }

    [Fact]
    public async Task FailedProcessReportsBoundedDiagnosticOutput()
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-Command", "[Console]::Error.Write(('x' * 8192)); exit 7" })
            info.ArgumentList.Add(argument);
        var started = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScrcpyLauncher.RunAsync(info, () => started = true, CancellationToken.None));
        Assert.True(started);
        Assert.Contains("7", error.Message);
        Assert.InRange(error.Message.Length, 2048, 2200);
    }

    [Fact]
    public async Task CancellationStopsOwnedProcess()
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 60" })
            info.ArgumentList.Add(argument);
        using var cancellation = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScrcpyLauncher.RunAsync(info, () => cancellation.Cancel(), cancellation.Token).WaitAsync(deadline.Token));
    }
}
