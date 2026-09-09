using Hinge.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;

namespace Hinge.Platform;

public class Win32NotificationPresenter : INotificationPresenter
{
    public const string AppUserModelId = "Hinge.Office";
    private readonly List<NotificationEventMessage> _activeNotifications = new();
    private readonly Dictionary<string, string> _verificationCodes = new();
    private readonly object _lock = new();
    private readonly AppNotificationManager? _appNotificationManager;
    private readonly Action<string>? _copyText;
    private bool _registered;
    private bool _legacyToastAvailable;
    private bool _disposed;
    private string? _registrationError;

    public bool IsSystemNotificationRegistered => _registered;

    public bool IsSystemNotificationAvailable
    {
        get
        {
            if (_legacyToastAvailable) return true;
            if (!_registered || _appNotificationManager == null) return false;
            try
            {
                return _appNotificationManager.Setting.ToString() == "Enabled";
            }
            catch
            {
                return false;
            }
        }
    }

    public string SystemNotificationStatus
    {
        get
        {
            if (_appNotificationManager == null)
            {
                return HasInstalledToastShortcut()
                    ? "已配置 Windows 原生通知"
                    : string.IsNullOrWhiteSpace(_registrationError)
                    ? "系统通知不可用，使用应用内通知"
                    : "系统通知注册失败，使用应用内通知";
            }

            try
            {
                return _appNotificationManager.Setting.ToString() switch
                {
                    "Enabled" => "已开启",
                    "DisabledForApplication" => "已关闭（应用通知）",
                    "DisabledForUser" => "已关闭（Windows 全局通知）",
                    "DisabledByGroupPolicy" => "已被组策略禁用",
                    "DisabledByManifest" => "已被清单禁用",
                    "Unsupported" => HasInstalledToastShortcut()
                        ? "已配置 Windows 原生通知"
                        : "系统通知需要安装开始菜单快捷方式",
                    _ => "状态未知"
                };
            }
            catch
            {
                return _registered ? "已注册" : "状态不可用";
            }
        }
    }

    public IReadOnlyList<NotificationEventMessage> ActiveNotifications
    {
        get
        {
            lock (_lock) return _activeNotifications.ToList();
        }
    }

    public event EventHandler<NotificationActionMessage>? ActionTriggered;

    public Win32NotificationPresenter(Action<string>? copyText = null)
    {
        _copyText = copyText;
        try
        {
            // Hinge is distributed as an unpackaged EXE. Give the current
            // process the same stable identity as the Start-menu shortcut so
            // Windows can route local Toast notifications to the notification
            // center instead of treating this process as an anonymous Win32
            // executable.
            SetCurrentProcessAppUserModelId(AppUserModelId);
            // Register the handler before Register(). This keeps button clicks
            // in the already-running Hinge process for unpackaged EXE builds.
            _appNotificationManager = AppNotificationManager.Default;
            _appNotificationManager.NotificationInvoked += OnNotificationInvoked;
            _appNotificationManager.Register();
            _registered = true;
        }
        catch (Exception exception)
        {
            // Native app notifications are optional on unsupported Windows
            // builds. The in-app notification state remains available.
            _registrationError = exception.Message;
            _appNotificationManager = null;
        }
    }

