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
    private string? _identityConflictWarning;
    private DateTime _identityConflictAt;

    /// <summary>Warn when two different Android models announce the same
    /// restored/cloned DeviceId. They cannot be connected independently until
    /// the Android installs adopt their own hardware-bound network IDs.</summary>
    public string? IdentityConflictWarning
    {
        get
        {
            lock (_sync)
                return DateTime.UtcNow - _identityConflictAt < TimeSpan.FromMinutes(10)
                    ? _identityConflictWarning : null;
        }
    }

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

        lock (_sync)
        {
            // Addresses are routing hints, never device identity or trust evidence.
            foreach (var record in _devices.Values.Where(record => record.Device.DeviceId != message.DeviceId))
            {
                if (record.Device.NetworkAddresses.RemoveAll(address =>
                    string.Equals(address, remoteAddress, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    isNewOrChanged = true;
                    if (record.Device.NetworkAddresses.Count == 0 && !IsLiveSession(record.Device.ConnectionState))
                        record.Device.ConnectionState = ConnectionState.Disconnected;
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
                        PairingRequired = message.PairingRequired,
                        Capabilities = message.Capabilities,
                        NetworkAddresses = MergeAddresses(
                            null,
                            remoteAddress,
                            message.Addresses),
                        ConnectionState = ConnectionState.Discovered,
                        TrustState = TrustState.Untrusted
                    }, now);
                },
                (_, existing) =>
                {
                    existing.LastSeen = now;
                    var dev = existing.Device;
                    // A restored identity.json can give two *different*
                    // physical phones the same DeviceId. Do not silently
                    // overwrite that evidence: the TCP deduplicator must use
                    // an actual unique device identity, not the phone name/IP.
                    if ((BothPresentAndDifferent(dev.Manufacturer, message.Manufacturer) ||
                         BothPresentAndDifferent(dev.Model, message.Model)) &&
                        dev.Platform == DevicePlatform.Android &&
                        string.Equals(message.Platform, "android", StringComparison.OrdinalIgnoreCase))
                    {
                        _identityConflictWarning =
                            $"设备身份冲突：{dev.Name} 与 {DisplayName(message.Name, message.Manufacturer, message.Model)} 共用同一设备 ID，请在两台手机上安装 Android 210 或更新版本。";
                        _identityConflictAt = now;
                        isNewOrChanged = true;
                    }
                    int addressIndex = dev.NetworkAddresses.FindIndex(address =>
                        string.Equals(address, remoteAddress, StringComparison.OrdinalIgnoreCase));
                    if (addressIndex < 0 && !string.IsNullOrWhiteSpace(remoteAddress))
                    {
                        dev.NetworkAddresses.Add(remoteAddress);
                        isNewOrChanged = true;
                    }
                    if (message.Addresses != null)
                    {
                        foreach (var addr in message.Addresses)
                        {
                            if (!string.IsNullOrWhiteSpace(addr) &&
                                !dev.NetworkAddresses.Contains(addr, StringComparer.OrdinalIgnoreCase))
                            {
                                dev.NetworkAddresses.Add(addr);
                                isNewOrChanged = true;
                            }
                        }
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
                    if (dev.PairingRequired != message.PairingRequired)
                    {
                        dev.PairingRequired = message.PairingRequired;
                        isNewOrChanged = true;
                    }
                    return existing;
                }
            );


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
                if (IsLiveSession(pair.Value.Device.ConnectionState))
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
                !IsLiveSession(record.Device.ConnectionState))
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

    public void MarkSessionSuspended(string deviceId)
    {
        bool changed = false;
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(deviceId) ||
                !_devices.TryGetValue(deviceId, out var record) ||
                !IsLiveSession(record.Device.ConnectionState) ||
                record.Device.ConnectionState == ConnectionState.Suspended)
            {
                return;
            }

            record.Device.ConnectionState = ConnectionState.Suspended;
            changed = true;
        }

        if (changed) NotifyChanged();
    }

    public void MarkSessionConnected(string deviceId)
    {
        bool changed = false;
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(deviceId) ||
                !_devices.TryGetValue(deviceId, out var record) ||
                record.Device.ConnectionState == ConnectionState.Connected)
            {
                return;
            }

            record.Device.ConnectionState = ConnectionState.Connected;
            changed = true;
        }

        if (changed) NotifyChanged();
    }

    private void NotifyChanged()
    {
        DevicesChanged?.Invoke(this, GetAllDevices());
    }

    private static bool BothPresentAndDifferent(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        !string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveSession(ConnectionState state) =>
        state is ConnectionState.Connected or ConnectionState.Suspended;

    private static int ValidDiscoveryPort(int port) =>
        port > 0 && port <= 65535 ? port : Constants.DiscoveryUdpPort;

    private static List<string> MergeAddresses(
        IEnumerable<string>? previous,
        string current,
        IEnumerable<string>? candidates = null)
    {
        var addresses = previous?
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(current) &&
            !addresses.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            addresses.Add(current);
        }
        if (candidates != null)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !addresses.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    addresses.Add(candidate);
                }
            }
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
