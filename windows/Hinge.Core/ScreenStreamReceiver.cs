using System.Text;

namespace Hinge.Core;

public enum ScreenStreamState
{
    Inactive,
    Negotiating,
    Streaming,
    Stopped
}

public class StreamStatistics
{
    public long TotalFramesReceived { get; set; }
    public long TotalBytesReceived { get; set; }
    public int DroppedPacketsCount { get; set; }
    public double CurrentFps { get; set; }
    public double CurrentBitrateBps { get; set; }
    public ulong LastTimestampUs { get; set; }
}

public class ScreenStreamReceiver : IDisposable
{
    private readonly IScreenRenderer _renderer;
    private readonly TrustStore _trustStore;
    private ScreenStreamState _state = ScreenStreamState.Inactive;
    private ushort _activeStreamId;
    private uint _expectedSequenceNumber;
    private long _framesReceived;
    private long _bytesReceived;
    private int _droppedPackets;
    private DateTime _lastMetricCalcTime = DateTime.UtcNow;
    private long _metricFramesCounter;
    private long _metricBytesCounter;
    private double _fps;
    private double _bitrateBps;
    private bool _disposed;

    public ScreenStreamState State => _state;
    public ushort ActiveStreamId => _activeStreamId;
    public IScreenRenderer Renderer => _renderer;

    public StreamStatistics Statistics => new()
    {
        TotalFramesReceived = _framesReceived,
        TotalBytesReceived = _bytesReceived,
        DroppedPacketsCount = _droppedPackets,
        CurrentFps = _fps,
        CurrentBitrateBps = _bitrateBps,
        LastTimestampUs = _lastTimestampUs
    };

    private ulong _lastTimestampUs;

    public event EventHandler<ScreenStreamConfig>? StreamStarted;
    public event EventHandler<string?>? StreamStopped;
    public event EventHandler<ScreenStreamPacket>? FrameReceived;
    public event EventHandler<int>? PacketsDropped;

    public ScreenStreamReceiver(IScreenRenderer renderer, TrustStore trustStore)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    public async Task<bool> HandleIncomingFrameAsync(SessionConnection conn, ProtocolFrame frame)
    {
        if (frame.Type != MessageType.ScreenStream) return false;

        // Security check: only trusted paired devices are allowed to stream
        if (conn != null && !string.IsNullOrEmpty(conn.RemoteDeviceId))
        {
            if (!_trustStore.IsTrusted(conn.RemoteDeviceId))
            {
                return false;
            }
        }

        if (!ScreenStreamPacket.TryParse(frame.Payload, out var packet) || packet == null)
        {
            return false;
        }

        switch (packet.Subtype)
        {
            case ScreenStreamSubtype.ControlRequest:
                await HandleControlRequestAsync(conn, packet);
                return true;

            case ScreenStreamSubtype.ControlResponse:
                HandleControlResponse(packet);
                return true;

            case ScreenStreamSubtype.FrameConfig:
            case ScreenStreamSubtype.KeyFrame:
            case ScreenStreamSubtype.InterFrame:
            case ScreenStreamSubtype.JpegFrame:
                HandleVideoPacket(conn, packet);
                return true;

            default:
                return false;
        }
    }

    private async Task HandleControlRequestAsync(SessionConnection? conn, ScreenStreamPacket packet)
    {
        string json = Encoding.UTF8.GetString(packet.Payload);
        var ctrl = ScreenStreamControlMessage.FromJson(json);
        if (ctrl == null) return;

        if (ctrl.Action == "start")
        {
            _activeStreamId = ctrl.StreamId > 0 ? ctrl.StreamId : (ushort)1;
            _expectedSequenceNumber = 0;
            _framesReceived = 0;
            _bytesReceived = 0;
            _droppedPackets = 0;
            _lastMetricCalcTime = DateTime.UtcNow;
            _metricFramesCounter = 0;
            _metricBytesCounter = 0;

            var config = new ScreenStreamConfig
            {
                Width = ctrl.Width > 0 ? ctrl.Width : 1080,
                Height = ctrl.Height > 0 ? ctrl.Height : 1920,
                Fps = ctrl.Fps > 0 ? ctrl.Fps : 60,
                Bitrate = ctrl.Bitrate > 0 ? ctrl.Bitrate : 8_000_000,
                Codec = ctrl.Codec ?? "H264",
                NativeResolution = ctrl.NativeResolution
            };

            _renderer.Initialize(config);
            _state = ScreenStreamState.Streaming;
            StreamStarted?.Invoke(this, config);

            if (conn != null)
            {
                var resp = new ScreenStreamControlMessage
                {
                    Status = "accepted",
                    StreamId = _activeStreamId,
                    Width = config.Width,
                    Height = config.Height,
                    Fps = config.Fps,
                    Bitrate = config.Bitrate,
                    Codec = config.Codec
                };
                await SendControlPacketAsync(conn, ScreenStreamSubtype.ControlResponse, resp.ToJson());
            }
        }
        else if (ctrl.Action == "stop")
        {
            _state = ScreenStreamState.Stopped;
            _renderer.Close();
            StreamStopped?.Invoke(this, ctrl.Reason ?? "stopped_by_remote");

            if (conn != null)
            {
                var resp = new ScreenStreamControlMessage
                {
                    Status = "stopped",
                    StreamId = _activeStreamId,
                    Reason = "acknowledged"
                };
                await SendControlPacketAsync(conn, ScreenStreamSubtype.ControlResponse, resp.ToJson());
            }
        }
    }

