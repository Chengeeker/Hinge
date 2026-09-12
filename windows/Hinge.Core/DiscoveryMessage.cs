using System.Text.Json.Serialization;

namespace Hinge.Core;

public class DiscoveryMessage
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "windows";

    [JsonPropertyName("port")]
    public int Port { get; set; } = Constants.SessionTcpPort;

    [JsonPropertyName("discoveryPort")]
    public int DiscoveryPort { get; set; } = Constants.DiscoveryUdpPort;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = Constants.ProtocolVersion;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("connectionRequested")]
    public bool ConnectionRequested { get; set; }

    // Distinguishes a trusted-device reconnect from a user-initiated request.
    // Older peers omit this field and therefore remain compatible as manual
    // connection requests.
    [JsonPropertyName("automaticReconnect")]
    public bool AutomaticReconnect { get; set; }
}
