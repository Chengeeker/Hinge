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
