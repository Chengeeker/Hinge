using System.Text;

namespace Hinge.Core;

public class NotificationManager : IDisposable
{
    private readonly INotificationPresenter _presenter;
    private readonly TrustStore _trustStore;
    private readonly NotificationFilter _filter;
    private readonly List<SessionConnection> _connections = new();
    private readonly object _connLock = new();
    private readonly Dictionary<string, (string DeviceId, string OriginalId)> _notificationOrigins =
        new(StringComparer.Ordinal);
    private bool _disposed;

    public NotificationFilter Filter => _filter;
    public INotificationPresenter Presenter => _presenter;

    public event EventHandler<NotificationEventMessage>? NotificationReceived;
    public event EventHandler<NotificationActionMessage>? NotificationActionReceived;

    public NotificationManager(INotificationPresenter presenter, TrustStore trustStore, NotificationFilter? filter = null)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _filter = filter ?? new NotificationFilter();

        _presenter.ActionTriggered += OnPresenterActionTriggered;
    }

    public void RegisterConnection(SessionConnection conn)
    {
        if (conn == null) return;
        lock (_connLock)
        {
            if (!_connections.Contains(conn))
            {
                _connections.Add(conn);
            }
        }
    }

    public void UnregisterConnection(SessionConnection conn)
    {
        if (conn == null) return;
        lock (_connLock)
        {
            _connections.Remove(conn);
        }
    }

    public async Task<bool> HandleIncomingFrameAsync(SessionConnection conn, ProtocolFrame frame)
    {
        await Task.Yield();
        if (frame.Type != MessageType.NotificationEvent && frame.Type != MessageType.NotificationAction)
        {
            return false;
        }

        // Security check: verify peer device is trusted
        if (conn != null && !string.IsNullOrEmpty(conn.RemoteDeviceId))
        {
            if (!_trustStore.IsTrusted(conn.RemoteDeviceId))
            {
                return false;
            }
        }

        string json = Encoding.UTF8.GetString(frame.Payload);

        if (frame.Type == MessageType.NotificationEvent)
        {
            var notif = NotificationEventMessage.FromJson(json);
            if (notif == null) return false;

            // The wire carries an unscoped Android notificationId. Namespace
            // the presenter-facing ID with its authenticated TCP peer so two
            // phones cannot overwrite each other's toast or action mapping.
            if (conn?.RemoteDeviceId is { Length: > 0 } sourceId)
            {
                notif.SourceDeviceId = sourceId;
                notif.SourceDeviceName = _trustStore.GetAllTrustedDevices()
                    .FirstOrDefault(d => string.Equals(d.DeviceId, sourceId,
                        StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
                var originalId = notif.NotificationId;
                notif.NotificationId = DeviceWorkspaceManager.CacheKey(sourceId, originalId);
                lock (_connLock)
                {
                    _notificationOrigins[notif.NotificationId] = (sourceId, originalId);
                    if (_notificationOrigins.Count > 1000)
                    {
                        // Avoid unbounded retention on long-running systems.
                        var oldest = _notificationOrigins.Keys.First();
                        _notificationOrigins.Remove(oldest);
                    }
                }
            }

            if (!_filter.ShouldAllow(notif))
            {
                return false; // Filtered or deduplicated
            }

            _presenter.ShowNotification(notif);
            NotificationReceived?.Invoke(this, notif);
            return true;
        }
        else if (frame.Type == MessageType.NotificationAction)
        {
            var action = NotificationActionMessage.FromJson(json);
            if (action == null) return false;

            NotificationActionReceived?.Invoke(this, action);
            return true;
        }

        return false;
    }

    private void OnPresenterActionTriggered(object? sender, NotificationActionMessage action)
    {
        // Copying an SMS verification code is a local Windows action. Never
        // send the action back to the phone as a remote notification command.
        if (string.Equals(action.ActionKey, "copy_code", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _ = SendActionToPeersAsync(action);
    }

    public async Task SendActionToPeersAsync(NotificationActionMessage action)
    {
        if (action == null) return;
        List<SessionConnection> targets;
        string? origin = action.SourceDeviceId;
        string originalNotificationId = action.NotificationId;
        lock (_connLock)
        {
            if (_notificationOrigins.TryGetValue(action.NotificationId, out var recorded))
            {
                origin = recorded.DeviceId;
                originalNotificationId = recorded.OriginalId;
            }
            var ready = _connections.Where(c => c.IsSessionReady &&
                !string.IsNullOrWhiteSpace(c.RemoteDeviceId)).ToArray();
            var targetIds = NotificationRoutingPolicy.TargetIds(
                ready.Select(c => c.RemoteDeviceId), origin)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            targets = ready.Where(c => c.RemoteDeviceId != null &&
                targetIds.Contains(c.RemoteDeviceId))
                .GroupBy(c => c.RemoteDeviceId!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
        }
        if (targets.Count == 0) return;
        var outgoing = new NotificationActionMessage
        {
            NotificationId = originalNotificationId,
            ActionKey = action.ActionKey,
            ReplyText = action.ReplyText,
            Timestamp = action.Timestamp
        };
        byte[] payloadBytes = Encoding.UTF8.GetBytes(outgoing.ToJson());

        foreach (var conn in targets)
        {
            try
            {
                await conn.SendFrameAsync(MessageType.NotificationAction, payloadBytes);
            }
            catch
            {
                // Ignore transient send failures
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _presenter.ActionTriggered -= OnPresenterActionTriggered;
        _presenter.Dispose();
    }
}
