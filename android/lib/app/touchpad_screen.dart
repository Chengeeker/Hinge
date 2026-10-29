import 'game_mode_screen.dart';
import 'desktop_video_view.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/protocol_frame.dart';
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
  late final StreamSubscription<ProtocolFrame> _frameSubscription;
  late final StreamSubscription<SessionState> _stateSubscription;
  final TextEditingController _textInputController = TextEditingController();
  final Map<int, Offset> _pointers = {};
  final Map<int, Offset> _pointerStarts = {};
  double _sensitivity = 3;
  bool _controlReady = false;
  bool _desktopReady = false;
  bool _desktopZoom = false;
  bool _desktopDirect = true, _desktopAbsoluteSupported = false;
  bool _desktopViewOnly = false;
  String get _modePreference =>
      'desktopInputMode.${widget.connection.peerInfo?.deviceId ?? widget.peerName}';
  bool _desktopGameMode = false;
  bool _desktopFullscreen = false;
  bool _desktopShowStats = false;
  List<DeviceOrientation> _desktopGameRestoreOrientations = const [];
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
    if (widget.showDesktop &&
        !_desktopGameMode &&
        _orientation != orientation) {
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
              setState(() => _desktopDirect = direct ?? true);
            }
          }, onError: (Object _) {});
    }
    _sender = RemoteInputSender(
      widget.connection,
      controlKey: widget.controlKey,
      onEncryptionRequired: widget.onEncryptionRequired,
      desktopTransport: widget.showDesktop ? _sendDesktopInput : null,
    );
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
    if (widget.showDesktop && _desktopViewOnly) return;
    _cancelGesture();
    final restore = _desktopFullscreen
        ? const [
            DeviceOrientation.landscapeLeft,
            DeviceOrientation.landscapeRight,
          ]
        : _orientationOverridden
        ? _orientation == Orientation.landscape
              ? [
                  DeviceOrientation.landscapeLeft,
                  DeviceOrientation.landscapeRight,
                ]
              : [DeviceOrientation.portraitUp, DeviceOrientation.portraitDown]
        : const <DeviceOrientation>[];
    if (widget.showDesktop) {
      setState(() {
        _desktopGameRestoreOrientations = restore;
        _desktopZoom = false;
        _desktopGameMode = true;
      });
      return;
    }
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
    if (releaseDrag && widget.connection.state == SessionState.connected) {
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
    _send(() => _sender.sendMouseUp(RemoteMouseButton.left));
  }

  void _showTextInputDialog() {
    if (!_canSend) return;
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
        if (_dragPressed && _controlReady) {
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
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed) _cancelGesture();
    if (state == AppLifecycleState.resumed &&
        _desktopFullscreen &&
        !_desktopGameMode) {
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
    _send(
      () => down
          ? _sender.sendMouseDown(RemoteMouseButton.left)
          : _sender.sendMouseUp(RemoteMouseButton.left),
    );
    if (mounted) setState(() => _dragPressed = down);
  }

  void _showDesktopActions() {
    if (!_controlReady || !_desktopReady) return;
    showModalBottomSheet<void>(
      context: context,
      builder: (sheetContext) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(20, 20, 20, 8),
              child: Text('桌面操作'),
            ),
            ListTile(
              leading: const Icon(Icons.touch_app),
              title: Text(
                _desktopDirect && _desktopAbsoluteSupported ? '✓ 直接触控' : '直接触控',
              ),
              subtitle: Text(
                _desktopAbsoluteSupported ? '点哪里操作哪里，按住滑动拖拽' : '需要更新 Windows 端',
              ),
              enabled: _desktopAbsoluteSupported,
              onTap: () {
                Navigator.pop(sheetContext);
                _cancelGesture();
                setState(() {
                  _desktopDirect = true;
                  _desktopZoom = false;
                  _desktopViewOnly = false;
                });
                unawaited(
                  const MethodChannel('hinge/desktop_video')
                      .invokeMethod<void>('saveInputMode', {
                        'peerId': _modePreference,
                        'direct': true,
                      })
                      .catchError((Object _) {}),
                );
              },
            ),
            ListTile(
              leading: const Icon(Icons.mouse),
              title: Text(
                !_desktopDirect || !_desktopAbsoluteSupported
                    ? '✓ 鼠标指针'
                    : '鼠标指针',
              ),
              subtitle: const Text('滑动移动，轻点左键，长按或双击按住拖拽'),
              onTap: () {
                Navigator.pop(sheetContext);
                _cancelGesture();
                setState(() {
                  _desktopDirect = false;
                  _desktopZoom = false;
                  _desktopViewOnly = false;
                });
                unawaited(
                  const MethodChannel('hinge/desktop_video')
                      .invokeMethod<void>('saveInputMode', {
                        'peerId': _modePreference,
                        'direct': false,
                      })
                      .catchError((Object _) {}),
                );
              },
            ),
            SwitchListTile(
              title: const Text('仅观看'),
              subtitle: const Text('查看画面，禁止鼠标和键盘输入'),
              value: _desktopViewOnly,
              onChanged: (v) {
                Navigator.pop(sheetContext);
                _cancelGesture();
                setState(() => _desktopViewOnly = v);
              },
            ),
            ListTile(
              leading: Icon(_dragPressed ? Icons.pan_tool : Icons.open_with),
              title: Text(_dragPressed ? '松开左键（结束拖拽）' : '按住左键拖拽'),
              subtitle: const Text('开启后滑动移动，完成后再次点击松开'),
              enabled: !_desktopViewOnly,
              onTap: () {
                Navigator.pop(sheetContext);
                _setDesktopDrag(!_dragPressed);
              },
            ),
            ListTile(
              leading: const Icon(Icons.mouse),
              title: const Text('鼠标左键'),
              onTap: () {
                Navigator.pop(sheetContext);
                _send(() => _sender.sendMouseClick(RemoteMouseButton.left));
              },
            ),
            ListTile(
              leading: const Icon(Icons.mouse_outlined),
              title: const Text('鼠标右键'),
              onTap: () {
                Navigator.pop(sheetContext);
                _send(() => _sender.sendMouseClick(RemoteMouseButton.right));
              },
            ),
            ListTile(
              leading: Icon(_desktopZoom ? Icons.mouse : Icons.zoom_in),
              title: Text(_desktopZoom ? '切回鼠标操作' : '缩放/平移画面'),
              onTap: () {
                Navigator.pop(sheetContext);
                _cancelGesture();
                setState(() => _desktopZoom = !_desktopZoom);
              },
            ),
            if (_sender.supportsGameKeys)
              ListTile(
                leading: const Icon(Icons.sports_esports),
                title: const Text('游戏模式'),
                onTap: () {
                  Navigator.pop(sheetContext);
                  unawaited(_openGameMode());
                },
              ),
            SwitchListTile(
              secondary: const Icon(Icons.monitor_heart_outlined),
              title: const Text('性能信息'),
              value: _desktopShowStats,
              onChanged: (value) {
                Navigator.pop(sheetContext);
                setState(() => _desktopShowStats = value);
              },
            ),
          ],
        ),
      ),
    );
  }

  void _sendDesktopShortcut(int key) {
    _send(() {
      _sender.sendKey(0x5B, true);
      _sender.sendKey(key, true);
      _sender.sendKey(key, false);
      _sender.sendKey(0x5B, false);
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
          child: SizedBox.expand(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(icon, color: color, size: 24),
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
    );
  }

  Widget _buildDesktopFullscreen(BuildContext context) => PopScope<Object?>(
    canPop: false,
    onPopInvokedWithResult: (didPop, result) {
      if (!didPop) unawaited(_toggleDesktopFullscreen());
    },
    child: Scaffold(
      backgroundColor: Colors.black,
      body: Row(
        children: [
          Expanded(
            child: _touchSurface(context, landscape: true, fullscreen: true),
          ),
          SizedBox(
            width: (MediaQuery.sizeOf(context).width * 0.1)
                .clamp(72.0, 88.0)
                .toDouble(),
            child: ColoredBox(
              color: const Color(0xFF050505),
              child: SafeArea(
                left: false,
                right: false,
                child: Column(
                  children: [
                    Expanded(
                      child: _desktopRailButton(
                        icon: Icons.tune,
                        label: '操作',
                        onTap: _canSend ? _showDesktopActions : null,
                      ),
                    ),
                    Expanded(
                      child: _desktopRailButton(
                        icon: Icons.keyboard,
                        label: '键盘',
                        onTap: _canSend ? _showTextInputDialog : null,
                      ),
                    ),
                    Expanded(
                      child: _desktopRailButton(
                        icon: Icons.desktop_windows_outlined,
                        label: '显示桌面',
                        onTap: _canSend
                            ? () => _sendDesktopShortcut(0x44)
                            : null,
                      ),
                    ),
                    Expanded(
                      child: _desktopRailButton(
                        icon: Icons.grid_view,
                        label: '所有窗口',
                        onTap: _canSend
                            ? () => _sendDesktopShortcut(0x09)
                            : null,
                      ),
                    ),
                    Expanded(
                      child: _desktopRailButton(
                        icon: Icons.fullscreen_exit,
                        label: '退出全屏',
                        onTap: _toggleDesktopFullscreen,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );

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
                  ? DesktopVideoView(
                      key: _desktopViewKey,
                      connection: widget.connection,
                      inputLabel: _desktopViewOnly
                          ? '仅观看'
                          : _desktopZoom
                          ? '画面缩放 / 平移'
                          : _dragPressed
                          ? '左键按住 · 拖拽中'
                          : _desktopDirect && _desktopAbsoluteSupported
                          ? '直接触控'
                          : '鼠标指针',
                      zoomEnabled: _desktopZoom,
                      inputEnabled:
                          _canSend &&
                          (!_desktopViewOnly || _desktopZoom) &&
                          !_desktopGameMode,
                      directTouch: _desktopDirect && _desktopAbsoluteSupported,
                      dragging: _dragPressed,
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
                        () => _sender.sendMouseMove(p.dx.round(), p.dy.round()),
                      ),
                      onDrag: _setDesktopDrag,
                      onClick: (right) => _send(
                        () => _sender.sendMouseClick(
                          right
                              ? RemoteMouseButton.right
                              : RemoteMouseButton.left,
                        ),
                      ),
                      onScroll: (v) => _send(() => _sender.sendMouseScroll(v)),
                      showStats: _desktopShowStats && !_desktopGameMode,
                      onControlChanged: (id, allowed, relativeMouse) {
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
    if (widget.showDesktop && _desktopGameMode) {
      return Stack(
        fit: StackFit.expand,
        children: [
          IgnorePointer(
            child: _touchSurface(context, landscape: true, fullscreen: true),
          ),
          GameModeScreen(
            sender: _sender,
            connection: widget.connection,
            restoreOrientations: _desktopGameRestoreOrientations,
            onExit: () {
              _cancelGesture();
              setState(() => _desktopGameMode = false);
              if (_desktopFullscreen) {
                WidgetsBinding.instance.addPostFrameCallback((_) {
                  if (mounted && _desktopFullscreen) {
                    unawaited(
                      SystemChrome.setEnabledSystemUIMode(
                        SystemUiMode.immersiveSticky,
                      ),
                    );
                  }
                });
              }
            },
          ),
        ],
      );
    }
    if (widget.showDesktop && _desktopFullscreen) {
      return _buildDesktopFullscreen(context);
    }
    if (MediaQuery.orientationOf(context) == Orientation.landscape) {
      return _buildLandscape(context);
    }
    return Scaffold(
      appBar: AppBar(
        title: Text('控制 ${widget.peerName}'),
        actions: [
          if (widget.showDesktop) _desktopZoomButton(),
          if (widget.showDesktop)
            IconButton(
              icon: const Icon(Icons.fullscreen),
              tooltip: '全屏显示桌面',
              onPressed: _toggleDesktopFullscreen,
            ),
          IconButton(
            icon: const Icon(Icons.screen_rotation),
            tooltip: '切换横屏',
            onPressed: _toggleOrientation,
          ),
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
                    ? '操作菜单可切换直接触控、鼠标指针或仅观看。双指轻点右键，平移滚动，捏合缩放；长按拖拽，操作菜单也可锁定左键。缩放/平移模式只调整手机画面。'
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
