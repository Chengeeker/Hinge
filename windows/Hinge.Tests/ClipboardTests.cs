using System.Text.Json;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class MockClipboardAdapter : IClipboardAdapter
{
    private string? _currentText;
    private bool _isMonitoring;

    public bool IsMonitoring => _isMonitoring;
    public event EventHandler<string>? TextChanged;

    public Task<string?> GetTextAsync() => Task.FromResult(_currentText);

    public Task SetTextAsync(string text)
    {
        _currentText = text;
        return Task.CompletedTask;
    }

    public void TriggerLocalChange(string text)
    {
        _currentText = text;
        TextChanged?.Invoke(this, text);
    }

    public void StartMonitoring() => _isMonitoring = true;
    public void StopMonitoring() => _isMonitoring = false;
    public void Dispose() => StopMonitoring();
}

public class ClipboardTests
{
    [Fact]
    public void LruRingCache_EvictsOldestWhenExceeding100()
    {
        var cache = new LruRingCache(100);

        for (int i = 1; i <= 100; i++)
        {
            Assert.True(cache.Add($"event-{i}"));
        }

        Assert.Equal(100, cache.Count);
        Assert.True(cache.Contains("event-1"));
        Assert.True(cache.Contains("event-100"));

        // Add 101th item -> should evict event-1
        Assert.True(cache.Add("event-101"));
        Assert.Equal(100, cache.Count);
        Assert.False(cache.Contains("event-1"));
        Assert.True(cache.Contains("event-2"));
        Assert.True(cache.Contains("event-101"));

        // Adding duplicate should return false and not grow count
        Assert.False(cache.Add("event-2"));
        Assert.Equal(100, cache.Count);
    }

    [Fact]
    public void ClipboardEventMessage_SerializationRoundtrip()
    {
        var msg = new ClipboardEventMessage
        {
            EventId = Guid.NewGuid().ToString(),
            OriginDeviceId = "device-windows-123",
            Timestamp = 1788500000000,
            ContentType = "text/plain",
            Content = "Hello from clipboard sync!"
        };

        var json = JsonSerializer.Serialize(msg);
        var parsed = JsonSerializer.Deserialize<ClipboardEventMessage>(json);

        Assert.NotNull(parsed);
        Assert.Equal(msg.EventId, parsed.EventId);
        Assert.Equal(msg.OriginDeviceId, parsed.OriginDeviceId);
        Assert.Equal(msg.Timestamp, parsed.Timestamp);
        Assert.Equal(msg.ContentType, parsed.ContentType);
        Assert.Equal(msg.Content, parsed.Content);
    }

    [Fact]
    public async Task ClipboardManager_DropsSelfOriginatedEvent()
    {
        var localId = new DeviceIdentity { DeviceId = "local-device", Name = "Local" };
        var adapter = new MockClipboardAdapter();
        using var manager = new ClipboardManager(localId, adapter);

        var selfMsg = new ClipboardEventMessage
        {
            EventId = "event-self",
            OriginDeviceId = "local-device", // Self
            Content = "Loopback text"
        };

        var frame = new ProtocolFrame { Type = MessageType.ClipboardEvent, Payload = JsonSerializer.SerializeToUtf8Bytes(selfMsg) };
        var handled = await manager.HandleIncomingFrameAsync(null!, frame);

        Assert.False(handled);
        Assert.Null(await adapter.GetTextAsync());
    }

    [Fact]
    public async Task ClipboardManager_DropsDuplicateEventId()
    {
        var localId = new DeviceIdentity { DeviceId = "local-device", Name = "Local" };
        var adapter = new MockClipboardAdapter();
        using var manager = new ClipboardManager(localId, adapter);

        var msg = new ClipboardEventMessage
        {
            EventId = "event-duplicate",
            OriginDeviceId = "remote-device",
            Content = "Sync content"
        };

        var frame = new ProtocolFrame { Type = MessageType.ClipboardEvent, Payload = JsonSerializer.SerializeToUtf8Bytes(msg) };

        // First receive
        var handled1 = await manager.HandleIncomingFrameAsync(null!, frame);
        Assert.True(handled1);
        Assert.Equal("Sync content", await adapter.GetTextAsync());

        // Second receive (duplicate eventId)
        var handled2 = await manager.HandleIncomingFrameAsync(null!, frame);
        Assert.False(handled2);
    }

    [Theory]
    [InlineData("https://github.com/Chengeeker/Hinge", true)]
    [InlineData("http://192.168.1.100:8080/file", true)]
    [InlineData("just plain text", false)]
    [InlineData("C:\\Users\\file.txt", false)]
    [InlineData("", false)]
    public void ClipboardManager_IsUrl(string input, bool expected)
    {
        Assert.Equal(expected, ClipboardManager.IsUrl(input));
    }

    [Fact]
    public async Task ClipboardManager_RaisesUrlHandoffEvent()
    {
        var localId = new DeviceIdentity { DeviceId = "local-device", Name = "Local" };
        var adapter = new MockClipboardAdapter();
        using var manager = new ClipboardManager(localId, adapter);

        string? receivedUrl = null;
        manager.UrlHandoffReceived += (_, url) => receivedUrl = url;

        var msg = new ClipboardEventMessage
        {
            EventId = "event-url",
            OriginDeviceId = "remote-device",
            Content = "https://flutter.dev"
        };

        var frame = new ProtocolFrame { Type = MessageType.ClipboardEvent, Payload = JsonSerializer.SerializeToUtf8Bytes(msg) };
        await manager.HandleIncomingFrameAsync(null!, frame);

        Assert.Equal("https://flutter.dev", receivedUrl);
    }
}
