using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Hinge.Core;

public sealed class DiscoveryConnectionRequestEventArgs : EventArgs
{
    public DiscoveryMessage Message { get; }
    public IPAddress RemoteAddress { get; }

    public DiscoveryConnectionRequestEventArgs(DiscoveryMessage message, IPAddress remoteAddress)
    {
        Message = message;
        RemoteAddress = remoteAddress;
    }
}

public class DiscoveryService : IDisposable
{
    private readonly DeviceIdentity _localIdentity;
    private readonly DeviceRegistry _registry;
    private readonly int _listenPort;
    private readonly Func<int>? _sessionPortProvider;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _broadcastTask;
    private Task? _subnetProbeTask;
    private Task? _pruneTask;
    private readonly ConcurrentDictionary<string, DateTime> _lastPeerReplies = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastConnectionRequests = new();

    public bool PairingRequired { get; set; }

    public DeviceRegistry Registry => _registry;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public bool IsListening { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler<DiscoveryConnectionRequestEventArgs>? ConnectionRequested;

    public DiscoveryService(
        DeviceIdentity localIdentity,
        DeviceRegistry registry,
        int listenPort = Constants.DiscoveryUdpPort,
        Func<int>? sessionPortProvider = null)
    {
        _localIdentity = localIdentity;
        _registry = registry;
        _listenPort = listenPort;
        _sessionPortProvider = sessionPortProvider;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _listenPort));
            _listener.EnableBroadcast = true;
            IsListening = true;
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = $"无法监听 UDP {_listenPort}：{exception.Message}";
            IsListening = false;
            // Keep a real receive socket. Its actual port is advertised in
            // discoveryPort so a peer can reply even when 52830 is occupied.
            _listener = new UdpClient(0) { EnableBroadcast = true };
        }

        _listenTask = Task.Run(() => ListenLoopAsync(token), token);
        _broadcastTask = Task.Run(() => BroadcastLoopAsync(token), token);
        _subnetProbeTask = Task.Run(() => SubnetProbeLoopAsync(token), token);
        _pruneTask = Task.Run(() => PruneLoopAsync(token), token);
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        try
        {
            _listener?.Close();
        }
        catch
        {
            // Ignore socket closure errors
        }
        _listener = null;
        IsListening = false;
        _lastPeerReplies.Clear();
        _lastConnectionRequests.Clear();
        _cts?.Dispose();
        _cts = null;
    }

    public async Task ProbeManualIpAsync(IPAddress targetIp, int port = Constants.DiscoveryUdpPort)
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        bool sent = false;

        var matchingIp = NetworkInterfaceHelper.FindMatchingLocalPhysicalAddress(targetIp);
        if (matchingIp != null)
        {
            try
            {
                using var sender = new UdpClient(new IPEndPoint(matchingIp, 0));
                await sender.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
                sent = true;
            }
            catch
            {
                // Fallback to other physical adapters or unbound sender below.
            }
        }

        if (!sent)
        {
            var endpoints = NetworkInterfaceHelper.GetPhysicalLanEndpoints();
            foreach (var ep in endpoints)
            {
                try
                {
                    using var sender = new UdpClient(new IPEndPoint(ep.Address, 0));
                    await sender.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
                    sent = true;
                }
                catch
                {
                }
            }
        }

        try
        {
            using var fallback = new UdpClient();
            await fallback.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
        }
        catch
        {
        }
    }

    public async Task RequestReverseConnectionAsync(
        IPAddress targetIp,
        int port = Constants.DiscoveryUdpPort,
        bool automaticReconnect = false)
    {
        var message = CreateDiscoveryMessage(
            connectionRequested: true,
            automaticReconnect: automaticReconnect);
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var matchingEp = NetworkInterfaceHelper.FindMatchingPhysicalLanEndpoint(targetIp);
        var endpoints = NetworkInterfaceHelper.GetPhysicalLanEndpoints();

        // Send 3 bursts spaced 120ms apart to overcome Wi-Fi power-save sleep or packet drop under VPN
        for (int burst = 0; burst < 3; burst++)
        {
            if (burst > 0)
            {
                await Task.Delay(120);
            }

            bool sent = false;
            if (matchingEp != null)
            {
                try
                {
                    using var sender = new UdpClient(new IPEndPoint(matchingEp.Address, 0)) { EnableBroadcast = true };
                    await sender.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
                    if (matchingEp.Broadcast != null && !IPAddress.IsLoopback(targetIp))
                    {
                        try
                        {
                            await sender.SendAsync(data, data.Length, new IPEndPoint(matchingEp.Broadcast, port));
                        }
                        catch { }
                    }
                    sent = true;
                }
                catch
                {
                }
            }

            if (!sent)
            {
                foreach (var ep in endpoints)
                {
                    try
                    {
                        using var sender = new UdpClient(new IPEndPoint(ep.Address, 0)) { EnableBroadcast = true };
                        await sender.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
                        sent = true;
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                using var fallback = new UdpClient();
                await fallback.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
            }
            catch
            {
            }
        }
    }

    public async Task ProbeLocalSubnetsAsync()
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var endpoints = NetworkInterfaceHelper.GetPhysicalLanEndpoints();
        foreach (var endpoint in endpoints)
        {
            byte[] local = endpoint.Address.GetAddressBytes();
            int interfaceBudget = 0;

            try
            {
                using var boundSender = new UdpClient(new IPEndPoint(endpoint.Address, 0));
                // Probing /24 hosts strictly from the physical LAN adapter to bypass VPN tunnels
                for (int host = 1; host < 255 && interfaceBudget < 512; host++)
                {
                    if (host == local[3]) continue;
                    var target = new IPAddress(new byte[] { local[0], local[1], local[2], (byte)host });
                    try
                    {
                        await boundSender.SendAsync(data, data.Length, new IPEndPoint(target, _listenPort));
                        interfaceBudget++;
                    }
                    catch
                    {
                        // Continue probing the remaining addresses.
                    }
                }
            }
            catch
            {
                // Move on to next physical interface if any
            }
        }
    }

    public async Task BroadcastOnceAsync()
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        bool sent = false;
        Exception? lastError = null;

        // 1. Explicitly bind and broadcast out of EVERY physical LAN/Wi-Fi interface.
        // This guarantees broadcast packets egress physical interfaces even if a VPN
        // route table has hijacked the default gateway (0.0.0.0/0).
        var physicalEndpoints = NetworkInterfaceHelper.GetPhysicalLanEndpoints();
        foreach (var ep in physicalEndpoints)
        {
            try
            {
                using var boundSender = new UdpClient(new IPEndPoint(ep.Address, 0)) { EnableBroadcast = true };
                try
                {
                    await boundSender.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, _listenPort));
                    sent = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                try
                {
                    await boundSender.SendAsync(data, data.Length, new IPEndPoint(ep.Broadcast, _listenPort));
                    sent = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        // 2. Fallback: also send through an unbound UdpClient for environments with
        // non-standard multi-homed or bridged networking.
        try
        {
            using var unboundSender = new UdpClient { EnableBroadcast = true };
            var targets = GetBroadcastAddresses();
            foreach (var target in targets)
            {
                try
                {
                    await unboundSender.SendAsync(data, data.Length, new IPEndPoint(target, _listenPort));
                    sent = true;
                }
                catch (Exception exception)
                {
                    lastError ??= exception;
                }
            }
        }
        catch (Exception ex)
        {
            lastError ??= ex;
        }

        if (!sent && lastError != null)
        {
            throw lastError;
        }
    }

    private static IReadOnlyList<IPAddress> GetBroadcastAddresses()
    {
        var targets = new List<IPAddress> { IPAddress.Broadcast };

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                NetworkInterfaceHelper.IsVirtualOrVpnInterface(networkInterface))
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    NetworkInterfaceHelper.IsReservedOrVirtualIp(unicast.Address) ||
                    unicast.IPv4Mask == null)
                {
                    continue;
                }

                var target = NetworkInterfaceHelper.CalculateBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                if (!targets.Any(existing => existing.Equals(target)))
                {
                    targets.Add(target);
                }
            }
        }

        return targets;
    }

    private DiscoveryMessage CreateDiscoveryMessage(
        bool connectionRequested = false,
        bool automaticReconnect = false)
    {
        return new DiscoveryMessage
        {
            Version = Constants.AppVersion,
            DeviceId = _localIdentity.DeviceId,
            Name = _localIdentity.Name,
            Platform = "windows",
            Port = GetSessionPort(),
            Capabilities = new List<string> { "file_transfer", "clipboard", "remote_control", "backup" },
            ProtocolVersion = Constants.ProtocolVersion,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ConnectionRequested = connectionRequested,
            AutomaticReconnect = automaticReconnect,
            PairingRequired = this.PairingRequired,
            DiscoveryPort = GetDiscoveryPort(),
            Addresses = NetworkInterfaceHelper.GetPhysicalCandidateAddresses()
        };
    }

    private int GetDiscoveryPort()
    {
        try
        {
            if (_listener?.Client.LocalEndPoint is IPEndPoint endpoint &&
                endpoint.Port > 0)
            {
                return endpoint.Port;
            }
        }
        catch
        {
            // Socket may be closing during a network transition.
        }
        return _listenPort;
    }

    private static int ValidDiscoveryPort(int port) =>
        port > 0 && port <= 65535 ? port : Constants.DiscoveryUdpPort;

    private int GetSessionPort()
    {
        try
        {
            int port = _sessionPortProvider?.Invoke() ?? 0;
            return port > 0 ? port : Constants.SessionTcpPort;
        }
        catch
        {
            return Constants.SessionTcpPort;
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var result = await _listener.ReceiveAsync(token);
                string json = Encoding.UTF8.GetString(result.Buffer);
                var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);

                if (message != null && !string.IsNullOrEmpty(message.DeviceId))
                {
                    // Ignore our own announcement
                    if (message.DeviceId != _localIdentity.DeviceId)
                    {
                        // A second Hinge process (for example an older installed
                        // copy that was not closed during an update) can still
                        // broadcast the machine's previous identity. The device
                        // ID alone cannot identify that as self, so discard
                        // Windows announcements whose source address belongs to
                        // one of this computer's network adapters.
                        if (IsLocalWindowsAnnouncement(message, result.RemoteEndPoint.Address))
                        {
                            continue;
                        }

                        string remoteIp = result.RemoteEndPoint.Address.ToString();
                        _registry.UpsertDevice(message, remoteIp);

                        DateTime now = DateTime.UtcNow;
                        if (message.ConnectionRequested)
                        {
                            DateTime lastRequest = _lastConnectionRequests.GetOrAdd(
                                message.DeviceId,
                                DateTime.MinValue);
                            if (now - lastRequest >= TimeSpan.FromSeconds(2))
                            {
                                _lastConnectionRequests[message.DeviceId] = now;
                                ConnectionRequested?.Invoke(
                                    this,
                                    new DiscoveryConnectionRequestEventArgs(
                                        message,
                                        result.RemoteEndPoint.Address));
                            }
                        }
                        DateTime lastReply = _lastPeerReplies.GetOrAdd(message.DeviceId, DateTime.MinValue);
                        if (now - lastReply > TimeSpan.FromSeconds(5))
                        {
                            _lastPeerReplies[message.DeviceId] = now;
                            _ = ProbeManualIpAsync(
                                result.RemoteEndPoint.Address,
                                ValidDiscoveryPort(message.DiscoveryPort));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested) break;
            }
        }
    }

    private static bool IsLocalWindowsAnnouncement(
        DiscoveryMessage message,
        IPAddress remoteAddress)
    {
        if (!string.Equals(message.Platform, "windows", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.IsLoopback(remoteAddress))
        {
            return false;
        }

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            try
            {
                if (networkInterface.GetIPProperties().UnicastAddresses.Any(unicast =>
                    unicast.Address.Equals(remoteAddress)))
                {
                    return true;
                }
            }
            catch
            {
                // An adapter can disappear while Wi-Fi switches networks.
            }
        }

        return false;
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync();
            }
            catch
            {
                // Network may temporarily be down
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PruneLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                _registry.PruneOffline(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SubnetProbeLoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try { await ProbeLocalSubnetsAsync(); }
            catch { }

            try { await Task.Delay(TimeSpan.FromSeconds(10), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
