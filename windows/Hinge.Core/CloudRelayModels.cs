using System.Text.Json.Serialization;

namespace Hinge.Core;

public static class CloudRelayConstants
{
    public const int ApiVersion = 1;
    public const int PartSize = 8 * 1024 * 1024;
    public const int GcmTagSize = 16;
    public const int GcmNonceSize = 12;
    public const int RelayKeySize = 32;
    public const int DefaultTtlHours = 168;
}

/// <summary>
/// User-owned relay configuration. The admin token is deliberately not part
/// of this persisted model; it is needed only for one-time device registration.
/// </summary>
public sealed record CloudRelaySettings
{
    public bool Enabled { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string DeviceToken { get; init; } = string.Empty;
    public string RelayEncryptionKey { get; init; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured => Enabled &&
        Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) &&
        endpoint.Scheme is "https" or "http" &&
        !string.IsNullOrWhiteSpace(DeviceToken) &&
        !string.IsNullOrWhiteSpace(RelayEncryptionKey);
}

public sealed record CloudRelayCredentials(
    string RelayDeviceId,
    string DeviceToken);

public sealed record CloudRelayUploadSession(
    string TransferId,
    string UploadId,
    int PartSize,
    int PartCount);

public sealed record CloudRelayUploadedPart(
    int PartNumber,
    string Etag);

public sealed record CloudRelayManifest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("transferId")]
    public string TransferId { get; init; } = string.Empty;

    [JsonPropertyName("senderRelayDeviceId")]
    public string SenderRelayDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("receiverRelayDeviceId")]
    public string ReceiverRelayDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; init; }

    [JsonPropertyName("partSize")]
    public int PartSize { get; init; }

    [JsonPropertyName("partCount")]
    public int PartCount { get; init; }

    [JsonPropertyName("ciphertextSize")]
    public long CiphertextSize { get; init; }

    [JsonPropertyName("encryption")]
    public string Encryption { get; init; } = "AES-256-GCM";

    [JsonPropertyName("metadataCiphertext")]
    public string MetadataCiphertext { get; init; } = string.Empty;

    [JsonPropertyName("metadataNonce")]
    public string MetadataNonce { get; init; } = string.Empty;

    [JsonPropertyName("metadataTag")]
    public string MetadataTag { get; init; } = string.Empty;
}

public sealed record CloudRelayReceipt
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("transferId")]
    public string TransferId { get; init; } = string.Empty;

    [JsonPropertyName("senderRelayDeviceId")]
    public string SenderRelayDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("receiverRelayDeviceId")]
    public string ReceiverRelayDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("deliveredAt")]
    public long DeliveredAt { get; init; }
}

public sealed record CloudRelayFileMetadata
{
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = "application/octet-stream";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; init; }

    [JsonPropertyName("modifiedTime")]
    public long ModifiedTime { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record CloudRelayReceiveResult(
    string TransferId,
    string SenderRelayDeviceId,
    string FilePath,
    string FileName,
    long FileSize);
