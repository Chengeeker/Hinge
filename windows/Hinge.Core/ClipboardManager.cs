using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hinge.Core;

public class ClipboardManager : IDisposable
{
    private readonly DeviceIdentity _localIdentity;
    private readonly IClipboardAdapter _clipboardAdapter;
    private readonly LruRingCache _cache;
    private readonly List<SessionConnection> _activeConnections = new();
    private readonly object _connectionLock = new();

    private string? _lastAppliedRemoteContent;
    private bool _disposed;

    public bool AutoSync { get; set; } = true;
    public bool AutoOpenUrls { get; set; } = false;
    public LruRingCache Cache => _cache;
    public IClipboardAdapter Adapter => _clipboardAdapter;

    public event EventHandler<ClipboardEventMessage>? ClipboardReceived;
    public event EventHandler<string>? UrlHandoffReceived;

    public ClipboardManager(
        DeviceIdentity localIdentity,
        IClipboardAdapter clipboardAdapter,
        LruRingCache? cache = null)
    {
        _localIdentity = localIdentity ?? throw new ArgumentNullException(nameof(localIdentity));
        _clipboardAdapter = clipboardAdapter ?? throw new ArgumentNullException(nameof(clipboardAdapter));
        _cache = cache ?? new LruRingCache(100);

        _clipboardAdapter.TextChanged += OnLocalClipboardChanged;
    }

    public void RegisterConnection(SessionConnection connection)
    {
        lock (_connectionLock)
        {
            if (!_activeConnections.Contains(connection))
            {
                _activeConnections.Add(connection);
            }
        }
    }

    public void UnregisterConnection(SessionConnection connection)
    {
        lock (_connectionLock)
        {
            _activeConnections.Remove(connection);
        }
    }

    private void OnLocalClipboardChanged(object? sender, string text)
    {
        if (!AutoSync || string.IsNullOrEmpty(text)) return;

        // Anti-echo: if the clipboard change is the exact content we just applied from a remote peer, ignore it
        if (string.Equals(text, _lastAppliedRemoteContent, StringComparison.Ordinal))
        {
            return;
        }

        var eventId = Guid.NewGuid().ToString();
        _cache.Add(eventId);

        var msg = new ClipboardEventMessage
        {
            EventId = eventId,
            OriginDeviceId = _localIdentity.DeviceId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ContentType = "text/plain",
            Content = text
        };

        BroadcastClipboardEvent(msg);
    }

    public void BroadcastClipboardEvent(ClipboardEventMessage msg)
    {
        List<SessionConnection> conns;
        lock (_connectionLock)
        {
            conns = _activeConnections.Where(c => c.State == SessionState.Connected).ToList();
        }

        foreach (var conn in conns)
        {
            _ = conn.SendJsonAsync(MessageType.ClipboardEvent, msg);
        }
    }

    public async Task<bool> HandleIncomingFrameAsync(SessionConnection conn, ProtocolFrame frame)
    {
        if (frame.Type != MessageType.ClipboardEvent) return false;

        ClipboardEventMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<ClipboardEventMessage>(frame.Payload);
        }
        catch
        {
            return false;
        }

        if (msg == null) return false;

        // 1. Anti-Loop Rule 1: Drop if originating from self
        if (string.Equals(msg.OriginDeviceId, _localIdentity.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Anti-Loop Rule 2: Drop if already processed (exists in LRU 100 cache)
        if (_cache.Contains(msg.EventId))
        {
            return false;
        }

        // Add to ring cache
        _cache.Add(msg.EventId);

        // Apply to local clipboard
        _lastAppliedRemoteContent = msg.Content;
        await _clipboardAdapter.SetTextAsync(msg.Content);

        ClipboardReceived?.Invoke(this, msg);

        // Check for URL handoff
        if (IsUrl(msg.Content))
        {
            UrlHandoffReceived?.Invoke(this, msg.Content.Trim());
        }

        return true;
    }

    public static bool IsUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clipboardAdapter.TextChanged -= OnLocalClipboardChanged;
        _clipboardAdapter.Dispose();
    }
}
