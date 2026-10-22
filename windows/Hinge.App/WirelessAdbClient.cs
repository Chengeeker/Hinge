using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hinge.App;

internal static class WirelessAdbClient
{
    private const string HingeAdbServerPort = "5038";
    private const string DiagnosticsUri = "content://com.hinge.office.diagnostics/connection";
    private const string PairedSetting = "WirelessAdbPairingCompleted";
    private const string VerifiedIdentitySetting = "WirelessAdbVerifiedIdentity";
    private const string PairingGuidSetting = "WirelessAdbPairingGuid";
    private const string HingeDeviceIdSetting = "WirelessAdbHingeDeviceId";
    private static readonly SemaphoreSlim ConnectionGate = new(1, 1);
    private static readonly object RuntimeInfoLock = new();
    private static Task<WirelessAdbRuntimeInfo>? _runtimeInfoTask;

    public static async Task<WirelessAdbPairingResult> PairAsync(
        string host, string pairingPort, string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        var target = await ResolvePairingEndpointAsync(host, pairingPort, cancellationToken);
        var result = await RunAsync(cancellationToken, "pair", target.Endpoint, code);
        EnsureSuccess(result);
        if (!IsSuccessfulPairingResult(result.ExitCode, result.Output))
            throw new InvalidOperationException("ADB 未返回配对成功确认，未更新 Hinge 的配对记录。请检查手机配对码和 adb pair 输出后重试。");
        var pairingGuid = ParsePairingGuid(result.Output);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Hinge");
        var previousGuid = key?.GetValue(PairingGuidSetting) as string;
        if (!string.IsNullOrWhiteSpace(pairingGuid) && !string.Equals(previousGuid, pairingGuid, StringComparison.OrdinalIgnoreCase))
        {
            key?.DeleteValue(VerifiedIdentitySetting, throwOnMissingValue: false);
            key?.DeleteValue(HingeDeviceIdSetting, throwOnMissingValue: false);
        }
        key?.SetValue(PairedSetting, 1);
        if (!string.IsNullOrWhiteSpace(pairingGuid)) key?.SetValue(PairingGuidSetting, pairingGuid);
        return new WirelessAdbPairingResult(target.Host, target.Port, pairingGuid);
    }

    public static async Task<string> ConnectAsync(string host, string connectPort)
        => await ConnectAsync(host, connectPort, CancellationToken.None);

    public static async Task<string> ConnectAsync(string host, string connectPort, CancellationToken cancellationToken,
        IEnumerable<string>? currentHosts = null, string? hingeDeviceId = null, bool reconnectExisting = false)
    {
        await ConnectionGate.WaitAsync(cancellationToken);
        try { return await ConnectCoreAsync(host, connectPort, cancellationToken, currentHosts, hingeDeviceId, reconnectExisting); }
        finally { ConnectionGate.Release(); }
    }

    internal static bool IsWirelessTransport(string transport) =>
        TryParseEndpoint(transport, out _, out _) ||
        Regex.IsMatch(transport, @"^[A-Za-z0-9_-]+\._adb-tls-connect\._tcp\.?$", RegexOptions.CultureInvariant);

    internal static async Task ValidateScreenTransportAsync(string transport, CancellationToken cancellationToken)
    {
        // ADB's TLS service transport is authoritative even when OEMs hide the port property.
        if (IsTlsScreenTransport(transport, string.Empty)) return;
        var result = await RunAsync(cancellationToken, "-s", transport, "shell", "getprop", "service.adb.tls.port");
        EnsureSuccess(result);
        if (!IsTlsScreenTransport(transport, result.Output.Trim()))
            throw new InvalidOperationException("无法确认此 ADB 连接为安全无线调试。请保持手机无线调试及局域网 mDNS 可用后重试；不使用传统明文 adb tcpip 端口。");
    }

    internal static bool IsTlsScreenTransport(string transport, string tlsPort) =>
        Regex.IsMatch(transport, @"^[A-Za-z0-9_-]+\._adb-tls-connect\._tcp\.?$", RegexOptions.CultureInvariant) ||
        (int.TryParse(tlsPort, out var port) && port is > 0 and <= 65535 &&
         TryParseEndpoint(transport, out _, out var endpointPort) && endpointPort == port);

