using System.Text.Json;

namespace Hinge.Core;

/// <summary>
/// A file-send request accepted by the Explorer integration while the target
/// device is temporarily offline. It contains only local file paths and the
/// already trusted device id; no account or network credential is persisted.
/// </summary>
public sealed record PendingFileSend(
    string Id,
    string DeviceId,
    IReadOnlyList<string> FilePaths,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Small, recoverable local queue for Explorer-to-device sends. The queue is
/// intentionally outside the registry so a shell click can survive a Hinge
/// restart and be consumed after the session is re-established.
/// </summary>
public sealed class PendingFileSendStore
{
    private const int MaxItems = 128;
    private readonly string _storagePath;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public PendingFileSendStore(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hinge",
                "pending-file-sends.json")
            : Path.GetFullPath(storagePath);
    }

    public IReadOnlyList<PendingFileSend> GetAll()
    {
        lock (_sync)
        {
            return LoadLocked();
        }
    }

    public PendingFileSend Enqueue(string deviceId, IEnumerable<string> filePaths)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("设备标识不能为空。", nameof(deviceId));
        }

        var paths = NormalizePaths(filePaths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("没有可加入队列的本地文件。", nameof(filePaths));
        }

        var item = new PendingFileSend(
            Guid.NewGuid().ToString("N"),
            deviceId.Trim(),
            paths,
            DateTimeOffset.UtcNow);
        lock (_sync)
        {
            var items = LoadLocked();
            items.Add(item);
            if (items.Count > MaxItems)
            {
                items.RemoveRange(0, items.Count - MaxItems);
            }
            SaveLocked(items);
        }
        return item;
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_sync)
        {
            var items = LoadLocked();
            var removed = items.RemoveAll(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) SaveLocked(items);
            return removed;
        }
    }

    public bool ReplacePaths(string id, IEnumerable<string> filePaths)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var paths = NormalizePaths(filePaths);
        lock (_sync)
        {
            var items = LoadLocked();
            var index = items.FindIndex(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (paths.Count == 0)
            {
                items.RemoveAt(index);
            }
            else
            {
                var current = items[index];
                items[index] = current with { FilePaths = paths };
            }
            SaveLocked(items);
            return true;
        }
    }

    private List<PendingFileSend> LoadLocked()
    {
        try
        {
            if (!File.Exists(_storagePath)) return new List<PendingFileSend>();
            var json = File.ReadAllText(_storagePath);
            var items = JsonSerializer.Deserialize<List<PendingFileSend>>(json);
            return items?
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Id) &&
                    !string.IsNullOrWhiteSpace(item.DeviceId) &&
                    item.FilePaths is { Count: > 0 })
                .ToList() ?? new List<PendingFileSend>();
        }
        catch
        {
            // A corrupt optional queue must not stop Hinge from starting.
            return new List<PendingFileSend>();
        }
    }

    private void SaveLocked(IReadOnlyList<PendingFileSend> items)
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
            // Explorer integration is optional. A locked profile should not
            // affect the main session or file-management workspace.
        }
    }

    private static List<string> NormalizePaths(IEnumerable<string> filePaths)
    {
        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                try { return Path.GetFullPath(path.Trim()); }
                catch { return string.Empty; }
            })
            .Where(path => path.Length > 0 && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
