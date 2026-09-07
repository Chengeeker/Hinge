using System.Text.Json.Serialization;

namespace Hinge.Core;

public class SyncFileEntry
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("modifiedTime")]
    public long ModifiedTime { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("platformFileIdentity")]
    public string? PlatformFileIdentity { get; set; }
}

public class SyncManifestMessage
{
    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("entries")]
    public List<SyncFileEntry> Entries { get; set; } = new();
}

public class SyncPullRequestMessage
{
    [JsonPropertyName("folderId")]
    public string FolderId { get; set; } = string.Empty;

    [JsonPropertyName("relativePaths")]
    public List<string> RelativePaths { get; set; } = new();
}

public class SyncDifference
{
    public List<SyncFileEntry> NeedPull { get; set; } = new();
    public List<SyncFileEntry> NeedPush { get; set; } = new();
    public List<SyncFileEntry> UpToDate { get; set; } = new();
}
