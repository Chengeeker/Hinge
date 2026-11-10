import 'game_mode_screen.dart';
import 'desktop_video_view.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/protocol_frame.dart';
import '../core/game_controls.dart';
import '../core/remote_input_model.dart';
import '../core/remote_input_sender.dart';
import '../core/session_manager.dart';

class TouchpadScreen extends StatefulWidget {
  final Uint8List? controlKey;
  final Future<Uint8List?> Function()? onEncryptionRequired;
  final String peerName;
  final SessionConnection connection;
  final ValueChanged<bool>? onControlStateChanged;
  final bool showDesktop;

  const TouchpadScreen({
    super.key,
    required this.peerName,
    this.controlKey,
    this.onEncryptionRequired,
    required this.connection,
    this.onControlStateChanged,
    this.showDesktop = false,
  });

  @override
  State<TouchpadScreen> createState() => _TouchpadScreenState();
}

class _TouchpadScreenState extends State<TouchpadScreen>
    with WidgetsBindingObserver {
  late final RemoteInputSender _sender;
  late final GameInputState _desktopMouse;
  final _desktopMouseHeld = ValueNotifier(false);
  late final StreamSubscription<ProtocolFrame> _frameSubscription;
  late final StreamSubscription<SessionState> _stateSubscription;
  final TextEditingController _textInputController = TextEditingController();
  final Map<int, Offset> _pointers = {};
  final Map<int, Offset> _pointerStarts = {};
  double _sensitivity = 3;
  bool _controlReady = false;
  bool _desktopReady = false;
  bool _desktopZoom = false;
  bool _desktopDirect = false, _desktopAbsoluteSupported = false;
  bool _desktopViewOnly = false;
  String get _modePreference =>
      'desktopInputMode.${widget.connection.peerInfo?.deviceId ?? widget.peerName}';
  bool _desktopFullscreen = false;
  bool _desktopShowStats = false;
  bool _desktopMouseButtons = true, _desktopSystemSupported = false;
  bool _desktopSystemPending = false;
  bool _desktopMappingEnabled = false, _desktopMappingCreated = false;
  bool _desktopGameCamera = false;
  bool _desktopToolsOpen = false;
  final _desktopMappingKey = GlobalKey<GameModeScreenState>();
  double _desktopViewScale = 1;
  int _desktopViewRevision = 0;
  Size _desktopVideoSize = Size.zero;
  final GlobalKey _desktopViewKey = GlobalKey();
  int? _desktopSessionId;
  bool _twoFingerGesture = false;
  bool _twoFingerTapCandidate = false;
  bool _singlePointerMoved = false;
  bool _dragPressed = false;
  bool _orientationOverridden = false;
  Orientation? _orientation;
  RemoteMouseButton _tapButton = RemoteMouseButton.left;
  String _status = '正在请求 Windows 允许控制…';
  Offset? _lastCentroid;
  DateTime? _lastTapAt;
  Offset? _lastTapPosition;

  bool get _canSend =>
      !_desktopSystemPending &&
      _controlReady &&
      widget.connection.state == SessionState.connected &&
      (!widget.showDesktop || _desktopReady);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final orientation = MediaQuery.orientationOf(context);
    if (_orientation != null && _orientation != orientation) {
      _cancelGesture();
    }
    if (widget.showDesktop && _orientation != orientation) {
      _desktopFullscreen = orientation == Orientation.landscape;
      unawaited(
        SystemChrome.setEnabledSystemUIMode(
          _desktopFullscreen
              ? SystemUiMode.immersiveSticky
              : SystemUiMode.edgeToEdge,
        ),
      );
    }
    _orientation = orientation;
  }

  void _toggleOrientation() {
    _orientationOverridden = true;
    final orientations = _orientation == Orientation.landscape
        ? [DeviceOrientation.portraitUp, DeviceOrientation.portraitDown]
        : [DeviceOrientation.landscapeLeft, DeviceOrientation.landscapeRight];
    unawaited(SystemChrome.setPreferredOrientations(orientations));
  }

  Future<void> _toggleDesktopFullscreen() async {
    if (_desktopMappingEnabled) {
      await _setDesktopMapping(false);
    }
    if (!_desktopFullscreen) {
      _cancelGesture();
      _orientationOverridden = true;
      setState(() {
        _desktopFullscreen = true;
        _desktopZoom = false;
      });
      await SystemChrome.setPreferredOrientations(const [
        DeviceOrientation.landscapeLeft,
        DeviceOrientation.landscapeRight,
      ]);
      await SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
      return;
    }

    _cancelGesture();
    _orientationOverridden = true;
    setState(() => _desktopFullscreen = false);
    await SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
    await SystemChrome.setPreferredOrientations(const [
      DeviceOrientation.portraitUp,
      DeviceOrientation.portraitDown,
    ]);
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.showDesktop) {
      const MethodChannel('hinge/desktop_video')
          .invokeMethod<bool>('loadInputMode', {'peerId': _modePreference})
          .then((direct) {
            if (mounted) {
              setState(() => _desktopDirect = direct ?? false);
            }
          }, onError: (Object _) {});
    }
    _sender = RemoteInputSender(
      widget.connection,
      controlKey: widget.controlKey,
      onEncryptionRequired: widget.onEncryptionRequired,
      desktopTransport: widget.showDesktop ? _sendDesktopInput : null,
    );
    _desktopMouse = GameInputState((key, down) {
      final button = RemoteMouseButton.values[-key - 1];
      down ? _sender.sendMouseDown(button) : _sender.sendMouseUp(button);
      _desktopMouseHeld.value = _desktopMouse.held.isNotEmpty;
    });
    _sender.addListener(_senderChanged);
    _frameSubscription = _sender.verifiedFrames.listen(
      _handleFrame,
      onError: _connectionLost,
    );
    _stateSubscription = widget.connection.stateStream.listen(
      _handleConnectionState,
    );
    if (widget.connection.state == SessionState.connected) {
      try {
        _sender.startControl();
      } on Object {
        _status = '无法向电脑发送控制请求，请重新连接后重试。';
      }
    } else {
      _status = 'Hinge 连接已断开，请返回设置重新连接电脑。';
    }
  }

  void _senderChanged() {
    if (!mounted) return;
    if (!_sender.ready) _endControl(_sender.status);
    if (_desktopMappingEnabled && !_sender.supportsGameKeys) {
      setState(() => _desktopMappingEnabled = false);
    }
  }

  void _sendDesktopInput(Uint8List bytes) {
    final id = _desktopSessionId;
    if (id == null) return;
    unawaited(
      const MethodChannel('hinge/desktop_video')
          .invokeMethod<bool>('sendInput', {'sessionId': id, 'data': bytes})
          .then(
            (accepted) {
              if (accepted != true && mounted && _desktopSessionId == id) {
                _endControl('桌面控制已停止，请重新连接');
              }
            },
            onError: (Object error) {
              if (mounted && _desktopSessionId == id) _endControl('桌面控制发送失败');
            },
          ),
    );
  }

  Future<void> _openGameMode() async {
    if (widget.showDesktop) return;
    _cancelGesture();
    final restore = _orientationOverridden
        ? _orientation == Orientation.landscape
              ? [
                  DeviceOrientation.landscapeLeft,
                  DeviceOrientation.landscapeRight,
                ]
              : [DeviceOrientation.portraitUp, DeviceOrientation.portraitDown]
        : const <DeviceOrientation>[];
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => GameModeScreen(
          sender: _sender,
          connection: widget.connection,
          restoreOrientations: restore,
        ),
      ),
    );
  }

  void _handleFrame(ProtocolFrame frame) {
    if (frame.type != MessageType.remoteInput) return;
    final event = RemoteInputEvent.tryParse(frame.payload);
    if (event == null) return;
    switch (event.actionType) {
      case RemoteActionType.controlAccepted:
        if (!mounted) return;
        setState(() {
          _controlReady = true;
          _status =
              '已连接 · ${widget.peerName} · ${_sender.isEncrypted ? '加密' : '局域网未加密'}';
        });
        widget.onControlStateChanged?.call(true);
        break;
      case RemoteActionType.controlDenied:
        _endControl('电脑端尚未允许远控，请在 Windows 设置中开启。');
        break;
      case RemoteActionType.controlEnded:
        _endControl('电脑端已结束远控。');
        break;
      default:
        break;
    }
  }

  void _handleConnectionState(SessionState state) {
    if (state != SessionState.connected) {
      _endControl('Hinge 连接已断开，远控已停止。');
    }
  }

  void _connectionLost(Object error, StackTrace stackTrace) {
    _endControl('连接异常，远控已停止。');
  }

  void _endControl(String status) {
    final wasReady = _controlReady;
    final releaseDrag = _dragPressed && _controlReady;
    _dragPressed = false;
    _controlReady = false;
    _desktopMappingEnabled = false;
    if (widget.showDesktop) _desktopMouse.releaseAll();
    if (!widget.showDesktop &&
        releaseDrag &&
        widget.connection.state == SessionState.connected) {
      try {
        _sender.sendMouseUp(RemoteMouseButton.left);
      } on Object {
        // Windows releases held buttons when the control session ends.
      }
    }
    if (wasReady) widget.onControlStateChanged?.call(false);
    if (!mounted) return;
    setState(() => _status = status);
  }

  void _send(void Function() action) {
    if (!_canSend || (widget.showDesktop && _desktopViewOnly)) return;
    try {
      action();
    } on Object {
      _endControl('发送控制指令失败，请重新连接电脑。');
    }
  }

  void _onPointerDown(PointerDownEvent event, bool landscape, double width) {
    if (!_canSend) return;
    _pointers[event.pointer] = event.localPosition;
    _pointerStarts[event.pointer] = event.localPosition;

    if (_pointers.length == 1) {
      _tapButton =
          landscape &&
              !widget.showDesktop &&
              event.localPosition.dx >= width / 2
          ? RemoteMouseButton.right
          : RemoteMouseButton.left;
      _singlePointerMoved = false;
      final lastTapAt = _lastTapAt;
      final lastTapPosition = _lastTapPosition;
      final isSecondTap =
          _tapButton == RemoteMouseButton.left &&
          lastTapAt != null &&
          lastTapPosition != null &&
          DateTime.now().difference(lastTapAt) <
              const Duration(milliseconds: 330) &&
          (event.localPosition - lastTapPosition).distance < 32;
      if (isSecondTap) {
        _send(() => _sender.sendMouseDown(RemoteMouseButton.left));
        _dragPressed = true;
        _lastTapAt = null;
        _lastTapPosition = null;
      }
      return;
    }

    if (_pointers.length == 2) {
      _releaseDragButton();
      _twoFingerGesture = true;
      _twoFingerTapCandidate = !_singlePointerMoved;
      _lastCentroid = _centroid;
      _lastTapAt = null;
      _lastTapPosition = null;
    } else {
      _twoFingerTapCandidate = false;
    }
  }

  void _onPointerMove(PointerMoveEvent event) {
    final previousPosition = _pointers[event.pointer];
    if (previousPosition == null || !_canSend) return;
    _pointers[event.pointer] = event.localPosition;

    if (_pointers.length == 1 && !_twoFingerGesture) {
      final delta = event.localPosition - previousPosition;
      final start = _pointerStarts[event.pointer];
      if (start != null && (event.localPosition - start).distance > 8) {
        _singlePointerMoved = true;
      }
      _sendMouseMove(delta);
      return;
    }

    if (_twoFingerGesture && _pointers.length >= 2) {
      final start = _pointerStarts[event.pointer];
      if (start != null && (event.localPosition - start).distance > 12) {
        _twoFingerTapCandidate = false;
      }
      final centroid = _centroid;
      final previousCentroid = _lastCentroid;
      _lastCentroid = centroid;
      if (centroid == null || previousCentroid == null) return;
      final delta = centroid - previousCentroid;
      if (delta.distance > 1) {
        _twoFingerTapCandidate = false;
        final wheel = (-delta.dy * 2 * _sensitivity).round();
        if (wheel != 0) _send(() => _sender.sendMouseScroll(wheel));
      }
    }
  }

  void _sendMouseMove(Offset delta) {
    final dx = (delta.dx * _sensitivity).round();
    final dy = (delta.dy * _sensitivity).round();
    if (dx != 0 || dy != 0) {
      _send(() => _sender.sendMouseMove(dx, dy));
    }
  }

  Offset? get _centroid {
    if (_pointers.length < 2) return null;
    final points = _pointers.values.take(2).toList(growable: false);
    return Offset(
      (points[0].dx + points[1].dx) / 2,
      (points[0].dy + points[1].dy) / 2,
    );
  }

  void _onPointerUp(PointerEvent event) {
    if (!_pointers.containsKey(event.pointer)) return;
    _pointers.remove(event.pointer);
    _pointerStarts.remove(event.pointer);
    if (_pointers.isNotEmpty) return;

    if (_twoFingerGesture) {
      if (_twoFingerTapCandidate) {
        _send(() => _sender.sendMouseClick(RemoteMouseButton.right));
      }
    } else if (_dragPressed) {
      _releaseDragButton();
    } else if (!_singlePointerMoved) {
      _send(() => _sender.sendMouseClick(_tapButton));
      _lastTapAt = _tapButton == RemoteMouseButton.left ? DateTime.now() : null;
      _lastTapPosition = _tapButton == RemoteMouseButton.left
          ? event.localPosition
          : null;
    } else {
      _lastTapAt = null;
      _lastTapPosition = null;
    }

    _twoFingerGesture = false;
    _twoFingerTapCandidate = false;
    _lastCentroid = null;
    _singlePointerMoved = false;
  }

  void _onPointerCancel(PointerCancelEvent event) => _cancelGesture();

  void _cancelGesture() {
    _pointers.clear();
    _pointerStarts.clear();
    _twoFingerGesture = false;
    _twoFingerTapCandidate = false;
    _singlePointerMoved = false;
    _lastCentroid = null;
    _lastTapAt = null;
    _lastTapPosition = null;
    _releaseDragButton();
  }

  void _releaseDragButton() {
    if (!_dragPressed) return;
    _dragPressed = false;
    if (widget.showDesktop) {
      _desktopMouse.update('desktop-gesture', {});
    } else {
      _send(() => _sender.sendMouseUp(RemoteMouseButton.left));
    }
  }

  void _showTextInputDialog() {
    if (!_canSend || (widget.showDesktop && _desktopViewOnly)) return;
    if (widget.showDesktop) {
      _showDesktopActions(initialPage: '键盘');
      return;
    }
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('向电脑光标处发送文本'),
        content: TextField(
          controller: _textInputController,
          autofocus: true,
          maxLength: 4096,
          decoration: const InputDecoration(
            hintText: '输入要发送到远程电脑光标处的文本…',
            border: OutlineInputBorder(),
          ),
          onSubmitted: (value) {
            if (value.isNotEmpty) {
              _send(() => _sender.sendTextInput(value));
              _textInputController.clear();
              Navigator.pop(ctx);
            }
          },
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          FilledButton.icon(
            icon: const Icon(Icons.send, size: 16),
            label: const Text('发送文本'),
            onPressed: () {
              final value = _textInputController.text;
              if (value.isNotEmpty) {
                _send(() => _sender.sendTextInput(value));
                _textInputController.clear();
              }
              Navigator.pop(ctx);
            },
          ),
        ],
      ),
    );
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    if (_desktopFullscreen || _orientationOverridden) {
      unawaited(SystemChrome.setPreferredOrientations([]));
    }
    if (_desktopFullscreen) {
      unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge));
    }
    if (widget.connection.state == SessionState.connected) {
      try {
        if (widget.showDesktop) _desktopMouse.releaseAll();
        if (!widget.showDesktop && _dragPressed && _controlReady) {
          _sender.sendMouseUp(RemoteMouseButton.left);
        }
        _sender.stopControl();
      } on Object {
        // The peer may close the session between the state check and send.
      }
    }
    if (_controlReady) widget.onControlStateChanged?.call(false);
    _sender.removeListener(_senderChanged);
    _sender.dispose();
    unawaited(_frameSubscription.cancel());
    unawaited(_stateSubscription.cancel());
    _textInputController.dispose();
    _desktopMouseHeld.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed) _cancelGesture();
    if (state == AppLifecycleState.resumed && _desktopFullscreen) {
      unawaited(
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
      );
    }
  }

  void _showSensitivityDialog() {
    showDialog<void>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text('灵敏度 ${_sensitivity.toStringAsFixed(1)}×'),
          content: Slider(
            value: _sensitivity,
            min: 1.5,
            max: 5,
            divisions: 14,
            onChanged: (value) {
              setState(() => _sensitivity = value);
              setDialogState(() {});
            },
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('完成'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _desktopZoomButton() => IconButton(
    tooltip: _desktopZoom ? '切回鼠标操作' : '缩放/平移画面',
    icon: Icon(_desktopZoom ? Icons.mouse : Icons.zoom_in),
    onPressed: () {
      _cancelGesture();
      setState(() => _desktopZoom = !_desktopZoom);
    },
  );

  void _setDesktopDrag(bool down) {
    if (down && (!_canSend || _desktopViewOnly)) return;
    if (down == _dragPressed) return;
    _desktopMouse.update('desktop-gesture', down ? {-1} : {});
    if (mounted) setState(() => _dragPressed = down);
  }

  void _setMappedMouseButton(RemoteMouseButton button, bool down) {
    _desktopMouse.update(button, down ? {-button.value - 1} : {});
  }

  void _clickDesktopMouse(RemoteMouseButton button) {
    if (_desktopMouse.held.contains(-button.value - 1)) return;
    _send(() => _sender.sendMouseClick(button));
  }

  Future<void> _setDesktopMapping(bool enabled) async {
    if (enabled &&
        (!_canSend || _desktopViewOnly || !_sender.supportsGameKeys)) {
      return;
    }
    _cancelGesture();
    if (enabled && !_desktopFullscreen) await _toggleDesktopFullscreen();
    if (!mounted ||
        (enabled &&
            (!_canSend || _desktopViewOnly || !_sender.supportsGameKeys))) {
      return;
    }
    setState(() {
      _desktopMappingEnabled = enabled;
      if (enabled) {
        _desktopMappingCreated = true;
        _desktopZoom = false;
      }
    });
  }

  void _setDesktopMode(bool direct) {
    _cancelGesture();
    setState(() {
      _desktopDirect = direct;
      _desktopGameCamera = false;
      _desktopZoom = false;
    });
    unawaited(
      const MethodChannel('hinge/desktop_video')
          .invokeMethod<void>('saveInputMode', {
            'peerId': _modePreference,
            'direct': direct,
          })
          .catchError((Object _) {}),
    );
  }

  void _sendKeys(List<int> keys) => _send(() {
    try {
      for (final key in keys) {
        _sender.sendKey(key, true);
      }
    } finally {
      for (final key in keys.reversed) {
        _sender.sendKey(key, false);
      }
    }
  });

  void _sendDesktopShortcut(int key) => _sendKeys([0x5B, key]);

  Future<void> _requestSystemAction(int action, String label) async {
    if (!_canSend || _desktopViewOnly || !_desktopSystemSupported) return;
    _cancelGesture();
    final session = _desktopSessionId;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('$label电脑？'),
        content: Text(
          action == 1
              ? '电脑将进入锁定界面，当前远控可能中断，需要在电脑上解锁。'
              : '请先保存电脑上的工作。确认后发送$label请求，远控将中断；未保存的应用可能阻止操作。',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: Text('确认$label'),
          ),
        ],
      ),
    );
    if (!mounted ||
        confirmed != true ||
        !_canSend ||
        _desktopViewOnly ||
        session == null ||
        session != _desktopSessionId) {
      return;
    }
    setState(() => _desktopSystemPending = true);
    try {
      final queued = await const MethodChannel('hinge/desktop_video')
          .invokeMethod<bool>('sendInput', {
            'sessionId': session,
            'data': RemoteInputEvent(
              actionType: RemoteActionType.desktopSystemAction,
              buttonOrKey: action,
            ).serialize(),
          });
      if (!mounted || session != _desktopSessionId) return;
      if (queued != true) throw StateError('控制会话已结束');
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('已发送$label请求，请等待电脑处理')));
      }
    } on Object {
      if (mounted && session == _desktopSessionId) {
        setState(() => _desktopSystemPending = false);
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('系统请求未能发送，请检查连接后重试')));
      }
    }
  }

  void _showDesktopActions({String initialPage = '操作'}) {
    if (!_desktopReady || _desktopToolsOpen) return;
    _cancelGesture();
    setState(() => _desktopToolsOpen = true);
    var page = initialPage;
    showGeneralDialog<String>(
      context: context,
      barrierDismissible: true,
      barrierLabel: '关闭桌面工具',
      barrierColor: Colors.black38,
      transitionDuration: MediaQuery.disableAnimationsOf(context)
          ? Duration.zero
          : const Duration(milliseconds: 160),
      pageBuilder: (panelContext, _, _) => AnimatedBuilder(
        animation: _sender,
        builder: (_, _) => StatefulBuilder(
          builder: (ctx, refresh) {
            final size = MediaQuery.sizeOf(ctx);
            final compact = size.width > size.height;
            final sidePanel = compact || size.width >= 600;
            final panelWidth = compact
                ? (size.width * .40).clamp(300.0, 400.0)
                : size.width >= 600
                ? (size.width * .45).clamp(360.0, 440.0)
                : size.width;
            final inset = compact ? 12.0 : 16.0;
            final actionWidth = compact
                ? ((panelWidth - inset * 2 - 12) / 3).floorToDouble()
                : 108.0;
            final inherited = Theme.of(ctx);
            final text = inherited.textTheme;
            final theme = compact
                ? inherited.copyWith(
                    textTheme: text.copyWith(
                      titleLarge: text.titleLarge?.copyWith(fontSize: 18),
                      titleMedium: text.titleMedium?.copyWith(fontSize: 14),
                      titleSmall: text.titleSmall?.copyWith(fontSize: 12),
                      bodyLarge: text.bodyLarge?.copyWith(fontSize: 13),
                      bodyMedium: text.bodyMedium?.copyWith(fontSize: 12),
                      bodySmall: text.bodySmall?.copyWith(
                        fontSize: 11,
                        height: 1.25,
                      ),
                      labelLarge: text.labelLarge?.copyWith(fontSize: 12),
                      labelMedium: text.labelMedium?.copyWith(fontSize: 11),
                    ),
                    iconTheme: inherited.iconTheme.copyWith(size: 20),
                    iconButtonTheme: IconButtonThemeData(
                      style:
                          (inherited.iconButtonTheme.style ??
                                  const ButtonStyle())
                              .copyWith(
                                iconSize: const WidgetStatePropertyAll(20),
                              ),
                    ),
                    listTileTheme: inherited.listTileTheme.copyWith(
                      dense: true,
                      minTileHeight: 48,
                      visualDensity: VisualDensity.standard,
                      minVerticalPadding: 4,
                      contentPadding: const EdgeInsets.symmetric(
                        horizontal: 12,
                      ),
                      minLeadingWidth: 20,
                      horizontalTitleGap: 10,
                      titleTextStyle: text.bodyLarge?.copyWith(fontSize: 13),
                      subtitleTextStyle: text.bodySmall?.copyWith(
                        fontSize: 11,
                        height: 1.25,
                      ),
                    ),
                    filledButtonTheme: FilledButtonThemeData(
                      style:
                          (inherited.filledButtonTheme.style ??
                                  const ButtonStyle())
                              .copyWith(
                                minimumSize: const WidgetStatePropertyAll(
                                  Size(0, 48),
                                ),
                                padding: const WidgetStatePropertyAll(
                                  EdgeInsets.symmetric(
                                    horizontal: 12,
                                    vertical: 8,
                                  ),
                                ),
                              ),
                    ),
                    outlinedButtonTheme: OutlinedButtonThemeData(
                      style:
                          (inherited.outlinedButtonTheme.style ??
                                  const ButtonStyle())
                              .copyWith(
                                minimumSize: const WidgetStatePropertyAll(
                                  Size(0, 48),
                                ),
                                padding: const WidgetStatePropertyAll(
                                  EdgeInsets.symmetric(
                                    horizontal: 12,
                                    vertical: 8,
                                  ),
                                ),
                              ),
                    ),
                    inputDecorationTheme: inherited.inputDecorationTheme
                        .copyWith(
                          isDense: true,
                          contentPadding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 10,
                          ),
                        ),
                    sliderTheme: inherited.sliderTheme.copyWith(
                      trackHeight: 3,
                      thumbShape: const RoundSliderThumbShape(
                        enabledThumbRadius: 7,
                      ),
                      overlayShape: const RoundSliderOverlayShape(
                        overlayRadius: 18,
                      ),
                    ),
                  )
                : inherited;
            final scheme = theme.colorScheme;
            final canInput = _canSend && !_desktopViewOnly;
            void update(VoidCallback action) {
              action();
              refresh(() {});
            }

            Widget heading(String title) => Padding(
              padding: compact
                  ? const EdgeInsets.fromLTRB(12, 8, 12, 4)
                  : const EdgeInsets.fromLTRB(16, 20, 16, 8),
              child: Text(
                title,
                style: theme.textTheme.titleSmall?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
            );
            Widget group(List<Widget> children) => Card(
              margin: EdgeInsets.only(bottom: compact ? 8 : 12),
              elevation: 0,
              color: scheme.surfaceContainerHigh,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(compact ? 16 : 24),
              ),
              clipBehavior: Clip.antiAlias,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: children,
              ),
            );
            Widget action(
              String label,
              IconData icon,
              VoidCallback? onTap, {
              bool selected = false,
            }) => Semantics(
              selected: selected,
              child: SizedBox(
                width: actionWidth,
                child: FilledButton.tonal(
                  onPressed: onTap,
                  style:
                      FilledButton.styleFrom(
                        minimumSize: Size(actionWidth, compact ? 64 : 96),
                        padding: EdgeInsets.all(compact ? 8 : 12),
                        backgroundColor: selected
                            ? scheme.primaryContainer
                            : scheme.surfaceContainerHigh,
                        foregroundColor: selected
                            ? scheme.onPrimaryContainer
                            : scheme.onSurface,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(
                            compact ? 14 : 20,
                          ),
                        ),
                      ).copyWith(
                        overlayColor:
                            theme.filledButtonTheme.style?.overlayColor,
                      ),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(icon, size: compact ? 20 : 28),
                      SizedBox(height: compact ? 4 : 8),
                      Text(
                        label,
                        textAlign: TextAlign.center,
                        style: theme.textTheme.labelMedium,
                      ),
                    ],
                  ),
                ),
              ),
            );
            Widget link(String title, IconData icon) => ListTile(
              leading: Icon(icon),
              title: Text(title),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => refresh(() => page = title),
            );
            Widget key(String title, List<int> keys) => OutlinedButton(
              style: OutlinedButton.styleFrom(side: BorderSide.none),
              onPressed: canInput ? () => _sendKeys(keys) : null,
              child: Text(title),
            );
            final contents = <Widget>[];
            if (page == '操作') {
              final sensitivitySlider = Slider(
                value: _sensitivity,
                min: 1.5,
                max: 5,
                divisions: 14,
                onChanged: canInput
                    ? (v) => update(() => setState(() => _sensitivity = v))
                    : null,
              );
              contents.addAll([
                Wrap(
                  spacing: compact ? 6 : 8,
                  runSpacing: compact ? 8 : 16,
                  children: [
                    action('退出远控', Icons.logout, () {
                      Navigator.pop(panelContext);
                      Navigator.pop(context);
                    }),
                    action(
                      _desktopFullscreen ? '退出全屏' : '全屏显示',
                      _desktopFullscreen
                          ? Icons.fullscreen_exit
                          : Icons.fullscreen,
                      () {
                        Navigator.pop(panelContext);
                        unawaited(_toggleDesktopFullscreen());
                      },
                    ),
                    action(
                      '仅观看',
                      Icons.visibility_outlined,
                      _controlReady && !_desktopSystemPending
                          ? () => update(() {
                              _cancelGesture();
                              setState(() {
                                _desktopViewOnly = !_desktopViewOnly;
                                if (_desktopViewOnly) {
                                  _desktopMappingEnabled = false;
                                }
                              });
                            })
                          : null,
                      selected: _desktopViewOnly || !_controlReady,
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                group([
                  SwitchListTile(
                    title: const Text('启用按键映射'),
                    subtitle: Text(
                      _sender.supportsGameKeys
                          ? '将常用按键叠加到桌面'
                          : '需要电脑端支持游戏按键与相对鼠标',
                    ),
                    value: _desktopMappingEnabled,
                    onChanged:
                        _desktopMappingEnabled ||
                            (canInput && _sender.supportsGameKeys)
                        ? (value) async {
                            await _setDesktopMapping(value);
                            if (ctx.mounted) refresh(() {});
                          }
                        : null,
                  ),
                  if (_desktopMappingEnabled) ...[
                    ListTile(
                      title: const Text('切换操控方案'),
                      subtitle: Text(
                        '当前方案：${_desktopMappingKey.currentState?.presetName ?? '桌面精简布局'}',
                      ),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.pop(panelContext, 'presets'),
                    ),
                    ListTile(
                      title: const Text('编辑当前方案'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.pop(panelContext, 'edit'),
                    ),
                    ListTile(
                      title: const Text('其他设置'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => Navigator.pop(panelContext, 'settings'),
                    ),
                  ],
                ]),
                heading('操控方式'),
                group([
                  Padding(
                    padding: EdgeInsets.all(compact ? 4 : 12),
                    child: SegmentedButton<bool>(
                      style: SegmentedButton.styleFrom(side: BorderSide.none),
                      showSelectedIcon: false,
                      segments: [
                        ButtonSegment(
                          value: true,
                          label: const Text('屏幕触控'),
                          enabled: _desktopAbsoluteSupported,
                        ),
                        const ButtonSegment(value: false, label: Text('鼠标指针')),
                      ],
                      emptySelectionAllowed:
                          _desktopMappingEnabled && _desktopGameCamera,
                      selected: _desktopMappingEnabled && _desktopGameCamera
                          ? <bool>{}
                          : {_desktopDirect && _desktopAbsoluteSupported},
                      onSelectionChanged: canInput
                          ? (v) => update(() => _setDesktopMode(v.single))
                          : null,
                    ),
                  ),
                  Padding(
                    padding: EdgeInsets.fromLTRB(
                      inset,
                      0,
                      inset,
                      compact ? 4 : 12,
                    ),
                    child: Text(
                      compact
                          ? (_desktopDirect && _desktopAbsoluteSupported
                                ? '点按操作 · 滑动移鼠标 · 长按后拖拽'
                                : '滑动移鼠标 · 轻点左键 · 长按后拖拽')
                          : _desktopDirect && _desktopAbsoluteSupported
                          ? '滑动只移动鼠标 · 轻点左键 · 停住长按后移动可拖拽'
                          : '滑动移动指针 · 轻点左键 · 双指轻点右键 · 停住长按后移动可拖拽',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ),
                  if (_desktopMappingEnabled)
                    SwitchListTile(
                      title: const Text('游戏视角'),
                      subtitle: const Text('滑动只转视角；操作地图或菜单时关闭'),
                      value: _desktopGameCamera,
                      onChanged: canInput
                          ? (value) => update(() {
                              _cancelGesture();
                              setState(() {
                                _desktopGameCamera = value;
                                _desktopZoom = false;
                              });
                            })
                          : null,
                    ),
                  SwitchListTile(
                    title: const Text('显示鼠标按键'),
                    value: _desktopMouseButtons,
                    onChanged: (v) => update(() {
                      _cancelGesture();
                      setState(() => _desktopMouseButtons = v);
                    }),
                  ),
                  if (compact)
                    Padding(
                      padding: EdgeInsets.symmetric(horizontal: inset),
                      child: Row(
                        children: [
                          const Text('灵敏度'),
                          Expanded(child: sensitivitySlider),
                          Text('${_sensitivity.toStringAsFixed(1)}×'),
                        ],
                      ),
                    )
                  else ...[
                    ListTile(
                      title: const Text('鼠标灵敏度'),
                      trailing: Text('${_sensitivity.toStringAsFixed(1)}×'),
                    ),
                    sensitivitySlider,
                  ],
                ]),
                group([
                  link('显示', Icons.monitor),
                  link('键盘', Icons.keyboard_outlined),
                  link('系统快捷操作', Icons.apps),
                ]),
              ]);
            } else if (page == '显示') {
              contents.addAll([
                heading('画面'),
                group([
                  ListTile(
                    title: const Text('当前视频尺寸'),
                    trailing: Text(
                      _desktopVideoSize == Size.zero
                          ? '等待画面'
                          : '${_desktopVideoSize.width.toInt()} × ${_desktopVideoSize.height.toInt()}',
                    ),
                  ),
                  SwitchListTile(
                    title: const Text('缩放 / 平移画面'),
                    subtitle: const Text('开启后滑动只移动画面，不操作电脑'),
                    value: _desktopZoom,
                    onChanged: (v) => update(() {
                      _cancelGesture();
                      setState(() => _desktopZoom = v);
                    }),
                  ),
                  Padding(
                    padding: EdgeInsets.fromLTRB(inset, 0, inset, inset),
                    child: Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        for (final scale in [1.0, 1.5, 2.0])
                          ActionChip(
                            side: BorderSide.none,
                            label: Text(
                              scale == 1 ? '适应屏幕' : '${(scale * 100).round()}%',
                            ),
                            onPressed: () => update(() {
                              _cancelGesture();
                              setState(() {
                                _desktopViewScale = scale;
                                _desktopViewRevision++;
                              });
                            }),
                          ),
                      ],
                    ),
                  ),
                  SwitchListTile(
                    title: const Text('性能信息'),
                    subtitle: const Text('显示实际视频输出、解码和帧间隔'),
                    value: _desktopShowStats,
                    onChanged: (v) =>
                        update(() => setState(() => _desktopShowStats = v)),
                  ),
                ]),
                Padding(
                  padding: EdgeInsets.symmetric(horizontal: inset),
                  child: Text(
                    '帧率、码率与共享显示器在电脑端的远控设置中调整。',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
              ]);
            } else if (page == '键盘') {
              contents.addAll([
                heading('发送文本'),
                group([
                  Padding(
                    padding: EdgeInsets.all(inset),
                    child: TextField(
                      controller: _textInputController,
                      enabled: canInput,
                      maxLength: 4096,
                      minLines: 1,
                      maxLines: 4,
                      decoration: const InputDecoration(
                        hintText: '输入到电脑当前光标处',
                        border: OutlineInputBorder(),
                      ),
                    ),
                  ),
                  Padding(
                    padding: EdgeInsets.fromLTRB(inset, 0, inset, inset),
                    child: FilledButton.icon(
                      onPressed: canInput
                          ? () {
                              final text = _textInputController.text;
                              if (text.isEmpty) return;
                              _send(() => _sender.sendTextInput(text));
                              _textInputController.clear();
                            }
                          : null,
                      icon: const Icon(Icons.send),
                      label: const Text('发送文本'),
                    ),
                  ),
                ]),
                heading('常用按键'),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    key('Esc', [0x1B]),
                    key('Tab', [0x09]),
                    key('Enter', [0x0D]),
                    key('退格', [0x08]),
                    key('Delete', [0x2E]),
                    key('空格', [0x20]),
                    key('↑', [0x26]),
                    key('←', [0x25]),
                    key('↓', [0x28]),
                    key('→', [0x27]),
                    for (var n = 1; n <= 12; n++) key('F$n', [0x6F + n]),
                  ],
                ),
                heading('编辑快捷键'),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    key('复制', [0x11, 0x43]),
                    key('粘贴', [0x11, 0x56]),
                    key('剪切', [0x11, 0x58]),
                    key('全选', [0x11, 0x41]),
                    key('撤销', [0x11, 0x5A]),
                    key('重做', [0x11, 0x59]),
                  ],
                ),
              ]);
            } else {
              contents.addAll([
                heading('系统快捷操作'),
                Wrap(
                  spacing: compact ? 6 : 8,
                  runSpacing: compact ? 8 : 16,
                  children: [
                    action(
                      '显示桌面',
                      Icons.desktop_windows_outlined,
                      canInput ? () => _sendDesktopShortcut(0x44) : null,
                    ),
                    action(
                      '所有窗口',
                      Icons.grid_view,
                      canInput ? () => _sendDesktopShortcut(0x09) : null,
                    ),
                    action(
                      '切换窗口',
                      Icons.swap_horiz,
                      canInput ? () => _sendKeys([0x12, 0x09]) : null,
                    ),
                    action(
                      '资源管理器',
                      Icons.folder_outlined,
                      canInput ? () => _sendDesktopShortcut(0x45) : null,
                    ),
                    action(
                      '任务管理器',
                      Icons.query_stats,
                      canInput ? () => _sendKeys([0x11, 0x10, 0x1B]) : null,
                    ),
                    action(
                      '开始菜单',
                      Icons.window,
                      canInput ? () => _sendKeys([0x5B]) : null,
                    ),
                  ],
                ),
                if (_desktopSystemSupported) ...[
                  heading('电脑电源'),
                  group([
                    for (final item in [
                      (1, '锁定', Icons.lock_outline),
                      (2, '重启', Icons.restart_alt),
                      (3, '关机', Icons.power_settings_new),
                    ])
                      ListTile(
                        leading: Icon(
                          item.$3,
                          color: item.$1 == 1 ? null : scheme.error,
                        ),
                        title: Text('${item.$2}电脑'),
                        trailing: const Icon(Icons.chevron_right),
                        enabled: canInput,
                        onTap: canInput
                            ? () {
                                Navigator.pop(panelContext);
                                unawaited(
                                  _requestSystemAction(item.$1, item.$2),
                                );
                              }
                            : null,
                      ),
                  ]),
                ],
              ]);
            }
            return Align(
              alignment: sidePanel
                  ? Alignment.centerRight
                  : Alignment.bottomCenter,
              child: SafeArea(
                child: SizedBox(
                  width: panelWidth,
                  height:
                      ((sidePanel ? size.height : size.height * .92) -
                              MediaQuery.viewInsetsOf(ctx).bottom)
                          .clamp(120.0, size.height),
                  child: Theme(
                    data: theme,
                    child: Material(
                      color: scheme.surfaceContainer,
                      borderRadius: BorderRadius.circular(compact ? 16 : 24),
                      clipBehavior: Clip.antiAlias,
                      child: Column(
                        children: [
                          Padding(
                            padding: EdgeInsets.symmetric(
                              horizontal: compact ? 4 : 8,
                              vertical: compact ? 0 : 8,
                            ),
                            child: Row(
                              children: [
                                IconButton(
                                  tooltip: page == '操作' ? '关闭工具栏' : '返回操作',
                                  icon: Icon(
                                    page == '操作'
                                        ? Icons.close
                                        : Icons.arrow_back,
                                  ),
                                  onPressed: () {
                                    if (page == '操作') {
                                      Navigator.pop(panelContext);
                                    } else {
                                      refresh(() => page = '操作');
                                    }
                                  },
                                ),
                                Expanded(
                                  child: Text(
                                    page,
                                    style: theme.textTheme.titleLarge,
                                  ),
                                ),
                                if (page != '操作')
                                  IconButton(
                                    tooltip: '关闭工具栏',
                                    icon: const Icon(Icons.close),
                                    onPressed: () =>
                                        Navigator.pop(panelContext),
                                  ),
                              ],
                            ),
                          ),
                          if (!_canSend || _desktopViewOnly)
                            Padding(
                              padding: EdgeInsets.fromLTRB(
                                inset,
                                0,
                                inset,
                                compact ? 4 : 8,
                              ),
                              child: Text(
                                _desktopSystemPending
                                    ? '系统请求已发送'
                                    : '仅观看 · 鼠标和键盘输入已暂停',
                                style: theme.textTheme.bodySmall,
                              ),
                            ),
                          Expanded(
                            child: ListView(
                              key: ValueKey('desktop-panel-$page'),
                              padding: EdgeInsets.fromLTRB(
                                inset,
                                0,
                                inset,
                                compact ? 12 : 24,
                              ),
                              children: contents,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            );
          },
        ),
      ),
      transitionBuilder: (_, animation, _, child) =>
          FadeTransition(opacity: animation, child: child),
    ).then((mappingAction) async {
      if (mounted) setState(() => _desktopToolsOpen = false);
      if (!mounted || !_desktopMappingEnabled) return;
      final mapping = _desktopMappingKey.currentState;
      if (mappingAction == 'edit') {
        mapping?.editCurrentPreset();
      } else if (mappingAction != null) {
        await mapping?.openSettings(
          preferencesOnly: mappingAction == 'settings',
        );
      }
    });
  }

  Widget _desktopRailButton({
    required IconData icon,
    required String label,
    required VoidCallback? onTap,
  }) {
    final color = onTap == null ? Colors.white38 : Colors.white;
    return Semantics(
      button: true,
      label: label,
      enabled: onTap != null,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minWidth: 48, minHeight: 64),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 3, vertical: 8),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(icon, color: color, size: 22),
                  const SizedBox(height: 3),
                  Text(
                    label,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.center,
                    style: TextStyle(color: color, fontSize: 11, height: 1.1),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _desktopControlRail({
    required double width,
    required double contentWidth,
    required bool left,
    required Key key,
    required List<Widget> actions,
  }) => SizedBox(
    key: key,
    width: width,
    child: ColoredBox(
      color: const Color(0xFF050505),
      child: SafeArea(
        left: left,
        right: !left,
        child: Align(
          alignment: left ? Alignment.centerLeft : Alignment.centerRight,
          child: SizedBox(
            width: contentWidth,
            child: ListView(padding: EdgeInsets.zero, children: actions),
          ),
        ),
      ),
    ),
  );

  Widget _desktopMappingLayer() => Positioned.fill(
    child: Visibility(
      visible: _desktopMappingEnabled,
      maintainState: true,
      child: GameModeScreen(
        key: _desktopMappingKey,
        desktop: true,
        gameCamera: _desktopGameCamera,
        mouseSensitivity: _sensitivity,
        onSensitivityChanged: (value) => setState(() => _sensitivity = value),
        onMouseButton: _setMappedMouseButton,
        sender: _sender,
        connection: widget.connection,
        inputEnabled:
            _desktopMappingEnabled &&
            !_desktopToolsOpen &&
            _canSend &&
            !_desktopViewOnly,
        onSettings: _showDesktopActions,
        onExit: () => unawaited(_setDesktopMapping(false)),
      ),
    ),
  );

  Widget _buildDesktopFullscreen(BuildContext context) {
    final size = MediaQuery.sizeOf(context);
    final safe = MediaQuery.viewPaddingOf(context);
    final railWidth = (size.width * 0.07).clamp(64.0, 72.0).toDouble();
    final railExtent =
        railWidth + (safe.left > safe.right ? safe.left : safe.right);
    final canSendMouse = _canSend && !_desktopViewOnly;
    final mappingActive = _desktopMappingEnabled;
    return PopScope<Object?>(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) {
        if (!didPop && !mappingActive) {
          unawaited(_toggleDesktopFullscreen());
        }
      },
      child: Scaffold(
        backgroundColor: Colors.black,
        body: Stack(
          fit: StackFit.expand,
          children: [
            Row(
              children: [
                Visibility(
                  visible: !mappingActive,
                  replacement: SizedBox(width: railExtent),
                  child: _desktopControlRail(
                    key: const ValueKey('desktop-control-rail-left'),
                    width: railExtent,
                    contentWidth: railWidth,
                    left: true,
                    actions: [
                      if (_desktopMouseButtons) ...[
                        _desktopRailButton(
                          icon: Icons.mouse,
                          label: '左键',
                          onTap: canSendMouse
                              ? () => _clickDesktopMouse(RemoteMouseButton.left)
                              : null,
                        ),
                        _desktopRailButton(
                          icon: Icons.mouse_outlined,
                          label: '右键',
                          onTap: canSendMouse
                              ? () =>
                                    _clickDesktopMouse(RemoteMouseButton.right)
                              : null,
                        ),
                      ],
                      _desktopRailButton(
                        icon: _desktopZoom ? Icons.mouse : Icons.zoom_in,
                        label: _desktopZoom ? '鼠标操作' : '缩放画面',
                        onTap:
                            _canSend &&
                                !(_desktopMappingEnabled && _desktopGameCamera)
                            ? () {
                                _cancelGesture();
                                setState(() => _desktopZoom = !_desktopZoom);
                              }
                            : null,
                      ),
                    ],
                  ),
                ),
                Expanded(
                  child: _touchSurface(
                    context,
                    landscape: true,
                    fullscreen: true,
                  ),
                ),
                Visibility(
                  visible: !mappingActive,
                  replacement: SizedBox(width: railExtent),
                  child: _desktopControlRail(
                    key: const ValueKey('desktop-control-rail-right'),
                    width: railExtent,
                    contentWidth: railWidth,
                    left: false,
                    actions: [
                      _desktopRailButton(
                        icon: Icons.tune,
                        label: '操作',
                        onTap: _desktopReady ? _showDesktopActions : null,
                      ),
                      _desktopRailButton(
                        icon: Icons.monitor,
                        label: '显示',
                        onTap: _desktopReady
                            ? () => _showDesktopActions(initialPage: '显示')
                            : null,
                      ),
                      _desktopRailButton(
                        icon: Icons.keyboard,
                        label: '键盘',
                        onTap: _canSend && !_desktopViewOnly
                            ? _showTextInputDialog
                            : null,
                      ),
                      _desktopRailButton(
                        icon: Icons.desktop_windows_outlined,
                        label: '显示桌面',
                        onTap: canSendMouse
                            ? () => _sendDesktopShortcut(0x44)
                            : null,
                      ),
                      _desktopRailButton(
                        icon: Icons.grid_view,
                        label: '所有窗口',
                        onTap: canSendMouse
                            ? () => _sendDesktopShortcut(0x09)
                            : null,
                      ),
                      _desktopRailButton(
                        icon: Icons.fullscreen_exit,
                        label: '退出全屏',
                        onTap: _toggleDesktopFullscreen,
                      ),
                    ],
                  ),
                ),
              ],
            ),
            if (_desktopMappingCreated) _desktopMappingLayer(),
          ],
        ),
      ),
    );
  }

  Widget _buildLandscape(BuildContext context) {
    if (widget.showDesktop) return _buildDesktopFullscreen(context);
    final theme = Theme.of(context);
    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: SingleChildScrollView(
                child: Column(
                  children: [
                    Row(
                      children: [
                        IconButton(
                          tooltip: '返回',
                          onPressed: () => Navigator.pop(context),
                          icon: const Icon(Icons.arrow_back),
                        ),
                        Expanded(
                          child: Text(
                            _status,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleSmall,
                          ),
                        ),
                        if (!widget.showDesktop)
                          IconButton(
                            tooltip: '游戏模式',
                            onPressed: _canSend && _sender.supportsGameKeys
                                ? _openGameMode
                                : null,
                            icon: const Icon(Icons.sports_esports),
                          ),
                        IconButton(
                          tooltip: '调节灵敏度',
                          onPressed: _canSend ? _showSensitivityDialog : null,
                          icon: const Icon(Icons.speed),
                        ),
                        IconButton(
                          tooltip: '发送文本',
                          onPressed: _canSend ? _showTextInputDialog : null,
                          icon: const Icon(Icons.keyboard),
                        ),
                        if (!widget.showDesktop)
                          IconButton(
                            tooltip: '切换竖屏',
                            onPressed: _toggleOrientation,
                            icon: const Icon(Icons.screen_rotation),
                          ),
                      ],
                    ),
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Text(
                        '左侧轻点：左键 · 右侧轻点：右键 · 单指滑动：移动鼠标 · 双指滑动：滚动 · 左侧双击按住：拖拽',
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            Expanded(flex: 3, child: _touchSurface(context, landscape: true)),
          ],
        ),
      ),
    );
  }

  Widget _touchSurface(
    BuildContext context, {
    required bool landscape,
    bool fullscreen = false,
  }) {
    final theme = Theme.of(context);
    final radius = BorderRadius.circular(fullscreen ? 0 : 20);
    return Container(
      margin: fullscreen
          ? EdgeInsets.zero
          : const EdgeInsets.fromLTRB(16, 4, 16, 8),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest.withValues(
          alpha: 0.55,
        ),
        borderRadius: radius,
        border: fullscreen
            ? null
            : Border.all(color: theme.colorScheme.outlineVariant),
      ),
      child: ClipRRect(
        borderRadius: radius,
        child: LayoutBuilder(
          builder: (context, constraints) => Listener(
            key: const ValueKey('touchpad-surface'),
            behavior: HitTestBehavior.opaque,
            onPointerDown: widget.showDesktop
                ? null
                : (event) =>
                      _onPointerDown(event, landscape, constraints.maxWidth),
            onPointerMove: widget.showDesktop ? null : _onPointerMove,
            onPointerUp: widget.showDesktop ? null : _onPointerUp,
            onPointerCancel: widget.showDesktop ? null : _onPointerCancel,
            child: Semantics(
              label: widget.showDesktop
                  ? '电脑桌面，滑动移动鼠标，双指滚动'
                  : landscape
                  ? '触控板，左侧左键，右侧右键'
                  : '触控板',
              child: widget.showDesktop
                  ? Stack(
                      fit: StackFit.expand,
                      children: [
                        DesktopVideoView(
                          key: _desktopViewKey,
                          connection: widget.connection,
                          inputLabel: _desktopViewOnly
                              ? '仅观看'
                              : _desktopMappingEnabled
                              ? '按键映射 · ${_desktopGameCamera
                                    ? '游戏视角'
                                    : _desktopDirect && _desktopAbsoluteSupported
                                    ? '直接触控'
                                    : '鼠标指针'}'
                              : _desktopZoom
                              ? '画面缩放 / 平移'
                              : _dragPressed
                              ? '左键按住 · 拖拽中'
                              : _desktopDirect && _desktopAbsoluteSupported
                              ? '直接触控'
                              : '鼠标指针',
                          zoomEnabled: _desktopZoom,
                          inputEnabled:
                              !(_desktopMappingEnabled && _desktopGameCamera) &&
                              !_desktopToolsOpen &&
                              _desktopReady &&
                              (_desktopZoom || (_canSend && !_desktopViewOnly)),
                          allowRecovery: !_desktopSystemPending,
                          viewScale: _desktopViewScale,
                          viewRevision: _desktopViewRevision,
                          onSystemActionsSupported: (v) {
                            if (mounted) {
                              setState(() => _desktopSystemSupported = v);
                            }
                          },
                          onVideoSizeChanged: (v) {
                            if (mounted) setState(() => _desktopVideoSize = v);
                          },
                          directTouch:
                              _desktopDirect && _desktopAbsoluteSupported,
                          dragging: _dragPressed,
                          mouseButtonsHeld: _desktopMouseHeld,
                          sensitivity: _sensitivity,
                          onAbsoluteSupported: (v) {
                            _sender.supportsAbsoluteMouse = v;
                            if (mounted) {
                              setState(() => _desktopAbsoluteSupported = v);
                            }
                          },
                          onAbsolute: (p) => _send(
                            () => _sender.sendAbsoluteMouse(
                              (p.dx * 65535).round(),
                              (p.dy * 65535).round(),
                            ),
                          ),
                          onRelative: (p) => _send(
                            () => _sender.sendMouseMove(
                              p.dx.round(),
                              p.dy.round(),
                            ),
                          ),
                          onDrag: _setDesktopDrag,
                          onClick: (right) => _clickDesktopMouse(
                            right
                                ? RemoteMouseButton.right
                                : RemoteMouseButton.left,
                          ),
                          onScroll: (v) =>
                              _send(() => _sender.sendMouseScroll(v)),
                          showStats: _desktopShowStats,
                          onControlChanged: (id, allowed, relativeMouse) {
                            if (id != _desktopSessionId) {
                              _desktopSystemPending = false;
                            }
                            _desktopSessionId = id;
                            _sender.setDesktopReady(
                              allowed,
                              relativeMouse: relativeMouse,
                            );
                          },
                          onReadyChanged: (ready) {
                            if (mounted) {
                              _cancelGesture();
                              if (!ready) {
                                _desktopSessionId = null;
                                _sender.setDesktopReady(false);
                              }
                              setState(() => _desktopReady = ready);
                            }
                          },
                        ),
                        if (_desktopMappingCreated && !fullscreen)
                          _desktopMappingLayer(),
                      ],
                    )
                  : landscape
                  ? Align(
                      alignment: Alignment.bottomCenter,
                      child: Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: Row(
                          children: [
                            Expanded(
                              child: Semantics(
                                button: true,
                                label: '鼠标左键',
                                onTap: _canSend
                                    ? () => _send(
                                        () => _sender.sendMouseClick(
                                          RemoteMouseButton.left,
                                        ),
                                      )
                                    : null,
                                child: Center(
                                  child: Text(
                                    '左键',
                                    style: theme.textTheme.labelMedium,
                                  ),
                                ),
                              ),
                            ),
                            Expanded(
                              child: Semantics(
                                button: true,
                                label: '鼠标右键',
                                onTap: _canSend
                                    ? () => _send(
                                        () => _sender.sendMouseClick(
                                          RemoteMouseButton.right,
                                        ),
                                      )
                                    : null,
                                child: Center(
                                  child: Text(
                                    '右键',
                                    style: theme.textTheme.labelMedium,
                                  ),
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    )
                  : Center(
                      child: Text(
                        '单指移动 · 轻点左键\n双指轻点右键 · 双指滑动滚动\n双击后按住并移动以拖拽',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    if (widget.showDesktop && (_desktopFullscreen || _desktopMappingEnabled)) {
      return _buildDesktopFullscreen(context);
    }
    if (MediaQuery.orientationOf(context) == Orientation.landscape) {
      return _buildLandscape(context);
    }
    return Scaffold(
      appBar: AppBar(
        title: Text('控制 ${widget.peerName}'),
        actions: [
          if (widget.showDesktop)
            IconButton(
              tooltip: '桌面操作',
              icon: const Icon(Icons.tune),
              onPressed: _desktopReady ? _showDesktopActions : null,
            ),
          if (widget.showDesktop) _desktopZoomButton(),
          if (widget.showDesktop)
            IconButton(
              icon: const Icon(Icons.fullscreen),
              tooltip: '全屏显示桌面',
              onPressed: _toggleDesktopFullscreen,
            ),
          if (!widget.showDesktop)
            IconButton(
              icon: const Icon(Icons.screen_rotation),
              tooltip: '切换横屏',
              onPressed: _toggleOrientation,
            ),
          if (!widget.showDesktop)
            IconButton(
              icon: const Icon(Icons.sports_esports),
              tooltip: '游戏模式',
              onPressed: _canSend && _sender.supportsGameKeys
                  ? _openGameMode
                  : null,
            ),
          IconButton(
            icon: const Icon(Icons.keyboard),
            tooltip: '虚拟键盘输入',
            onPressed: _canSend ? _showTextInputDialog : null,
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            ListTile(
              dense: true,
              leading: Icon(
                _canSend ? Icons.check_circle : Icons.info_outline,
                color: _canSend
                    ? theme.colorScheme.primary
                    : theme.colorScheme.onSurfaceVariant,
              ),
              title: Text(_status),
            ),
            if (!widget.showDesktop)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Row(
                  children: [
                    const Icon(Icons.speed, size: 18),
                    const SizedBox(width: 8),
                    Text(
                      '灵敏度 ${_sensitivity.toStringAsFixed(1)}×',
                      style: theme.textTheme.bodySmall,
                    ),
                    Expanded(
                      child: Slider(
                        value: _sensitivity,
                        min: 1.5,
                        max: 5,
                        divisions: 14,
                        onChanged: _canSend
                            ? (value) => setState(() => _sensitivity = value)
                            : null,
                      ),
                    ),
                  ],
                ),
              ),
            Expanded(child: _touchSurface(context, landscape: false)),
            if (!widget.showDesktop || _desktopMouseButtons)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton.tonal(
                        onPressed: _canSend
                            ? () => _send(
                                () => _sender.sendMouseClick(
                                  RemoteMouseButton.left,
                                ),
                              )
                            : null,
                        child: const Text('左键'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    IconButton.filledTonal(
                      tooltip: '发送文本',
                      onPressed: _canSend ? _showTextInputDialog : null,
                      icon: const Icon(Icons.keyboard),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: FilledButton.tonal(
                        onPressed: _canSend
                            ? () => _send(
                                () => _sender.sendMouseClick(
                                  RemoteMouseButton.right,
                                ),
                              )
                            : null,
                        child: const Text('右键'),
                      ),
                    ),
                  ],
                ),
              ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 8),
              child: Text(
                widget.showDesktop
                    ? '普通滑动只移动鼠标；停住长按后再移动才拖拽，抬手松开。双指轻点右键、滑动滚动、捏合缩放。操作菜单可切换鼠标模式或仅观看。'
                    : '此功能传输控制输入，不显示电脑画面。当前局域网控制流未加密，请勿输入密码或敏感内容。',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
