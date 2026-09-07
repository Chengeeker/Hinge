namespace Hinge.Core;

public interface INotificationPresenter : IDisposable
{
    void ShowNotification(NotificationEventMessage notification);
    void DismissNotification(string notificationId);
    event EventHandler<NotificationActionMessage>? ActionTriggered;
}