    internal static async Task ReconnectTransportAsync(string transport, CancellationToken cancellationToken,
        Func<CancellationToken, string[], Task<AdbResult>>? run = null)
    {
        // An empty adb disconnect target disconnects every phone on this server.
        if (!IsWirelessTransport(transport)) throw new ArgumentException("只能重新连接已验证的无线 ADB 设备。", nameof(transport));
        cancellationToken.ThrowIfCancellationRequested();
        run ??= RunAsync;
        EnsureSuccess(await run(cancellationToken, ["disconnect", transport]));
        cancellationToken.ThrowIfCancellationRequested();
        var result = await run(cancellationToken, ["connect", transport]);
        if (result.ExitCode != 0 || IsAdbConnectionFailure(result.Output, result.Error))
        {
            var failure = ClassifyConnectionFailure(false, false,
                IsAdbAuthorizationFailure(result.Output, result.Error), IsAdbOfflineResult(result.Output, result.Error),
                string.Empty, JoinDetails(result.Output, result.Error));
            throw new WirelessAdbException(failure.State, failure.Message);
        }
    }

    public static Task<WirelessAdbRuntimeInfo> GetRuntimeInfoAsync(CancellationToken cancellationToken)
    {
        Task<WirelessAdbRuntimeInfo> task;
        lock (RuntimeInfoLock) task = _runtimeInfoTask ??= ReadRuntimeInfoAsync();
        return task.WaitAsync(cancellationToken);
    }

    private static async Task<WirelessAdbRuntimeInfo> ReadRuntimeInfoAsync()
    {
        string ReadOutput(AdbResult result) => string.Join("\n", result.Output, result.Error).Trim();
        var version = await RunAsync("version");
        AdbResult serverStatus;
        AdbResult mdnsCheck;
        try { serverStatus = await RunAsync("server-status"); }
        catch { serverStatus = new AdbResult(-1, string.Empty, string.Empty); }
        try { mdnsCheck = await RunAsync("mdns", "check"); }
        catch { mdnsCheck = new AdbResult(-1, string.Empty, string.Empty); }

        var mdnsProto = string.Empty;
        var parsedVersion = ParsePlatformToolsVersion(ReadOutput(version));
        if (parsedVersion >= new Version(37, 0, 0) && mdnsCheck.ExitCode == 0)
        {
            try
            {
                var probe = await RunForDurationAsync(TimeSpan.FromSeconds(1), CancellationToken.None,
                    "mdns", "track-services", "--proto-text");
                mdnsProto = probe.Output;
            }
            catch { }
        }
        return ParseRuntimeInfo(ReadOutput(version), ReadOutput(serverStatus), ReadOutput(mdnsCheck), mdnsProto);
    }

