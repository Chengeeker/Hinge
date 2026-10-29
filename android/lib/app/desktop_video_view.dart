import 'desktop_gesture_surface.dart';

import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter/scheduler.dart';

import '../core/protocol_frame.dart';
import '../core/session_manager.dart';
import '../core/desktop_frame_statistics.dart';

/// Only texture metadata and infrequent statistics cross the platform channel.
class DesktopVideoView extends StatefulWidget {
  final SessionConnection connection;
  final bool zoomEnabled;
  final bool showStats;
  final String? inputLabel;
  final bool inputEnabled, directTouch, dragging;
  final double sensitivity;
  final ValueChanged<bool>? onAbsoluteSupported, onDrag, onClick;
  final ValueChanged<Offset>? onAbsolute, onRelative;
  final ValueChanged<int>? onScroll;
  final ValueChanged<bool> onReadyChanged;
  final void Function(int?, bool, bool)? onControlChanged;
  const DesktopVideoView({
    super.key,
    required this.connection,
    required this.zoomEnabled,
    this.showStats = true,
    this.inputLabel,
    this.inputEnabled = false,
    this.directTouch = false,
    this.dragging = false,
    this.sensitivity = 3,
    this.onAbsoluteSupported,
    this.onAbsolute,
    this.onRelative,
    this.onDrag,
    this.onClick,
    this.onScroll,
    required this.onReadyChanged,
    this.onControlChanged,
  });

  @override
  State<DesktopVideoView> createState() => _DesktopVideoViewState();
}