    private static void SetCurrentProcessAppUserModelId(string appUserModelId)
    {
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        }
        catch
        {
            // The notification manager still provides its normal registration
            // path when this shell API is unavailable.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);

    public void ShowNotification(NotificationEventMessage notification)
    {
        if (notification == null) return;
        var isSmsNotification = IsSmsNotification(notification);

        lock (_lock)
        {
            _activeNotifications.RemoveAll(n => n.NotificationId == notification.NotificationId);
            _activeNotifications.Add(notification);
            if (isSmsNotification &&
                notification.IsVerificationCode &&
                !string.IsNullOrWhiteSpace(notification.VerificationCode))
            {
                _verificationCodes[notification.NotificationId] = notification.VerificationCode!;
                while (_verificationCodes.Count > 100)
                {
                    _verificationCodes.Remove(_verificationCodes.Keys.First());
                }
            }
        }

        // SMS/MMS stays on the tray balloon path. It is intended to work while
        // Hinge is minimized, and MainWindow attaches the copy-on-click action
        // only when this is a real verification message.
        if (isSmsNotification) return;

        try
        {
            if (_appNotificationManager != null)
            {
                var builder = new AppNotificationBuilder()
                    .AddArgument("notificationId", notification.NotificationId)
                    .AddText(string.IsNullOrWhiteSpace(notification.AppName)
                        ? "Hinge"
                        : notification.AppName)
                    .AddText(string.IsNullOrWhiteSpace(notification.Title)
                        ? "新通知"
                        : notification.Title)
                    .AddText(notification.Content);

                _appNotificationManager.Show(builder.BuildNotification());
                return;
            }
        }
        catch
        {
            // Older Windows App SDK runtimes may report Unsupported even
            // though the inbox desktop Toast API is available.
        }

        try
        {
            ShowLegacyDesktopToast(notification);
            _legacyToastAvailable = true;
            return;
        }
        catch
        {
            // Fall through to the non-invasive diagnostic path below.
        }

        // Keep a fallback for older Windows versions or a missing App SDK
        // notification registration, without printing SMS content.
        Console.WriteLine($"[Hinge notification] {notification.AppName} · {notification.Title}");
    }

    private static void ShowLegacyDesktopToast(NotificationEventMessage notification)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            $"<toast><visual><binding template=\"ToastGeneric\">" +
            $"<text>{EscapeXml(string.IsNullOrWhiteSpace(notification.AppName) ? "Hinge" : notification.AppName)}</text>" +
            $"<text>{EscapeXml(string.IsNullOrWhiteSpace(notification.Title) ? "收到新通知" : notification.Title)}</text>" +
            $"<text>{EscapeXml(notification.Content)}</text>" +
            "</binding></visual></toast>");
        var toast = new Windows.UI.Notifications.ToastNotification(xml);
        Windows.UI.Notifications.ToastNotificationManager
            .CreateToastNotifier(AppUserModelId)
            .Show(toast);
    }

    private static string EscapeXml(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static bool IsSmsNotification(NotificationEventMessage notification) =>
        string.Equals(notification.Source, "sms", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(notification.Source, "mms", StringComparison.OrdinalIgnoreCase);

    public bool TryCopyVerificationCode(string notificationId)
    {
        string? code;
        lock (_lock)
        {
            _verificationCodes.TryGetValue(notificationId, out code);
        }

        if (string.IsNullOrWhiteSpace(code) || _copyText == null) return false;

        try
        {
            _copyText(code);
            TriggerAction(notificationId, "copy_code");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasInstalledToastShortcut()
    {
        var shortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "Hinge.lnk");
        return File.Exists(shortcut);
    }

    public void DismissNotification(string notificationId)
    {
        lock (_lock)
        {
            _activeNotifications.RemoveAll(n => n.NotificationId == notificationId);
            _verificationCodes.Remove(notificationId);
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("action", out var action) ||
            !string.Equals(action, "copyCode", StringComparison.OrdinalIgnoreCase) ||
            !args.Arguments.TryGetValue("notificationId", out var notificationId))
        {
            return;
        }

        _ = TryCopyVerificationCode(notificationId);
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
        try
        {
            if (_appNotificationManager != null)
            {
                _appNotificationManager.NotificationInvoked -= OnNotificationInvoked;
                if (_registered) _appNotificationManager.Unregister();
            }
        }
        catch
        {
            // Notification registration is best-effort during shutdown.
        }
        lock (_lock) _activeNotifications.Clear();
        lock (_lock) _verificationCodes.Clear();
    }
}
