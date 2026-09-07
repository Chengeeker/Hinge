using System.Text.Json.Serialization;

namespace Hinge.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DevicePlatform
{
    Android,
    Windows,
    Linux,
    MacOS,
    iOS,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectionState
{
    Disconnected,
    Discovered,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustState
{
    Untrusted,
    PendingVerification,
    Trusted,
    Blocked
}

public class Device
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public DevicePlatform Platform { get; set; } = DevicePlatform.Windows;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = Constants.AppVersion;

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = Constants.ProtocolVersion;

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonPropertyName("networkAddresses")]
    public List<string> NetworkAddresses { get; set; } = new();

    [JsonPropertyName("connectionState")]
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Discovered;

    [JsonPropertyName("trustState")]
    public TrustState TrustState { get; set; } = TrustState.Untrusted;
}
