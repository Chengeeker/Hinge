import 'dart:async';
import 'dart:io';

import 'package:flutter/services.dart';

import 'screen_stream_model.dart';

class AndroidScreenStreamCapturer implements ScreenStreamCapturer {
  static const MethodChannel _controlChannel = MethodChannel(
    'hinge/screen_capture',
  );
  static const EventChannel _eventChannel = EventChannel(
    'hinge/screen_capture/events',
  );

  final StreamController<ScreenStreamPacket> _controller =
      StreamController<ScreenStreamPacket>.broadcast();
  StreamSubscription<dynamic>? _eventSubscription;
  ScreenStreamConfig? _config;
  bool _isCapturing = false;
  int _sequenceNumber = 0;

  @override
  bool get isCapturing => _isCapturing;

  @override
  ScreenStreamConfig? get currentConfig => _config;

  @override
  Stream<ScreenStreamPacket> get frameStream => _controller.stream;

  @override
  Future<bool> startCapture(ScreenStreamConfig config) async {
    if (!Platform.isAndroid) return false;
    await stopCapture();
    _config = config;
    _sequenceNumber = 0;
    _eventSubscription = _eventChannel.receiveBroadcastStream().listen((value) {
      if (!_isCapturing || value is! Uint8List) return;
      _controller.add(
        ScreenStreamPacket(
          subtype: ScreenStreamSubtype.jpegFrame,
          flags: ScreenStreamFlags.endOfFrame,
          streamId: 1,
          sequenceNumber: _sequenceNumber++,
          timestampUs: DateTime.now().microsecondsSinceEpoch,
          payload: value,
        ),
      );
    }, onError: (_) {});
    final started =
        await _controlChannel.invokeMethod<bool>('startScreenCapture', {
          'width': config.width,
          'height': config.height,
          'fps': config.fps,
        }) ??
        false;
    _isCapturing = started;
    if (!started) {
      await _eventSubscription?.cancel();
      _eventSubscription = null;
    }
    return started;
  }

  @override
  Future<void> stopCapture() async {
    _isCapturing = false;
    try {
      await _controlChannel.invokeMethod<void>('stopScreenCapture');
    } on MissingPluginException {
      // The method is only available on the Android host.
    } catch (_) {}
    await _eventSubscription?.cancel();
    _eventSubscription = null;
  }

  @override
  void requestKeyframe() {}

  @override
  void dispose() {
    stopCapture();
    _controller.close();
  }
}

abstract class ScreenStreamCapturer {
  bool get isCapturing;
  ScreenStreamConfig? get currentConfig;
  Stream<ScreenStreamPacket> get frameStream;

  Future<bool> startCapture(ScreenStreamConfig config);
  Future<void> stopCapture();
  void requestKeyframe();
  void dispose();
}

class MockScreenStreamCapturer implements ScreenStreamCapturer {
  final _controller = StreamController<ScreenStreamPacket>.broadcast();
  bool _isCapturing = false;
  ScreenStreamConfig? _config;
  Timer? _timer;
  int _sequenceNumber = 0;
  bool _forceKeyframeNext = true;

  @override
  bool get isCapturing => _isCapturing;

  @override
  ScreenStreamConfig? get currentConfig => _config;

  @override
  Stream<ScreenStreamPacket> get frameStream => _controller.stream;

  @override
  Future<bool> startCapture(ScreenStreamConfig config) async {
    _config = config;
    _isCapturing = true;
    _sequenceNumber = 0;
    _forceKeyframeNext = true;

    // Send SPS/PPS Config packet
    final spsPpsPayload = Uint8List.fromList([
      0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1F, // SPS
      0x00, 0x00, 0x00, 0x01, 0x68, 0xCE, 0x3C, 0x80, // PPS
    ]);

    final configPacket = ScreenStreamPacket(
      subtype: ScreenStreamSubtype.frameConfig,
      flags: ScreenStreamFlags.configFrame,
      streamId: 1,
      sequenceNumber: _sequenceNumber++,
      timestampUs: DateTime.now().microsecondsSinceEpoch,
      payload: spsPpsPayload,
    );
    _controller.add(configPacket);

    // Periodically emit synthetic video frames at configured FPS
    final intervalMs = (1000 / config.fps).round().clamp(10, 100);
    _timer = Timer.periodic(Duration(milliseconds: intervalMs), (_) {
      if (!_isCapturing) return;
      _emitNextFrame();
    });

    return true;
  }

  void _emitNextFrame() {
    final nowUs = DateTime.now().microsecondsSinceEpoch;
    if (_forceKeyframeNext) {
      _forceKeyframeNext = false;
      // Synthetic IDR slice (Annex-B 00 00 00 01 + 0x65)
      final idrPayload = Uint8List.fromList([
        0x00,
        0x00,
        0x00,
        0x01,
        0x65,
        0x88,
        0x84,
        0x00,
      ]);
      _controller.add(
        ScreenStreamPacket(
          subtype: ScreenStreamSubtype.keyFrame,
          flags: ScreenStreamFlags.keyFrame | ScreenStreamFlags.endOfFrame,
          streamId: 1,
          sequenceNumber: _sequenceNumber++,
          timestampUs: nowUs,
          payload: idrPayload,
        ),
      );
    } else {
      // Synthetic P-frame slice (Annex-B 00 00 00 01 + 0x41)
      final interPayload = Uint8List.fromList([
        0x00,
        0x00,
        0x00,
        0x01,
        0x41,
        0x9A,
        0x00,
      ]);
      _controller.add(
        ScreenStreamPacket(
          subtype: ScreenStreamSubtype.interFrame,
          flags: ScreenStreamFlags.endOfFrame,
          streamId: 1,
          sequenceNumber: _sequenceNumber++,
          timestampUs: nowUs,
          payload: interPayload,
        ),
      );
    }
  }

  void emitPacket(ScreenStreamPacket packet) {
    _controller.add(packet);
  }

  @override
  void requestKeyframe() {
    _forceKeyframeNext = true;
  }

  @override
  Future<void> stopCapture() async {
    _timer?.cancel();
    _timer = null;
    _isCapturing = false;
  }

  @override
  void dispose() {
    stopCapture();
    _controller.close();
  }
}
