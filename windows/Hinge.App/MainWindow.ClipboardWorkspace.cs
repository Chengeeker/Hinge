using Hinge.Core;

namespace Hinge.App;

/// <summary>
/// The desktop has ONE system clipboard. Bind its ADB bridge to exactly one
/// explicitly selected device at a time, with per-device opt-in. Other online
/// phones cannot overwrite or receive the clipboard merely by connecting.
/// </summary>
public sealed partial class MainWindow
{
    private const string ScopedClipboardSettingPrefix = "WirelessAdbClipboardSync.";
    private bool _updatingDeviceClipboardSettings;
    private string? _clipboardBridgeDeviceId;

    private static string ClipboardSettingKey(string deviceId) =>
        ScopedClipboardSettingPrefix + deviceId.ToLowerInvariant();

    private bool IsDeviceClipboardEnabled(string deviceId)
    {
        var setting = ReadUserSetting(ClipboardSettingKey(deviceId));
        if (setting is int value) return value != 0;
        // One-time compatibility with the former single-device toggle.
        return string.Equals(deviceId, WirelessAdbClient.LoadHingeDeviceId(),
            StringComparison.OrdinalIgnoreCase) && LoadWirelessAdbClipboardSync();
    }

    private void SetDeviceClipboardEnabled(string deviceId, bool enabled)
    {
        WriteUserSetting(ClipboardSettingKey(deviceId), enabled ? 1 : 0);
    }

    private void RefreshScopedWirelessAdbSettings()
    {
        if (_transferSettingsPage is not { } page) return;
        _updatingDeviceClipboardSettings = true;
        try
        {
            var id = _deviceWorkspace.SelectedDeviceId;
            var endpoint = id == null ? (Host: "", Port: "") :
                GetDeviceAdbEndpoint(id);
            page.WirelessAdbHost.Text = endpoint.Host;
            page.WirelessAdbConnectPort.Text = endpoint.Port;
            page.WirelessAdbClipboardSync.IsOn =
                id != null && IsDeviceClipboardEnabled(id);
            page.WirelessAdbClipboardSync.IsEnabled = id != null;
        }
        finally { _updatingDeviceClipboardSettings = false; }
    }

    private async Task RebindClipboardToSelectedPhoneAsync()
    {
        var id = _deviceWorkspace.SelectedDeviceId;
        var enabled = id != null && IsDeviceClipboardEnabled(id);
        var endpoint = id != null ? GetDeviceAdbEndpoint(id) : (Host: "", Port: "");
        // The configure gate cancels the previous bridge before starting a
        // new one; no two Android sources may race for the Windows clipboard.
        await ConfigureWirelessAdbClipboardSyncAsync(enabled, endpoint.Host,
            endpoint.Port, id);
    }
}
