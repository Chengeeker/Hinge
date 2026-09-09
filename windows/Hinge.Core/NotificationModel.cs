using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public class NotificationEventMessage
{
    [JsonPropertyName("notificationId")]
    public string NotificationId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("isVerificationCode")]
    public bool IsVerificationCode { get; set; }

    [JsonPropertyName("verificationCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerificationCode { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("canReply")]
    public bool CanReply { get; set; }

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);

    public static NotificationEventMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NotificationEventMessage>(json);
        }
        catch
        {
            return null;
        }
    }
}

public class NotificationActionMessage
{
    [JsonPropertyName("notificationId")]
    public string NotificationId { get; set; } = string.Empty;

    [JsonPropertyName("actionKey")]
    public string ActionKey { get; set; } = string.Empty;

    [JsonPropertyName("replyText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplyText { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public string ToJson() => JsonSerializer.Serialize(this);

    public static NotificationActionMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NotificationActionMessage>(json);
        }
        catch
        {
            return null;
        }
    }
}

public class NotificationFilter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _recentDedupeCache = new();

    public HashSet<string> WhitelistPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BlacklistPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SuppressOngoing { get; set; } = true;
    public TimeSpan DebounceWindow { get; set; } = TimeSpan.FromSeconds(2);

    public bool ShouldAllow(NotificationEventMessage msg)
    {
        if (msg == null || string.IsNullOrWhiteSpace(msg.PackageName)) return false;

        // 1. Blacklist check
        if (BlacklistPackages.Contains(msg.PackageName)) return false;

        // 2. Whitelist check (if any configured)
        if (WhitelistPackages.Count > 0 && !WhitelistPackages.Contains(msg.PackageName)) return false;

        // 3. Debounce deduplication check (pkg + title + content)
        lock (_lock)
        {
            string key = $"{msg.PackageName}:{msg.Title}:{msg.Content}";
            DateTime now = DateTime.UtcNow;

            // Prune expired entries
            if (_recentDedupeCache.Count > 200)
            {
                var expiredKeys = _recentDedupeCache
                    .Where(kv => (now - kv.Value) > DebounceWindow)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in expiredKeys) _recentDedupeCache.Remove(k);
            }

            if (_recentDedupeCache.TryGetValue(key, out var lastTime))
            {
                if ((now - lastTime) < DebounceWindow)
                {
                    return false; // Suppressed as duplicate spam
                }
            }

            _recentDedupeCache[key] = now;
        }

        return true;
    }
}