class _DesktopVideoViewState extends State<DesktopVideoView>
    with WidgetsBindingObserver {
  static const _channel = MethodChannel('hinge/desktop_video');
  static final _events = const EventChannel('hinge/desktop_video/events')
      .receiveBroadcastStream();
  StreamSubscription<dynamic>? _subscription;
  StreamSubscription<SessionState>? _connectionSubscription;
  int? _textureId, _sessionId;
  int _generation = 0;
  bool _ready = false, _starting = false;
  double _aspect = 16 / 9;
  String _status = '正在查询电脑桌面共享状态…';
  String _stats = '';
  int? _lastTime, _lastPresented, _lastConsumed;
  Completer<ProtocolFrame>? _descriptionRequest;
  BuildContext? _verificationContext;
  Timer? _retryTimer, _stableTimer;
  int _retryCount = 0;
  bool _foreground = true, _resumeDesktop = false;
  final _frameStatistics = DesktopFrameStatistics();
  final _frameClock = Stopwatch()..start();

  void _onFrameTimings(List<FrameTiming> timings) {
    if (_ready && _foreground) {
      _frameStatistics.add(timings, _frameClock.elapsedMicroseconds);
    }
  }

  void _cancelRecovery() {
    _retryTimer?.cancel();
    _retryTimer = null;
    _stableTimer?.cancel();
    _stableTimer = null;
  }

  void _scheduleRecovery() {
    _stableTimer?.cancel();
    if (!_foreground ||
        _retryTimer != null ||
        _retryCount >= 3 ||
        widget.connection.state != SessionState.connected) {
      return;
    }
    final delay = [500, 1500, 4000][_retryCount++];
    setState(() => _status = '连接中断，正在恢复（$_retryCount/3）…');
    _retryTimer = Timer(Duration(milliseconds: delay), () async {
      _retryTimer = null;
      if (!mounted ||
          !_foreground ||
          widget.connection.state != SessionState.connected) {
        return;
      }
      await _restart(recovering: true);
    });
  }

  void _closeVerification() {
    final dialog = _verificationContext;
    _verificationContext = null;
    if (dialog != null &&
        dialog.mounted &&
        ModalRoute.of(dialog)?.isCurrent == true) {
      Navigator.of(dialog).pop(false);
    }
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    SchedulerBinding.instance.addTimingsCallback(_onFrameTimings);
    _subscription = _events.listen(
      _onEvent,
      onError: (Object error) => _failed('视频组件连接失败：$error'),
    );
    _watchConnection();
    unawaited(_start());
  }

  void _watchConnection() {
    _connectionSubscription = widget.connection.stateStream.listen((state) {
      if (state != SessionState.connected) {
        _resumeDesktop = false;
        unawaited(_stop());
        _failed('Hinge 连接已断开');
      }
    });
  }

  @override
  void didUpdateWidget(covariant DesktopVideoView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.connection, widget.connection)) {
      unawaited(_connectionSubscription?.cancel());
      _watchConnection();
      _retryCount = 0;
      unawaited(_restart());
    }
  }

  Future<void> _start({bool recovering = false}) async {
    if (_starting || !mounted || !_foreground) return;
    _starting = true;
    final generation = ++_generation;
    setState(() {
      _status = '正在连接电脑桌面…';
      _stats = '';
    });
    try {
      final requestId = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
      final frame = await _describe(requestId);
      if (!mounted || generation != _generation) return;
      final description =
          jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
      if (description['enabled'] != true) {
        throw StateError('请在电脑的“设置 → 投屏与远控 → 手机远控电脑”开启桌面共享');
      }
      final port = description['port'];
      if (port is! int ||
          port < 1 ||
          port > 65535 ||
          description['version'] != 1) {
        throw StateError('电脑桌面协议不兼容，请更新两端 Hinge');
      }
      final peerId = widget.connection.peerInfo?.deviceId;
      if (peerId == null) throw StateError('电脑身份信息缺失');
      final result = await _channel.invokeMapMethod<String, dynamic>('start', {
        'host': widget.connection.remoteAddress,
        'port': port,
        'peerId': peerId,
        'deviceId': widget.connection.localIdentity.deviceId,
        'name': widget.connection.localIdentity.name,
      });
      if (!mounted || generation != _generation) {
        if (result?['sessionId'] != null) {
          await _channel.invokeMethod<void>('stop', {
            'sessionId': result!['sessionId'],
          });
        }
        return;
      }
      setState(() {
        _textureId = result!['textureId'] as int;
        _sessionId = result['sessionId'] as int;
      });
    } on TimeoutException {
      if (mounted && generation == _generation) {
        _failed('电脑未回复，请确认两端已更新并开启桌面共享');
        if (recovering) _scheduleRecovery();
      }
    } on Object catch (error) {
      if (mounted && generation == _generation) _failed(error.toString());
    } finally {
      if (generation == _generation) {
        _starting = false;
        if (mounted) setState(() {});
      }
    }
  }

  Future<ProtocolFrame> _describe(String requestId) async {
    final request = Completer<ProtocolFrame>();
    _descriptionRequest = request;
    final subscription = widget.connection.frames.listen(
      (frame) {
        if (request.isCompleted ||
            frame.type != MessageType.desktopControl ||
            frame.payload.length > 8192) {
          return;
        }
        try {
          final json =
              jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
          if (json['action'] == 'description' &&
              json['requestId'] == requestId) {
            request.complete(frame);
          }
        } on Object {
          /* Ignore unrelated or invalid description frames. */
        }
      },
      onError: (Object error) {
        if (!request.isCompleted) request.completeError(error);
      },
      onDone: () {
        if (!request.isCompleted) request.completeError(StateError('电脑连接已结束'));
      },
    );
    // Attach the error listener before a synchronous send or cancellation can fail.
    final response = request.future.timeout(const Duration(seconds: 8));
    try {
      try {
        widget.connection.sendJson(MessageType.desktopControl, {
          'action': 'describe',
          'requestId': requestId,
        });
      } on Object catch (error, stack) {
        if (!request.isCompleted) request.completeError(error, stack);
      }
      return await response;
    } finally {
      if (identical(_descriptionRequest, request)) _descriptionRequest = null;
      unawaited(subscription.cancel());
    }
  }

  void _setReady(bool ready) {
    if (_ready == ready) return;
    _ready = ready;
    _frameStatistics.reset();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && _ready == ready) widget.onReadyChanged(ready);
    });
  }

  void _failed(String message) {
    if (!mounted) return;
    _closeVerification();
    _stableTimer?.cancel();
    _setReady(false);
    setState(() {
      _status = message;
      _textureId = null;
    });
  }

  void _onEvent(dynamic event) {
    if (!mounted || event is! Map || event['sessionId'] != _sessionId) return;
    switch (event['state']) {
      case 'connecting':
        _resumeDesktop = true;
        setState(() => _status = '身份已验证，正在接收首帧…');
      case 'verify':
        unawaited(_verify(event['code'] as String, _sessionId!));
      case 'ready':
        widget.onAbsoluteSupported?.call(event['absoluteMouse'] == true);
        _resumeDesktop = true;
        widget.onControlChanged?.call(
          _sessionId,
          event['canControl'] == true,
          event['relativeMouse'] == true,
        );
        _setReady(true);
        _stableTimer?.cancel();
        _stableTimer = Timer(const Duration(seconds: 10), () {
          if (mounted && _ready) _retryCount = 0;
        });
        setState(() {
          _status = '桌面画面已加密';
          _lastTime = null;
          _lastPresented = null;
          _lastConsumed = null;
        });
      case 'format':
        final width = event['width'] as int, height = event['height'] as int;
        if (width > 0 && height > 0) setState(() => _aspect = width / height);
      case 'stats':
        final time = event['timeMs'] as int,
            presented = event['presented'] as int;
        final consumed = event['consumed'] as int?;
        if (_lastTime != null && time > _lastTime!) {
          final fps =
              (presented - _lastPresented!) * 1000 / (time - _lastTime!);
          final decoder = event['decoderName'] as String? ?? '';
          final mode = switch (event['decoderMode']) {
            'hardware' => '硬解',
            'software' => '软解',
            _ => '解码器',
          };
          final app = _frameStatistics.sample(_frameClock.elapsedMicroseconds);
          final textureFps =
              consumed != null &&
                  _lastConsumed != null &&
                  event['textureSampling'] != false
              ? '${((consumed - _lastConsumed!) * 1000 / (time - _lastTime!)).toStringAsFixed(0)} FPS'
              : '待采样';
          final textureGap = (event['maxTextureGapMs'] as num?)?.toDouble();
          final textureGapLabel = textureGap != null && textureGap > 0
              ? textureGap.toStringAsFixed(1)
              : '—';
          final appP95 = app?.p95Ms.toStringAsFixed(1) ?? '—';
          final videoGap = (event['maxVideoGapMs'] as num?)?.toDouble();
          final videoGapLabel = videoGap != null && videoGap > 0
              ? videoGap.toStringAsFixed(1)
              : '—';
          final refresh = (event['displayHz'] as num?)?.toDouble();
          final refreshLabel = refresh != null && refresh > 0
              ? '${refresh.toStringAsFixed(0)} Hz'
              : '未知';
          setState(
            () => _stats =
                '视频输出 ${fps.toStringAsFixed(0)} FPS · 纹理取帧 $textureFps'
                '\n视频最大间隔 $videoGapLabel ms · 取帧最大间隔 $textureGapLabel ms'
                '\n屏幕 $refreshLabel · 界面重建 P95 $appP95 ms'
                '${decoder.isEmpty ? '' : '\n$mode · 待解码 ${event['pending'] ?? 0} · $decoder'}',
          );
        }
        _lastTime = time;
        _lastPresented = presented;
        _lastConsumed = consumed;
      case 'error':
        _resumeDesktop = event['retryable'] == true;
        _failed(event['message'] as String? ?? '桌面连接失败');
        if (event['retryable'] == true) _scheduleRecovery();
      case 'stopped':
        _sessionId = null;
        _closeVerification();
        _setReady(false);
        setState(() {
          _textureId = null;
          if (_status == '桌面画面已加密') _status = '桌面共享已停止';
        });
    }
  }

  Future<void> _verify(String code, int id) async {
    if (!mounted || id != _sessionId) return;
    final accepted = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        _verificationContext = context;
        return AlertDialog(
          title: const Text('核对电脑桌面身份'),
          content: Text('请确认电脑也显示完全相同的身份码：\n\n$code\n\n两端一致后分别允许，后续无需重复确认。'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('拒绝'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('身份码一致'),
            ),
          ],
        );
      },
    );
    _verificationContext = null;
    if (!mounted || id != _sessionId) return;
    try {
      await _channel.invokeMethod<void>('confirm', {
        'sessionId': id,
        'allow': accepted == true,
      });
    } on PlatformException catch (error) {
      _failed(error.message ?? '桌面确认已过期');
    }
  }

  Future<void> _restart({bool recovering = false}) async {
    final stopped = _stop();
    final generation = _generation;
    await stopped;
    if (_generation == generation) await _start(recovering: recovering);
  }

  Future<void> _stop() async {
    _cancelRecovery();
    _closeVerification();
    ++_generation;
    _starting = false;
    final pending = _descriptionRequest;
    if (pending != null && !pending.isCompleted) {
      pending.completeError(StateError('桌面连接已取消'));
    }
    final id = _sessionId;
    _sessionId = null;
    _textureId = null;
    _setReady(false);
    if (id != null) {
      try {
        await _channel.invokeMethod<void>('stop', {'sessionId': id});
      } on PlatformException {
        /* Already stopped natively. */
      }
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _foreground = true;
      if (_resumeDesktop) {
        _resumeDesktop = false;
        unawaited(_start());
      }
      return;
    }
    if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.hidden ||
        state == AppLifecycleState.detached) {
      if (_foreground) {
        _resumeDesktop =
            _resumeDesktop ||
            _ready ||
            _starting ||
            _sessionId != null ||
            _retryTimer != null;
      }
      _foreground = false;
      unawaited(_stop());
      _failed('桌面已暂停');
    }
  }

  @override
  void dispose() {
    SchedulerBinding.instance.removeTimingsCallback(_onFrameTimings);
    _frameClock.stop();
    WidgetsBinding.instance.removeObserver(this);
    unawaited(_stop());
    unawaited(_subscription?.cancel());
    unawaited(_connectionSubscription?.cancel());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ColoredBox(
    color: Colors.black,
    child: Stack(
      fit: StackFit.expand,
      children: [
        if (_textureId != null)
          DesktopGestureSurface(
            aspect: _aspect,
            enabled: _ready && widget.inputEnabled,
            direct: widget.directTouch,
            zoomOnly: widget.zoomEnabled,
            dragging: widget.dragging,
            sensitivity: widget.sensitivity,
            onAbsolute: (p) => widget.onAbsolute?.call(p),
            onRelative: (p) => widget.onRelative?.call(p),
            onDrag: (v) => widget.onDrag?.call(v),
            onClick: (v) => widget.onClick?.call(v),
            onScroll: (v) => widget.onScroll?.call(v),
            child: Center(
              child: AspectRatio(
                aspectRatio: _aspect,
                child: Texture(textureId: _textureId!),
              ),
            ),
          ),
        if (!_ready)
          Center(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    _status,
                    textAlign: TextAlign.center,
                    style: const TextStyle(color: Colors.white),
                  ),
                  const SizedBox(height: 12),
                  TextButton(
                    onPressed: _starting
                        ? null
                        : () async {
                            _retryCount = 0;
                            await _restart();
                          },
                    child: const Text('重新连接'),
                  ),
                ],
              ),
            ),
          ),
        if (_ready && widget.inputLabel != null)
          Positioned(
            left: 12,
            bottom: 12,
            child: IgnorePointer(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: Colors.black54,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10,
                    vertical: 6,
                  ),
                  child: Text(
                    widget.inputLabel!,
                    style: const TextStyle(color: Colors.white, fontSize: 12),
                  ),
                ),
              ),
            ),
          ),
        if (_ready && widget.showStats)
          Positioned(
            left: 8,
            right: 8,
            top: 8,
            child: IgnorePointer(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: Colors.black54,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Padding(
                  padding: const EdgeInsets.all(6),
                  child: Text(
                    _stats.isEmpty ? _status : _stats,
                    maxLines: 4,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(color: Colors.white, fontSize: 11),
                  ),
                ),
              ),
            ),
          ),
      ],
    ),
  );
}
