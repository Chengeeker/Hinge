using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hinge.Core;

namespace Hinge.App;

public sealed class RemoteFileEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "未命名文件";

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("modifiedAt")]
    public long ModifiedAt { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
}

public sealed class RemoteMediaMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("rotation")]
    public int Rotation { get; set; }

    [JsonPropertyName("bitrate")]
    public long Bitrate { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("cameraMake")]
    public string CameraMake { get; set; } = string.Empty;

    [JsonPropertyName("cameraModel")]
    public string CameraModel { get; set; } = string.Empty;

    [JsonPropertyName("dateTimeOriginal")]
    public string DateTimeOriginal { get; set; } = string.Empty;

    [JsonPropertyName("orientation")]
    public int Orientation { get; set; }
}

public sealed class RemoteFilePage
{
    public IReadOnlyList<RemoteFileEntry> Entries { get; init; } = Array.Empty<RemoteFileEntry>();
    public int Total { get; init; }
}

public sealed class RemoteCalendarEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "未命名日程";

    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long? End { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("allDay")]
    public bool AllDay { get; set; }
}

public sealed class RemotePhotoAlbum
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "未命名相册";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("coverUri")]
    public string CoverUri { get; set; } = string.Empty;

    [JsonPropertyName("latestTakenAt")]
    public long LatestTakenAt { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;
}

public sealed class RemotePhotoItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "未命名图片";

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("takenAt")]
    public long TakenAt { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public sealed class RemotePhotoPage
{
    public IReadOnlyList<RemotePhotoItem> Items { get; init; } = Array.Empty<RemotePhotoItem>();
    public int Total { get; init; }
}

public sealed class RemoteNotificationHistoryApplication
{
    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("iconBase64")]
    public string IconBase64 { get; set; } = string.Empty;
}

public sealed class RemoteNotificationHistoryItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("ongoing")]
    public bool Ongoing { get; set; }

    [JsonPropertyName("notificationKey")]
    public string NotificationKey { get; set; } = string.Empty;

    [JsonPropertyName("iconBase64")]
    public string IconBase64 { get; set; } = string.Empty;
}

public sealed class RemoteNotificationHistoryPage
{
    [JsonPropertyName("access")]
    public bool AccessEnabled { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("items")]
    public IReadOnlyList<RemoteNotificationHistoryItem> Items { get; set; } =
        Array.Empty<RemoteNotificationHistoryItem>();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("applications")]
    public IReadOnlyList<RemoteNotificationHistoryApplication> Applications { get; set; } =
        Array.Empty<RemoteNotificationHistoryApplication>();
}

public sealed class RemoteWorkspaceNote
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }
}

public sealed class RemoteWorkspaceTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }

    [JsonPropertyName("dueAt")]
    public long? DueAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
}

public sealed class RemoteDeviceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;
}

