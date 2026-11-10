using System.Diagnostics;

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
}
