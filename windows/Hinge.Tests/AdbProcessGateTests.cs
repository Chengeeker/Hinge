using System.Diagnostics;
using Hinge.App;

namespace Hinge.Tests;

public sealed class AdbProcessGateTests
{
    [Fact]
    public async Task SessionEndBlocksLaunchAndCanceledShutdownAllowsItAgain()
    {
        var gate = new AdbProcessGate { SessionEnding = true };
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        Assert.Throws<OperationCanceledException>(() => gate.Start(process, CancellationToken.None, "launch failed"));

        gate.SessionEnding = false;
        Assert.Throws<OperationCanceledException>(() => gate.Start(process, new CancellationToken(true), "launch failed"));
        gate.Start(process, CancellationToken.None, "launch failed");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
    }
}
