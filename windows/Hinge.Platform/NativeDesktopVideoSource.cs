using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Globalization;
using Hinge.Core;

namespace Hinge.Platform;

/// <summary>One native capture process per authorized viewer; no encoded-frame queue in the UI.</summary>
public sealed class NativeDesktopVideoSource
{
    private readonly string _executable;
    public event EventHandler<string>? EncoderSelected;
    public NativeDesktopVideoSource(string? executable = null) =>
        _executable = executable ?? Path.Combine(AppContext.BaseDirectory, "Hinge.DesktopVideo.exe");

    public async Task<IReadOnlyList<DesktopMonitor>> ListMonitorsAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = new ProcessStartInfo(_executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } };
        process.StartInfo.ArgumentList.Add("--list-monitors");
        if (!process.Start()) throw new IOException("无法读取电脑显示器。");
        try
        {
            var buffer = new char[8193];
            int length = await process.StandardOutput.ReadBlockAsync(buffer.AsMemory(), timeout.Token);
            if (length > 8192) throw new InvalidDataException("显示器列表过长。");
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new IOException("无法读取电脑显示器，请检查显示器连接。");
            return ParseMonitorList(new string(buffer, 0, length));
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
        }
    }

    public static IReadOnlyList<DesktopMonitor> ParseMonitorList(string text)
    {
        if (text.Length > 8192) throw new InvalidDataException("显示器列表过长。");
        var monitors = new List<DesktopMonitor>();
        foreach (string row in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = row.Split('\t');
            if (parts.Length != 5 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index != monitors.Count || index > 15 ||
                !int.TryParse(parts[2], out int width) || width is < 2 or > 32768 ||
                !int.TryParse(parts[3], out int height) || height is < 2 or > 32768 ||
                !int.TryParse(parts[4], out int rotation) || rotation is < 0 or > 3 ||
                monitors.Any(item => item.DeviceName == parts[1]))
                throw new InvalidDataException("显示器列表格式无效。");
            try { new DesktopVideoSettings(Monitor: index, MonitorDeviceName: parts[1]).Validate(); }
            catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("显示器名称无效。", error); }
            monitors.Add(new DesktopMonitor(index, parts[1], width, height, rotation));
        }
        return monitors;
    }

    public async IAsyncEnumerable<DesktopVideoPacket> CaptureAsync(DesktopVideoSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        settings.Validate();
        if (!File.Exists(_executable)) throw new FileNotFoundException("桌面采集组件缺失，请重新安装 Hinge。", _executable);
        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_executable)!,
        };
        foreach (string argument in new[] { "--capture", "--monitor", settings.Monitor.ToString(),
            "--fps", settings.FramesPerSecond.ToString(), "--longest", settings.LongestEdge.ToString(),
            "--bitrate", settings.Bitrate.ToString(), "--parent-pid", Environment.ProcessId.ToString() }) info.ArgumentList.Add(argument);
        if (settings.SoftwareEncoding) info.ArgumentList.Add("--software");
        if (settings.MonitorDeviceName != null) {
            info.ArgumentList.Add("--monitor-name"); info.ArgumentList.Add(settings.MonitorDeviceName);
        }
        using var process = new Process { StartInfo = info };
        using var diagnosticsStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var diagnostics = new Queue<string>();
        if (!process.Start()) throw new IOException("无法启动电脑桌面采集组件。");
        Task errors = ReadDiagnosticsAsync(process, diagnostics, diagnosticsStop.Token);
        try
        {
            await foreach (var packet in ReadPacketsAsync(process.StandardOutput.BaseStream,
                TimeSpan.FromSeconds(8), cancellationToken))
            {
                yield return packet;
            }
            await errors;
            cancellationToken.ThrowIfCancellationRequested();
            string details;
            lock (diagnostics) details = string.Join("; ", diagnostics);
            throw new IOException("电脑桌面采集已结束。" + details);
        }
        finally
        {
            diagnosticsStop.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { }
            try { await errors; }
            catch (OperationCanceledException) { }
        }
    }

    internal static async IAsyncEnumerable<DesktopVideoPacket> ReadPacketsAsync(Stream stream,
        TimeSpan startupTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(startupTimeout);
        bool hasVideo = false;
        while (true)
        {
            DesktopVideoPacket? packet;
            try { packet = await DesktopVideoProtocol.ReadAsync(stream, hasVideo ? cancellationToken : startup.Token); }
            catch (OperationCanceledException) when (startup.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("电脑桌面采集未能在8秒内输出首帧，请检查编码器状态。"); }
            if (packet == null) yield break;
            if (packet.Flags != DesktopVideoFlags.Heartbeat &&
                ((packet.Flags & DesktopVideoFlags.Configuration) == 0 || (packet.Flags & DesktopVideoFlags.KeyFrame) != 0))
            {
                hasVideo = true;
                startup.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            yield return packet;
        }
    }

    private async Task ReadDiagnosticsAsync(Process process, Queue<string> diagnostics,
        CancellationToken cancellationToken)
    {
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.StartsWith("encoder=", StringComparison.Ordinal))
                EncoderSelected?.Invoke(this, line.Length > 512 ? line[..512] : line);
            lock (diagnostics)
            {
                if (diagnostics.Count == 8) diagnostics.Dequeue();
                diagnostics.Enqueue(line.Length > 512 ? line[..512] : line);
            }
        }
    }
}

public sealed record DesktopMonitor(int Index, string DeviceName, int Width, int Height, int Rotation)
{
    public override string ToString() => $"显示器 {Index + 1} · {Width} × {Height} · {DeviceName}";
}
