using System.Diagnostics;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32;

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
        var identity = await ReadIdentityAsync(connectionEndpoint, CancellationToken.None);
        RememberIdentity(connectionEndpoint, identity);
    }

    public static async Task<string> ConnectAsync(string host, string connectPort)
        => await ConnectAsync(host, connectPort, CancellationToken.None);

    public static async Task<string> ConnectAsync(string host, string connectPort, CancellationToken cancellationToken,
        IEnumerable<string>? currentHosts = null)
    {
        var endpoint = Endpoint(host, connectPort);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Hinge");
        var identity = key?.GetValue(IdentitySetting(endpoint)) as string;
        var devices = await RunAsync(cancellationToken, "devices");
        EnsureSuccess(devices);
        var transports = ParseConnectedTransports(devices.Output);
        foreach (var transport in transports.Where(value => identity != null || value == endpoint))
        {
            var actualIdentity = await ReadIdentityAsync(transport, cancellationToken);
            if (identity != null && actualIdentity != identity) continue;
            RememberIdentity(endpoint, actualIdentity);
            return transport;
        }

        var candidates = new List<string>();
        if (identity != null)
        {
            var services = await RunAsync(cancellationToken, "mdns", "services");
            if (services.ExitCode == 0) candidates.AddRange(ParseMdnsEndpoints(services.Output, identity));
            // Reuse Hinge's current LAN addresses when a VPN hides multicast discovery.
            foreach (var currentHost in (currentHosts ?? []).Distinct().Take(4))
            {
                if (Uri.CheckHostName(currentHost) != UriHostNameType.Unknown)
                    candidates.Add(Endpoint(currentHost, connectPort));
            }
        }
        candidates.Add(endpoint);
        foreach (var candidate in candidates.Distinct())
        {
            AdbResult connection;
            try { connection = await RunAsync(cancellationToken, "connect", candidate); }
            catch (InvalidOperationException) { continue; }
            if (connection.ExitCode != 0) continue;
            var actualIdentity = await ReadIdentityAsync(candidate, cancellationToken);
            if (actualIdentity.Length == 0 || (identity != null && actualIdentity != identity)) continue;
            RememberIdentity(endpoint, actualIdentity);
            return candidate;
        }
        throw new InvalidOperationException("未找到原先配对的手机。请保持无线调试开启并允许 VPN 访问局域网；若 VPN 阻断 mDNS 且连接端口已变，请更新连接端口（无需重新配对）。");
    }

    internal static string[] ParseConnectedTransports(string output) => output
        .Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[1] == "device").Select(parts => parts[0]).Distinct().ToArray();

    internal static string[] ParseMdnsEndpoints(string output, string identity) => output
        .Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length == 3 && parts[1].TrimEnd('.') == "_adb-tls-connect._tcp" &&
            MatchesServiceIdentity(parts[0], identity) &&
            Uri.TryCreate("tcp://" + parts[2], UriKind.Absolute, out var uri) && uri.Port is > 0 and <= 65535 &&
            IPAddress.TryParse(uri.Host.Trim('[', ']'), out _))
        .Select(parts => parts[2]).Distinct().ToArray();

    private static bool MatchesServiceIdentity(string service, string identity)
    {
        service = service.Replace("._adb-tls-connect._tcp", "", StringComparison.Ordinal).TrimEnd('.');
        if (!identity.StartsWith("serial:", StringComparison.Ordinal)) return service == identity;
        var prefix = "adb-" + identity[7..];
        return service == prefix || service.StartsWith(prefix + "-", StringComparison.Ordinal);
    }

    private static async Task<string> ReadIdentityAsync(string transport, CancellationToken cancellationToken)
    {
        foreach (var property in new[] { "ro.serialno", "persist.adb.wifi.guid" })
        {
            AdbResult result;
            try { result = await RunAsync(cancellationToken, "-s", transport, "shell", "getprop", property); }
            catch (InvalidOperationException) { return string.Empty; }
            if (result.ExitCode != 0) return string.Empty;
            var identity = result.Output.Trim();
            if (identity.Length is > 0 and <= 128 && identity.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return property == "ro.serialno" ? "serial:" + identity : identity;
        }
        return string.Empty;
    }

    private static string IdentitySetting(string endpoint) => "WirelessAdbIdentity_" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    private static void RememberIdentity(string endpoint, string identity)
    {
        if (identity.Length == 0) throw new InvalidOperationException("无法读取手机的无线调试身份，请确认无线调试已开启。");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Hinge");
        key.SetValue(IdentitySetting(endpoint), identity);
    }

    public static async Task<WirelessAdbClipboardBridge> StartClipboardBridgeAsync(
        string endpoint,
        Action<string> onTextReceived,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(cancellationToken, "-s", endpoint, "shell", "pm", "path", "com.hinge.office");
        EnsureSuccess(result);
        var apkPath = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("package:", StringComparison.Ordinal))?[8..];
        if (string.IsNullOrWhiteSpace(apkPath) || !apkPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("手机上没有找到 Hinge Android 安装包。请先安装当前版本的 Hinge。 ");
        }

        return await WirelessAdbClipboardBridge.StartAsync(
            CreateStartInfo("-s", endpoint, "shell", "-T", $"CLASSPATH={apkPath}", "app_process", "/system/bin", "com.hinge.office.AdbClipboardBridge"),
            onTextReceived,
            cancellationToken);
    }

    public static async Task<string> PullDiagnosticsAsync(string host, string connectPort)
    {
        var endpoint = await ConnectAsync(host, connectPort);
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
        => await RunAsync(CancellationToken.None, arguments);

    private static async Task<AdbResult> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        if (!process.Start()) throw new InvalidOperationException("无法启动 ADB。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(arguments.FirstOrDefault() == "connect" ? 8 : 15));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("ADB 操作超时；请确认手机和电脑在同一网络，并检查无线调试状态。");
        }

        return new AdbResult(process.ExitCode, await stdout, await stderr);
    }

    private static ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(FindAdb())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
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
        return startInfo;
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

