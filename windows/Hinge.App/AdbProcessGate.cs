using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Hinge.App;

internal sealed class AdbProcessGate
{
    internal static AdbProcessGate Shared { get; } = new();

    private volatile bool _sessionEnding;
    internal bool SessionEnding
    {
        get => _sessionEnding;
        set => _sessionEnding = value;
    }

    internal void Start(Process process, CancellationToken cancellationToken, string failureMessage)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SessionEnding)
            throw new OperationCanceledException("Windows 正在结束会话，已暂停启动 ADB 和投屏。");
        if (!process.Start()) throw new InvalidOperationException(failureMessage);
    }

    // The standalone ADB server outlives the adb client that starts it. When
    // Windows closes a session, stop only Hinge's private server (port 5038),
    // not another application's ADB server (normally port 5037).
    // Talking to the server directly avoids launching adb.exe during shutdown.
    internal static bool TryStopPrivateServer(int port = 5038)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork) { SendTimeout = 250 };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).AsTask().GetAwaiter().GetResult();
            // ADB host service protocol: four ASCII hex length bytes + service name.
            // 'host:kill' has length 9, so the request is '0009host:kill'.
            client.GetStream().Write("0009host:kill"u8);
            return true;
        }
        catch (SocketException) { return false; }
        catch (IOException) { return false; }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }
}
