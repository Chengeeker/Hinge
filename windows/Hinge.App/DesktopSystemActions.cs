using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hinge.Core;

namespace Hinge.App;

internal static class DesktopSystemActions
{
    internal static ProcessStartInfo ShutdownStartInfo(DesktopSystemAction action)
    {
        if (action is not (DesktopSystemAction.Restart or DesktopSystemAction.Shutdown))
            throw new ArgumentOutOfRangeException(nameof(action));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        info.ArgumentList.Add(action == DesktopSystemAction.Restart ? "/r" : "/s");
        // A positive timeout implies /f in shutdown.exe; use zero to preserve unsaved-app prompts.
        info.ArgumentList.Add("/t");
        info.ArgumentList.Add("0");
        return info;
    }

    internal static void Execute(DesktopSystemAction action)
    {
        if (action == DesktopSystemAction.Lock)
        {
            if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
            return;
        }
        using var process = Process.Start(ShutdownStartInfo(action))
            ?? throw new IOException("无法启动系统电源操作。");
        if (!process.WaitForExit(3000)) throw new IOException("系统电源请求响应超时。");
        if (process.ExitCode != 0) throw new IOException("系统拒绝了电源请求，请检查电脑上的提示及账户权限。");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
