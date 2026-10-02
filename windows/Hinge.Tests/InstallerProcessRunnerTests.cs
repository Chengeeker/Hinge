using System.Diagnostics;
using Hinge.Setup;

namespace Hinge.Tests;

public sealed class InstallerProcessRunnerTests
{
    private static ProcessStartInfo Command(string script) => new("powershell.exe")
    {
        Arguments = "-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)),
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    [Fact]
    public void SuccessfulHelperReturns() =>
        InstallerProcessRunner.Run(Command("Write-Output 'ok'"), 10000, "test");

    [Fact]
    public void FailedHelperReportsError()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            InstallerProcessRunner.Run(Command("[Console]::Error.WriteLine('helper failed'); exit 7"), 10000, "test"));
        Assert.Contains("helper failed", error.Message);
    }

    [Fact]
    public void DeploymentErrorIsPlainTextNotCliXml()
    {
        var error = Assert.Throws<InvalidOperationException>(() => InstallerProcessRunner.Run(
            Command("$ProgressPreference='SilentlyContinue'; try { throw 'HRESULT: 0x80073CFB' } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"),
            10000, "test"));
        Assert.Equal("HRESULT: 0x80073CFB", error.Message);
    }

    [Fact]
    public void StuckHelperTimesOutAndIsStopped()
    {
        using var process = Process.Start(Command("Start-Sleep -Seconds 60"))!;
        var elapsed = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => InstallerProcessRunner.WaitForExit(process, 500, "test"));
        Assert.True(process.WaitForExit(5000));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }
}
