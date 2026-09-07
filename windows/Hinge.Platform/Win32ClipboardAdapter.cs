using System.Runtime.InteropServices;
using Hinge.Core;

namespace Hinge.Platform;

public class Win32ClipboardAdapter : IClipboardAdapter
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private readonly object _lock = new();
    private System.Threading.Timer? _pollTimer;
    private uint _lastSequenceNumber;
    private string? _lastKnownText;
    private bool _isMonitoring;
    private bool _disposed;

    public bool IsMonitoring => _isMonitoring;
    public event EventHandler<string>? TextChanged;

    public Task<string?> GetTextAsync()
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                return ReadClipboardText();
            }
        });
    }

    public Task SetTextAsync(string text)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                WriteClipboardText(text);
                _lastKnownText = text;
                _lastSequenceNumber = GetClipboardSequenceNumber();
            }
        });
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;
        _lastSequenceNumber = GetClipboardSequenceNumber();
        _lastKnownText = ReadClipboardText();

        _pollTimer = new System.Threading.Timer(_ => CheckClipboardChange(), null, 250, 250);
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void CheckClipboardChange()
    {
        if (!_isMonitoring) return;

        uint currentSeq = GetClipboardSequenceNumber();
        if (currentSeq == _lastSequenceNumber) return;

        string? currentText = null;
        lock (_lock)
        {
            _lastSequenceNumber = currentSeq;
            currentText = ReadClipboardText();
            if (string.Equals(currentText, _lastKnownText, StringComparison.Ordinal))
            {
                return;
            }
            _lastKnownText = currentText;
        }

        if (!string.IsNullOrEmpty(currentText))
        {
            TextChanged?.Invoke(this, currentText);
        }
    }

    private static string? ReadClipboardText()
    {
        for (int i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                    if (handle == IntPtr.Zero) return null;

                    IntPtr ptr = GlobalLock(handle);
                    if (ptr == IntPtr.Zero) return null;

                    try
                    {
                        return Marshal.PtrToStringUni(ptr);
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
        return null;
    }

    private static void WriteClipboardText(string text)
    {
        if (text == null) return;

        int bytesNeeded = (text.Length + 1) * 2;
        IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytesNeeded);
        if (hGlobal == IntPtr.Zero) return;

        IntPtr target = GlobalLock(hGlobal);
        if (target == IntPtr.Zero) return;

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0); // null terminator
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        for (int i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
