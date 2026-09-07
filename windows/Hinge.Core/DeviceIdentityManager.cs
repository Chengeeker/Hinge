using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public class DeviceIdentity
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;
}

public class DeviceIdentityManager
{
    private readonly string _storagePath;
    private DeviceIdentity? _cachedIdentity;

    public DeviceIdentityManager(string? storagePath = null)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string suiteDir = Path.Combine(appData, "Hinge");
            Directory.CreateDirectory(suiteDir);
            _storagePath = Path.Combine(suiteDir, "identity.json");
        }
        else
        {
            _storagePath = storagePath;
        }
    }

    public DeviceIdentity GetOrCreateIdentity()
    {
        if (_cachedIdentity != null)
        {
            return _cachedIdentity;
        }

        if (File.Exists(_storagePath))
        {
            try
            {
                string json = File.ReadAllText(_storagePath);
                var identity = JsonSerializer.Deserialize<DeviceIdentity>(json);
                if (identity != null && !string.IsNullOrWhiteSpace(identity.DeviceId))
                {
                    // Environment.MachineName is commonly returned in upper case
                    // on Windows. Use the host name for the user-facing device
                    // label and migrate the old cached label without changing the
                    // stable device ID or key used by pairing.
                    string currentName = GetMachineDisplayName();
                    if (!string.Equals(identity.Name, currentName, StringComparison.Ordinal))
                    {
                        identity.Name = currentName;
                        SaveIdentity(identity);
                    }
                    _cachedIdentity = identity;
                    return _cachedIdentity;
                }
            }
            catch
            {
                // In case of corrupt file, generate new one
            }
        }

        byte[] keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);

        var newIdentity = new DeviceIdentity
        {
            DeviceId = Guid.NewGuid().ToString(),
            Name = GetMachineDisplayName(),
            PublicKey = Convert.ToHexString(keyBytes).ToLowerInvariant()
        };

        SaveIdentity(newIdentity);

        _cachedIdentity = newIdentity;
        return _cachedIdentity;
    }

    private static string GetMachineDisplayName()
    {
        string name;
        try
        {
            name = Dns.GetHostName();
        }
        catch
        {
            name = Environment.MachineName;
        }

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = Environment.MachineName.Trim();
        return string.IsNullOrWhiteSpace(name) ? "Windows 设备" : name.ToLowerInvariant();
    }

    private void SaveIdentity(DeviceIdentity identity)
    {
        try
        {
            string json = JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch
        {
            // Fallback for non-writable environments
        }

    }
}
