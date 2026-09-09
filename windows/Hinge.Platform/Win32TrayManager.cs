using Hinge.Core;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace Hinge.Platform;

public class Win32TrayManager : ITrayManager
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private bool _isVisible;
    private string _appName = "Hinge";
    private string _currentTooltip = "Hinge — LAN Cross-Device Hub";
    private readonly object _notificationLock = new();
    private Action? _balloonTipClickAction;
    private bool _disposed;

    public bool IsVisible => _isVisible;
    public string CurrentTooltip => _currentTooltip;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public void Initialize(string appName, string initialTooltip)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Win32TrayManager));

        // Initialization can be reached again after an activation redirect.
        // Dispose the previous native icon first so the shell never receives
        // duplicate icons for the same application instance.
        DisposeNativeTray();
        _appName = appName;
        _currentTooltip = initialTooltip;
        _isVisible = true;

        try
        {
            _contextMenu = new Forms.ContextMenuStrip();
            var open = new Forms.ToolStripMenuItem("打开 Hinge");
            open.Click += (_, _) => RequestOpen();
            _contextMenu.Items.Add(open);
            _contextMenu.Items.Add(new Forms.ToolStripSeparator());
            var exit = new Forms.ToolStripMenuItem("退出");
            exit.Click += (_, _) => RequestExit();
            _contextMenu.Items.Add(exit);

            _notifyIcon = new Forms.NotifyIcon
            {
                Text = LimitTooltip(initialTooltip),
                Icon = LoadApplicationIcon(),
                ContextMenuStrip = _contextMenu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => RequestOpen();
            _notifyIcon.BalloonTipClicked += (_, _) =>
            {
                Action? clickAction;
                lock (_notificationLock)
                {
                    clickAction = _balloonTipClickAction;
                    _balloonTipClickAction = null;
                }

                if (clickAction != null)
                {
                    try
                    {
                        clickAction();
                    }
                    catch
                    {
                        // Notification actions must not take down the tray host.
                    }
                    return;
                }

                RequestOpen();
            };
        }
        catch
        {
            // Keep the logical tray lifecycle usable in test hosts without a shell session.
            DisposeNativeTray();
        }
    }

    public void UpdateTooltip(string tooltip)
    {
        _currentTooltip = tooltip;
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = LimitTooltip(tooltip);
        }
    }

    public void ShowNotification(string title, string text)
    {
        ShowNotification(title, text, null);
    }

    public void ShowNotification(string title, string text, Action? clickAction)
    {
        if (_notifyIcon != null)
        {
            lock (_notificationLock)
            {
                _balloonTipClickAction = clickAction;
            }
            _notifyIcon.BalloonTipTitle = LimitTooltip(title);
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(3000);
            return;
        }
        Console.WriteLine($"\n[Tray Balloon - {title}]: {text}");
    }

    public void RequestOpen()
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isVisible = false;
        DisposeNativeTray();
    }

    private void DisposeNativeTray()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _contextMenu?.Dispose();
        _contextMenu = null;
        lock (_notificationLock)
        {
            _balloonTipClickAction = null;
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Icon.ExtractAssociatedIcon(processPath);
                if (icon != null) return icon;
            }
        }
        catch
        {
            // Use the standard Windows application icon below.
        }
        return SystemIcons.Application;
    }

    private static string LimitTooltip(string text) =>
        string.IsNullOrWhiteSpace(text) ? "Hinge" : text[..Math.Min(63, text.Length)];
}
