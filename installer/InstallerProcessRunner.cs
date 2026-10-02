using System.Diagnostics;

namespace Hinge.Setup;

internal static class InstallerProcessRunner
{
    public static void WaitForExit(Process process, int timeoutMilliseconds, string operation)
    {
        if (process.WaitForExit(timeoutMilliseconds)) return;
        // Only terminate the helper started by this installer, never Explorer or AppX services.
        try { process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        throw new TimeoutException($"{operation}超过 {timeoutMilliseconds / 1000.0:g} 秒未返回。已停止等待；Windows 包部署结果可能尚未确定，请勿连续重复安装。");
    }

    public static void Run(ProcessStartInfo startInfo, int timeoutMilliseconds, string operation)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动{operation}。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        WaitForExit(process, timeoutMilliseconds, operation);
        if (!Task.WaitAll([output, error], 2000))
            throw new TimeoutException($"{operation}的输出流未关闭，已停止等待。");
        if (process.ExitCode == 0) return;
        var detail = string.IsNullOrWhiteSpace(error.Result) ? output.Result : error.Result;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"{operation}失败，退出代码 {process.ExitCode}。" : detail.Trim());
    }
}
