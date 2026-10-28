import 'game_mode_screen.dart';

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

  const TouchpadScreen({
    super.key,
    required this.peerName,
    this.controlKey,
    this.onEncryptionRequired,
    required this.connection,
    this.onControlStateChanged,
  });

  @override
  State<TouchpadScreen> createState() => _TouchpadScreenState();
}

class _TouchpadScreenState extends State<TouchpadScreen> {
  late final RemoteInputSender _sender;
  late final StreamSubscription<ProtocolFrame> _frameSubscription;
  late final StreamSubscription<SessionState> _stateSubscription;
  final TextEditingController _textInputController = TextEditingController();
  final Map<int, Offset> _pointers = {};
  final Map<int, Offset> _pointerStarts = {};
  double _sensitivity = 3;
  bool _controlReady = false;
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
      _controlReady && widget.connection.state == SessionState.connected;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final orientation = MediaQuery.orientationOf(context);
    if (_orientation != null && _orientation != orientation) {
      _cancelGesture();
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

  @override
  void initState() {
    super.initState();
    _sender = RemoteInputSender(
      widget.connection,
      controlKey: widget.controlKey,
      onEncryptionRequired: widget.onEncryptionRequired,
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

  Future<void> _openGameMode() async {
    _cancelGesture();
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => GameModeScreen(
          sender: _sender,
          connection: widget.connection,
          restoreOrientations: _orientationOverridden
              ? [
                  DeviceOrientation.landscapeLeft,
                  DeviceOrientation.landscapeRight,
                ]
              : const [],
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
    if (!_canSend) return;
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
      _tapButton = landscape && event.localPosition.dx >= width / 2
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
    if (_orientationOverridden) {
      unawaited(SystemChrome.setPreferredOrientations([]));
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

  Widget _buildLandscape(BuildContext context) {
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

  Widget _touchSurface(BuildContext context, {required bool landscape}) {
    final theme = Theme.of(context);
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 4, 16, 8),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest.withValues(
          alpha: 0.55,
        ),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: theme.colorScheme.outlineVariant),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(20),
        child: LayoutBuilder(
          builder: (context, constraints) => Listener(
            key: const ValueKey('touchpad-surface'),
            behavior: HitTestBehavior.opaque,
            onPointerDown: (event) =>
                _onPointerDown(event, landscape, constraints.maxWidth),
            onPointerMove: _onPointerMove,
            onPointerUp: _onPointerUp,
            onPointerCancel: _onPointerCancel,
            child: Semantics(
              label: landscape ? '触控板，左侧左键，右侧右键' : '触控板',
              child: landscape
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
    if (MediaQuery.orientationOf(context) == Orientation.landscape) {
      return _buildLandscape(context);
    }
    return Scaffold(
      appBar: AppBar(
        title: Text('控制 ${widget.peerName}'),
        actions: [
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
                '此功能传输控制输入，不显示电脑画面。当前局域网控制流未加密，请勿输入密码或敏感内容。',
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
