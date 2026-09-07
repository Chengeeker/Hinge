using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Hinge.Core;

public class DiscoveryService : IDisposable
{
    private readonly DeviceIdentity _localIdentity;
    private readonly DeviceRegistry _registry;
    private readonly int _listenPort;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _broadcastTask;
    private Task? _subnetProbeTask;
    private Task? _pruneTask;
    private readonly ConcurrentDictionary<string, DateTime> _lastPeerReplies = new();

    public DeviceRegistry Registry => _registry;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public bool IsListening { get; private set; }
    public string? LastError { get; private set; }

    public DiscoveryService(DeviceIdentity localIdentity, DeviceRegistry registry, int listenPort = Constants.DiscoveryUdpPort)
    {
        _localIdentity = localIdentity;
        _registry = registry;
        _listenPort = listenPort;
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
            // Keep a sender alive so manual probing can still be used.
            _listener = new UdpClient { EnableBroadcast = true };
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
        _cts?.Dispose();
        _cts = null;
    }

    public async Task ProbeManualIpAsync(IPAddress targetIp, int port = Constants.DiscoveryUdpPort)
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        using var sender = new UdpClient();
        await sender.SendAsync(data, data.Length, new IPEndPoint(targetIp, port));
    }

    public async Task ProbeLocalSubnetsAsync()
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        using var sender = new UdpClient();
        int sent = 0;

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try { properties = networkInterface.GetIPProperties(); }
            catch { continue; }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] local = unicast.Address.GetAddressBytes();

                // Home and enterprise Wi-Fi normally use /24 or smaller host ranges.
                // Limiting the fallback to this /24 avoids broad network scans.
                for (int host = 1; host < 255 && sent < 512; host++)
                {
                    if (host == local[3]) continue;
                    var target = new IPAddress(new byte[] { local[0], local[1], local[2], (byte)host });
                    try
                    {
                        await sender.SendAsync(data, data.Length, new IPEndPoint(target, _listenPort));
                        sent++;
                    }
                    catch
                    {
                        // Continue probing the remaining addresses.
                    }
                }
            }
        }
    }

    public async Task BroadcastOnceAsync()
    {
        var message = CreateDiscoveryMessage();
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        using var sender = new UdpClient { EnableBroadcast = true };

        // The limited broadcast is not forwarded by some Wi-Fi access points.
        // Also send to each active adapter's directed broadcast address so a
        // phone and a PC on the same subnet can still discover one another.
        var targets = GetBroadcastAddresses();
        Exception? lastError = null;
        bool sent = false;
        foreach (var target in targets)
        {
            try
            {
                await sender.SendAsync(data, data.Length, new IPEndPoint(target, _listenPort));
                sent = true;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
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
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
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
                    unicast.IPv4Mask == null)
                {
                    continue;
                }

                byte[] address = unicast.Address.GetAddressBytes();
                byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (int index = 0; index < broadcast.Length; index++)
                {
                    broadcast[index] = (byte)(address[index] | (byte)~mask[index]);
                }

                var target = new IPAddress(broadcast);
                if (!targets.Any(existing => existing.Equals(target)))
                {
                    targets.Add(target);
                }
            }
        }

        return targets;
    }

    private DiscoveryMessage CreateDiscoveryMessage()
    {
        return new DiscoveryMessage
        {
            Version = Constants.AppVersion,
            DeviceId = _localIdentity.DeviceId,
            Name = _localIdentity.Name,
            Platform = "windows",
            Port = Constants.SessionTcpPort,
            Capabilities = new List<string> { "file_transfer", "clipboard", "remote_control", "backup" },
            ProtocolVersion = Constants.ProtocolVersion,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
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
                        string remoteIp = result.RemoteEndPoint.Address.ToString();
                        _registry.UpsertDevice(message, remoteIp);

                        DateTime now = DateTime.UtcNow;
                        DateTime lastReply = _lastPeerReplies.GetOrAdd(message.DeviceId, DateTime.MinValue);
                        if (now - lastReply > TimeSpan.FromSeconds(5))
                        {
                            _lastPeerReplies[message.DeviceId] = now;
                            _ = ProbeManualIpAsync(result.RemoteEndPoint.Address);
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
