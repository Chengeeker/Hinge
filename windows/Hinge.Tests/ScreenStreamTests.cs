using System.Text;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class MockScreenRenderer : IScreenRenderer
{
    public bool IsInitialized { get; private set; }
    public int CurrentWidth { get; private set; }
    public int CurrentHeight { get; private set; }
    public int RenderCount { get; private set; }
    public int CloseCount { get; private set; }
    public List<byte[]> RenderedFrames { get; } = new();

    public void Initialize(ScreenStreamConfig config)
    {
        CurrentWidth = config.Width;
        CurrentHeight = config.Height;
        IsInitialized = true;
    }

    public void RenderFrame(ScreenStreamPacket packet, ReadOnlySpan<byte> naluData)
    {
        RenderCount++;
        RenderedFrames.Add(naluData.ToArray());
    }

    public void Close()
    {
        IsInitialized = false;
        CloseCount++;
    }

    public void Dispose()
    {
        Close();
    }
}

public class ScreenStreamTests
{
    [Fact]
    public void ScreenStreamPacket_KeyFrame_SerializationRoundtrip()
    {
        byte[] naluPayload = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00 };
        var packet = new ScreenStreamPacket
        {
            Subtype = ScreenStreamSubtype.KeyFrame,
            Flags = ScreenStreamFlags.KeyFrame | ScreenStreamFlags.EndOfFrame,
            StreamId = 7,
            SequenceNumber = 105,
            PayloadLength = (uint)naluPayload.Length,
            TimestampUs = 9876543210UL,
            Payload = naluPayload
        };

        byte[] serialized = packet.Serialize();
        Assert.Equal(20 + naluPayload.Length, serialized.Length);

