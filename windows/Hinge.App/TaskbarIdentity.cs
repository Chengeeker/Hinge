using System.IO;
using System.Runtime.InteropServices;
using Hinge.Platform;

namespace Hinge.App;

internal static class TaskbarIdentity
{
    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    internal static void SetWindowProperties(nint hwnd, string iconPath)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Hinge.exe");
        if (!File.Exists(executable)) return;

        IPropertyStore? store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out store));
            if (store == null) return;

            // Set relaunch properties before the ID, which makes the shell read them.
            SetString(store, 2, $"\"{executable}\"");
            SetString(store, 3, $"{iconPath},0");
            SetString(store, 4, "Hinge");
            SetString(store, 5, Win32NotificationPresenter.AppUserModelId);
        }
        catch (Exception error)
        {
            App.LogLifecycle($"taskbar-properties-failed hr=0x{error.HResult:X8}");
        }
        finally
        {
            if (store != null) Marshal.ReleaseComObject(store);
        }
    }

    private static void SetString(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey { FormatId = AppUserModel, Id = id };
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, out IPropertyStore? store);
}
