using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

    [Fact]
    public async Task ShutdownSendsProtocolKillOnlyToSelectedAdbPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        Assert.True(AdbProcessGate.TryStopPrivateServer(port));

        using var client = await accepted.WaitAsync(TimeSpan.FromSeconds(5));
        var request = new byte[13];
        await client.GetStream().ReadExactlyAsync(request);
        Assert.Equal("0009host:kill", Encoding.ASCII.GetString(request));
    }

    [Fact]
    public void MissingServerDoesNotStartAnotherAdbProcess()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.False(AdbProcessGate.TryStopPrivateServer(port));
    }
}
