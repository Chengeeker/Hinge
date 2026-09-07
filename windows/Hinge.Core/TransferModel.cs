using System.Text.Json.Serialization;

namespace Hinge.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransferState
{
    Idle,
    Offering,
    WaitingAccept,
    Transferring,
    Verifying,
    Completed,
    Failed,
    Cancelled
}

public class TextTransferMessage
{
    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text"; // "text" or "url"

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public class FileOfferMessage
{
    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";

    [JsonPropertyName("modifiedTime")]
    public long ModifiedTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; } = string.Empty;
}

public class FileAcceptMessage
{
    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = string.Empty;

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; } = true;

    [JsonPropertyName("offset")]
    public long Offset { get; set; } = 0; // For resume

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public class FileCompleteMessage
{
    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}

public class TransferProgress
{
    public string TransferId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100.0 : 0.0;
    public TransferState State { get; set; }
}

public sealed class TransferFailure
{
    public string TransferId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}
