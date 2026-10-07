using System.Diagnostics;
using System.Net;
using System.Text;

namespace Hinge.App;

internal static class WirelessAdbClient
{
    private const string DiagnosticsUri = "content://com.hinge.office.diagnostics/connection";

    public static async Task PairAndConnectAsync(string host, string pairingPort, string code, string connectPort)
    {
        ValidateCode(code);
        var pairingEndpoint = Endpoint(host, pairingPort);
        var connectionEndpoint = Endpoint(host, connectPort);
        EnsureSuccess(await RunAsync("pair", pairingEndpoint, code));
        EnsureSuccess(await RunAsync("connect", connectionEndpoint));
    }

    public static async Task ConnectAsync(string host, string connectPort)
    {
        var endpoint = Endpoint(host, connectPort);
        EnsureSuccess(await RunAsync("connect", endpoint));
        var state = await RunAsync("-s", endpoint, "get-state");
        EnsureSuccess(state);
        if (!string.Equals(state.Output.Trim(), "device", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"ADB 设备状态为 {state.Output.Trim()}，未建立可用连接。");
        }
    }

    public static async Task<string> PullDiagnosticsAsync(string host, string connectPort)
    {
        var endpoint = Endpoint(host, connectPort);
        var result = await RunAsync("-s", endpoint, "exec-out", "content", "read", "--uri", DiagnosticsUri);
        EnsureSuccess(result);
        if (string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException("手机没有可导出的 Hinge 连接诊断日志。请先在手机上启动 Hinge 的连接服务并重试。");
        }

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Hinge-Android-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(path, result.Output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static async Task<AdbResult> RunAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(FindAdb())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var androidUserHome = Environment.GetEnvironmentVariable("ANDROID_USER_HOME");
        if (string.IsNullOrWhiteSpace(androidUserHome))
        {
            androidUserHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android");
        }
        Directory.CreateDirectory(androidUserHome);
        startInfo.Environment["ANDROID_USER_HOME"] = androidUserHome;
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("无法启动 ADB。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("ADB 操作超时；请确认手机和电脑在同一网络，并检查无线调试状态。");
        }

        return new AdbResult(process.ExitCode, await stdout, await stderr);
    }

    private static string FindAdb()
    {
        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory.Trim('"'), executable);
            if (File.Exists(candidate)) return candidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? "", "platform-tools", executable),
            Path.Combine(Environment.GetEnvironmentVariable("ANDROID_HOME") ?? "", "platform-tools", executable),
            Path.Combine(localAppData, "Android", "Sdk", "platform-tools", executable),
            @"C:\adb\platform-tools\adb.exe",
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("未找到 adb.exe。请安装 Android SDK Platform-Tools 并将其加入 PATH。");
    }

    private static string Endpoint(string host, string portText)
    {
        host = host.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown ||
            !int.TryParse(portText.Trim(), out var port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("请输入有效的手机 IP/主机名和 1–65535 端口。");
        }

        var formattedHost = IPAddress.TryParse(host, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;
        return $"{formattedHost}:{port}";
    }

    private static void ValidateCode(string code)
    {
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException("首次配对需要输入手机显示的 6 位数字配对码。");
        }
    }

    private static void EnsureSuccess(AdbResult result)
    {
        if (result.ExitCode == 0) return;
        var detail = string.Join(Environment.NewLine, new[] { result.Output, result.Error }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? "ADB 操作失败。" : detail);
    }

    private sealed record AdbResult(int ExitCode, string Output, string Error);
}
