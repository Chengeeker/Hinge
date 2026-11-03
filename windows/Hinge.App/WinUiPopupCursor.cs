using System.Runtime.InteropServices;
using System.Text;

namespace Hinge.App;

internal static class WinUiPopupCursor
{
    private const int ClassCursorIndex = -12;
    private const uint ObjectShow = 0x8002;
    private static readonly WinEventCallback ShowCallback = OnWindowShown;
    private static IntPtr _showHook;
    private static Action<string>? _log;
    private static int _showsLogged;
    private static int _selectionInputsLogged;

    internal static void ShowArrowForSelectionInput()
    {
        if (!GetCursorPos(out var point) ||
            GetWindowThreadProcessId(WindowFromPoint(point), out _) != GetCurrentThreadId()) return;
        var arrow = LoadCursor(IntPtr.Zero, (IntPtr)32512);
        if (arrow == IntPtr.Zero) return;
        SetCursor(arrow);
        if (_selectionInputsLogged++ < 3) _log?.Invoke("selector-pointer-arrow-restored");
    }

    internal static bool StartMonitoringCurrentThread(Action<string> log)
    {
        if (_showHook != IntPtr.Zero) return true;
        _log = log;
        _showsLogged = 0;
        _selectionInputsLogged = 0;
        _showHook = SetWinEventHook(ObjectShow, ObjectShow, IntPtr.Zero, ShowCallback,
            (uint)Environment.ProcessId, GetCurrentThreadId(), 0);
        return _showHook != IntPtr.Zero;
    }

    internal static void StopMonitoring()
    {
        if (_showHook == IntPtr.Zero || !UnhookWinEvent(_showHook)) return;
        _showHook = IntPtr.Zero;
        _log = null;
    }

    private static void OnWindowShown(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        if (hook != _showHook || objectId != 0 || childId != 0 || !IsPopupInputWindow(window)) return;
        try
        {
            // XAML DropDownOpened can run before the native popup is shown and owns the pointer.
            var updated = ApplyToCurrentThread();
            if (_showsLogged++ < 3) _log?.Invoke($"popup-cursor-native-show patched={updated}");
        }
        catch
        {
            // Never propagate a managed exception through the native event callback.
        }
    }

    internal static int ApplyToCurrentThread()
    {
        var arrow = LoadCursor(IntPtr.Zero, (IntPtr)32512);
        if (arrow == IntPtr.Zero) return 0;

        var updated = 0;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (!IsPopupWindow(window) ||
                GetClassLongPtr(window, ClassCursorIndex) == arrow) return true;

            // WinUI #8829: popup creation can retain a stale wait/resize cursor until mouse movement.
            Marshal.SetLastPInvokeError(0);
            var previous = SetClassLongPtr(window, ClassCursorIndex, arrow);
            if (previous != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0) updated++;
            return true;
        }, IntPtr.Zero);

        // Refresh every opening, even when the class was already repaired on a previous opening.
        if (GetCursorPos(out var point))
        {
            var window = WindowFromPoint(point);
            if (IsPopupInputWindow(window))
                SetCursor(arrow);
        }
        return updated;
    }

    internal static bool IsPopupInputWindow(IntPtr window) =>
        window != IntPtr.Zero && GetWindowThreadProcessId(window, out _) == GetCurrentThreadId() &&
        IsPopupWindow(GetAncestor(window, 2));

    private static bool IsPopupWindow(IntPtr window)
    {
        var className = new StringBuilder(128);
        return GetClassName(window, className, className.Capacity) != 0 &&
            className.ToString() == "Microsoft.UI.Content.PopupWindowSiteBridge";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X, Y; }

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, WindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
}