    internal static WirelessAdbRuntimeInfo ParseRuntimeInfo(
        string versionOutput, string serverStatusOutput, string mdnsCheckOutput, string mdnsProtoOutput)
    {
        var version = ParsePlatformToolsVersion(versionOutput);
        var serverVersionMatch = Regex.Match(serverStatusOutput, @"\bversion\s*:\s*""?(?<version>\d+\.\d+\.\d+)""?", RegexOptions.IgnoreCase);
        var backendMatch = Regex.Match(serverStatusOutput, @"\bmdns_backend\s*:\s*(?<backend>[A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
        var backend = backendMatch.Success ? backendMatch.Groups["backend"].Value : mdnsCheckOutput.Trim();
        var supportsWifi20 = version >= new Version(37, 0, 0) &&
            Regex.IsMatch(mdnsProtoOutput, @"mdns_service_version\s*:\s*" + "\"" + @"2\.\d+" + "\"", RegexOptions.CultureInvariant);
        return new WirelessAdbRuntimeInfo(version.ToString(), serverVersionMatch.Groups["version"].Value,
            backend, supportsWifi20, mdnsCheckOutput.Length > 0);
    }

    private static Version ParsePlatformToolsVersion(string output)
    {
        var value = Regex.Match(output, @"(?:^|\r?\n)Version\s+(?<version>\d+\.\d+\.\d+)")
            .Groups["version"].Value;
        return Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private static async Task<string> ConnectCoreAsync(string host, string connectPort, CancellationToken cancellationToken,
        IEnumerable<string>? currentHosts, string? hingeDeviceId, bool reconnectExisting = false)
    {
        var endpoint = TryEndpoint(host, connectPort);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Hinge");
        var legacyIdentity = endpoint == null ? null : key?.GetValue(IdentitySetting(endpoint)) as string;
        var pairingGuid = key?.GetValue(PairingGuidSetting) as string;
        var verifiedIdentity = key?.GetValue(VerifiedIdentitySetting) as string;
        var identity = ResolveVerifiedIdentity(verifiedIdentity, pairingGuid, legacyIdentity);
        var serviceIdentity = identity ?? pairingGuid;
        if (key?.GetValue(PairedSetting) is int paired && paired != 0 && serviceIdentity == null)
            throw new WirelessAdbException(WirelessAdbConnectionState.AuthorizationInvalid,
                "此配对记录缺少可验证的 ADB 设备身份。请更新 Android SDK Platform-Tools 后重新配对一次；不会尝试连接未验证的手机。");
        var savedHingeDeviceId = key?.GetValue(HingeDeviceIdSetting) as string;
        if (!string.IsNullOrWhiteSpace(savedHingeDeviceId) &&
            !string.Equals(savedHingeDeviceId, hingeDeviceId, StringComparison.OrdinalIgnoreCase)) currentHosts = [];

        var devices = await RunAsync(cancellationToken, "devices", "-l");
        EnsureSuccess(devices);
        var deviceStates = ParseDeviceStates(devices.Output);
        if (deviceStates.Any(device => device.State == "unauthorized" &&
                ((endpoint != null && (device.Transport == endpoint || device.Transport.StartsWith(host + ":", StringComparison.OrdinalIgnoreCase))) ||
                 MatchesServiceIdentity(device.Transport, identity) || MatchesServiceIdentity(device.Transport, pairingGuid))))
            throw new WirelessAdbException(WirelessAdbConnectionState.AuthorizationInvalid,
                "ADB 授权已失效；请在手机无线调试页面撤销旧授权后重新配对。");

        foreach (var device in deviceStates.Where(device => device.State == "device")
                     .OrderByDescending(device => IsTlsScreenTransport(device.Transport, string.Empty)))
        {
            var transport = device.Transport;
            if (reconnectExisting && !IsWirelessTransport(transport)) continue;
            if (identity == null && !MatchesServiceIdentity(transport, pairingGuid) && transport != endpoint) continue;
            var actualIdentity = await ReadIdentityAsync(transport, cancellationToken);
            if (identity != null && actualIdentity != identity) continue;
            if (actualIdentity.Length == 0) continue;
            RememberIdentity(endpoint ?? transport, actualIdentity);
            await RememberAdvertisedEndpointAsync(transport, actualIdentity, pairingGuid, cancellationToken);
            if (reconnectExisting && IsWirelessTransport(transport))
            {
                await ReconnectTransportAsync(transport, cancellationToken);
                // Recheck device state and identity after the new connection, without refreshing twice.
                return await ConnectCoreAsync(host, connectPort, cancellationToken, currentHosts, hingeDeviceId);
            }
            return transport;
        }

        var candidates = new List<string>();
        var pairingGuidCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedServiceIdentity = serviceIdentity;
        var mdnsCheckOutput = string.Empty;
        var mdnsAvailable = false;
        var mdnsDiscoveryFailed = false;
        var mdnsFailureDetail = string.Empty;
        var connectFailureDetail = string.Empty;
        var connectTimedOut = false;
        var unauthorized = false;
        var targetOffline = deviceStates.Any(device => device.State == "offline" &&
            (MatchesServiceIdentity(device.Transport, identity) || MatchesServiceIdentity(device.Transport, pairingGuid) ||
             (endpoint != null && device.Transport == endpoint)));
        if (expectedServiceIdentity != null)
        {
            try
            {
                var mdnsCheck = await RunAsync(cancellationToken, "mdns", "check");
                mdnsCheckOutput = string.Join(" ", mdnsCheck.Output, mdnsCheck.Error).Trim();
                mdnsAvailable = mdnsCheck.ExitCode == 0 && mdnsCheckOutput.Contains("mdns daemon version", StringComparison.OrdinalIgnoreCase);
                if (!mdnsAvailable)
                {
                    mdnsDiscoveryFailed = true;
                    mdnsFailureDetail = mdnsCheckOutput;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                mdnsDiscoveryFailed = true;
                mdnsFailureDetail = exception.Message;
            }

            try
            {
                var services = await RunAsync(cancellationToken, "mdns", "services");
                if (services.ExitCode == 0)
                {
                    var discovered = ParseMdnsEndpoints(services.Output, expectedServiceIdentity);
                    candidates.AddRange(discovered);
                    if (identity == null && pairingGuid != null) pairingGuidCandidates.UnionWith(discovered);
                }
                else
                {
                    mdnsDiscoveryFailed = true;
                    mdnsFailureDetail = JoinDetails(mdnsFailureDetail, services.Output, services.Error);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                mdnsDiscoveryFailed = true;
                mdnsFailureDetail = JoinDetails(mdnsFailureDetail, exception.Message);
            }

            if (mdnsAvailable && candidates.Count == 0)
            {
                try
                {
                    var runtimeInfo = await GetRuntimeInfoAsync(cancellationToken);
                    if (Version.TryParse(runtimeInfo.PlatformToolsVersion, out var platformToolsVersion) &&
                        platformToolsVersion >= new Version(37, 0, 0))
                    {
                        var tracked = await RunForDurationAsync(TimeSpan.FromSeconds(1), cancellationToken,
                            "mdns", "track-services", "--proto-text");
                        var discovered = ParseMdnsEndpoints(tracked.Output, expectedServiceIdentity);
                        candidates.AddRange(discovered);
                        if (identity == null && pairingGuid != null) pairingGuidCandidates.UnionWith(discovered);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    mdnsDiscoveryFailed = true;
                    mdnsFailureDetail = JoinDetails(mdnsFailureDetail, exception.Message);
                }
            }

            if (candidates.Count == 0)
            {
                mdnsDiscoveryFailed = true;
                mdnsFailureDetail = JoinDetails(mdnsFailureDetail,
                    "没有发现匹配的 _adb-tls-connect._tcp 服务");
            }
            else
            {
                mdnsDiscoveryFailed = false;
                mdnsFailureDetail = string.Empty;
            }

            // Hinge addresses only locate a candidate; the ADB identity is still checked below.
            foreach (var currentHost in (identity == null ? [] : currentHosts ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
            {
                if (TryEndpoint(currentHost, connectPort) is { } candidate) candidates.Add(candidate);
            }
        }
        // Pairing and connection use different ports. Keep the saved connection endpoint as the final fallback.
        foreach (var candidate in BuildConnectionCandidates(candidates, endpoint))
        {
            AdbResult connectResult;
            try { connectResult = await RunAsync(cancellationToken, "connect", candidate); }
            catch (WirelessAdbException exception) when (exception.State == WirelessAdbConnectionState.ConnectionTimedOut)
            {
                connectTimedOut = true;
                connectFailureDetail = JoinDetails(connectFailureDetail, exception.Message);
                continue;
            }
            catch (InvalidOperationException exception)
            {
                connectFailureDetail = JoinDetails(connectFailureDetail, exception.Message);
                continue;
            }
            if (connectResult.ExitCode != 0 || IsAdbConnectionFailure(connectResult.Output, connectResult.Error))
                connectFailureDetail = JoinDetails(connectFailureDetail, connectResult.Output, connectResult.Error);
            unauthorized |= IsAdbAuthorizationFailure(connectResult.Output, connectResult.Error);
            targetOffline |= IsAdbOfflineResult(connectResult.Output, connectResult.Error);

            var refreshedDevices = await RunAsync(cancellationToken, "devices", "-l");
            EnsureSuccess(refreshedDevices);
            var matchedDevice = FindDeviceForCandidate(refreshedDevices.Output, candidate, identity, pairingGuid);
            if (matchedDevice is null) continue;
            if (matchedDevice.Value.State == "unauthorized")
            {
                unauthorized = true;
                continue;
            }
            if (matchedDevice.Value.State == "offline")
            {
                targetOffline = true;
                continue;
            }
            if (matchedDevice.Value.State != "device") continue;
            if (identity == null && pairingGuid != null && !pairingGuidCandidates.Contains(candidate) && candidate != endpoint) continue;
            var actualIdentity = await ReadIdentityAsync(matchedDevice.Value.Transport, cancellationToken);
            if (actualIdentity.Length == 0 || (identity != null && actualIdentity != identity)) continue;
            RememberIdentity(candidate, actualIdentity);
            RememberLastEndpoint(candidate);
            return matchedDevice.Value.Transport;
        }

        if (!NetworkInterface.GetIsNetworkAvailable())
            throw new WirelessAdbException(WirelessAdbConnectionState.NetworkUnavailable, "电脑当前没有可用网络，正在等待网络恢复。");
        var failure = ClassifyConnectionFailure(mdnsDiscoveryFailed,
            connectTimedOut, unauthorized, targetOffline, mdnsFailureDetail, connectFailureDetail);
        throw new WirelessAdbException(failure.State, failure.Message);
    }

    internal static string[] ParseConnectedTransports(string output) => ParseDeviceStates(output)
        .Where(device => device.State == "device").Select(device => device.Transport).Distinct().ToArray();

    internal static bool IsSuccessfulPairingResult(int exitCode, string output) => exitCode == 0 &&
        Regex.IsMatch(output, @"(?:^|\r?\n)\s*Successfully paired\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string[] BuildConnectionCandidates(
        IEnumerable<string> discoveredEndpoints, string? endpoint, IEnumerable<string>? trustedHostEndpoints = null)
    {
        var candidates = discoveredEndpoints.Concat(trustedHostEndpoints ?? []);
        if (!string.IsNullOrWhiteSpace(endpoint)) candidates = candidates.Append(endpoint);
        return candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool IsAdbConnectionFailure(string output, string error)
    {
        var text = string.Join(" ", output, error);
        return Regex.IsMatch(text, @"\b(failed|unable|cannot|can't|refused|unauthorized|offline|error)\b", RegexOptions.IgnoreCase);
    }

    internal static bool IsAdbAuthorizationFailure(string output, string error) => Regex.IsMatch(
        string.Join(" ", output, error), @"\b(unauthorized|failed to authenticate|authentication failed)\b", RegexOptions.IgnoreCase);

    internal static bool IsAdbOfflineResult(string output, string error) => Regex.IsMatch(
        string.Join(" ", output, error), @"\bdevice offline\b", RegexOptions.IgnoreCase);

    internal static (WirelessAdbConnectionState State, string Message) ClassifyConnectionFailure(
        bool discoveryFailed, bool timedOut, bool authorizationFailed, bool offline, string discoveryDetail, string connectDetail)
    {
        var details = new List<string>();
        if (discoveryFailed) details.Add("mDNS 发现失败" + FormatDetail(discoveryDetail));
        if (!string.IsNullOrWhiteSpace(connectDetail)) details.Add("ADB IP 连接反馈：" + FormatDetail(connectDetail));
        var suffix = details.Count == 0 ? string.Empty : " " + string.Join("；", details);
        if (authorizationFailed)
            return (WirelessAdbConnectionState.AuthorizationInvalid, "ADB 认证失败；请在手机无线调试页面确认此电脑仍在已配对设备列表中。" + suffix);
        if (offline)
            return (WirelessAdbConnectionState.DeviceOffline, "ADB 设备当前为 offline；请确认手机已联网且无线调试仍开启。" + suffix);
        if (timedOut)
            return (WirelessAdbConnectionState.ConnectionTimedOut, "ADB 连接超时；请确认手机当前连接端口和网络可达。" + suffix);
        if (discoveryFailed)
            return (WirelessAdbConnectionState.DiscoveryFailed, "未能通过 mDNS 发现已配对设备，已尝试手机 IP 与无线调试连接端口兜底，但 adb devices -l 未返回 device。" + suffix);
        return (WirelessAdbConnectionState.ConnectionFailed, "ADB 未建立连接；adb devices -l 未返回 device。" + suffix);
    }

    private static string JoinDetails(params string[] values) => string.Join("; ", values
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));

    private static string FormatDetail(string value)
    {
        var compact = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length == 0 ? string.Empty : "（" + compact[..Math.Min(compact.Length, 240)] + "）";
    }

    internal static (string Transport, string State)? FindDeviceForCandidate(
        string output, string candidate, string? identity, string? pairingGuid)
    {
        var devices = ParseDeviceStates(output);
        var exact = devices.FirstOrDefault(device =>
            string.Equals(device.Transport, candidate, StringComparison.OrdinalIgnoreCase));
        if (exact.Transport != null) return exact;
        var identified = devices.FirstOrDefault(device =>
            MatchesServiceIdentity(device.Transport, identity) || MatchesServiceIdentity(device.Transport, pairingGuid));
        return identified.Transport == null ? null : identified;
    }

    internal static (string Transport, string State)[] ParseDeviceStates(string output) => output
        .Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[0] != "List")
        .Select(parts => (parts[0], parts[1])).ToArray();

    internal static string[] ParseMdnsEndpoints(string output, string identity) => ParseMdnsEndpointEntries(output)
        .Where(service => service.ServiceType == "_adb-tls-connect._tcp" && MatchesServiceIdentity(service.Instance, identity))
        .Select(service => service.Endpoint).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string[] ParsePairingEndpoints(string output, string hostFilter = "", string portFilter = "") =>
        ParseMdnsEndpointEntries(output)
            .Where(service => service.ServiceType == "_adb-tls-pairing._tcp" &&
                (hostFilter.Length == 0 || TryParseEndpoint(service.Endpoint, out var host, out _) &&
                    string.Equals(host, hostFilter, StringComparison.OrdinalIgnoreCase)) &&
                (portFilter.Length == 0 || TryParseEndpoint(service.Endpoint, out _, out var port) &&
                    port.ToString() == portFilter))
            .Select(service => service.Endpoint).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static MdnsEndpoint[] ParseMdnsEndpointEntries(string output)
    {
        var entries = new List<MdnsEndpoint>();
        foreach (Match match in Regex.Matches(output, @"service\s*\{(?<body>[^{}]*)\}", RegexOptions.CultureInvariant))
        {
            var body = match.Groups["body"].Value;
            string Field(string name) => Regex.Match(body, $@"\b{name}\s*:\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant)
                .Groups["value"].Value;
            var portText = Regex.Match(body, @"\bport\s*:\s*(?<value>\d+)", RegexOptions.CultureInvariant).Groups["value"].Value;
            if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) continue;
            var host = Field("ipv4");
            if (host.Length == 0) host = Field("ipv6");
            if (!IPAddress.TryParse(host, out _)) continue;
            var service = Field("service").TrimEnd('.');
            var instance = Field("instance");
            if (service.Length == 0 || instance.Length == 0) continue;
            entries.Add(new MdnsEndpoint(instance, service, Endpoint(host, port.ToString()), Field("mdns_service_version")));
        }
        if (entries.Count > 0) return entries.ToArray();

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !TryParseEndpoint(parts[2], out var host, out var port)) continue;
            var service = parts[1].TrimEnd('.');
            if (service is not ("_adb-tls-connect._tcp" or "_adb-tls-pairing._tcp")) continue;
            entries.Add(new MdnsEndpoint(parts[0].TrimEnd('.'), service, Endpoint(host, port.ToString()), string.Empty));
        }
        return entries.ToArray();
    }

    internal static bool MatchesServiceIdentity(string service, string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity)) return false;
        service = service.Replace("._adb-tls-connect._tcp", "", StringComparison.Ordinal)
            .Replace("._adb-tls-pairing._tcp", "", StringComparison.Ordinal).TrimEnd('.');
        if (identity.StartsWith("serial:", StringComparison.Ordinal))
        {
            var prefix = "adb-" + identity[7..];
            return service.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                service.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
        }
        return service.Equals(identity, StringComparison.OrdinalIgnoreCase) ||
            service.StartsWith(identity + "-", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ResolveStableIdentity(string? stableIdentity, string? legacyEndpointIdentity) =>
        !string.IsNullOrWhiteSpace(stableIdentity) ? stableIdentity : legacyEndpointIdentity;

    internal static string? ResolveVerifiedIdentity(
        string? stableIdentity, string? pairingGuid, string? legacyEndpointIdentity) =>
        !string.IsNullOrWhiteSpace(stableIdentity) ? stableIdentity :
        pairingGuid == null ? legacyEndpointIdentity : null;

    internal static string? ParsePairingGuid(string output)
    {
        var match = Regex.Match(output, @"\[guid=(?<guid>[^\]\r\n]+)\]", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["guid"].Value.Trim() : null;
    }

    internal static bool SupportsAdbWifi20(string adbVersion, string mdnsProtoOutput)
    {
        var versionText = Regex.Match(adbVersion, @"(?:^|\r?\n)Version\s+(?<version>\d+\.\d+\.\d+)")
            .Groups["version"].Value;
        return Version.TryParse(versionText, out var version) && version >= new Version(37, 0, 0) &&
            Regex.IsMatch(mdnsProtoOutput, @"mdns_service_version\s*:\s*" + "\"" + @"2\.\d+" + "\"", RegexOptions.CultureInvariant);
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
            if (IsStableIdentityValue(identity))
                return property == "ro.serialno" ? "serial:" + identity : identity;
        }
        return string.Empty;
    }

    internal static bool IsStableIdentityValue(string identity) =>
        identity.Length is > 0 and <= 128 && !identity.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
        identity.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string IdentitySetting(string endpoint) => "WirelessAdbIdentity_" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    internal static bool IsPaired(string host, string port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Hinge");
        if (key?.GetValue(PairedSetting) is int paired && paired != 0) return true;
        if (!string.IsNullOrWhiteSpace(key?.GetValue(VerifiedIdentitySetting) as string) ||
            !string.IsNullOrWhiteSpace(key?.GetValue(PairingGuidSetting) as string)) return true;
        var endpoint = TryEndpoint(host, port);
        return endpoint != null && !string.IsNullOrWhiteSpace(key?.GetValue(IdentitySetting(endpoint)) as string);
    }

    internal static void SaveHingeDeviceId(string? deviceId)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Hinge");
        if (string.IsNullOrWhiteSpace(deviceId)) key?.DeleteValue(HingeDeviceIdSetting, throwOnMissingValue: false);
        else key?.SetValue(HingeDeviceIdSetting, deviceId);
    }

    internal static string? LoadHingeDeviceId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Hinge");
        return key?.GetValue(HingeDeviceIdSetting) as string;
    }

    private static void RememberIdentity(string endpoint, string identity)
    {
        if (identity.Length == 0) throw new InvalidOperationException("无法读取手机的无线调试身份，请确认无线调试已开启。");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Hinge");
        key?.SetValue(VerifiedIdentitySetting, identity);
        key?.SetValue(PairedSetting, 1);
        key?.SetValue(IdentitySetting(endpoint), identity);
    }

    private static async Task RememberAdvertisedEndpointAsync(
        string transport, string identity, string? pairingGuid, CancellationToken cancellationToken)
    {
        if (TryParseEndpoint(transport, out _, out _))
        {
            RememberLastEndpoint(transport);
            return;
        }
        try
        {
            var services = await RunAsync(cancellationToken, "mdns", "services");
            if (services.ExitCode != 0) return;
            var endpoint = ParseMdnsEndpointEntries(services.Output)
                .FirstOrDefault(service => service.ServiceType == "_adb-tls-connect._tcp" &&
                    (MatchesServiceIdentity(service.Instance, identity) || MatchesServiceIdentity(service.Instance, pairingGuid)))
                ?.Endpoint;
            if (endpoint != null) RememberLastEndpoint(endpoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { }
    }

    private static void RememberLastEndpoint(string endpoint)
    {
        if (!TryParseEndpoint(endpoint, out var host, out var port)) return;
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Hinge");
        key?.SetValue("WirelessAdbHost", host);
        key?.SetValue("WirelessAdbConnectPort", port.ToString());
    }

    private static async Task<(string Host, int Port, string Endpoint)> ResolvePairingEndpointAsync(
        string host, string pairingPort, CancellationToken cancellationToken)
    {
        host = host.Trim();
        pairingPort = pairingPort.Trim();
        if (TryEndpoint(host, pairingPort) is { } endpoint && IPAddress.TryParse(host, out _))
            return (host, int.Parse(pairingPort), endpoint);

        var services = await ReadMdnsServicesAsync(cancellationToken);
        var matches = ParsePairingEndpoints(services, host, pairingPort);
        if (matches.Length != 1)
        {
            var message = matches.Length == 0
                ? "没有发现可配对的 Android 无线调试服务；请确认手机已开启无线调试，并与电脑处于可互通网络。"
                : "发现多台可配对手机；请在高级设置中填写目标手机 IP，避免把配对码用于错误设备。";
            throw new InvalidOperationException(message);
        }
        if (!TryParseEndpoint(matches[0], out var resolvedHost, out var resolvedPort))
            throw new InvalidOperationException("无法解析手机公布的无线 ADB 配对服务地址。");
        return (resolvedHost, resolvedPort, matches[0]);
    }

    private static async Task<string> ReadMdnsServicesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(cancellationToken, "mdns", "services");
        EnsureSuccess(result);
        return result.Output;
    }

    private static string? TryEndpoint(string host, string portText)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(portText)) return null;
        try { return Endpoint(host, portText); }
        catch (ArgumentException) { return null; }
    }

    private static bool TryParseEndpoint(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!Uri.TryCreate("tcp://" + value, UriKind.Absolute, out var uri) || uri.Port is <= 0 or > 65535 ||
            !IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)) return false;
        host = uri.Host.Trim('[', ']');
        port = uri.Port;
        return true;
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
        return await PullDiagnosticsFromTransportAsync(endpoint);
    }

    public static async Task<string> PullDiagnosticsFromTransportAsync(string endpoint)
    {
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
            if (arguments.FirstOrDefault() == "connect")
                throw new WirelessAdbException(WirelessAdbConnectionState.ConnectionTimedOut,
                    "adb connect 在 8 秒内没有返回结果。");
            throw new InvalidOperationException("ADB 操作超时；请确认手机和电脑在同一网络，并检查无线调试状态。");
        }

        return new AdbResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task<AdbResult> RunForDurationAsync(
        TimeSpan duration, CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        if (!process.Start()) throw new InvalidOperationException("无法启动 ADB mDNS 服务监听。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        var exitTask = process.WaitForExitAsync();
        var delayTask = Task.Delay(duration, cancellationToken);
        var completed = await Task.WhenAny(exitTask, delayTask);
        if (completed == delayTask)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill();
                await process.WaitForExitAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
        }
        else
        {
            await exitTask;
        }
        return new AdbResult(process.ExitCode, await stdout, await stderr);
    }

    internal static ProcessStartInfo CreateStartInfo(params string[] arguments)
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
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(HingeAdbServerPort);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string FindAdb()
    {
        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "scrcpy", executable);
        if (File.Exists(bundled)) return bundled;
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

    internal sealed record AdbResult(int ExitCode, string Output, string Error);
}

internal sealed record WirelessAdbRuntimeInfo(
    string PlatformToolsVersion, string ServerVersion, string MdnsBackend, bool SupportsAdbWifi20, bool MdnsAvailable);

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
