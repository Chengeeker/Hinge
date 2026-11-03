using Hinge.App;
using System.Diagnostics;

namespace Hinge.Tests;

public class WirelessAdbConsoleTests
{
    [Fact]
    public void ConsoleUsesTheIsolatedServerAndOneExplicitDevice()
    {
        var start = WirelessAdbClient.CreateConsoleStartInfo("192.168.1.4:42200", "adb shell id");
        Assert.Equal("-P 5038 -s \"192.168.1.4:42200\" shell id", start.Arguments);
        Assert.Empty(start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Throws<ArgumentException>(() => WirelessAdbClient.CreateConsoleStartInfo("device\" -s other", "shell id"));
    }

    [Theory]
    [InlineData("adb shell getprop ro.product.model", "shell getprop ro.product.model")]
    [InlineData("adb.exe pull \"/sdcard/My File.txt\" \"D:\\My Files\\\"", "pull \"/sdcard/My File.txt\" \"D:\\My Files\\\"")]
    [InlineData("shell \"echo hello && echo world\"", "shell \"echo hello && echo world\"")]
    public void CommandsPreserveQuotingWithoutInvokingAHostShell(string input, string expected)
    {
        Assert.Equal(expected, WirelessAdbClient.NormalizeConsoleCommand(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("adb")]
    [InlineData("adb -s other-device shell id")]
    [InlineData("-P 5037 shell id")]
    [InlineData("adb kill-server")]
    [InlineData("adb disconnect")]
    [InlineData("shell id\r\nreboot")]
    public void CommandsCannotOverrideThePairedTargetOrSharedServer(string input)
    {
        Assert.Throws<ArgumentException>(() => WirelessAdbClient.NormalizeConsoleCommand(input));
    }

    [Fact]
    public async Task ConsoleOutputIsBoundedAndDrainedWithoutADevice()
    {
        string result = "";
        var start = StartInfo("[Console]::Write(('x' * 70000)); [Console]::Error.Write('stderr')");
        Assert.Equal(0, await WirelessAdbClient.RunConsoleProcessAsync(start,
            new CallbackProgress(text => result = text), CancellationToken.None));
        Assert.InRange(result.Length, 65536, 65620);
        Assert.Contains("后续内容已省略", result);
    }

    [Fact]
    public async Task StoppingConsoleTerminatesOnlyItsOwnedProcessAndRetainsOutput()
    {
        string result = "";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WirelessAdbClient.RunConsoleProcessAsync(
            StartInfo("[Console]::Write('ready'); Start-Sleep -Seconds 30"),
            new CallbackProgress(text => result = text), cancellation.Token));
        Assert.Contains("ready", result);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
    }

    private static ProcessStartInfo StartInfo(string script)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(arg);
        return start;
    }

    private sealed class CallbackProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