    private void HandleControlResponse(ScreenStreamPacket packet)
    {
        string json = Encoding.UTF8.GetString(packet.Payload);
        var ctrl = ScreenStreamControlMessage.FromJson(json);
        if (ctrl == null) return;

        if (ctrl.Status == "accepted")
        {
            _activeStreamId = ctrl.StreamId > 0 ? ctrl.StreamId : _activeStreamId;
            var config = new ScreenStreamConfig
            {
                Width = ctrl.Width > 0 ? ctrl.Width : 1080,
                Height = ctrl.Height > 0 ? ctrl.Height : 1920,
                Fps = ctrl.Fps > 0 ? ctrl.Fps : 60,
                Bitrate = ctrl.Bitrate > 0 ? ctrl.Bitrate : 8_000_000,
                Codec = ctrl.Codec ?? "JPEG",
                NativeResolution = ctrl.NativeResolution
            };
            _renderer.Initialize(config);
            _state = ScreenStreamState.Streaming;
            StreamStarted?.Invoke(this, config);
        }
        else if (ctrl.Status == "stopped")
        {
            _state = ScreenStreamState.Stopped;
            _renderer.Close();
            StreamStopped?.Invoke(this, ctrl.Reason);
        }
    }

    private void HandleVideoPacket(SessionConnection? conn, ScreenStreamPacket packet)
    {
        if (_state != ScreenStreamState.Streaming) return;

        // Sequence number check
        if (_expectedSequenceNumber > 0 && packet.SequenceNumber > _expectedSequenceNumber)
        {
            int dropped = (int)(packet.SequenceNumber - _expectedSequenceNumber);
            _droppedPackets += dropped;
            PacketsDropped?.Invoke(this, dropped);

            // Request recovery keyframe if frames dropped and connection is alive
            if (conn != null)
            {
                _ = RequestKeyframeAsync(conn, _activeStreamId);
            }
        }
        _expectedSequenceNumber = packet.SequenceNumber + 1;

        _framesReceived++;
        _bytesReceived += packet.Payload.Length;
        _lastTimestampUs = packet.TimestampUs;

        // Metrics calculation
        _metricFramesCounter++;
        _metricBytesCounter += packet.Payload.Length;
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastMetricCalcTime).TotalSeconds;
        if (elapsed >= 1.0)
        {
            _fps = _metricFramesCounter / elapsed;
            _bitrateBps = (_metricBytesCounter * 8) / elapsed;
            _metricFramesCounter = 0;
            _metricBytesCounter = 0;
            _lastMetricCalcTime = now;
        }

        _renderer.RenderFrame(packet, packet.Payload);
        FrameReceived?.Invoke(this, packet);
    }

    public async Task RequestKeyframeAsync(SessionConnection conn, ushort streamId)
    {
        if (conn == null) return;
        var req = new ScreenStreamControlMessage
        {
            Action = "request_keyframe",
            StreamId = streamId
        };
        await SendControlPacketAsync(conn, ScreenStreamSubtype.ControlRequest, req.ToJson());
    }

    public async Task StopStreamAsync(SessionConnection conn, ushort streamId)
    {
        _state = ScreenStreamState.Stopped;
        _renderer.Close();
        StreamStopped?.Invoke(this, "local_stopped");

        if (conn != null)
        {
            var req = new ScreenStreamControlMessage
            {
                Action = "stop",
                StreamId = streamId,
                Reason = "user_cancelled"
            };
            await SendControlPacketAsync(conn, ScreenStreamSubtype.ControlRequest, req.ToJson());
        }
    }

    public async Task StartStreamAsync(SessionConnection conn, ScreenStreamConfig config)
    {
        if (conn == null || conn.State != SessionState.Connected)
        {
            throw new InvalidOperationException("设备会话尚未连接。");
        }

        _activeStreamId = _activeStreamId == 0 ? (ushort)1 : _activeStreamId;
        _state = ScreenStreamState.Negotiating;
        var request = new ScreenStreamControlMessage
        {
            Action = "start",
            StreamId = _activeStreamId,
            Width = config.Width,
            Height = config.Height,
            Fps = config.Fps,
            Bitrate = config.Bitrate,
            Codec = "JPEG",
            NativeResolution = config.NativeResolution
        };
        await SendControlPacketAsync(conn, ScreenStreamSubtype.ControlRequest, request.ToJson());
    }

    private async Task SendControlPacketAsync(SessionConnection conn, ScreenStreamSubtype subtype, string jsonPayload)
    {
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var packet = new ScreenStreamPacket
        {
            Subtype = subtype,
            Flags = ScreenStreamFlags.None,
            StreamId = _activeStreamId,
            SequenceNumber = 0,
            PayloadLength = (uint)payloadBytes.Length,
            TimestampUs = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000),
            Payload = payloadBytes
        };

        await conn.SendFrameAsync(MessageType.ScreenStream, packet.Serialize());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
    }
}