internal sealed class WirelessAdbClipboardBridge : IDisposable
{
    private const int MaxTextBytes = 1_048_576;
    private readonly Process _process;
    private readonly Action<string> _onTextReceived;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly CancellationToken _readToken;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _readTask;
    private readonly Task _errorTask;
    private string _startupFailure = string.Empty;
    private int _disposed;

    private WirelessAdbClipboardBridge(Process process, Action<string> onTextReceived)
    {
        _process = process;
        _onTextReceived = onTextReceived;
        _readToken = _readCancellation.Token;
        _errorTask = ReadErrorAsync();
        _readTask = ReadOutputAsync();
    }

    public Task Completion => _readTask;

    internal static async Task<WirelessAdbClipboardBridge> StartAsync(
        ProcessStartInfo startInfo,
        Action<string> onTextReceived,
        CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("无法启动 Android 剪贴板桥。");

        var bridge = new WirelessAdbClipboardBridge(process, onTextReceived);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await bridge._ready.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Android 剪贴板桥启动超时。请确认手机已安装 Hinge，并允许这台电脑使用无线调试。");
            }
            return bridge;
        }
        catch
        {
            bridge.Dispose();
            throw;
        }
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxTextBytes) return;
        var line = $"SET\t{Convert.ToBase64String(bytes)}";
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadOutputAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(_readToken) is { } line)
            {
                if (line == "READY")
                {
                    _ready.TrySetResult();
                    continue;
                }
                if (line.StartsWith("ERROR\t", StringComparison.Ordinal))
                {
                    var errorType = line.Split('\t').Last();
                    var message = line.StartsWith("ERROR\tclipboard_permission", StringComparison.Ordinal)
                        ? "Android 拒绝 ADB shell 读取剪贴板。该系统版本或厂商系统可能不允许此桥接方式。"
                        : "Android 剪贴板桥初始化失败。" +
                            (errorType.Length <= 80 && errorType.All(char.IsAsciiLetter) ? $"（{errorType}）" : "");
                    var exception = new InvalidOperationException(message);
                    _ready.TrySetException(exception);
                    throw exception;
                }
                if (!line.StartsWith("CLIP\t", StringComparison.Ordinal)) continue;

                var bytes = Convert.FromBase64String(line[5..]);
                if (bytes.Length > MaxTextBytes) continue;
                var text = new UTF8Encoding(false, true).GetString(bytes);
                try { _onTextReceived(text); }
                catch { }
            }
            await _process.WaitForExitAsync(_readToken);
            await _errorTask;
            throw new InvalidOperationException($"Android 剪贴板桥已退出（退出码 {_process.ExitCode}）。{_startupFailure}");
        }
        catch (OperationCanceledException) when (_readCancellation.IsCancellationRequested)
        {
            _ready.TrySetCanceled(_readToken);
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            throw;
        }
    }

    private async Task ReadErrorAsync()
    {
        // Classify startup failures without retaining stderr or clipboard contents.
        while (await _process.StandardError.ReadLineAsync(_readToken) is { } line)
        {
            if (line.Contains("ClassNotFoundException", StringComparison.Ordinal) ||
                line.Contains("Could not find class", StringComparison.Ordinal))
                _startupFailure = "手机安装包缺少剪贴板桥，请安装最新 Hinge APK。";
            else if (line.Contains("SecurityException", StringComparison.Ordinal))
                _startupFailure = "Android 拒绝 shell 剪贴板访问。";
            else if (line.Contains("device offline", StringComparison.Ordinal) ||
                line.Contains("device not found", StringComparison.Ordinal))
                _startupFailure = "无线 ADB 连接已断开。";
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _readCancellation.Cancel();
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch { }
        try { _process.StandardInput.Close(); }
        catch { }
        _ = _readTask.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _ = _errorTask.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _process.Dispose();
    }
}
