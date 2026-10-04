using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public class TrustedDevice
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("pairedAt")]
    public long PairedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("lastSeen")]
    public long LastSeen { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("trustState")]
    public TrustState TrustState { get; set; } = TrustState.Trusted;
}

public class TrustStore
{
    private readonly string _storagePath;
    private readonly Dictionary<string, TrustedDevice> _trustedDevices = new();
    private readonly object _lock = new();

    public TrustStore(string? storagePath = null)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string suiteDir = Path.Combine(appData, "Hinge");
            Directory.CreateDirectory(suiteDir);
            _storagePath = Path.Combine(suiteDir, "trust_store.json");
        }
        else
        {
            _storagePath = storagePath;
        }

        Load();
    }

    public bool IsTrusted(string deviceId)
    {
        lock (_lock)
        {
            return _trustedDevices.TryGetValue(deviceId, out var dev) && dev.TrustState == TrustState.Trusted;
        }
    }

    public TrustedDevice? GetDevice(string deviceId)
    {
        lock (_lock)
        {
            return _trustedDevices.TryGetValue(deviceId, out var dev) ? dev : null;
        }
    }

    public IReadOnlyList<TrustedDevice> GetAllTrustedDevices()
    {
        lock (_lock)
        {
            return _trustedDevices.Values.ToList();
        }
    }

    public void AddOrUpdate(TrustedDevice device)
    {
        lock (_lock)
        {
            _trustedDevices[device.DeviceId] = device;
            Save();
        }
    }

    public bool Revoke(string deviceId)
    {
        lock (_lock)
        {
            if (_trustedDevices.Remove(deviceId))
            {
                Save();
                return true;
            }
            return false;
        }
    }

    // Names are only a cleanup suggestion, never proof of device identity.
    public IReadOnlyList<TrustedDevice> GetDuplicateHistory(IEnumerable<string> protectedDeviceIds)
    {
        lock (_lock)
        {
            var keep = protectedDeviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _trustedDevices.Values.Where(device => device.TrustState == TrustState.Trusted &&
                    !string.IsNullOrWhiteSpace(device.Name))
                .GroupBy(device => device.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group.OrderByDescending(device => device.LastSeen)
                    .ThenByDescending(device => device.PairedAt).ThenBy(device => device.DeviceId)
                    .Skip(1).Where(device => !keep.Contains(device.DeviceId)))
                .ToArray();
        }
    }

    public (int Removed, string? BackupPath) RemoveDuplicateHistory(
        IEnumerable<string> confirmedNames, IEnumerable<string> protectedDeviceIds)
    {
        lock (_lock)
        {
            var names = confirmedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remove = GetDuplicateHistory(protectedDeviceIds)
                .Where(device => names.Contains(device.Name.Trim())).Select(device => device.DeviceId).ToHashSet();
            if (remove.Count == 0) return (0, null);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var before = _trustedDevices.Values.ToArray();
            var remaining = before.Where(device => !remove.Contains(device.DeviceId)).ToArray();
            var suffix = Guid.NewGuid().ToString("N");
            var backup = _storagePath + ".backup-" + suffix + ".json";
            var temporary = _storagePath + "." + suffix + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_storagePath))!);
            // If either the backup or durable replacement fails, leave memory unchanged.
            File.WriteAllText(backup, JsonSerializer.Serialize(before, options));
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(remaining, options));
                File.Move(temporary, _storagePath, overwrite: true);
                foreach (var id in remove) _trustedDevices.Remove(id);
                return (remove.Count, backup);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_storagePath)) return;
            try
            {
                string json = File.ReadAllText(_storagePath);
                var list = JsonSerializer.Deserialize<List<TrustedDevice>>(json);
                if (list != null)
                {
                    _trustedDevices.Clear();
                    foreach (var item in list)
                    {
                        _trustedDevices[item.DeviceId] = item;
                    }
                }
            }
            catch
            {
                // Fallback for corrupt file
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(_trustedDevices.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
            catch
            {
                // Fallback
            }
        }
    }
}
