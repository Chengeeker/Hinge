using System.Text.RegularExpressions;

namespace Hinge.App;

internal sealed record WirelessAdbDeviceInfo(
    string Key, string Name, string Model, string Status, string Details, bool Connected);

internal sealed record WirelessAdbDeviceObservation(
    string Transport, string Status, string Brand, string Model, string Identity);

internal static partial class WirelessAdbClient
{
    internal static async Task<IReadOnlyList<WirelessAdbDeviceInfo>> ReadDeviceInventoryAsync(
        CancellationToken cancellationToken)
    {
        var devices = await RunAsync(cancellationToken, "devices", "-l");
        EnsureSuccess(devices);
        var observations = new List<WirelessAdbDeviceObservation>();
        foreach (var entry in ParseDeviceStates(devices.Output).Take(20))
        {
            var brand = string.Empty;
            var model = ParseDeviceModel(entry.Transport, devices.Output);
            var identity = string.Empty;
            if (entry.State == "device")
            {
                // A model can appear twice under TCP and TLS transport names.
                // Only a device-reported serial is reliable enough to merge them.
                var details = await Task.WhenAll(
                    SafePropertyAsync(entry.Transport, "ro.product.brand", cancellationToken),
                    SafePropertyAsync(entry.Transport, "ro.product.model", cancellationToken),
                    SafePropertyAsync(entry.Transport, "ro.serialno", cancellationToken));
                brand = details[0];
                if (!string.IsNullOrWhiteSpace(details[1])) model = details[1];
                identity = details[2];
            }
            observations.Add(new WirelessAdbDeviceObservation(
                entry.Transport, entry.State, brand, model, identity));
        }

        var discovered = Array.Empty<MdnsEndpoint>();
        try
        {
            var mdns = await RunAsync(cancellationToken, "mdns", "services");
            if (mdns.ExitCode == 0)
                discovered = ParseMdnsEndpointEntries(mdns.Output)
                    .Where(s => s.ServiceType == "_adb-tls-connect._tcp").ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* ADB's device list still works without mDNS. */ }

        return BuildDeviceInventory(observations, discovered);
    }

    private static async Task<string> SafePropertyAsync(
        string transport, string property, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(cancellationToken, "-s", transport, "shell", "getprop", property);
            return result.ExitCode == 0 ? result.Output.Trim() : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return string.Empty; }
    }

    private static string ParseDeviceModel(string transport, string deviceList)
    {
        var line = deviceList.Split('\n').FirstOrDefault(l =>
            l.TrimStart().StartsWith(transport + " ", StringComparison.Ordinal) ||
            l.TrimStart().StartsWith(transport + "\t", StringComparison.Ordinal)) ?? string.Empty;
        return Regex.Match(line, @"\bmodel:(?<model>[^\s]+)").Groups["model"].Value;
    }

    internal static IReadOnlyList<WirelessAdbDeviceInfo> BuildDeviceInventory(
        IEnumerable<WirelessAdbDeviceObservation> observations,
        IEnumerable<MdnsEndpoint> discovered)
    {
        var items = observations.ToArray();
        var results = new List<WirelessAdbDeviceInfo>();
        foreach (var group in items.GroupBy(item =>
            item.Status == "device" && IsStableIdentityValue(item.Identity)
                ? "serial:" + item.Identity.ToUpperInvariant()
                : "transport:" + item.Transport.ToUpperInvariant()))
        {
            var first = group.OrderByDescending(x => x.Status == "device").First();
            var name = string.Join(" ", new[] { first.Brand, first.Model }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (name.Length == 0) name = "Android 设备";
            var online = group.Any(x => x.Status == "device");
            var address = string.Join(" · ", group.Select(x => x.Transport).Distinct());
            results.Add(new WirelessAdbDeviceInfo(group.Key, name,
                first.Model, online ? "已连接" :
                first.Status == "unauthorized" ? "未授权" : "离线",
                address, online));
        }
        foreach (var service in discovered)
        {
            if (items.Any(o =>
                string.Equals(o.Transport, service.Endpoint, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Transport.TrimEnd('.'), service.Instance.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Transport.TrimEnd('.'),
                    service.Instance.TrimEnd('.') + "._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (results.Any(row => row.Details.Contains(service.Endpoint, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new WirelessAdbDeviceInfo("discovered:" + service.Instance,
                "发现的 Android 设备", "", "已发现 · 未连接",
                service.Endpoint + " · 尚未完成授权（非已连接设备）", false));
        }
        return results.OrderByDescending(item => item.Connected).ThenBy(item => item.Name).ToArray();
    }
}
