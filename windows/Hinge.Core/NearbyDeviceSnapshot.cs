namespace Hinge.Core;

/// <summary>
/// Keeps every discovered device visible and adds authenticated TCP peers even
/// when the latest UDP discovery snapshot is temporarily incomplete.
/// Device IDs, never names or addresses, determine row identity.
/// </summary>
public static class NearbyDeviceSnapshot
{
    public static IReadOnlyList<Device> Build(
        IEnumerable<Device> discovered,
        IEnumerable<SessionPeerInfo> activePeers)
    {
        var visible = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in discovered)
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceId))
                visible.TryAdd(device.DeviceId, device);
        }

        foreach (var peer in activePeers)
        {
            if (string.IsNullOrWhiteSpace(peer.DeviceId) ||
                visible.ContainsKey(peer.DeviceId))
                continue;

            visible.Add(peer.DeviceId, new Device
            {
                DeviceId = peer.DeviceId,
                Name = string.IsNullOrWhiteSpace(peer.Name) ? "Android 设备" : peer.Name,
                Manufacturer = peer.Manufacturer,
                Model = peer.Model,
                Platform = Enum.TryParse<DevicePlatform>(peer.Platform, true, out var platform)
                    ? platform : DevicePlatform.Android,
                ConnectionState = ConnectionState.Connected,
                TrustState = TrustState.Trusted,
                NetworkAddresses = new List<string>()
            });
        }

        return visible.Values
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
