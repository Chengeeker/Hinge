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
    private readonly object _sync = new();

    public event EventHandler<IReadOnlyList<Device>>? DevicesChanged;

    public IReadOnlyList<Device> GetAllDevices()
    {
        lock (_sync)
        {
            return _devices.Values.Select(r => r.Device).ToList();
        }
    }

    public bool TryGetDevice(string deviceId, out Device? device)
    {
        lock (_sync)
        {
            if (_devices.TryGetValue(deviceId, out var record))
            {
                device = record.Device;
                return true;
            }
        }
        device = null;
        return false;
    }

    public void UpsertDevice(DiscoveryMessage message, string remoteAddress)
    {
        var now = DateTime.UtcNow;
        bool isNewOrChanged = false;
        DeviceRecord? replacedRecord = null;

        lock (_sync)
        {
            // Reinstalling or migrating the Android package can regenerate its
            // persisted device ID. If the old record is already offline but has
            // the same identity and LAN address, collapse it into the new ID
            // instead of showing two rows for one phone.
            if (!_devices.ContainsKey(message.DeviceId))
            {
                foreach (var pair in _devices.ToArray())
                {
                    if (!CanCollapseStaleIdentity(pair.Value.Device, message, remoteAddress))
                    {
                        continue;
                    }

                    if (_devices.TryRemove(pair.Key, out replacedRecord))
                    {
                        isNewOrChanged = true;
                        break;
                    }
                }
            }

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
                        SessionPort = message.Port > 0 ? message.Port : Constants.SessionTcpPort,
                        DiscoveryPort = ValidDiscoveryPort(message.DiscoveryPort),
                        Capabilities = message.Capabilities,
                        NetworkAddresses = MergeAddresses(
                            replacedRecord?.Device.NetworkAddresses,
                            remoteAddress),
                        ConnectionState = ConnectionState.Discovered,
                        TrustState = replacedRecord?.Device.TrustState ?? TrustState.Untrusted
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
                        dev.Model != message.Model ||
                        dev.SessionPort != (message.Port > 0 ? message.Port : Constants.SessionTcpPort) ||
                        dev.DiscoveryPort != ValidDiscoveryPort(message.DiscoveryPort))
                    {
                        dev.Name = displayName;
                        dev.Manufacturer = message.Manufacturer;
                        dev.Model = message.Model;
                        dev.SessionPort = message.Port > 0 ? message.Port : Constants.SessionTcpPort;
                        dev.DiscoveryPort = ValidDiscoveryPort(message.DiscoveryPort);
                        isNewOrChanged = true;
                    }
                    return existing;
                }
            );

            // A record can become offline after the replacement check above has
            // already run. Reconcile on every packet so a stale row is removed
            // on the next announcement instead of living beside the live row.
            isNewOrChanged |= ReconcileDuplicateRecords(message.DeviceId);
        }

        if (isNewOrChanged)
        {
            NotifyChanged();
        }
    }

    public void PruneOffline(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        bool changed = false;

        lock (_sync)
        {
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
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    public void MarkSessionDisconnected(string deviceId)
    {
        bool changed = false;
        lock (_sync)
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
            changed = true;
        }

        if (changed)
        {
            NotifyChanged();
        }
    }

    private bool ReconcileDuplicateRecords(string preferredDeviceId)
    {
        if (!_devices.TryGetValue(preferredDeviceId, out var preferredRecord))
        {
            return false;
        }

        var group = _devices
            .ToArray()
            .Where(pair => pair.Key == preferredDeviceId ||
                CanMergeDuplicateDevices(preferredRecord.Device, pair.Value.Device))
            .ToList();
        if (group.Count <= 1)
        {
            return false;
        }

        var survivor = group
            .OrderByDescending(pair => pair.Value.Device.ConnectionState == ConnectionState.Connected)
            .ThenByDescending(pair => string.Equals(pair.Key, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(pair => pair.Value.LastSeen)
            .First();
        bool changed = false;

        foreach (var candidate in group)
        {
            if (string.Equals(candidate.Key, survivor.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MergeRecords(survivor.Value, candidate.Value);
            if (_devices.TryRemove(candidate.Key, out _))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool CanMergeDuplicateDevices(Device left, Device right)
    {
        if (string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            left.Platform != right.Platform ||
            !string.Equals(left.Name?.Trim(), right.Name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !SameOptionalIdentity(left.Manufacturer, right.Manufacturer) ||
            !SameOptionalIdentity(left.Model, right.Model))
        {
            return false;
        }

        // The same LAN address is the strong signal here: two different phones
        // may have the same model/name, but cannot own the same active address.
        return left.NetworkAddresses.Any(address =>
            right.NetworkAddresses.Any(other =>
                string.Equals(address, other, StringComparison.OrdinalIgnoreCase)));
    }

    private static void MergeRecords(DeviceRecord target, DeviceRecord source)
    {
        bool sourceIsNewer = source.LastSeen >= target.LastSeen;
        target.LastSeen = target.LastSeen >= source.LastSeen
            ? target.LastSeen
            : source.LastSeen;

        foreach (var address in source.Device.NetworkAddresses)
        {
            if (!target.Device.NetworkAddresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                target.Device.NetworkAddresses.Add(address);
            }
        }

        if (source.Device.TrustState == TrustState.Trusted ||
            source.Device.TrustState == TrustState.Blocked)
        {
            target.Device.TrustState = source.Device.TrustState;
        }

        if (sourceIsNewer)
        {
            target.Device.Name = source.Device.Name;
            target.Device.Manufacturer = source.Device.Manufacturer;
            target.Device.Model = source.Device.Model;
            target.Device.AppVersion = source.Device.AppVersion;
            target.Device.ProtocolVersion = source.Device.ProtocolVersion;
            target.Device.SessionPort = source.Device.SessionPort;
            target.Device.DiscoveryPort = source.Device.DiscoveryPort;
            target.Device.Capabilities = source.Device.Capabilities;
        }
    }

    private void NotifyChanged()
    {
        DevicesChanged?.Invoke(this, GetAllDevices());
    }

    private static bool CanCollapseStaleIdentity(
        Device existing,
        DiscoveryMessage incoming,
        string remoteAddress)
    {
        if (existing.ConnectionState != ConnectionState.Disconnected ||
            !string.Equals(existing.Name, DisplayName(
                incoming.Name,
                incoming.Manufacturer,
                incoming.Model),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var incomingPlatform = Enum.TryParse<DevicePlatform>(
            incoming.Platform,
            true,
            out var parsedPlatform)
            ? parsedPlatform
            : DevicePlatform.Unknown;
        if (existing.Platform != incomingPlatform ||
            !SameOptionalIdentity(existing.Manufacturer, incoming.Manufacturer) ||
            !SameOptionalIdentity(existing.Model, incoming.Model))
        {
            return false;
        }

        return existing.NetworkAddresses.Any(address =>
            string.Equals(address, remoteAddress, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameOptionalIdentity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return true;
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int ValidDiscoveryPort(int port) =>
        port > 0 && port <= 65535 ? port : Constants.DiscoveryUdpPort;

    private static List<string> MergeAddresses(
        IEnumerable<string>? previous,
        string current)
    {
        var addresses = previous?
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        if (!addresses.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            addresses.Add(current);
        }
        return addresses;
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