public sealed class WorkspaceRemoteClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<RemoteFileEntry>> BrowseFilesAsync(
        SessionConnection connection,
        string category,
        string path = "",
        bool forceRefresh = false)
    {
        var raw = await InvokeAsync(
            connection,
            "browseFiles",
            new Dictionary<string, object?>
            {
                ["category"] = category,
                ["path"] = path,
                ["forceRefresh"] = forceRefresh
            });

        if (raw.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RemoteFileEntry>();
        }

        var entries = new List<RemoteFileEntry>();
        foreach (var item in raw.EnumerateArray())
        {
            try
            {
                var entry = JsonSerializer.Deserialize<RemoteFileEntry>(
                    item.GetRawText(),
                    JsonOptions);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // One malformed item must not hide the rest of the directory.
            }
        }
        return entries;
    }

    public async Task<RemoteFilePage> BrowseFilesPageAsync(
        SessionConnection connection,
        string category,
        string path,
        int offset,
        int limit,
        bool forceRefresh = false)
    {
        JsonElement raw;
        try
        {
            raw = await InvokeAsync(
                connection,
                "browseFilesPage",
                new Dictionary<string, object?>
                {
                    ["category"] = category,
                    ["path"] = path,
                    ["offset"] = offset,
                    ["limit"] = limit,
                    ["forceRefresh"] = forceRefresh
                });
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("不支持", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            // Keep compatibility with an older Android APK that only exposes
            // browseFiles. New builds use the paged command to avoid sending
            // tens of thousands of entries in one ToolResult frame.
            var entries = await BrowseFilesAsync(connection, category, path, forceRefresh);
            return new RemoteFilePage
            {
                Entries = entries.Skip(offset).Take(limit).ToArray(),
                Total = entries.Count
            };
        }

        if (raw.ValueKind != JsonValueKind.Object)
        {
            return new RemoteFilePage();
        }

        var items = raw.TryGetProperty("items", out var rawItems) &&
            rawItems.ValueKind == JsonValueKind.Array
            ? DeserializeFileEntries(rawItems)
            : Array.Empty<RemoteFileEntry>();
        var total = raw.TryGetProperty("total", out var rawTotal) &&
            rawTotal.TryGetInt32(out var count)
            ? count
            : items.Count;
        return new RemoteFilePage { Entries = items, Total = total };
    }

    private static IReadOnlyList<RemoteFileEntry> DeserializeFileEntries(JsonElement raw)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RemoteFileEntry>>(
                raw.GetRawText(), JsonOptions) ?? new List<RemoteFileEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<RemoteFileEntry>();
        }
    }

    public Task<IReadOnlyList<RemoteCalendarEvent>> LoadCalendarAsync(SessionConnection connection) =>
        InvokeListAsync<RemoteCalendarEvent>(connection, "calendarEvents", null);

    public async Task<RemoteNotificationHistoryPage> LoadNotificationHistoryAsync(
        SessionConnection connection,
        int offset,
        int limit,
        bool ascending,
        string? packageName = null)
    {
        var raw = await InvokeAsync(
            connection,
            "notificationHistory",
            new Dictionary<string, object?>
            {
                ["offset"] = Math.Max(0, offset),
                ["limit"] = Math.Clamp(limit, 1, 200),
                ["ascending"] = ascending,
                ["packageName"] = packageName ?? string.Empty,
            });
        if (raw.ValueKind != JsonValueKind.Object)
        {
            return new RemoteNotificationHistoryPage();
        }

        return JsonSerializer.Deserialize<RemoteNotificationHistoryPage>(
                   raw.GetRawText(),
                   JsonOptions) ??
            new RemoteNotificationHistoryPage();
    }

    public async Task<bool> OpenNotificationHistoryItemAsync(
        SessionConnection connection,
        RemoteNotificationHistoryItem item)
    {
        var raw = await InvokeAsync(
            connection,
            "notificationHistoryAction",
            new Dictionary<string, object?>
            {
                ["action"] = "open",
                ["id"] = item.Id,
                ["packageName"] = item.PackageName,
                ["appName"] = item.AppName,
                ["title"] = item.Title,
                ["content"] = item.Content,
                ["timestamp"] = item.Timestamp,
                ["notificationKey"] = item.NotificationKey,
            });
        if (raw.ValueKind == JsonValueKind.True) return true;
        return raw.ValueKind == JsonValueKind.Object &&
            raw.TryGetProperty("opened", out var opened) &&
            opened.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> DeleteNotificationHistoryItemAsync(
        SessionConnection connection,
        string id)
    {
        var raw = await InvokeAsync(
            connection,
            "notificationHistoryAction",
            new Dictionary<string, object?>
            {
                ["action"] = "delete",
                ["id"] = id,
            });
        return raw.ValueKind == JsonValueKind.Object &&
            raw.TryGetProperty("deleted", out var deleted) &&
            deleted.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> ClearNotificationHistoryAsync(SessionConnection connection)
    {
        var raw = await InvokeAsync(
            connection,
            "notificationHistoryAction",
            new Dictionary<string, object?>
            {
                ["action"] = "clear",
            });
        return raw.ValueKind == JsonValueKind.Object &&
            raw.TryGetProperty("cleared", out var cleared) &&
            cleared.ValueKind == JsonValueKind.True;
    }

    public Task<IReadOnlyList<RemotePhotoAlbum>> LoadPhotoAlbumsAsync(SessionConnection connection) =>
        InvokeListAsync<RemotePhotoAlbum>(connection, "photoAlbums", null);

    public Task<IReadOnlyList<RemotePhotoItem>> LoadPhotosAsync(
        SessionConnection connection,
        string? albumId = null)
    {
        IReadOnlyDictionary<string, object?>? payload = string.IsNullOrWhiteSpace(albumId)
            ? null
            : new Dictionary<string, object?> { ["albumId"] = albumId };
        return InvokeListAsync<RemotePhotoItem>(connection, "photos", payload);
    }

    public async Task<RemotePhotoPage> LoadPhotoPageAsync(
        SessionConnection connection,
        string? albumId,
        int offset,
        int limit)
    {
        var payload = new Dictionary<string, object?>
        {
            ["offset"] = Math.Max(0, offset),
            ["limit"] = Math.Clamp(limit, 1, 200),
        };
        if (!string.IsNullOrWhiteSpace(albumId))
        {
            payload["albumId"] = albumId;
        }

        JsonElement raw;
        try
        {
            raw = await InvokeAsync(connection, "photosPage", payload);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("不支持", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            // Older Android builds return the complete list. Keep this
            // fallback so the new Windows client can still connect, while
            // current builds use the bounded page response below.
            var all = await LoadPhotosAsync(connection, albumId);
            return new RemotePhotoPage
            {
                Items = all.Skip(Math.Max(0, offset)).Take(Math.Max(1, limit)).ToArray(),
                Total = all.Count,
            };
        }

        if (raw.ValueKind == JsonValueKind.Array)
        {
            var all = DeserializePhotoItems(raw);
            return new RemotePhotoPage
            {
                Items = all,
                Total = all.Count,
            };
        }

        if (raw.ValueKind != JsonValueKind.Object)
        {
            return new RemotePhotoPage();
        }

        var items = raw.TryGetProperty("items", out var rawItems) &&
            rawItems.ValueKind == JsonValueKind.Array
            ? DeserializePhotoItems(rawItems)
            : Array.Empty<RemotePhotoItem>();
        var total = raw.TryGetProperty("total", out var rawTotal) &&
            rawTotal.TryGetInt32(out var count)
            ? count
            : items.Count;
        return new RemotePhotoPage { Items = items, Total = total };
    }

    private static IReadOnlyList<RemotePhotoItem> DeserializePhotoItems(JsonElement raw)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RemotePhotoItem>>(
                raw.GetRawText(), JsonOptions) ?? new List<RemotePhotoItem>();
        }
        catch (JsonException)
        {
            return Array.Empty<RemotePhotoItem>();
        }
    }

    public async Task<RemoteMediaMetadata?> LoadMediaMetadataAsync(
        SessionConnection connection,
        string uri,
        string name,
        string mimeType)
    {
        var raw = await InvokeAsync(
            connection,
            "mediaMetadata",
            new Dictionary<string, object?>
            {
                ["uri"] = uri,
                ["name"] = name,
                ["mimeType"] = mimeType,
            });
        return raw.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<RemoteMediaMetadata>(raw.GetRawText(), JsonOptions)
            : null;
    }

    public Task<IReadOnlyList<RemoteWorkspaceNote>> LoadNotesAsync(SessionConnection connection) =>
        InvokeListAsync<RemoteWorkspaceNote>(connection, "loadNotes", null);

    public Task<IReadOnlyList<RemoteWorkspaceTask>> LoadTasksAsync(SessionConnection connection) =>
        InvokeListAsync<RemoteWorkspaceTask>(connection, "loadTasks", null);

    public async Task<RemoteDeviceInfo?> LoadDeviceInfoAsync(SessionConnection connection)
    {
        var raw = await InvokeAsync(connection, "deviceInfo", EmptyPayload);
        return raw.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<RemoteDeviceInfo>(raw.GetRawText(), JsonOptions)
            : null;
    }

    public async Task SaveNoteAsync(SessionConnection connection, RemoteWorkspaceNote note)
    {
        await InvokeAsync(connection, "saveNote", ToPayload(note));
    }

    public async Task DeleteNoteAsync(SessionConnection connection, string id)
    {
        await InvokeAsync(connection, "deleteNote", new Dictionary<string, object?> { ["id"] = id });
    }

    public async Task SaveTaskAsync(SessionConnection connection, RemoteWorkspaceTask task)
    {
        await InvokeAsync(connection, "saveTask", ToPayload(task));
    }

    public async Task DeleteTaskAsync(SessionConnection connection, string id)
    {
        await InvokeAsync(connection, "deleteTask", new Dictionary<string, object?> { ["id"] = id });
    }

    public async Task PairDeviceAsync(SessionConnection connection, DeviceIdentity identity)
    {
        await InvokeAsync(
            connection,
            "pairDevice",
            new Dictionary<string, object?>
            {
                ["deviceId"] = identity.DeviceId,
                ["name"] = identity.Name,
                ["publicKey"] = identity.PublicKey,
            });
    }

    public Task<byte[]?> LoadPhotoBytesAsync(SessionConnection connection, string uri) =>
        LoadBytesAsync(connection, "photoBytes", uri);

    public Task<byte[]?> LoadPhotoThumbnailBytesAsync(SessionConnection connection, string uri) =>
        LoadBytesAsync(connection, "photoThumbnailBytes", uri);

    public Task<byte[]?> LoadPhotoPreviewBytesAsync(SessionConnection connection, string uri) =>
        LoadBytesAsync(connection, "photoPreviewBytes", uri);

    public async Task SendMediaToComputerAsync(
        SessionConnection connection,
        string uri,
        string fileName,
        string mimeType)
    {
        await InvokeAsync(
            connection,
            "sendMediaToComputer",
            new Dictionary<string, object?>
            {
                ["uri"] = uri,
                ["fileName"] = fileName,
                ["mimeType"] = mimeType,
            });
    }

    public async Task<int> DeleteFilesAsync(
        SessionConnection connection,
        IReadOnlyCollection<string> uris)
    {
        var raw = await InvokeAsync(
            connection,
            "deleteFiles",
            new Dictionary<string, object?>
            {
                ["uris"] = uris.Where(uri => !string.IsNullOrWhiteSpace(uri)).ToArray()
            });
        if (raw.ValueKind == JsonValueKind.Object &&
            raw.TryGetProperty("deleted", out var deleted) &&
            deleted.TryGetInt32(out var count))
        {
            return count;
        }
        return 0;
    }

    private static IReadOnlyDictionary<string, object?> ToPayload<T>(T value)
    {
        var json = JsonSerializer.SerializeToElement(value, JsonOptions);
        return json.Deserialize<Dictionary<string, object?>>(JsonOptions)
            ?? new Dictionary<string, object?>();
    }

    private async Task<IReadOnlyList<T>> InvokeListAsync<T>(
        SessionConnection connection,
        string command,
        IReadOnlyDictionary<string, object?>? payload)
    {
        var raw = await InvokeAsync(connection, command, payload ?? EmptyPayload);
        if (raw.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<T>();
        }

        var result = new List<T>();
        foreach (var item in raw.EnumerateArray())
        {
            try
            {
                var value = JsonSerializer.Deserialize<T>(item.GetRawText(), JsonOptions);
                if (value != null) result.Add(value);
            }
            catch (JsonException)
            {
                // Keep usable entries when one remote item is malformed.
            }
        }
        return result;
    }

    private async Task<byte[]?> LoadBytesAsync(
        SessionConnection connection,
        string command,
        string uri)
    {
        var raw = await InvokeAsync(
            connection,
            command,
            new Dictionary<string, object?> { ["uri"] = uri });
        if (raw.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var encoded = raw.GetString();
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload =
        new Dictionary<string, object?>();
    private readonly ConcurrentDictionary<SessionConnection, ConnectionDispatcher> _dispatchers = new();

    private sealed class ConnectionDispatcher
    {
        public ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Pending { get; } = new();
        public EventHandler<ProtocolFrame>? FrameHandler { get; set; }
        public EventHandler<SessionState>? StateHandler { get; set; }
    }

    private async Task<JsonElement> InvokeAsync(
        SessionConnection connection,
        string command,
        IReadOnlyDictionary<string, object?> payload)
    {
        if (!connection.IsSessionReady)
        {
            throw new InvalidOperationException("设备会话尚未连接。");
        }

        string commandId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = EnsureDispatcher(connection);
        dispatcher.Pending[commandId] = completion;
        try
        {
            await connection.SendJsonAsync(
                MessageType.ToolCommand,
                new
                {
                    commandId,
                    command,
                    payload
                });
            // File queries and cold ContentResolver scans can legitimately
            // exceed the old fixed 15-second window. The connection remains
            // healthy while this request is pending; only the request expires.
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        finally
        {
            dispatcher.Pending.TryRemove(commandId, out _);
        }
    }

    private ConnectionDispatcher EnsureDispatcher(SessionConnection connection)
    {
        return _dispatchers.GetOrAdd(connection, static (session, owner) =>
        {
            var dispatcher = new ConnectionDispatcher();
            dispatcher.FrameHandler = (_, frame) => owner.HandleFrame(dispatcher, frame);
            dispatcher.StateHandler = (_, state) =>
            {
                if (state != SessionState.Disconnected) return;
                FailPending(dispatcher, new IOException("设备会话已断开。"));
                owner._dispatchers.TryRemove(session, out ConnectionDispatcher? _);
            };
            session.FrameReceived += dispatcher.FrameHandler;
            session.StateChanged += dispatcher.StateHandler;
            return dispatcher;
        }, this);
    }

    private void HandleFrame(ConnectionDispatcher dispatcher, ProtocolFrame frame)
    {
        if (frame.Type != MessageType.ToolResult) return;
        try
        {
            using var document = JsonDocument.Parse(frame.Payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("commandId", out var id)) return;
            var commandId = id.GetString() ?? string.Empty;
            if (!dispatcher.Pending.TryGetValue(commandId, out var completion)) return;

            if (root.TryGetProperty("success", out var success) &&
                success.ValueKind == JsonValueKind.True)
            {
                var result = root.TryGetProperty("payload", out var resultPayload)
                    ? resultPayload.Clone()
                    : JsonDocument.Parse("null").RootElement.Clone();
                completion.TrySetResult(result);
            }
            else
            {
                string message = root.TryGetProperty("error", out var error)
                    ? error.GetString() ?? "远端操作失败"
                    : "远端操作失败";
                completion.TrySetException(new InvalidOperationException(message));
            }
        }
        catch (JsonException exception)
        {
            FailPending(dispatcher, exception);
        }
    }

    private static void FailPending(ConnectionDispatcher dispatcher, Exception exception)
    {
        foreach (var pending in dispatcher.Pending.Values)
        {
            pending.TrySetException(exception);
        }
        dispatcher.Pending.Clear();
    }
}
