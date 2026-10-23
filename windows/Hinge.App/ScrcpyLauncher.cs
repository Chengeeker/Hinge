using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Hinge.App;

internal sealed record ScreenControlOptions(string VideoCodec = "h264", int MaxFps = 60,
    int BitRateMbps = 8, int MaxSize = 1920, bool PreferHardwareDecoding = true, int VideoBufferMs = 0)
{
    public void Validate()
    {
        if (VideoCodec is not ("h264" or "h265" or "av1")) throw new ArgumentException("请选择 H.264、H.265 或 AV1 编码。");
        if (MaxFps is < 1 or > 240) throw new ArgumentException("帧率上限需为 1–240 的整数。");
        if (BitRateMbps is < 1 or > 200) throw new ArgumentException("视频码率需为 1–200 Mbps 的整数。");
        if (MaxSize != 0 && (MaxSize is < 16 or > 7680)) throw new ArgumentException("最长边需为 16–7680 的整数，或填 0 使用原始分辨率。");
        if (VideoBufferMs is < 0 or > 200) throw new ArgumentException("画面平滑缓冲需为 0–200 毫秒。");
    }
}

internal sealed record ScreenFrameRate(int RenderedFps, int SkippedFrames);

internal static class ScrcpyLauncher
{
    public static string FindExecutable(string? appDirectory = null)
    {
        var directory = Path.Combine(appDirectory ?? AppContext.BaseDirectory, "tools", "scrcpy");
        foreach (var name in new[] { "scrcpy.exe", "scrcpy-server", "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll",
                     "SDL3.dll", "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll", "swresample-7.dll", "libusb-1.0.dll" })
        {
            if (!File.Exists(Path.Combine(directory, name)))
                throw new FileNotFoundException($"Hinge 内置投屏组件缺失（{name}），请重新安装完整的 Hinge 安装包。");
        }
        return Path.GetFullPath(Path.Combine(directory, "scrcpy.exe"));
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string transport, bool control,
        ScreenControlOptions? options = null)
    {
        options ??= new ScreenControlOptions();
        options.Validate();
        if (!WirelessAdbClient.IsWirelessTransport(transport))
            throw new ArgumentException("投屏需要已验证的无线 ADB 设备。", nameof(transport));
        var adb = WirelessAdbClient.CreateStartInfo();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["ADB"] = Path.GetFullPath(adb.FileName);
        start.Environment["ANDROID_USER_HOME"] = adb.Environment["ANDROID_USER_HOME"];
        // Override inherited socket settings so scrcpy never switches to a different ADB server.
        start.Environment["ADB_SERVER_SOCKET"] = "tcp:127.0.0.1:" + adb.ArgumentList[1];
        start.Environment["ANDROID_ADB_SERVER_PORT"] = adb.ArgumentList[1];
        start.Environment.Remove("ANDROID_SERIAL");
        start.Environment.Remove("SCRCPY_SERVER_PATH");
        foreach (var argument in new[] { "--serial=" + transport, "--window-title=Hinge · " + (control ? "远控" : "投屏"),
                     "--video-codec=" + options.VideoCodec, "--max-size=" + options.MaxSize,
                     "--max-fps=" + options.MaxFps, "--video-bit-rate=" + options.BitRateMbps + "M",
                     options.PreferHardwareDecoding ? "--hwdec=auto" : "--hwdec=disabled",
                     "--video-buffer=" + options.VideoBufferMs, "--print-fps", "--no-audio",
                     "--no-clipboard-autosync" })
            start.ArgumentList.Add(argument);
        if (!control) start.ArgumentList.Add("--no-control");
        return start;
    }

    public static async Task RunAsync(ProcessStartInfo startInfo, Action started, CancellationToken cancellationToken,
        Action<ScreenFrameRate>? frameRate = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("无法启动 scrcpy。");
        using var stop = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited && !process.CloseMainWindow()) process.Kill(); }
            catch (InvalidOperationException) { }
        });
        var stdout = ReadTailAsync(process.StandardOutput, frameRate);
        var stderr = ReadTailAsync(process.StandardError, frameRate);
        try
        {
            started();
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException)
                {
                    // Kill only this scrcpy process; the shared ADB server must survive.
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    await process.WaitForExitAsync();
                }
            }
            await Task.WhenAll(stdout, stderr);
        }
        var details = await stderr;
        var output = await stdout;
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"scrcpy 已退出（{process.ExitCode}）：{string.Join("\n", new[] { details, output }.Where(value => value.Length > 0))}");
    }

    internal static ScreenFrameRate? ParseFrameRate(string line)
    {
        var match = Regex.Match(line.Trim(), @"^(?:INFO: )?(?<fps>[0-9]{1,4}) fps(?: \(\+(?<skipped>[0-9]{1,7}) frames skipped\))?$",
            RegexOptions.CultureInvariant);
        return match.Success ? new ScreenFrameRate(int.Parse(match.Groups["fps"].Value),
            match.Groups["skipped"].Success ? int.Parse(match.Groups["skipped"].Value) : 0) : null;
    }

    private static async Task<string> ReadTailAsync(StreamReader reader, Action<ScreenFrameRate>? frameRate)
    {
        var buffer = new char[512];
        var tail = new StringBuilder();
        var line = new StringBuilder();
        var lineOverflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 2048) tail.Remove(0, tail.Length - 2048);
            if (frameRate == null) continue;
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    if (!lineOverflow && ParseFrameRate(line.ToString()) is { } sample) frameRate(sample);
                    line.Clear();
                    lineOverflow = false;
                }
                else if (line.Length < 512) line.Append(character);
                else lineOverflow = true;
            }
        }
        return tail.ToString().Trim();
    }
}
