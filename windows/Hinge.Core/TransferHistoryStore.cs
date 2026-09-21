using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransferDirection
{
    Send,
    Receive
}

/// <summary>
/// A compact, local-only record used by the Windows home page. File paths are
/// retained only for correlating durable Explorer queue items; the UI shows
/// the safe file name and never exposes the full path.
/// </summary>
public sealed record TransferHistoryRecord
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; init; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; init; } = string.Empty;

    [JsonPropertyName("pendingId")]
    public string PendingId { get; init; } = string.Empty;

    [JsonPropertyName("transferId")]
    public string TransferId { get; init; } = string.Empty;

    [JsonPropertyName("direction")]
    public TransferDirection Direction { get; init; } = TransferDirection.Send;

    [JsonPropertyName("state")]
    public TransferState State { get; init; } = TransferState.Idle;

    [JsonPropertyName("bytesTransferred")]
    public long BytesTransferred { get; init; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonIgnore]
    public bool CanCancel =>
        Direction == TransferDirection.Send &&
        State is TransferState.Offering or
            TransferState.WaitingAccept or
            TransferState.Transferring or
            TransferState.Verifying;

    [JsonIgnore]
    public bool IsInProgress => State is
        TransferState.Offering or
        TransferState.WaitingAccept or
        TransferState.Transferring or
        TransferState.Verifying;

    [JsonIgnore]
    public bool CanDelete => !IsInProgress;

    [JsonIgnore]
    public string DirectionText => Direction == TransferDirection.Send ? "发送" : "接收";

    [JsonIgnore]
    public string StatusText => State switch
    {
        TransferState.WaitingAccept when !string.IsNullOrWhiteSpace(PendingId) =>
            "待发送 · 等待设备连接",
        TransferState.Offering => "正在建立发送",
        TransferState.WaitingAccept => "等待对方接受",
        TransferState.Transferring when TotalBytes > 0 =>
            $"正在发送 · {Math.Clamp((double)BytesTransferred / TotalBytes * 100, 0, 100):F0}%",
        TransferState.Transferring => "正在发送",
        TransferState.Verifying => "正在校验",
        TransferState.Completed => Direction == TransferDirection.Send ? "发送完成" : "接收完成",
        TransferState.Failed when !string.IsNullOrWhiteSpace(Error) => $"失败 · {Error}",
        TransferState.Failed => "失败",
        TransferState.Cancelled => "已取消",
        _ => DirectionText
    };

    [JsonIgnore]
    public string TimeText => UpdatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
}

/// <summary>
/// Small recoverable history store. It is intentionally separate from the
/// pending-send queue: completed and cancelled records remain visible while
/// the queue only contains work that still needs delivery.
/// </summary>
public sealed class TransferHistoryStore
{
    private const int MaxItems = 100;
    private readonly string _storagePath;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public TransferHistoryStore(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hinge",
                "transfer-history.json")
            : Path.GetFullPath(storagePath);
    }

    public IReadOnlyList<TransferHistoryRecord> GetAll()
    {
        lock (_sync)
        {
            return LoadLocked();
        }
    }

    public void Upsert(TransferHistoryRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Id)) return;

        lock (_sync)
        {
            var items = LoadLocked();
            var index = items.FindIndex(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase));
            var normalized = record with
            {
                FileName = string.IsNullOrWhiteSpace(record.FileName)
                    ? Path.GetFileName(record.FilePath)
                    : record.FileName,
                UpdatedAtUtc = record.UpdatedAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : record.UpdatedAtUtc
            };

            if (index >= 0)
            {
                items[index] = normalized;
            }
            else
            {
                items.Add(normalized);
            }

            items = items
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(MaxItems)
                .ToList();
            SaveLocked(items);
        }
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        lock (_sync)
        {
            var items = LoadLocked();
            var remaining = items
                .Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remaining.Count == items.Count) return false;

            SaveLocked(remaining);
            return true;
        }
    }

    public int DeleteWhere(Func<TransferHistoryRecord, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_sync)
        {
            var items = LoadLocked();
            var remaining = items.Where(item => !predicate(item)).ToList();
            var deleted = items.Count - remaining.Count;
            if (deleted == 0) return 0;

            SaveLocked(remaining);
            return deleted;
        }
    }

    private List<TransferHistoryRecord> LoadLocked()
    {
        try
        {
            if (!File.Exists(_storagePath)) return new List<TransferHistoryRecord>();
            var json = File.ReadAllText(_storagePath);
            var items = JsonSerializer.Deserialize<List<TransferHistoryRecord>>(json);
            return items?
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Id) &&
                    !string.IsNullOrWhiteSpace(item.FileName))
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .Take(MaxItems)
                .ToList() ?? new List<TransferHistoryRecord>();
        }
        catch
        {
            // History is optional. A corrupt file must never block Hinge startup.
            return new List<TransferHistoryRecord>();
        }
    }

    private void SaveLocked(IReadOnlyList<TransferHistoryRecord> items)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = _storagePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(temporaryPath, _storagePath, overwrite: true);
        }
        catch
        {
            // A locked profile should not affect transfers or the main UI.
        }
    }
}
