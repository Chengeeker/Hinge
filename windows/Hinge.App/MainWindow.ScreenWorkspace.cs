using System.Collections.Concurrent;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class MainWindow
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _screenSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _screenStatuses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _screenFrameRates =
        new(StringComparer.OrdinalIgnoreCase);

    private static string DeviceAdbHostKey(string deviceId) =>
        "WirelessAdbHost." + deviceId.ToLowerInvariant();
    private static string DeviceAdbPortKey(string deviceId) =>
        "WirelessAdbConnectPort." + deviceId.ToLowerInvariant();

    private (string Host, string Port) GetDeviceAdbEndpoint(string deviceId)
    {
        var host = ReadUserSetting(DeviceAdbHostKey(deviceId)) as string ?? string.Empty;
        var port = ReadUserSetting(DeviceAdbPortKey(deviceId)) as string ?? string.Empty;
        // Legacy single-phone installs get a no-data-loss migration only
        // when their stored Hinge identity matches this selected device.
        if (string.IsNullOrWhiteSpace(host) &&
            string.Equals(WirelessAdbClient.LoadHingeDeviceId(), deviceId,
                StringComparison.OrdinalIgnoreCase))
        {
            host = ReadUserSetting(WirelessAdbHostSettingName) as string ?? string.Empty;
            port = ReadUserSetting(WirelessAdbConnectPortSettingName) as string ?? string.Empty;
        }
        return (host, port);
    }

    private void RememberDeviceAdbEndpoint(string host, string port)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        var id = FindTrustedAndroidDeviceId(host);
        if (string.IsNullOrWhiteSpace(id)) return;
        WriteUserSetting(DeviceAdbHostKey(id), host);
        if (!string.IsNullOrWhiteSpace(port))
            WriteUserSetting(DeviceAdbPortKey(id), port);
    }

    private void SetDeviceScreenStatus(string deviceId, string status)
    {
        _screenStatuses[deviceId] = status;
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetDeviceScreenStatus(deviceId, status));
            return;
        }
        if (_deviceWorkspace.IsSelected(deviceId))
            UpdateScreenStatus(status);
        else if (_remoteSettingsPage != null)
            _remoteSettingsPage.PhoneStatus.Text = _screenSessions.Count > 0
                ? $"有 {_screenSessions.Count} 台手机正在投屏" : "未运行";
    }

    private void RefreshScreenTargetLabel()
    {
        var id = _deviceWorkspace.SelectedDeviceId;
        if (_screenControlPage is { } page)
        {
            var name = id == null ? "尚未选择" :
                GetPhoneTargets().FirstOrDefault(p => string.Equals(p.DeviceId, id,
                    StringComparison.OrdinalIgnoreCase))?.Name ?? "离线手机";
            page.TargetDevice.Text = "目标手机：" + name;
        }
    }

    private void RefreshSelectedScreenStatus()
    {
        var id = _deviceWorkspace.SelectedDeviceId;
        RefreshScreenTargetLabel();
        _screenFrameRate = id != null &&
            _screenFrameRates.TryGetValue(id, out var fps)
                ? fps : "实时渲染帧率：未运行";
        UpdateScreenStatus(id != null && _screenStatuses.TryGetValue(id, out var status)
            ? status : "所选手机尚未启动投屏");
    }

    private async Task StartScreenForDeviceAsync(bool control)
    {
        if (_screenControlPage is not { } page || _wirelessAdbBusy) return;
        var deviceId = _deviceWorkspace.SelectedDeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            await ShowDialogAsync("请先选择手机",
                "请先在侧边栏选择需要投屏的 Android 手机。", false);
            return;
        }
        var cancellation = new CancellationTokenSource();
        if (!_screenSessions.TryAdd(deviceId, cancellation))
        {
            cancellation.Dispose();
            return;
        }

        var options = page.Options;
        _screenFrameRates[deviceId] = "实时渲染帧率：等待统计";
        SetDeviceScreenStatus(deviceId, "正在验证所选手机的无线 ADB 身份…");
        try
        {
            var (host, port) = GetDeviceAdbEndpoint(deviceId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var transport = await WirelessAdbClient.FindTransportForHingeDeviceAsync(
                deviceId, timeout.Token, host, port);
            if (string.IsNullOrWhiteSpace(transport))
                throw new InvalidOperationException(
                    "找不到与当前手机 Hinge 设备 ID 匹配的无线 ADB。请在该手机上安装新版 Hinge，" +
                    "开启无线调试，并在设置中为该手机完成配对和连接。不会使用其他手机的 ADB。");

            var executable = ScrcpyLauncher.FindExecutable();
            await ScrcpyLauncher.RunAsync(
                ScrcpyLauncher.CreateStartInfo(executable, transport, control, options),
                () => SetDeviceScreenStatus(deviceId,
                    $"已启动{(control ? "投屏并控制" : "仅投屏")} · {transport} · " +
                    $"{options.VideoCodec.ToUpperInvariant()}，{options.MaxFps} FPS 上限"),
                cancellation.Token,
                sample => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_screenSessions.TryGetValue(deviceId, out var current) ||
                        !ReferenceEquals(current, cancellation) ||
                        cancellation.IsCancellationRequested) return;
                    var label = $"实时渲染帧率：{sample.RenderedFps} FPS · 跳帧 {sample.SkippedFrames}";
                    _screenFrameRates[deviceId] = label;
                    if (_deviceWorkspace.IsSelected(deviceId))
                    {
                        _screenFrameRate = label;
                        if (_screenControlPage != null)
                            _screenControlPage.FrameRate.Text = label;
                    }
                }));
            SetDeviceScreenStatus(deviceId, "投屏已结束。");
        }
        catch (OperationCanceledException)
        {
            SetDeviceScreenStatus(deviceId,
                cancellation.IsCancellationRequested ? "投屏已停止。" :
                "无线 ADB 连接超时。");
        }
        catch (Exception exception)
        {
            SetDeviceScreenStatus(deviceId, "投屏失败：" + exception.Message);
        }
        finally
        {
            _screenSessions.TryRemove(deviceId, out _);
            _screenFrameRates.TryRemove(deviceId, out _);
            cancellation.Dispose();
            if (_deviceWorkspace.IsSelected(deviceId))
                RefreshSelectedScreenStatus();
        }
    }
}
