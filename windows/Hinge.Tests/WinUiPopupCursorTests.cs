using System.ComponentModel;
using System.Runtime.InteropServices;
using Hinge.App;

namespace Hinge.Tests;

public sealed class WinUiPopupCursorTests
{
    [Fact]
    public void EnforcesPopupArrowWithoutOverridingOtherThreadsOrWindowClasses()
    {
        using var popup = new HiddenWindow("Microsoft.UI.Content.PopupWindowSiteBridge");
        using var other = new HiddenWindow($"Hinge.CursorTest.{Guid.NewGuid():N}");
        using var inputSite = new HiddenWindow("InputSiteWindowClass", popup.Handle);
        using var otherChild = new HiddenWindow($"Hinge.OtherChild.{Guid.NewGuid():N}", other.Handle);
        Assert.True(WinUiPopupCursor.IsPopupInputWindow(popup.Handle));
        Assert.True(WinUiPopupCursor.IsPopupInputWindow(inputSite.Handle));
        Assert.False(WinUiPopupCursor.IsPopupInputWindow(otherChild.Handle));
        Assert.False(WinUiPopupCursor.IsPopupInputWindow(IntPtr.Zero));
        Assert.Equal(IntPtr.Zero, popup.Cursor);

        var otherThreadUpdates = -1;
        var thread = new Thread(() => otherThreadUpdates = WinUiPopupCursor.ApplyToCurrentThread());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, otherThreadUpdates);
        Assert.Equal(IntPtr.Zero, popup.Cursor);

        Assert.Equal(1, WinUiPopupCursor.ApplyToCurrentThread());
        Assert.Equal(LoadCursor(IntPtr.Zero, (IntPtr)32512), popup.Cursor);
        Assert.NotEqual(IntPtr.Zero, popup.Cursor);
        Assert.Equal(IntPtr.Zero, other.Cursor);
        Assert.Equal(0, WinUiPopupCursor.ApplyToCurrentThread());

        var resize = LoadCursor(IntPtr.Zero, (IntPtr)32645);
        Assert.NotEqual(IntPtr.Zero, resize);
        popup.Cursor = resize;
        other.Cursor = resize;
        Assert.Equal(1, WinUiPopupCursor.ApplyToCurrentThread());
        Assert.Equal(LoadCursor(IntPtr.Zero, (IntPtr)32512), popup.Cursor);
        Assert.Equal(resize, other.Cursor);
        Assert.Equal(0, WinUiPopupCursor.ApplyToCurrentThread());

        var log = new List<string>();
        Assert.True(WinUiPopupCursor.StartMonitoringCurrentThread(log.Add));
        try
        {
            popup.Cursor = resize;
            NotifyWinEvent(0x8002, other.Handle, 0, 0);
            PumpEventsUntil(() => false, TimeSpan.FromMilliseconds(50));
            Assert.Equal(resize, popup.Cursor);
            NotifyWinEvent(0x8002, inputSite.Handle, 0, 0);
            Assert.True(PumpEventsUntil(() => popup.Cursor != resize, TimeSpan.FromSeconds(2)));
            Assert.Equal(LoadCursor(IntPtr.Zero, (IntPtr)32512), popup.Cursor);
            Assert.Single(log);
            popup.Cursor = resize;
            NotifyWinEvent(0x8002, popup.Handle, 0, 0);
            Assert.True(PumpEventsUntil(() => popup.Cursor != resize, TimeSpan.FromSeconds(2)));
            Assert.Equal(2, log.Count);
        }
        finally
        {
            WinUiPopupCursor.StopMonitoring();
        }
        popup.Cursor = resize;
        NotifyWinEvent(0x8002, popup.Handle, 0, 0);
        PumpEventsUntil(() => false, TimeSpan.FromMilliseconds(50));
        Assert.Equal(resize, popup.Cursor);
    }

    private static bool PumpEventsUntil(Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1)) DispatchMessage(ref message);
            if (condition()) return true;
            Thread.Sleep(1);
        }
        return condition();
    }

    private sealed class HiddenWindow : IDisposable
    {
        private readonly string _className;
        private readonly IntPtr _instance = GetModuleHandle(null);
        private readonly WindowProcedure _procedure = DefWindowProc;
        private readonly IntPtr _window;

        internal IntPtr Handle => _window;

        internal HiddenWindow(string className, IntPtr parent = default)
        {
            _className = className;
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
                Instance = _instance,
                ClassName = className
            };
            if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _window = CreateWindowEx(0, className, "", parent == IntPtr.Zero ? 0u : 0x40000000u,
                0, 0, 1, 1, parent, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_window == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                UnregisterClass(className, _instance);
                throw new Win32Exception(error);
            }
        }

        internal IntPtr Cursor
        {
            get => GetClassLongPtr(_window, -12);
            set => SetClassLongPtr(_window, -12, value);
        }

        public void Dispose()
        {
            DestroyWindow(_window);
            UnregisterClass(_className, _instance);
            GC.KeepAlive(_procedure);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size, Style;
        internal IntPtr Procedure;
        internal int ClassExtra, WindowExtra;
        internal IntPtr Instance, Icon, Cursor, Background;
        internal string? MenuName, ClassName;
        internal IntPtr SmallIcon;
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal IntPtr Window;
        internal uint Message;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int X, Y;
        internal uint Private;
    }
    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventType, IntPtr window, int objectId, int childId);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
}
