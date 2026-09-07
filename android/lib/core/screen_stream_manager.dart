import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'protocol_frame.dart';
import 'screen_stream_capturer.dart';
import 'screen_stream_model.dart';
import 'session_manager.dart';

enum ScreenStreamState { inactive, negotiating, streaming, stopped }

class ScreenStreamStatistics {
  final int framesSent;
  final int bytesSent;
  final double currentFps;
  final double currentBitrateBps;
  final Duration duration;

  const ScreenStreamStatistics({
    this.framesSent = 0,
    this.bytesSent = 0,
    this.currentFps = 0.0,
    this.currentBitrateBps = 0.0,
    this.duration = Duration.zero,
  });
}

class ScreenStreamManager {
  final ScreenStreamCapturer _capturer;
  ScreenStreamState _state = ScreenStreamState.inactive;
  SessionConnection? _activeConn;
  StreamSubscription<ScreenStreamPacket>? _frameSubscription;

  int _framesSent = 0;
  int _bytesSent = 0;
  DateTime? _startTime;
  DateTime _lastCalcTime = DateTime.now();
  int _metricFramesCounter = 0;
  int _metricBytesCounter = 0;
  double _fps = 0.0;
  double _bitrateBps = 0.0;
  bool _disposed = false;

  final _stateController = StreamController<ScreenStreamState>.broadcast();
  final _statsController = StreamController<ScreenStreamStatistics>.broadcast();
  final _packetController = StreamController<ScreenStreamPacket>.broadcast();

  ScreenStreamState get state => _state;
  ScreenStreamCapturer get capturer => _capturer;
  Stream<ScreenStreamState> get stateStream => _stateController.stream;
  Stream<ScreenStreamStatistics> get statsStream => _statsController.stream;
  Stream<ScreenStreamPacket> get packetStream => _packetController.stream;

  ScreenStreamStatistics get currentStats => ScreenStreamStatistics(
    framesSent: _framesSent,
    bytesSent: _bytesSent,
    currentFps: _fps,
    currentBitrateBps: _bitrateBps,
    duration: _startTime == null
        ? Duration.zero
        : DateTime.now().difference(_startTime!),
  );

  ScreenStreamManager({ScreenStreamCapturer? capturer})
    : _capturer =
          capturer ??
          (Platform.isAndroid
              ? AndroidScreenStreamCapturer()
              : MockScreenStreamCapturer());

  Future<void> requestRemoteStart(
    SessionConnection conn,
    ScreenStreamConfig config,
  ) async {
    if (_disposed) return;
    _activeConn = conn;
    _state = ScreenStreamState.negotiating;
    _emitState();
    final jsonBytes = utf8.encode(
      ScreenStreamControlMessage(
        action: 'start',
        streamId: 1,
        width: config.width,
        height: config.height,
        fps: config.fps,
        bitrate: config.bitrate,
        codec: 'JPEG',
        nativeResolution: config.nativeResolution,
      ).serialize(),
    );
    conn.sendFrame(
      MessageType.screenStream,
      ScreenStreamPacket(
        subtype: ScreenStreamSubtype.controlRequest,
        streamId: 1,
        payload: Uint8List.fromList(jsonBytes),
      ).serialize(),
    );
  }

  Future<bool> startStream(
    SessionConnection conn,
    ScreenStreamConfig config,
  ) async {
    if (_disposed) return false;
    _activeConn = conn;
    _state = ScreenStreamState.negotiating;
    _emitState();

    _framesSent = 0;
    _bytesSent = 0;
    _metricFramesCounter = 0;
    _metricBytesCounter = 0;
    _fps = 0.0;
    _bitrateBps = 0.0;
    _startTime = DateTime.now();
    _lastCalcTime = DateTime.now();

    // 1. Send Start Control Request
    final startMsg = ScreenStreamControlMessage(
      action: 'start',
      streamId: 1,
      width: config.width,
      height: config.height,
      fps: config.fps,
      bitrate: config.bitrate,
      codec: config.codec,
    );

    final jsonBytes = utf8.encode(startMsg.serialize());
    final ctrlPacket = ScreenStreamPacket(
      subtype: ScreenStreamSubtype.controlRequest,
      streamId: 1,
      payloadLength: jsonBytes.length,
      payload: Uint8List.fromList(jsonBytes),
    );

    conn.sendFrame(MessageType.screenStream, ctrlPacket.serialize());

    // 2. Pipe captured packets over TCP session connection
    await _frameSubscription?.cancel();
    _frameSubscription = _capturer.frameStream.listen((packet) {
      if (_disposed ||
          _state != ScreenStreamState.streaming ||
          _activeConn == null) {
        return;
      }

      _framesSent++;
      _bytesSent += packet.payload.length;
      _metricFramesCounter++;
      _metricBytesCounter += packet.payload.length;

      final now = DateTime.now();
      final elapsed = now.difference(_lastCalcTime).inMilliseconds / 1000.0;
      if (elapsed >= 1.0) {
        _fps = _metricFramesCounter / elapsed;
        _bitrateBps = (_metricBytesCounter * 8) / elapsed;
        _metricFramesCounter = 0;
        _metricBytesCounter = 0;
        _lastCalcTime = now;
        _emitStats();
      }

      _activeConn?.sendFrame(MessageType.screenStream, packet.serialize());
    });

    _state = ScreenStreamState.streaming;
    _emitState();

    // 3. Start native or mock capturer
    final started = await _capturer.startCapture(config);
    if (!started) {
      await _frameSubscription?.cancel();
      _frameSubscription = null;
      _state = ScreenStreamState.stopped;
      _emitState();
      return false;
    }

    return true;
  }

