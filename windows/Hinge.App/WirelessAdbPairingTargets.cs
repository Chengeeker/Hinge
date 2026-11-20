using Microsoft.Win32;
using System.Net;

namespace Hinge.App;

internal sealed record WirelessAdbPairingConnectionResult(
    bool Connected, string? Transport, string Message);

internal static partial class WirelessAdbClient
{
    private const string PairedGuidsSettingV2 = "WirelessAdbPairingGuids.v2";

    // ADB's own key store authorizes many devices. Keep each pairing GUID,
    // rather than replacing an existing phone's Hinge identity on every pair.
    internal static void RememberPairedGuid(RegistryKey? key, string? guid)
    {
        if (key == null || string.IsNullOrWhiteSpace(guid)) return;
        var saved = key.GetValue(PairedGuidsSettingV2) as string[] ?? [];
        var legacy = key.GetValue(PairingGuidSetting) as string;
        var values = saved.Concat(legacy == null ? [] : [legacy]).Append(guid)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32).ToArray();
        key.SetValue(PairedGuidsSettingV2, values, RegistryValueKind.MultiString);

        // Migrate only an empty legacy setting, so the background manager and
        // any selected phone keep their previous identity and endpoint.
        if (string.IsNullOrWhiteSpace(legacy))
            key.SetValue(PairingGuidSetting, guid);
        key.SetValue(PairedSetting, 1);
    }

    internal static async Task<WirelessAdbPairingConnectionResult> ConnectNewlyPairedDeviceAsync(
        WirelessAdbPairingResult pairing, CancellationToken cancellationToken)
    {
        // A background reconnect must not race with the new-device handshake.
        await ConnectionGate.WaitAsync(cancellationToken);
        try { return await ConnectNewlyPairedCoreAsync(pairing, cancellationToken); }
        finally { ConnectionGate.Release(); }
    }

    private static async Task<WirelessAdbPairingConnectionResult> ConnectNewlyPairedCoreAsync(
        WirelessAdbPairingResult pairing, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        try
        {
            var servicesResult = await RunAsync(cancellationToken, "mdns", "services");
            if (servicesResult.ExitCode == 0)
            {
                var services = ParseMdnsEndpointEntries(servicesResult.Output)
                    .Where(service => service.ServiceType == "_adb-tls-connect._tcp").ToArray();
                // Prefer the exact pairing GUID. Never connect to an unrelated
                // phone merely because another device was the last selection.
                if (!string.IsNullOrWhiteSpace(pairing.PairingGuid))
                    candidates.AddRange(services.Where(service =>
                        MatchesServiceIdentity(service.Instance, pairing.PairingGuid))
                        .Select(service => service.Endpoint));
                if (candidates.Count == 0)
                {
                    var sameHost = services.Where(service =>
                        TryParseEndpoint(service.Endpoint, out var address, out _) &&
                        string.Equals(address, pairing.Host, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (sameHost.Length == 1) candidates.Add(sameHost[0].Endpoint);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* Report missing mDNS below instead of invalidating successful pairing. */ }

        if (candidates.Count == 0)
            return new WirelessAdbPairingConnectionResult(false, null,
                "配对密钥已保存，但未发现该设备的无线调试连接服务。请保持设备无线调试开启，稍后刷新列表。");

        var errors = new List<string>();
        foreach (var endpoint in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = await RunAsync(cancellationToken, "connect", endpoint);
                if (result.ExitCode != 0 || IsAdbConnectionFailure(result.Output, result.Error))
                {
                    errors.Add(string.Join(" ", result.Output, result.Error).Trim());
                    continue;
                }
                var devices = await RunAsync(cancellationToken, "devices", "-l");
                EnsureSuccess(devices);
                var match = FindDeviceForCandidate(devices.Output, endpoint, null, pairing.PairingGuid);
                if (match is { State: "device" })
                {
                    var identity = await ReadIdentityAsync(match.Value.Transport, cancellationToken);
                    if (identity.Length > 0)
                    {
                        // Never overwrite the previously selected phone's global record.
                        return new WirelessAdbPairingConnectionResult(true, match.Value.Transport,
                            "设备已成功通过安全无线 ADB 连接");
                    }
                }
                errors.Add("配对后尚未返回已授权的 device 状态");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { errors.Add(exception.Message); }
        }
        var reason = errors.LastOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "尚未建立安全无线调试连接";
        return new WirelessAdbPairingConnectionResult(false, null,
            "配对密钥已保存，但连接阶段失败：" + reason);
    }
}
