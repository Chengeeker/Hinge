using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Hinge.Core;
using Hinge.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace Hinge.App;

public sealed partial class MainWindow
{
    private DesktopMediaServer? _desktopMediaServer;
    private X509Certificate2? _desktopCertificate;
    private readonly SemaphoreSlim _desktopLifecycle = new(1, 1);
    private bool _desktopToggleChanging;
    private bool _desktopSharingClosed;
    private bool _desktopSharingEnabled = LoadDesktopSharingEnabled();
    private string? _desktopSharingError;
    private string? _desktopEncoderStatus;
    private DesktopVideoSettings _desktopVideoSettings = LoadDesktopVideoSettings();

    private const string DesktopSharingEnabledSettingName = "DesktopSharingEnabled";
    private const string DesktopVideoSettingsSettingName = "DesktopVideoSettingsV1";

    private static bool LoadDesktopSharingEnabled()
    {
        if (ReadUserSetting(DesktopSharingEnabledSettingName) is int registryValue)
            return registryValue != 0;
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[DesktopSharingEnabledSettingName] is bool value)
            {
                WriteUserSetting(DesktopSharingEnabledSettingName, value ? 1 : 0);
                return value;
            }
        }
        catch { }
        return false;
    }

    private static void SaveDesktopSharingEnabled(bool value)
    {
        WriteUserSetting(DesktopSharingEnabledSettingName, value ? 1 : 0);
        try { ApplicationData.Current.LocalSettings.Values[DesktopSharingEnabledSettingName] = value; }
        catch { }
    }

    private static DesktopVideoSettings LoadDesktopVideoSettings()
    {
        try
        {
            var json = ReadUserSetting(DesktopVideoSettingsSettingName) as string;
            if (json == null && ApplicationData.Current.LocalSettings.Values[DesktopVideoSettingsSettingName] is string localJson)
            {
                json = localJson;
                WriteUserSetting(DesktopVideoSettingsSettingName, json);
            }
            if (json is not null &&
                JsonSerializer.Deserialize<DesktopVideoSettings>(json) is { } settings)
            {
                settings.Validate();
                return settings;
            }
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine("Desktop video settings: " + error.Message); }
        return new DesktopVideoSettings();
    }

    private bool IsDesktopPeerAllowed(string id) => _trustStore.IsTrusted(id) &&
        _sessionManager.ActiveConnections.Any(connection => connection.State == SessionState.Connected &&
            connection.RemoteDeviceId == id && string.Equals(connection.PeerInfo?.Platform, "android", StringComparison.OrdinalIgnoreCase));

    private void ConfigureDesktopSharing(RemoteInputSettingsPage page)
    {
        page.DesktopFps.Value = _desktopVideoSettings.FramesPerSecond;
        page.DesktopEdge.Value = _desktopVideoSettings.LongestEdge;
        page.DesktopBitrate.Value = _desktopVideoSettings.Bitrate / 1_000_000.0;
        void UpdateQualityLabels()
        {
            page.DesktopFpsCaption.Text = $"帧率上限：{page.DesktopFps.Value:0} FPS";
            page.DesktopEdgeCaption.Text = $"画面最长边：{page.DesktopEdge.Value:0} 像素";
            page.DesktopBitrateCaption.Text = $"目标码率：{page.DesktopBitrate.Value:0} Mbps";
        }
        UpdateQualityLabels();
        page.DesktopFps.ValueChanged += (_, _) => UpdateQualityLabels();
        page.DesktopEdge.ValueChanged += (_, _) => UpdateQualityLabels();
        page.DesktopBitrate.ValueChanged += (_, _) => UpdateQualityLabels();
        page.RefreshDesktopMonitors.Click += async (_, _) => await RefreshDesktopMonitorsAsync(page);
        _ = RefreshDesktopMonitorsAsync(page);
        page.ApplyDesktopVideo.Click += async (_, _) =>
        {
            page.ApplyDesktopVideo.IsEnabled = page.ShareDesktop.IsEnabled = false;
            page.StopDesktop.IsEnabled = page.ResetDesktopPeers.IsEnabled = false;
            page.RefreshDesktopMonitors.IsEnabled = false;
            try
            {
                if (page.DesktopMonitor.SelectedItem is not ComboBoxItem { Tag: DesktopMonitor monitor })
                    throw new InvalidOperationException("请选择当前已连接的显示器。");
                var settings = _desktopVideoSettings with {
                    Monitor = monitor.Index, MonitorDeviceName = monitor.DeviceName,
                    FramesPerSecond = (int)Math.Round(page.DesktopFps.Value),
                    LongestEdge = (int)Math.Round(page.DesktopEdge.Value),
                    Bitrate = (int)Math.Round(page.DesktopBitrate.Value) * 1_000_000 };
                settings.Validate();
                var json = JsonSerializer.Serialize(settings);
                WriteUserSetting(DesktopVideoSettingsSettingName, json);
                try { ApplicationData.Current.LocalSettings.Values[DesktopVideoSettingsSettingName] = json; }
                catch { }
                if (settings == _desktopVideoSettings) return;
                _desktopVideoSettings = settings;
                bool restart = _desktopMediaServer != null;
                if (restart) { await StopDesktopSharingAsync(); await StartDesktopSharingAsync(); }
                UpdateDesktopSharingUi();
            }
            catch (Exception error) { page.DesktopStatus.Text = "画质设置未能应用：" + error.Message; }
            finally
            {
                page.ApplyDesktopVideo.IsEnabled = page.ShareDesktop.IsEnabled = page.ResetDesktopPeers.IsEnabled = true;
                page.RefreshDesktopMonitors.IsEnabled = true;
                page.StopDesktop.IsEnabled = _desktopMediaServer != null;
            }
        };
        page.ShareDesktop.IsOn = _desktopSharingEnabled;
        page.StopDesktop.Click += async (_, _) =>
        {
            SetDesktopSharingEnabled(false);
            await StopDesktopSharingAsync();
        };
        page.ResetDesktopPeers.Click += async (_, _) =>
        {
            var dialog = new ContentDialog { Title = "清除手机桌面身份？",
                Content = "当前桌面共享会停止，下次手机连接需重新在两端核对身份码。",
                PrimaryButtonText = "清除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
                XamlRoot = ((FrameworkElement)Content).XamlRoot };
            if (await ShowContentDialogAsync(dialog) != ContentDialogResult.Primary) return;
            try
            {
                SetDesktopSharingEnabled(false);
                await StopDesktopSharingAsync();
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hinge", "desktop-peer-pins.json"));
                page.DesktopStatus.Text = "已清除手机身份，重新开启共享后核对身份码。";
            }
            catch (Exception error) { page.DesktopStatus.Text = "无法清除桌面身份：" + error.Message; }
        };
        page.ShareDesktop.Toggled += async (_, _) =>
        {
            if (_desktopToggleChanging) return;
            page.ShareDesktop.IsEnabled = false;
            try
            {
                if (page.ShareDesktop.IsOn) await StartDesktopSharingAsync();
                else
                {
                    SetDesktopSharingEnabled(false);
                    await StopDesktopSharingAsync();
                }
            }
            catch (Exception exception)
            {
                SetDesktopSharingEnabled(false);
                _desktopToggleChanging = true; page.ShareDesktop.IsOn = false; _desktopToggleChanging = false;
                page.DesktopStatus.Text = "桌面共享无法启动：" + exception.Message;
            }
            finally { page.ShareDesktop.IsEnabled = true; }
        };
        UpdateDesktopSharingUi();
    }

    private async Task RefreshDesktopMonitorsAsync(RemoteInputSettingsPage page)
    {
        page.RefreshDesktopMonitors.IsEnabled = page.ApplyDesktopVideo.IsEnabled = false;
        string? selectedName = ((page.DesktopMonitor.SelectedItem as ComboBoxItem)?.Tag as DesktopMonitor)?.DeviceName ?? _desktopVideoSettings.MonitorDeviceName;
        try
        {
            var monitors = await new NativeDesktopVideoSource().ListMonitorsAsync();
            if (_desktopSharingClosed) return;
            page.DesktopMonitor.Items.Clear();
            foreach (var monitor in monitors) {
                var item = new ComboBoxItem { Tag = monitor, Content = new TextBlock {
                    Text = monitor.ToString(), Style = (Style)Application.Current.Resources["HingeM3BodyStyle"] } };
                page.DesktopMonitor.Items.Add(item);
                if (selectedName == null ? monitor.Index == _desktopVideoSettings.Monitor : monitor.DeviceName == selectedName)
                    page.DesktopMonitor.SelectedItem = item;
            }
            if (page.DesktopMonitor.SelectedItem == null) {
                page.DesktopMonitor.PlaceholderText = "请选择已连接的显示器";
                page.DesktopStatus.Text = "原显示器不可用，请重新选择后保存；不会自动共享其他屏幕。";
            }
        }
        catch (Exception error) { page.DesktopStatus.Text = "无法刷新显示器：" + error.Message; }
        finally { page.RefreshDesktopMonitors.IsEnabled = page.ApplyDesktopVideo.IsEnabled = true; }
    }

    private async Task StartDesktopSharingAsync()
    {
        await _desktopLifecycle.WaitAsync();
        try
        {
            if (_desktopSharingClosed || _desktopMediaServer != null) return;
            _desktopSharingError = null;
            _desktopEncoderStatus = null;
            _desktopCertificate = await DesktopCertificateStore.LoadOrCreateAsync();
            var source = new NativeDesktopVideoSource();
            var monitors = await source.ListMonitorsAsync();
            if (_desktopSharingClosed) return;
            var monitor = _desktopVideoSettings.MonitorDeviceName is { } name ?
                monitors.FirstOrDefault(item => item.DeviceName == name) :
                monitors.FirstOrDefault(item => item.Index == _desktopVideoSettings.Monitor);
            if (monitor == null) throw new IOException("所选显示器不可用，请刷新并重新选择。");
            var videoSettings = _desktopVideoSettings with { MonitorDeviceName = monitor.DeviceName };
            var server = new DesktopMediaServer(_desktopCertificate, IsDesktopPeerAllowed, ApproveDesktopPeerAsync,
                token => source.CaptureAsync(videoSettings, token),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hinge", "desktop-peer-pins.json"),
                id => _remoteInputManager.BeginAuthenticatedControl(id, Win32InputInjector.ForDesktop(videoSettings.MonitorDeviceName)));
            source.EncoderSelected += (_, message) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_desktopMediaServer != server) return;
                _desktopEncoderStatus = message;
                UpdateDesktopSharingUi();
            });
            server.StatusChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_desktopMediaServer != server) return;
                if (server.ActiveDeviceId == null) _desktopEncoderStatus = null;
                if (server.ActiveDeviceId != null) _desktopSharingError = null;
                UpdateDesktopSharingUi();
            });
            server.Error += (_, message) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_desktopMediaServer != server) return;
                _desktopSharingError = message;
                App.LogLifecycle("desktop-connection-error " + message.Replace('\r', ' ').Replace('\n', ' '));
                UpdateDesktopSharingUi();
            });
            try { server.Start(); _desktopMediaServer = server; }
            catch { await server.DisposeAsync(); throw; }
            SetDesktopSharingEnabled(true);
            UpdateDesktopSharingUi();
        }
        catch { _desktopCertificate?.Dispose(); _desktopCertificate = null; throw; }
        finally { _desktopLifecycle.Release(); }
    }

    private async Task StopDesktopSharingAsync()
    {
        await _desktopLifecycle.WaitAsync();
        try
        {
            var server = _desktopMediaServer; _desktopMediaServer = null;
            _desktopSharingError = null;
            try { if (server != null) await server.DisposeAsync(); }
            finally { _desktopCertificate?.Dispose(); _desktopCertificate = null; }
            DispatcherQueue.TryEnqueue(UpdateDesktopSharingUi);
        }
        finally { _desktopLifecycle.Release(); }
    }

    private void UpdateDesktopSharingUi()
    {
        if (_remoteInputSettingsPage is not { } page) return;
        _desktopToggleChanging = true;
        page.ShareDesktop.IsOn = _desktopSharingEnabled;
        _desktopToggleChanging = false;
        page.StopDesktop.IsEnabled = _desktopMediaServer != null;
        page.DesktopEncoderStatus.Text = _desktopMediaServer == null ? "未启动桌面编码" :
            _desktopEncoderStatus == null ? "手机连接后显示实际编码器；优先硬件编码，不可用时自动回退。" :
            "实际编码器：" + _desktopEncoderStatus.Replace("encoder=", "").Replace(" hardware=1", " · 硬件编码").Replace(" hardware=0", " · 软件编码").Replace(" dimensions=", " · ").Replace(" fps=", " · 目标 FPS ");
        page.DesktopStatus.Text = _desktopMediaServer == null ? !_desktopSharingEnabled ? "桌面共享已关闭" :
            _desktopSharingError != null ? "已保留开启状态，但本次启动失败；关闭后重新开启可重试：" + _desktopSharingError : "正在恢复桌面共享…" :
            _desktopMediaServer.ActiveDeviceId is { } id ? $"正在向 {_trustStore.GetDevice(id)?.Name ?? "手机"} 共享桌面" :
            _desktopSharingError != null ? "等待重连，上次连接原因：" + _desktopSharingError :
            $"桌面共享已开启，等待手机连接（显示器 {_desktopVideoSettings.Monitor + 1} · H.264 · {_desktopVideoSettings.FramesPerSecond} FPS · {_desktopVideoSettings.LongestEdge} 像素 · {_desktopVideoSettings.Bitrate / 1_000_000} Mbps）";
    }

    private void SetDesktopSharingEnabled(bool enabled)
    {
        _desktopSharingEnabled = enabled;
        SaveDesktopSharingEnabled(enabled);
        UpdateDesktopSharingUi();
    }

    private async Task RestoreDesktopSharingPreferenceAsync()
    {
        try { await StartDesktopSharingAsync(); }
        catch (Exception error)
        {
            _desktopSharingError = error.Message;
            App.LogLifecycle("desktop-sharing-restore-error " + error.Message.Replace('\r', ' ').Replace('\n', ' '));
            UpdateDesktopSharingUi();
        }
    }

    private Task<bool> ApproveDesktopPeerAsync(DesktopPairingRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            bool gate = false;
            try
            {
                await _contentDialogGate.WaitAsync(cancellationToken); gate = true;
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new ContentDialog
                {
                    Title = "确认手机桌面身份",
                    Content = $"{request.Name} 请求查看电脑桌面。\n请确认手机上显示完全相同的身份码：\n\n{request.VerificationCode}\n\n仅在两端一致时允许，后续连接会复用本次身份。",
                    PrimaryButtonText = "身份码一致，允许", CloseButtonText = "拒绝",
                    DefaultButton = ContentDialogButton.Close, XamlRoot = ((FrameworkElement)Content).XamlRoot,
                };
                using var registration = cancellationToken.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
                cancellationToken.ThrowIfCancellationRequested();
                var result = await dialog.ShowAsync();
                completion.TrySetResult(result == ContentDialogResult.Primary && !cancellationToken.IsCancellationRequested);
            }
            catch { completion.TrySetResult(false); }
            finally { if (gate) _contentDialogGate.Release(); }
        })) completion.TrySetResult(false);
        return completion.Task.WaitAsync(cancellationToken);
    }

    private async Task HandleDesktopDescriptionAsync(SessionConnection connection, ProtocolFrame frame)
    {
        if (frame.Payload.Length > 8192 || connection.RemoteDeviceId is not { } id || !IsDesktopPeerAllowed(id)) return;
        using var request = JsonDocument.Parse(frame.Payload);
        if (request.RootElement.GetProperty("action").GetString() != "describe") return;
        string requestId = request.RootElement.GetProperty("requestId").GetString() ?? "";
        if (requestId.Length is < 1 or > 64) return;
        var server = _desktopMediaServer;
        var reply = new { action = "description", version = 1, requestId, enabled = server != null,
            port = server?.Port ?? 0, name = _localIdentity.Name };
        await connection.SendFrameAsync(MessageType.DesktopControl, JsonSerializer.SerializeToUtf8Bytes(reply));
    }
}