  Future<void> stopStream([String reason = 'user_cancelled']) async {
    if (_state == ScreenStreamState.stopped ||
        _state == ScreenStreamState.inactive) {
      return;
    }

    // Send Stop Control Request
    if (_activeConn != null) {
      final stopMsg = ScreenStreamControlMessage(
        action: 'stop',
        streamId: 1,
        reason: reason,
      );
      final jsonBytes = utf8.encode(stopMsg.serialize());
      final stopPacket = ScreenStreamPacket(
        subtype: ScreenStreamSubtype.controlRequest,
        streamId: 1,
        payloadLength: jsonBytes.length,
        payload: Uint8List.fromList(jsonBytes),
      );
      _activeConn?.sendFrame(MessageType.screenStream, stopPacket.serialize());
    }

    await _frameSubscription?.cancel();
    _frameSubscription = null;
    await _capturer.stopCapture();

    _state = ScreenStreamState.stopped;
    _emitState();
    _emitStats();
  }

  Future<void> handleIncomingFrame(
    SessionConnection conn,
    ProtocolFrame frame,
  ) async {
    if (_disposed) return;
    if (frame.type != MessageType.screenStream) return;

    final packet = ScreenStreamPacket.tryParse(frame.payload);
    if (packet == null) return;

    if (packet.subtype == ScreenStreamSubtype.controlRequest) {
      final jsonStr = utf8.decode(packet.payload, allowMalformed: true);
      final msg = ScreenStreamControlMessage.fromJson(jsonStr);
      if (msg == null) return;

      if (msg.action == 'start') {
        await _startRemoteCapture(conn, msg);
      } else if (msg.action == 'request_keyframe') {
        _capturer.requestKeyframe();
      } else if (msg.action == 'stop') {
        stopStream(msg.reason ?? 'remote_stopped');
      }
    } else if (packet.subtype == ScreenStreamSubtype.controlResponse) {
      final jsonStr = utf8.decode(packet.payload, allowMalformed: true);
      final msg = ScreenStreamControlMessage.fromJson(jsonStr);
      if (msg == null) return;

      if (msg.status == 'accepted') {
        _state = ScreenStreamState.streaming;
        _emitState();
      } else if (msg.status == 'stopped') {
        stopStream(msg.reason ?? 'remote_acknowledged');
      }
    } else if (packet.subtype == ScreenStreamSubtype.frameConfig ||
        packet.subtype == ScreenStreamSubtype.keyFrame ||
        packet.subtype == ScreenStreamSubtype.interFrame ||
        packet.subtype == ScreenStreamSubtype.jpegFrame) {
      _emitPacket(packet);
    }
  }

  Future<void> _startRemoteCapture(
    SessionConnection conn,
    ScreenStreamControlMessage message,
  ) async {
    if (_disposed) return;
    await _frameSubscription?.cancel();
    _activeConn = conn;
    final config = ScreenStreamConfig(
      width: message.width > 0 ? message.width : 1080,
      height: message.height > 0 ? message.height : 1920,
      fps: message.fps > 0 ? message.fps : 60,
      bitrate: message.bitrate > 0 ? message.bitrate : 8000000,
      codec: message.codec ?? 'JPEG',
      nativeResolution: message.nativeResolution,
    );
    final captureConfig = config.nativeResolution
        ? ScreenStreamConfig(
            width: 0,
            height: 0,
            fps: config.fps,
            bitrate: config.bitrate,
            codec: config.codec,
            nativeResolution: true,
          )
        : config;
    _state = ScreenStreamState.negotiating;
    _emitState();
    _frameSubscription = _capturer.frameStream.listen((packet) {
      if (!_disposed) {
        _activeConn?.sendFrame(MessageType.screenStream, packet.serialize());
      }
    });
    final started = await _capturer.startCapture(captureConfig);
    if (!started) {
      _state = ScreenStreamState.stopped;
      _emitState();
      _sendControlResponse(conn, 'rejected', '系统未授予屏幕录制权限');
      return;
    }
    _state = ScreenStreamState.streaming;
    _emitState();
    // Return the negotiated values. The Android host may clamp the request to
    // the device display, and Windows must render using the actual stream
    // dimensions rather than its old 720p/15 FPS fallback.
    _sendControlResponse(conn, 'accepted', null, captureConfig);
  }

  void _emitState() {
    if (!_stateController.isClosed) _stateController.add(_state);
  }

  void _emitStats() {
    if (!_statsController.isClosed) _statsController.add(currentStats);
  }

  void _emitPacket(ScreenStreamPacket packet) {
    if (!_packetController.isClosed) _packetController.add(packet);
  }

  void _sendControlResponse(
    SessionConnection conn,
    String status, [
    String? reason,
    ScreenStreamConfig? config,
  ]) {
    final message = ScreenStreamControlMessage(
      status: status,
      streamId: 1,
      reason: reason,
      width: config?.width ?? 0,
      height: config?.height ?? 0,
      fps: config?.fps ?? 0,
      bitrate: config?.bitrate ?? 0,
      codec: config?.codec,
      nativeResolution: config?.nativeResolution ?? false,
    );
    conn.sendFrame(
      MessageType.screenStream,
      ScreenStreamPacket(
        subtype: ScreenStreamSubtype.controlResponse,
        streamId: 1,
        payload: Uint8List.fromList(utf8.encode(message.serialize())),
      ).serialize(),
    );
  }

  void dispose() {
    if (_disposed) return;
    _disposed = true;
    _frameSubscription?.cancel();
    _frameSubscription = null;
    _capturer.dispose();
    _activeConn = null;
    _stateController.close();
    _statsController.close();
    _packetController.close();
  }
}
