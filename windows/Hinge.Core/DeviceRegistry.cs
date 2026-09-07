using System.Collections.Concurrent;

namespace Hinge.Core;

public class DeviceRecord
{
    public Device Device { get; }
    public DateTime LastSeen { get; set; }

    public DeviceRecord(Device device, DateTime lastSeen)
    {
        Device = device;
        LastSeen = lastSeen;
    }
}

public class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceRecord> _devices = new();

    public event EventHandler<IReadOnlyList<Device>>? DevicesChanged;

    public IReadOnlyList<Device> GetAllDevices()
    {
        return _devices.Values.Select(r => r.Device).ToList();
    }

    public bool TryGetDevice(string deviceId, out Device? device)
    {
        if (_devices.TryGetValue(deviceId, out var record))
        {
            device = record.Device;
            return true;
        }
        device = null;
        return false;
    }

    public void UpsertDevice(DiscoveryMessage message, string remoteAddress)
    {
        var now = DateTime.UtcNow;
        bool isNewOrChanged = false;

        _devices.AddOrUpdate(
            message.DeviceId,
            _ =>
            {
                isNewOrChanged = true;
                return new DeviceRecord(new Device
                {
                    DeviceId = message.DeviceId,
                    Name = DisplayName(message.Name, message.Manufacturer, message.Model),
                    Manufacturer = message.Manufacturer,
                    Model = message.Model,
                    Platform = Enum.TryParse<DevicePlatform>(message.Platform, true, out var p) ? p : DevicePlatform.Unknown,
                    AppVersion = message.Version,
                    ProtocolVersion = message.ProtocolVersion,
                    Capabilities = message.Capabilities,
                    NetworkAddresses = new List<string> { remoteAddress },
                    ConnectionState = ConnectionState.Discovered,
                    TrustState = TrustState.Untrusted
                }, now);
            },
            (_, existing) =>
            {
                existing.LastSeen = now;
                var dev = existing.Device;
                int addressIndex = dev.NetworkAddresses.FindIndex(address =>
                    string.Equals(address, remoteAddress, StringComparison.OrdinalIgnoreCase));
                if (addressIndex < 0)
                {
                    dev.NetworkAddresses.Add(remoteAddress);
                    isNewOrChanged = true;
                }
                if (dev.ConnectionState == ConnectionState.Disconnected)
                {
                    dev.ConnectionState = ConnectionState.Discovered;
                    isNewOrChanged = true;
                }
                var displayName = DisplayName(message.Name, message.Manufacturer, message.Model);
                if (dev.Name != displayName ||
                    dev.Manufacturer != message.Manufacturer ||
                    dev.Model != message.Model)
                {
                    dev.Name = displayName;
                    dev.Manufacturer = message.Manufacturer;
                    dev.Model = message.Model;
                    isNewOrChanged = true;
                }
                return existing;
            }
        );

        if (isNewOrChanged)
        {
            NotifyChanged();
        }
    }

    public void PruneOffline(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        bool changed = false;

        foreach (var pair in _devices)
        {
            if (pair.Value.Device.ConnectionState == ConnectionState.Connected)
            {
                // A live TCP session is authoritative. UDP discovery packets can be
                // lost briefly without making an established session offline.
                continue;
            }
            if (pair.Value.LastSeen < cutoff && pair.Value.Device.ConnectionState != ConnectionState.Disconnected)
            {
                pair.Value.Device.ConnectionState = ConnectionState.Disconnected;
                changed = true;
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public void MarkSessionDisconnected(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) ||
            !_devices.TryGetValue(deviceId, out var record) ||
            record.Device.ConnectionState != ConnectionState.Connected)
        {
            return;
        }

        bool stillPresent = DateTime.UtcNow - record.LastSeen <= TimeSpan.FromSeconds(30);
        record.Device.ConnectionState = stillPresent
            ? ConnectionState.Discovered
            : ConnectionState.Disconnected;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        DevicesChanged?.Invoke(this, GetAllDevices());
    }

    private static string DisplayName(string name, string manufacturer, string model)
    {
        var candidate = name?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(candidate) &&
            !IsPlaceholderName(candidate) &&
            !string.Equals(candidate, model?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        var fallback = string.Join(" ", new[] { manufacturer?.Trim(), model?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(fallback) ? "Android 设备" : fallback;
    }

    private static bool IsPlaceholderName(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "android" or
            "android device" or
            "android 设备" or
            "phone" or
            "mobile" or
            "unknown" or
            "unknown device" or
            "未命名设备" or
            "null" or
            "none" => true,
            _ => false
        };
    }
}
