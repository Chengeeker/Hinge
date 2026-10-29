using System.Runtime.InteropServices;
using Hinge.Core;

namespace Hinge.Platform;

public class Win32InputInjector : IInputInjector
{
    private readonly Func<DesktopPointerBounds>? _desktopBounds;
    public Win32InputInjector() { }
    private Win32InputInjector(Func<DesktopPointerBounds> desktopBounds) => _desktopBounds = desktopBounds;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX {
        public int Size; public RECT Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref RECT bounds, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static Win32InputInjector ForDesktop(string deviceName)
    {
        new DesktopVideoSettings(MonitorDeviceName: deviceName).Validate();
        IntPtr selected = IntPtr.Zero;
        MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref RECT bounds, IntPtr data) => {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info) && info.DeviceName == deviceName) selected = monitor;
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || selected == IntPtr.Zero)
            throw new IOException("所选显示器不可用，请刷新后重新连接。");
        DesktopPointerBounds ReadBounds()
        {
            var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(selected, ref info) || info.DeviceName != deviceName)
                throw new IOException("所选显示器已断开，已停止远程输入。");
            var bounds = new DesktopPointerBounds(info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
            bounds.Validate(); return bounds;
        }
        var original = ReadBounds();
        return new Win32InputInjector(() => {
            var bounds = ReadBounds();
            if (bounds != original) throw new IOException("显示器布局或分辨率已变化，请重新连接。");
            return bounds;
        });
    }

    private INPUT DesktopPointerMove(int deltaX, int deltaY)
    {
        var bounds = _desktopBounds!();
        if (!GetCursorPos(out var cursor)) throw new IOException("无法读取桌面鼠标位置。");
        var target = bounds.Move(cursor.X, cursor.Y, deltaX, deltaY);
        var virtualDesktop = new DesktopPointerBounds(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        var normalized = virtualDesktop.Normalize(target.X, target.Y);
        return new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT {
            dx = normalized.X, dy = normalized.Y, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } } };
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    public void KeyDown(byte virtualKey) => SendKey(virtualKey, false);
    public void KeyUp(byte virtualKey) => SendKey(virtualKey, true);

    private static void SendKey(byte key, bool up)
    {
        if (key < 8 || key == 255) return;
        uint scan = MapVirtualKey(key, 4);
        bool useScan = scan != 0 && (scan >> 8) != 0xE1;
        var input = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT {
            wVk = useScan ? (ushort)0 : key,
            wScan = useScan ? (ushort)(scan & 0xFF) : (ushort)0,
            dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (useScan ? KEYEVENTF_SCANCODE : 0) |
                (useScan && (scan >> 8) == 0xE0 ? KEYEVENTF_EXTENDEDKEY : 0)
        } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    public void MoveMouse(int deltaX, int deltaY)
    {
        if (_desktopBounds != null) {
            var move = DesktopPointerMove(deltaX, deltaY);
            if (SendInput(1, new[] { move }, Marshal.SizeOf<INPUT>()) != 1)
                throw new IOException("系统未接受远程鼠标移动。");
            return;
        }
        MoveRelativeMouse(deltaX, deltaY);
    }

    public void MoveAbsoluteMouse(int x, int y)
    {
        if (_desktopBounds == null) throw new InvalidOperationException("Absolute input requires a selected desktop.");
        var target = _desktopBounds().FromNormalized(x, y);
        var virtualDesktop = new DesktopPointerBounds(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        var normalized = virtualDesktop.Normalize(target.X, target.Y);
        var input = new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT {
            dx = normalized.X, dy = normalized.Y,
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
            throw new IOException("系统未接受桌面触控定位。");
    }


    public void MoveRelativeMouse(int deltaX, int deltaY)
    {
        _desktopBounds?.Invoke();
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = deltaX,
                    dy = deltaY,
                    dwFlags = MOUSEEVENTF_MOVE
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1 && _desktopBounds != null)
            throw new IOException("系统未接受游戏相对鼠标移动。");
    }

    public void MouseDown(RemoteMouseButton button)
    {
        uint flag = button switch
        {
            RemoteMouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
            RemoteMouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_LEFTDOWN
        };

        SendMouseFlag(flag);
    }

    public void MouseUp(RemoteMouseButton button)
    {
        uint flag = button switch
        {
            RemoteMouseButton.Right => MOUSEEVENTF_RIGHTUP,
            RemoteMouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => MOUSEEVENTF_LEFTUP
        };

        SendMouseFlag(flag);
    }

    public void MouseClick(RemoteMouseButton button)
    {
        MouseDown(button);
        MouseUp(button);
    }

    public void MouseDoubleClick()
    {
        MouseClick(RemoteMouseButton.Left);
        Thread.Sleep(30);
        MouseClick(RemoteMouseButton.Left);
    }

    public void MouseScroll(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = MOUSEEVENTF_WHEEL
                }
            }
        };

        SendPointerInput(input, true);
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            // Key down
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE
                    }
                }
            });

            // Key up
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
                    }
                }
            });
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private void SendMouseFlag(uint flag)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flag
                }
            }
        };

        SendPointerInput(input, (flag & (MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_MIDDLEDOWN)) != 0);
    }

    private void SendPointerInput(INPUT input, bool position)
    {
        var inputs = _desktopBounds != null && position ? new[] { DesktopPointerMove(0, 0), input } : new[] { input };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length && _desktopBounds != null && position)
            throw new IOException("系统未接受远程鼠标操作。");
    }

    public void Dispose()
    {
    }
}
