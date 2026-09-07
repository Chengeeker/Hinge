using Hinge.Core;

namespace Hinge.Platform;

public class Win32NotificationPresenter : INotificationPresenter
{
    private readonly List<NotificationEventMessage> _activeNotifications = new();
    private readonly object _lock = new();
    private bool _disposed;

    public IReadOnlyList<NotificationEventMessage> ActiveNotifications
    {
        get
        {
            lock (_lock) return _activeNotifications.ToList();
        }
    }

    public event EventHandler<NotificationActionMessage>? ActionTriggered;

    public void ShowNotification(NotificationEventMessage notification)
    {
        if (notification == null) return;

        lock (_lock)
        {
            _activeNotifications.RemoveAll(n => n.NotificationId == notification.NotificationId);
            _activeNotifications.Add(notification);
        }

        // Output to console or Windows Action Center
        string replyHint = notification.CanReply ? " [Quick Reply Supported]" : "";
        Console.WriteLine($"\n[Toast Notification - {notification.AppName}] {notification.Title}: {notification.Content}{replyHint}");
    }

    public void DismissNotification(string notificationId)
    {
        lock (_lock)
        {
            _activeNotifications.RemoveAll(n => n.NotificationId == notificationId);
        }
    }

    public void TriggerAction(string notificationId, string actionKey, string? replyText = null)
    {
        ActionTriggered?.Invoke(this, new NotificationActionMessage
        {
            NotificationId = notificationId,
            ActionKey = actionKey,
            ReplyText = replyText,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock) _activeNotifications.Clear();
    }
}
