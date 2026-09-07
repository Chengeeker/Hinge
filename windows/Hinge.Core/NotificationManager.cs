using System.Text;

namespace Hinge.Core;

public class NotificationManager : IDisposable
{
    private readonly INotificationPresenter _presenter;
    private readonly TrustStore _trustStore;
    private readonly NotificationFilter _filter;
    private readonly List<SessionConnection> _connections = new();
    private readonly object _connLock = new();
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
        _ = SendActionToPeersAsync(action);
    }

    public async Task SendActionToPeersAsync(NotificationActionMessage action)
    {
        if (action == null) return;
        byte[] payloadBytes = Encoding.UTF8.GetBytes(action.ToJson());

        List<SessionConnection> targets;
        lock (_connLock)
        {
            targets = _connections.Where(c => c.State == SessionState.Connected).ToList();
        }

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