        bool success = ScreenStreamPacket.TryParse(serialized, out var parsed);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(ScreenStreamSubtype.KeyFrame, parsed.Subtype);
        Assert.Equal(ScreenStreamFlags.KeyFrame | ScreenStreamFlags.EndOfFrame, parsed.Flags);
        Assert.Equal((ushort)7, parsed.StreamId);
        Assert.Equal(105u, parsed.SequenceNumber);
        Assert.Equal((uint)naluPayload.Length, parsed.PayloadLength);
        Assert.Equal(9876543210UL, parsed.TimestampUs);
        Assert.Equal(naluPayload, parsed.Payload);
    }

    [Fact]
    public void ScreenStreamPacket_TryParse_RejectsTruncatedHeader()
    {
        byte[] truncated = new byte[19]; // less than 20 bytes
        bool success = ScreenStreamPacket.TryParse(truncated, out var packet);
        Assert.False(success);
        Assert.Null(packet);
    }

    [Fact]
    public void ScreenStreamPacket_TryParse_RejectsTruncatedPayload()
    {
        var packet = new ScreenStreamPacket
        {
            Subtype = ScreenStreamSubtype.InterFrame,
            Flags = ScreenStreamFlags.None,
            StreamId = 1,
            SequenceNumber = 1,
            TimestampUs = 1000,
            Payload = new byte[20]
        };

        byte[] serialized = packet.Serialize();
        // Pass only 10 bytes of payload instead of the 20 bytes written in the header
        bool success = ScreenStreamPacket.TryParse(serialized.AsSpan(0, 20 + 10), out var parsed);
        Assert.False(success);
        Assert.Null(parsed);
    }

    [Fact]
    public void ScreenStreamControlMessage_Roundtrip()
    {
        var msg = new ScreenStreamControlMessage
        {
            Action = "start",
            StreamId = 2,
            Width = 1920,
            Height = 1080,
            Fps = 60,
            Bitrate = 5_000_000,
            Codec = "H264"
        };

        string json = msg.ToJson();
        var parsed = ScreenStreamControlMessage.FromJson(json);

        Assert.NotNull(parsed);
        Assert.Equal("start", parsed.Action);
        Assert.Equal((ushort)2, parsed.StreamId);
        Assert.Equal(1920, parsed.Width);
        Assert.Equal(1080, parsed.Height);
        Assert.Equal(60, parsed.Fps);
        Assert.Equal(5_000_000, parsed.Bitrate);
        Assert.Equal("H264", parsed.Codec);
    }

    [Fact]
    public async Task ScreenStreamReceiver_HandlesStartStopLifecycle()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trust_stream_{Guid.NewGuid():N}.json");
        try
        {
            var trustStore = new TrustStore(tempFile);
            string trustedDevId = "dev-trusted-sender";
            trustStore.AddOrUpdate(new TrustedDevice { DeviceId = trustedDevId, Name = "Sender Phone" });

            using var renderer = new MockScreenRenderer();
            using var receiver = new ScreenStreamReceiver(renderer, trustStore);

            Assert.Equal(ScreenStreamState.Inactive, receiver.State);

            // Send Start request
            var startMsg = new ScreenStreamControlMessage
            {
                Action = "start",
                StreamId = 1,
                Width = 1280,
                Height = 720,
                Fps = 30,
                Bitrate = 2_500_000
            };
            byte[] startBytes = Encoding.UTF8.GetBytes(startMsg.ToJson());
            var startPacket = new ScreenStreamPacket
            {
                Subtype = ScreenStreamSubtype.ControlRequest,
                StreamId = 1,
                PayloadLength = (uint)startBytes.Length,
                Payload = startBytes
            };

            var frame = new ProtocolFrame
            {
                Type = MessageType.ScreenStream,
                Payload = startPacket.Serialize()
            };

            // Pass null connection (conn.RemoteDeviceId empty/null allows execution or test bypass)
            bool handled = await receiver.HandleIncomingFrameAsync(null!, frame);
            Assert.True(handled);
            Assert.Equal(ScreenStreamState.Streaming, receiver.State);
            Assert.True(renderer.IsInitialized);
            Assert.Equal(1280, renderer.CurrentWidth);
            Assert.Equal(720, renderer.CurrentHeight);

            // Send a video frame
            byte[] videoFrameData = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0x01, 0x02 };
            var videoPacket = new ScreenStreamPacket
            {
                Subtype = ScreenStreamSubtype.KeyFrame,
                Flags = ScreenStreamFlags.KeyFrame | ScreenStreamFlags.EndOfFrame,
                StreamId = 1,
                SequenceNumber = 0,
                PayloadLength = (uint)videoFrameData.Length,
                Payload = videoFrameData
            };
            var videoProtoFrame = new ProtocolFrame
            {
                Type = MessageType.ScreenStream,
                Payload = videoPacket.Serialize()
            };

            bool videoHandled = await receiver.HandleIncomingFrameAsync(null!, videoProtoFrame);
            Assert.True(videoHandled);
            Assert.Equal(1, renderer.RenderCount);
            Assert.Equal(1L, receiver.Statistics.TotalFramesReceived);

            // Send Stop request
            var stopMsg = new ScreenStreamControlMessage
            {
                Action = "stop",
                StreamId = 1,
                Reason = "user_done"
            };
            byte[] stopBytes = Encoding.UTF8.GetBytes(stopMsg.ToJson());
            var stopPacket = new ScreenStreamPacket
            {
                Subtype = ScreenStreamSubtype.ControlRequest,
                StreamId = 1,
                PayloadLength = (uint)stopBytes.Length,
                Payload = stopBytes
            };
            var stopProtoFrame = new ProtocolFrame
            {
                Type = MessageType.ScreenStream,
                Payload = stopPacket.Serialize()
            };

            bool stopHandled = await receiver.HandleIncomingFrameAsync(null!, stopProtoFrame);
            Assert.True(stopHandled);
            Assert.Equal(ScreenStreamState.Stopped, receiver.State);
            Assert.False(renderer.IsInitialized);
            Assert.Equal(1, renderer.CloseCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ScreenStreamReceiver_DetectsDroppedPackets()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trust_stream_{Guid.NewGuid():N}.json");
        try
        {
            var trustStore = new TrustStore(tempFile);
            using var renderer = new MockScreenRenderer();
            using var receiver = new ScreenStreamReceiver(renderer, trustStore);

            // Start stream
            var startMsg = new ScreenStreamControlMessage { Action = "start", StreamId = 1, Width = 640, Height = 480 };
            byte[] startBytes = Encoding.UTF8.GetBytes(startMsg.ToJson());
            var startPacket = new ScreenStreamPacket
            {
                Subtype = ScreenStreamSubtype.ControlRequest,
                PayloadLength = (uint)startBytes.Length,
                Payload = startBytes
            };
            await receiver.HandleIncomingFrameAsync(null!, new ProtocolFrame { Type = MessageType.ScreenStream, Payload = startPacket.Serialize() });

            int droppedEventsSum = 0;
            receiver.PacketsDropped += (s, count) => droppedEventsSum += count;

            // Send packet 0
            var p0 = new ScreenStreamPacket { Subtype = ScreenStreamSubtype.InterFrame, StreamId = 1, SequenceNumber = 0, PayloadLength = 4, Payload = new byte[] { 1, 2, 3, 4 } };
            await receiver.HandleIncomingFrameAsync(null!, new ProtocolFrame { Type = MessageType.ScreenStream, Payload = p0.Serialize() });

            // Send packet 4 (packets 1, 2, 3 skipped)
            var p4 = new ScreenStreamPacket { Subtype = ScreenStreamSubtype.InterFrame, StreamId = 1, SequenceNumber = 4, PayloadLength = 4, Payload = new byte[] { 1, 2, 3, 4 } };
            await receiver.HandleIncomingFrameAsync(null!, new ProtocolFrame { Type = MessageType.ScreenStream, Payload = p4.Serialize() });

            Assert.Equal(3, droppedEventsSum);
            Assert.Equal(3, receiver.Statistics.DroppedPacketsCount);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
