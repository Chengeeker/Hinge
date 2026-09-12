using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class DiscoveryTests
{
    [Fact]
    public void DeviceIdentityManager_Generates_And_Caches_Identity()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_id_{Guid.NewGuid()}.json");
        try
        {
            var manager = new DeviceIdentityManager(tempFile);
            var id1 = manager.GetOrCreateIdentity();

            Assert.False(string.IsNullOrWhiteSpace(id1.DeviceId));
            Assert.False(string.IsNullOrWhiteSpace(id1.Name));

            // Reload from same file
            var manager2 = new DeviceIdentityManager(tempFile);
            var id2 = manager2.GetOrCreateIdentity();

            Assert.Equal(id1.DeviceId, id2.DeviceId);
            Assert.Equal(id1.Name, id2.Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiscoveryMessage_Serialization_MatchesSchema()
    {
        var msg = new DiscoveryMessage
        {
            DeviceId = "dev-test-123",
            Name = "Galaxy S24",
            Platform = "android",
            Port = 52831,
            DiscoveryPort = 52930,
            Capabilities = new List<string> { "file_transfer", "clipboard" },
            ConnectionRequested = true,
            AutomaticReconnect = true
        };

        string json = JsonSerializer.Serialize(msg);
        var parsed = JsonSerializer.Deserialize<DiscoveryMessage>(json);

        Assert.NotNull(parsed);
        Assert.Equal("dev-test-123", parsed.DeviceId);
        Assert.Equal("Galaxy S24", parsed.Name);
        Assert.Equal("android", parsed.Platform);
        Assert.Equal(52831, parsed.Port);
        Assert.Equal(52930, parsed.DiscoveryPort);
        Assert.Equal(2, parsed.Capabilities.Count);
        Assert.True(parsed.ConnectionRequested);
        Assert.True(parsed.AutomaticReconnect);

        var legacyJson = JsonSerializer.Serialize(new
        {
            version = parsed.Version,
            deviceId = parsed.DeviceId,
            name = parsed.Name,
            platform = parsed.Platform,
            port = parsed.Port,
            discoveryPort = parsed.DiscoveryPort,
            capabilities = parsed.Capabilities,
            connectionRequested = parsed.ConnectionRequested
        });
        var legacyParsed = JsonSerializer.Deserialize<DiscoveryMessage>(legacyJson);
        Assert.NotNull(legacyParsed);
        Assert.False(legacyParsed!.AutomaticReconnect);
    }

    [Fact]
    public async Task DiscoveryService_Raises_ReverseConnectionRequest()
    {
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
        }

        var identity = new DeviceIdentity { DeviceId = "local-device", Name = "Local" };
        var registry = new DeviceRegistry();
        using var service = new DiscoveryService(identity, registry, port);
        var completion = new TaskCompletionSource<DiscoveryConnectionRequestEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionRequested += (_, args) => completion.TrySetResult(args);
        service.Start();

        var request = new DiscoveryMessage
        {
            DeviceId = "remote-device",
            Name = "Remote",
            Platform = "android",
            ConnectionRequested = true
        };
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
        using var sender = new UdpClient();
        await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));

        var received = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("remote-device", received.Message.DeviceId);
        Assert.Equal(IPAddress.Loopback, received.RemoteAddress);
    }

    [Fact]
    public void DeviceRegistry_Upsert_And_PruneOffline_Works()
    {
        var registry = new DeviceRegistry();
        bool eventFired = false;
        registry.DevicesChanged += (_, _) => eventFired = true;

        var msg = new DiscoveryMessage
        {
            DeviceId = "phone-001",
            Name = "Phone 1",
            Platform = "android",
            Capabilities = new List<string> { "file_transfer" }
        };

        registry.UpsertDevice(msg, "192.168.1.50");
        Assert.True(eventFired);

        var devices = registry.GetAllDevices();
        Assert.Single(devices);
        Assert.Equal(ConnectionState.Discovered, devices[0].ConnectionState);
        Assert.Contains("192.168.1.50", devices[0].NetworkAddresses);

        // Prune with zero timeout should mark device Disconnected
        eventFired = false;
        registry.PruneOffline(TimeSpan.Zero);
        Assert.True(eventFired);

        var updated = registry.GetAllDevices();
        Assert.Equal(ConnectionState.Disconnected, updated[0].ConnectionState);
    }

    [Fact]
    public void DeviceRegistry_MostRecentlySeenAddress_IsTriedFirst()
    {
        var registry = new DeviceRegistry();
        var message = new DiscoveryMessage
        {
            DeviceId = "phone-moving",
            Name = "Phone",
            Platform = "android"
        };

        registry.UpsertDevice(message, "192.168.1.20");
        registry.UpsertDevice(message, "192.168.3.34");
        registry.UpsertDevice(message, "192.168.1.20");

        Assert.True(registry.TryGetDevice(message.DeviceId, out var device));
        Assert.Equal("192.168.1.20", device!.NetworkAddresses[0]);
        Assert.Equal(2, device.NetworkAddresses.Count);
    }

    [Fact]
    public void DeviceRegistry_Collapses_Reinstalled_Offline_Identity()
    {
        var registry = new DeviceRegistry();
        var oldMessage = new DiscoveryMessage
        {
            DeviceId = "phone-old-id",
            Name = "vivo X200 Pro mini",
            Manufacturer = "vivo",
            Model = "V2419A",
            Platform = "android"
        };
        registry.UpsertDevice(oldMessage, "192.168.3.27");
        registry.PruneOffline(TimeSpan.Zero);

        var newMessage = oldMessage;
        newMessage.DeviceId = "phone-new-id";
        registry.UpsertDevice(newMessage, "192.168.3.27");

        var devices = registry.GetAllDevices();
        Assert.Single(devices);
        Assert.Equal("phone-new-id", devices[0].DeviceId);
        Assert.Equal(ConnectionState.Discovered, devices[0].ConnectionState);
        Assert.Contains("192.168.3.27", devices[0].NetworkAddresses);
    }

    [Fact]
    public void DeviceRegistry_Reconciles_Duplicate_Identities_After_They_Are_Discovered()
    {
        var registry = new DeviceRegistry();
        var first = new DiscoveryMessage
        {
            DeviceId = "phone-first-id",
            Name = "vivo X200 Pro mini",
            Manufacturer = "vivo",
            Model = "V2419A",
            Platform = "android"
        };
        var second = new DiscoveryMessage
        {
            DeviceId = "phone-second-id",
            Name = first.Name,
            Manufacturer = first.Manufacturer,
            Model = first.Model,
            Platform = first.Platform
        };

        // Neither row is offline yet. This is the timing window the old
        // implementation missed; the second packet must still reconcile them.
        registry.UpsertDevice(first, "192.168.3.27");
        registry.UpsertDevice(second, "192.168.3.27");

        var devices = registry.GetAllDevices();
        Assert.Single(devices);
        Assert.Equal("phone-second-id", devices[0].DeviceId);
    }

    [Fact]
    public void DeviceRegistry_Preserves_Connected_Record_When_Reconciling()
    {
        var registry = new DeviceRegistry();
        var connected = new DiscoveryMessage
        {
            DeviceId = "phone-connected-id",
            Name = "vivo X200 Pro mini",
            Manufacturer = "vivo",
            Model = "V2419A",
            Platform = "android"
        };
        registry.UpsertDevice(connected, "192.168.3.27");
        Assert.True(registry.TryGetDevice(connected.DeviceId, out var connectedDevice));
        connectedDevice!.ConnectionState = ConnectionState.Connected;

        var duplicate = new DiscoveryMessage
        {
            DeviceId = "phone-duplicate-id",
            Name = connected.Name,
            Manufacturer = connected.Manufacturer,
            Model = connected.Model,
            Platform = connected.Platform
        };
        registry.UpsertDevice(duplicate, "192.168.3.27");

        var devices = registry.GetAllDevices();
        Assert.Single(devices);
        Assert.Equal("phone-connected-id", devices[0].DeviceId);
        Assert.Equal(ConnectionState.Connected, devices[0].ConnectionState);
    }

    [Fact]
    public void DiscoveryService_Lifecycle_StartsAndStops()
    {
        var identity = new DeviceIdentity { DeviceId = "test-local", Name = "LocalTest" };
        var registry = new DeviceRegistry();
        using var service = new DiscoveryService(identity, registry, 52840);

        Assert.False(service.IsRunning);
        service.Start();
        Assert.True(service.IsRunning);
        service.Stop();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void DiscoveryMessage_Parses_AndroidSampleJson_Successfully()
    {
        string json = """
        {
          "version": "1.0",
          "deviceId": "c85d7b5f-519b-4e12-8e10-3b0222a7f05a",
          "name": "Pixel 9 Pro",
          "platform": "android",
          "port": 52831,
          "capabilities": [
            "file_transfer",
            "clipboard",
            "remote_control",
            "screen_mirror"
          ],
          "protocolVersion": "0.1",
          "timestamp": 1756992000
        }
        """;

        var parsed = JsonSerializer.Deserialize<DiscoveryMessage>(json);
        Assert.NotNull(parsed);
        Assert.Equal("c85d7b5f-519b-4e12-8e10-3b0222a7f05a", parsed.DeviceId);
        Assert.Equal("Pixel 9 Pro", parsed.Name);
        Assert.Equal("android", parsed.Platform);
        Assert.Equal(4, parsed.Capabilities.Count);
        Assert.Contains("file_transfer", parsed.Capabilities);
    }
}
