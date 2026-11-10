import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/game_controls.dart';
import '../core/remote_input_model.dart';
import '../core/remote_input_sender.dart';
import '../core/session_manager.dart';

class GameModeScreen extends StatefulWidget {
  final RemoteInputSender sender;
  final SessionConnection connection;
  final List<DeviceOrientation> restoreOrientations;
  final VoidCallback? onExit;
  final VoidCallback? onSettings;
  final bool inputEnabled;
  final bool desktop;
  final bool gameCamera;
  final double? mouseSensitivity;
  final ValueChanged<double>? onSensitivityChanged;
  final void Function(RemoteMouseButton, bool)? onMouseButton;
  const GameModeScreen({
    super.key,
    required this.sender,
    required this.connection,
    this.restoreOrientations = const [],
    this.onExit,
    this.onSettings,
    this.inputEnabled = true,
    this.desktop = false,
    this.gameCamera = false,
    this.mouseSensitivity,
    this.onSensitivityChanged,
    this.onMouseButton,
  });
  @override
  GameModeScreenState createState() => GameModeScreenState();
}

class GameModeScreenState extends State<GameModeScreen>
    with WidgetsBindingObserver {
  late final _store = GamePresetStore(desktop: widget.desktop);
  late final GameInputState _input;
  StreamSubscription<SessionState>? _subscription;
  bool _loading = true, _editing = false, _suspended = false, _haptics = true;
  String? _error;
  GamePreset? _draft;
  String? _selectedControl;
  bool _editorProperties = false;
  final TransformationController _editorView = TransformationController();
  final List<GamePreset> _undo = [];
  int _epoch = 0;
  Orientation? _orientation;
  final Map<int, Offset> _camera = {};
  double _remainderX = 0, _remainderY = 0, _sensitivity = 3;
  double _opacity = .55;
  bool _canvasDragging = false;
  bool _canvasMulti = false;
  bool _externalSyncPending = false;
  Offset? _canvasCentroid;
  Offset? _canvasAnchor;
  bool get _captureCamera => !widget.desktop || widget.gameCamera;
  double get _mouseSensitivity => widget.mouseSensitivity ?? _sensitivity;
  GamePreset get _preset => _draft ?? _store.current;
  String get presetName => _loading || _error != null ? '桌面精简布局' : _preset.name;

  void editCurrentPreset() {
    if (_loading || _error != null) return;
    _release();
    setState(() {
      _draft ??= _store.current.copy();
      _undo.clear();
      _editorProperties = false;
      _editorView.value = Matrix4.identity();
      _selectedControl = null;
      _editing = true;
    });
  }

  bool get _canInput =>
      widget.inputEnabled &&
      !_loading &&
      _error == null &&
      !_editing &&
      !_suspended &&
      widget.sender.ready &&
      widget.sender.supportsGameKeys &&
      widget.connection.state == SessionState.connected;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    GamePresetStore.syncRevision.addListener(_externalPresetSync);
    if (widget.onExit == null) {
      unawaited(
        SystemChrome.setPreferredOrientations([
          DeviceOrientation.landscapeLeft,
          DeviceOrientation.landscapeRight,
        ]),
      );
    }
    _input = GameInputState((key, down) {
      if (key > 0) {
        widget.sender.sendKey(key, down);
      } else {
        final button = RemoteMouseButton.values[-key - 1];
        if (widget.onMouseButton != null) {
          widget.onMouseButton!(button, down);
        } else {
          down
              ? widget.sender.sendMouseDown(button)
              : widget.sender.sendMouseUp(button);
        }
      }
    });
    widget.sender.addListener(_connectionChanged);
    _subscription = widget.connection.stateStream.listen(
      (_) => _connectionChanged(),
    );
    if (widget.onExit == null) {
      unawaited(
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
      );
    }
    _load();
  }

  Future<void> _load() async {
    try {
      await _store.load();
      try {
        _haptics =
            await const MethodChannel('hinge/platform')
                .invokeMethod<bool>('hapticFeedbackEnabled') ??
            true;
      } on PlatformException {
        /* Keep system haptic preference. */
      }
      if (mounted) setState(() => _loading = false);
    } on Object {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = '读取预设失败。为保护原有布局，本次不会覆盖保存。请退出后重试。';
        });
      }
    }
  }

  void _connectionChanged() {
    if (!mounted) return;
    if (!widget.sender.ready ||
        !widget.sender.supportsGameKeys ||
        widget.connection.state != SessionState.connected) {
      _release();
    }
    setState(() {});
  }

  @override
  void didUpdateWidget(covariant GameModeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.inputEnabled && !widget.inputEnabled ||
        oldWidget.gameCamera != widget.gameCamera) {
      _release();
    }
  }

  void _externalPresetSync() {
    if (!mounted) return;
    if (_editing) {
      _externalSyncPending = true;
      return;
    }
    unawaited(_reloadPresets());
  }

  Future<void> _reloadPresets() async {
    _release();
    try {
      await _store.load();
      if (mounted) setState(() => _epoch++);
    } on Object {
      if (mounted) _snack('电脑端同步的布局无法读取，已保留当前画面。');
    }
  }

  void _release() {
    _input.releaseAll();
    _camera.clear();
    _canvasDragging = false;
    _canvasMulti = false;
    _canvasCentroid = null;
    _canvasAnchor = null;
    _remainderX = _remainderY = 0;
    _epoch++;
  }

  void _endCanvasPointer(int pointer) {
    if (!_camera.containsKey(pointer)) return;
    if (_canvasMulti) {
      _input.update('canvas-drag', {});
      _camera.clear();
      _canvasDragging = false;
      _canvasMulti = false;
      _canvasCentroid = null;
      _remainderX = _remainderY = 0;
    } else {
      _camera.remove(pointer);
    }
  }

  void _cameraDown(PointerDownEvent event) {
    if (!_canInput) return;
    _camera[event.pointer] = event.localPosition;
    if (_camera.length >= 2) {
      _canvasMulti = true;
      _canvasAnchor = _canvasCentroid =
          _camera.values.reduce((a, b) => a + b) / _camera.length.toDouble();
    }
  }

  void _cameraMove(PointerMoveEvent event) {
    if (!_canInput || !_camera.containsKey(event.pointer)) return;
    final previous = _camera[event.pointer]!;
    _camera[event.pointer] = event.localPosition;
    if (_canvasMulti) {
      final center =
          _camera.values.reduce((a, b) => a + b) / _camera.length.toDouble();
      if (!_canvasDragging) {
        if ((center - _canvasAnchor!).distance < 8) return;
        _canvasDragging = true;
        _input.update('canvas-drag', {-1});
        _feedback();
      }
      _moveMouse(center - _canvasCentroid!);
      _canvasCentroid = center;
    } else {
      _moveMouse(event.localPosition - previous);
    }
  }

  void _moveMouse(Offset delta) {
    _remainderX += delta.dx * _mouseSensitivity;
    _remainderY += delta.dy * _mouseSensitivity;
    final dx = _remainderX.truncate(), dy = _remainderY.truncate();
    _remainderX -= dx;
    _remainderY -= dy;
    if (dx != 0 || dy != 0) {
      widget.sender.sendMouseMove(dx, dy, relative: _captureCamera);
    }
  }

  void _feedback() {
    if (_haptics) unawaited(HapticFeedback.selectionClick());
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final orientation = MediaQuery.orientationOf(context);
    if (_orientation != null && _orientation != orientation) _release();
    _orientation = orientation;
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (!mounted) return;
    setState(() {
      _suspended = state != AppLifecycleState.resumed;
      if (_suspended) _release();
    });
    if (!_suspended && widget.onExit == null) {
      unawaited(
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
      );
    }
  }

  @override
  void dispose() {
    _editorView.dispose();
    _release();
    GamePresetStore.syncRevision.removeListener(_externalPresetSync);
    widget.sender.removeListener(_connectionChanged);
    unawaited(_subscription?.cancel());
    WidgetsBinding.instance.removeObserver(this);
    // Embedded controls leave orientation and system bars to the desktop owner.
    if (widget.onExit == null) {
      unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge));
      unawaited(
        SystemChrome.setPreferredOrientations(widget.restoreOrientations),
      );
    }
    super.dispose();
  }

  void _snack(String message) {
    if (mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Future<bool> _saveStore() async {
    try {
      await _store.save();
      return true;
    } on Object {
      _snack('保存失败，原文件保留。请重试。');
      return false;
    }
  }

  Future<String?> _askName(String initial) async {
    final controller = TextEditingController(text: initial);
    final value = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('预设名称'),
        content: TextField(
          controller: controller,
          maxLength: 40,
          autofocus: true,
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () {
              if (controller.text.trim().isNotEmpty) {
                Navigator.pop(ctx, controller.text.trim());
              }
            },
            child: const Text('确定'),
          ),
        ],
      ),
    );
    await Future<void>.delayed(Duration.zero);
    controller.dispose();
    return value;
  }

  Future<bool> _confirm(String text) async =>
      await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text(text),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('确定'),
            ),
          ],
        ),
      ) ??
      false;

  Future<void> openSettings({bool preferencesOnly = false}) async {
    if (_loading || _error != null) return;
    if (_editing && !preferencesOnly) {
      await _exit();
      if (!mounted || _editing) return;
    }
    _release();
    setState(() => _suspended = true);
    try {
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        useSafeArea: true,
        constraints: BoxConstraints(maxWidth: widget.desktop ? 420 : 640),
        builder: (sheetContext) => Theme(
          data: widget.desktop
              ? Theme.of(context).copyWith(
                  textTheme: Theme.of(context).textTheme.copyWith(
                    titleLarge: Theme.of(context).textTheme.titleLarge
                        ?.copyWith(fontSize: 18),
                    bodyLarge: Theme.of(context).textTheme.bodyLarge
                        ?.copyWith(fontSize: 13),
                    bodyMedium: Theme.of(context).textTheme.bodyMedium
                        ?.copyWith(fontSize: 13),
                    bodySmall: Theme.of(context).textTheme.bodySmall
                        ?.copyWith(fontSize: 11),
                  ),
                  listTileTheme: const ListTileThemeData(
                    minTileHeight: 48,
                    dense: true,
                  ),
                )
              : Theme.of(context),
          child: StatefulBuilder(
            builder: (ctx, refresh) => SizedBox(
              height: MediaQuery.sizeOf(ctx).height * .9,
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          preferencesOnly
                              ? '映射设置'
                              : widget.desktop
                              ? '操控方案'
                              : '游戏预设',
                          style: Theme.of(ctx).textTheme.titleLarge,
                        ),
                      ),
                      if (!preferencesOnly)
                        TextButton(
                          onPressed: () {
                            Navigator.pop(sheetContext);
                            editCurrentPreset();
                          },
                          child: const Text('编辑布局'),
                        ),
                    ],
                  ),
                  Text(
                    '空白区域滑动：移动视角 · 按住按钮：持续按键 · 多指可同时操作',
                    style: Theme.of(ctx).textTheme.bodySmall,
                  ),
                  const SizedBox(height: 8),
                  if (!preferencesOnly) ...[
                    OutlinedButton.icon(
                      icon: const Icon(Icons.file_open_outlined),
                      label: const Text('导入预设文件'),
                      onPressed: () async {
                        if (_store.presets.length >= 32) {
                          _snack('最多保存32套预设');
                          return;
                        }
                        try {
                          final raw = await GamePresetStore.channel
                              .invokeMethod<String>('importPreset');
                          if (raw == null || !mounted) return;
                          await _store.importPreset(raw);
                          if (mounted && ctx.mounted) {
                            setState(() {});
                            refresh(() {});
                            _snack('已导入为新预设，原有方案保留');
                          }
                        } on Object {
                          if (mounted) _snack('无法导入，请使用有效的 Hinge 预设文件并检查存储空间');
                        }
                      },
                    ),
                    for (final preset in _store.presets)
                      ListTile(
                        leading: Icon(
                          preset.id == _store.current.id
                              ? Icons.radio_button_checked
                              : Icons.radio_button_off,
                        ),
                        title: Text(preset.name),
                        subtitle: Text('${preset.controls.length} 个控件'),
                        onTap: () async {
                          final old = _store.selected;
                          _store.selected = preset.id;
                          if (!await _saveStore()) _store.selected = old;
                          if (mounted && ctx.mounted) {
                            setState(() {});
                            refresh(() {});
                          }
                        },
                        trailing: PopupMenuButton<String>(
                          onSelected: (action) async {
                            if (action == 'export') {
                              try {
                                final saved = await GamePresetStore.channel
                                    .invokeMethod<bool>(
                                      'exportPreset',
                                      jsonEncode(preset.toJson()),
                                    );
                                if (saved == true && mounted) {
                                  _snack('预设已导出，可发送此文件给其他设备或用户');
                                }
                              } on Object {
                                if (mounted) _snack('导出失败，请检查图片或保存位置后重试');
                              }
                              return;
                            }
                            if (action == 'delete') {
                              if (_store.presets.length == 1) {
                                _snack('至少保留一套预设');
                                return;
                              }
                              if (!await _confirm('删除“${preset.name}”？')) {
                                return;
                              }
                              final index = _store.presets.indexOf(preset),
                                  oldSelected = _store.selected;
                              _store.presets.removeAt(index);
                              if (_store.selected == preset.id) {
                                _store.selected = _store.presets.first.id;
                              }
                              if (!await _saveStore()) {
                                _store.presets.insert(index, preset);
                                _store.selected = oldSelected;
                              }
                            } else {
                              if (action == 'copy' &&
                                  _store.presets.length >= 32) {
                                _snack('最多保存32套预设');
                                return;
                              }
                              final name = await _askName(
                                action == 'copy'
                                    ? '${preset.name} 副本'
                                    : preset.name,
                              );
                              if (name == null || !mounted) return;
                              final oldName = preset.name;
                              final copy = preset.copy(
                                id: GamePreset.newId(),
                                name: name,
                              );
                              if (action == 'copy') {
                                _store.presets.add(copy);
                              } else {
                                preset.name = name;
                              }
                              if (!await _saveStore()) {
                                if (action == 'copy') {
                                  _store.presets.remove(copy);
                                } else {
                                  preset.name = oldName;
                                }
                              }
                            }
                            if (mounted && ctx.mounted) {
                              setState(() {});
                              refresh(() {});
                            }
                          },
                          itemBuilder: (_) => const [
                            PopupMenuItem(
                              value: 'export',
                              child: Text('导出预设文件'),
                            ),
                            PopupMenuItem(value: 'rename', child: Text('重命名')),
                            PopupMenuItem(value: 'copy', child: Text('复制为新预设')),
                            PopupMenuItem(value: 'delete', child: Text('删除')),
                          ],
                        ),
                      ),
                    OutlinedButton.icon(
                      icon: const Icon(Icons.add),
                      label: Text(widget.desktop ? '从桌面布局新建' : '从基础布局新建'),
                      onPressed: () async {
                        if (_store.presets.length >= 32) {
                          _snack('最多保存32套预设');
                          return;
                        }
                        final name = await _askName(
                          widget.desktop ? '新桌面方案' : '新游戏布局',
                        );
                        if (name == null || !mounted) return;
                        final preset = widget.desktop
                            ? GamePreset.desktop().copy(
                                id: GamePreset.newId(),
                                name: name,
                              )
                            : (GamePreset.basic()..name = name);
                        final oldSelected = _store.selected;
                        _store.presets.add(preset);
                        _store.selected = preset.id;
                        if (!await _saveStore()) {
                          _store.presets.remove(preset);
                          _store.selected = oldSelected;
                        }
                        if (mounted && ctx.mounted) {
                          setState(() {});
                          refresh(() {});
                        }
                      },
                    ),
                  ],
                  if (preferencesOnly || !widget.desktop) ...[
                    SwitchListTile(
                      title: const Text('触控反馈'),
                      value: _haptics,
                      onChanged: (value) {
                        setState(() => _haptics = value);
                        refresh(() {});
                      },
                    ),
                    Text(
                      '${widget.desktop ? '鼠标' : '视角'}灵敏度 ${_mouseSensitivity.toStringAsFixed(1)}',
                    ),
                    Slider(
                      value: _mouseSensitivity,
                      min: 1.5,
                      max: 5,
                      divisions: 14,
                      onChanged: (value) {
                        setState(() => _sensitivity = value);
                        widget.onSensitivityChanged?.call(value);
                        refresh(() {});
                      },
                    ),
                    if (widget.desktop) ...[
                      Text('按键不透明度 ${(_opacity * 100).round()}%'),
                      Slider(
                        value: _opacity,
                        min: .2,
                        max: .9,
                        divisions: 14,
                        onChanged: (value) {
                          setState(() => _opacity = value);
                          refresh(() {});
                        },
                      ),
                    ],
                    Text(
                      widget.desktop
                          ? '沿用操作面板所选的触控或指针模式；按住映射的鼠标键滑动可拖拽。'
                          : '此模式控制电脑键鼠；电脑画面仍需在电脑屏幕查看。部分游戏或管理员窗口可能不接受系统模拟输入。',
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      );
    } finally {
      if (mounted) {
        setState(() {
          _suspended = false;
          _epoch++;
        });
      }
    }
  }

  Future<int?> _chooseKey(int current, {bool keyboardOnly = false}) async {
    final keys = [
      0,
      if (!keyboardOnly) ...[-1, -2, -3],
      ...List.generate(26, (i) => 65 + i),
      ...List.generate(10, (i) => 48 + i),
      32,
      27,
      9,
      13,
      8,
      16,
      17,
      18,
      160,
      161,
      162,
      163,
      164,
      165,
      91,
      92,
      37,
      38,
      39,
      40,
      33,
      34,
      35,
      36,
      45,
      46,
      20,
      19,
      44,
      ...List.generate(24, (i) => 112 + i),
      ...List.generate(16, (i) => 96 + i),
      144,
      186,
      187,
      188,
      189,
      190,
      191,
      192,
      219,
      220,
      221,
      222,
    ];
    return showDialog<int>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('映射电脑按键'),
        content: SizedBox(
          width: 480,
          child: SingleChildScrollView(
            child: Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final key in keys)
                  ChoiceChip(
                    label: Text(gameKeyName(key)),
                    selected: key == current,
                    onSelected: (_) => Navigator.pop(ctx, key),
                  ),
                ActionChip(
                  label: const Text('其他虚拟键码…'),
                  onPressed: () async {
                    final controller = TextEditingController();
                    final key = await showDialog<int>(
                      context: ctx,
                      builder: (inner) => AlertDialog(
                        title: const Text('Windows 虚拟键码'),
                        content: TextField(
                          controller: controller,
                          keyboardType: TextInputType.number,
                          decoration: const InputDecoration(
                            helperText: '十进制 8–254（如 176：下一曲）',
                          ),
                        ),
                        actions: [
                          TextButton(
                            onPressed: () => Navigator.pop(inner),
                            child: const Text('取消'),
                          ),
                          FilledButton(
                            onPressed: () {
                              final value = int.tryParse(controller.text);
                              if (value != null && value >= 8 && value <= 254) {
                                Navigator.pop(inner, value);
                              }
                            },
                            child: const Text('确定'),
                          ),
                        ],
                      ),
                    );
                    await Future<void>.delayed(Duration.zero);
                    controller.dispose();
                    if (key != null && ctx.mounted) Navigator.pop(ctx, key);
                  },
                ),
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
        ],
      ),
    );
  }

  Future<void> _editControl(GameControl control) async {
    _rememberEdit();
    setState(() {
      _selectedControl = control.id;
      _editorProperties = true;
    });
  }

  Widget _controlFields(BuildContext ctx, GameControl control) {
    void refresh(VoidCallback change) => setState(change);
    void close() => setState(() => _editorProperties = false);
    return ListView(
      padding: const EdgeInsets.all(12),
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                control.joystick ? '方向摇杆' : '编辑按钮',
                style: Theme.of(ctx).textTheme.titleLarge,
              ),
            ),
            IconButton(
              tooltip: '完成',
              onPressed: () => close(),
              icon: const Icon(Icons.check),
            ),
          ],
        ),
        TextFormField(
          key: ValueKey('label-${control.id}'),
          initialValue: control.label,
          maxLength: 64,
          decoration: const InputDecoration(labelText: '显示文字 / 字母 / 数字'),
          onChanged: (value) => setState(() => control.label = value),
        ),
        if (!control.joystick)
          ListTile(
            title: const Text('电脑按键'),
            subtitle: Text(gameKeyName(control.binding)),
            trailing: const Icon(Icons.keyboard),
            onTap: () async {
              final key = await _chooseKey(control.binding);
              if (key != null && mounted && ctx.mounted) {
                setState(() => control.binding = key);
                refresh(() {});
              }
            },
          ),
        if (control.joystick)
          for (int i = 0; i < 4; i++)
            ListTile(
              title: Text(['向上', '向左', '向下', '向右'][i]),
              subtitle: Text(gameKeyName(control.directions[i])),
              onTap: () async {
                final key = await _chooseKey(
                  control.directions[i],
                  keyboardOnly: true,
                );
                if (key != null && mounted && ctx.mounted) {
                  setState(() => control.directions[i] = key);
                  refresh(() {});
                }
              },
            ),
        if (!control.joystick)
          DropdownButtonFormField<String>(
            initialValue: control.shape,
            isExpanded: true,
            decoration: const InputDecoration(labelText: '控件外观'),
            items: const [
              DropdownMenuItem(value: 'auto', child: Text('按原布局')),
              DropdownMenuItem(value: 'circle', child: Text('圆形（锁定比例）')),
              DropdownMenuItem(value: 'rounded', child: Text('圆角矩形')),
              DropdownMenuItem(value: 'rect', child: Text('直角矩形')),
            ],
            onChanged: (value) {
              if (value == null) return;
              setState(() {
                control.shape = value;
                if (value == 'circle') {
                  control.aspect = 1;
                  control.cornerRadius = .5;
                }
              });
              refresh(() {});
            },
          ),
        if (!control.joystick && control.shape == 'rounded') ...[
          Text('圆角比例 ${(control.cornerRadius * 100).round()}%'),
          Slider(
            value: control.cornerRadius,
            min: 0,
            max: .5,
            onChanged: (value) {
              setState(() => control.cornerRadius = value);
              refresh(() {});
            },
          ),
        ],
        Text('大小 ${(control.size * 100).round()}%'),
        Slider(
          value: control.size,
          min: .10,
          max: .48,
          onChanged: (value) {
            setState(() => control.size = value);
            refresh(() {});
          },
        ),
        if (!control.joystick && control.shape != 'circle') ...[
          const Text('宽高比'),
          Slider(
            value: control.aspect,
            min: .5,
            max: 3,
            onChanged: (value) {
              setState(() => control.aspect = value);
              refresh(() {});
            },
          ),
        ],
        const Text('位置也可返回编辑画布后直接拖动'),
        Text('水平 ${(control.x * 100).round()}%'),
        Slider(
          value: control.x,
          onChanged: (value) {
            setState(() => control.x = value);
            refresh(() {});
          },
        ),
        Text('垂直 ${(control.y * 100).round()}%'),
        Slider(
          value: control.y,
          onChanged: (value) {
            setState(() => control.y = value);
            refresh(() {});
          },
        ),
        Wrap(
          spacing: 8,
          children: [
            OutlinedButton.icon(
              icon: const Icon(Icons.image),
              label: const Text('选择图片'),
              onPressed: () async {
                try {
                  final path = await GamePresetStore.channel
                      .invokeMethod<String>('icon');
                  if (path != null && mounted && ctx.mounted) {
                    setState(() => control.image = path);
                    refresh(() {});
                  }
                } on Object {
                  _snack('无法导入图片，请换一张图片重试。');
                }
              },
            ),
            if (control.image != null)
              TextButton(
                onPressed: () {
                  setState(() => control.image = null);
                  refresh(() {});
                },
                child: const Text('移除图片'),
              ),
            TextButton.icon(
              icon: const Icon(Icons.delete_outline),
              label: const Text('删除控件'),
              onPressed: () {
                setState(() {
                  _draft!.controls.remove(control);
                  _selectedControl = null;
                });
                close();
              },
            ),
          ],
        ),
      ],
    );
  }

  Future<void> _saveDraft() async {
    final index = _store.presets.indexWhere((p) => p.id == _draft!.id);
    if (index < 0) {
      _snack('原方案已改变，请保留修改并重新选择方案。');
      return;
    }
    final old = _store.presets[index];
    _store.presets[index] = _draft!;
    if (await _saveStore()) {
      if (mounted) {
        setState(() {
          _draft = null;
          _editing = false;
          _selectedControl = null;
          _epoch++;
        });
      }
    } else {
      _store.presets[index] = old;
    }
  }

  Future<void> _exit() async {
    _release();
    if (_editing) {
      if (await _confirm('放弃未保存的布局修改？') && mounted) {
        setState(() {
          _draft = null;
          _editing = false;
          _selectedControl = null;
        });
        if (_externalSyncPending) {
          _externalSyncPending = false;
          await _reloadPresets();
        }
      }
    } else if (mounted) {
      if (widget.onExit != null) {
        widget.onExit!();
      } else {
        Navigator.pop(context);
      }
    }
  }

  void _rememberEdit() {
    if (_draft == null) return;
    _undo.add(_draft!.copy());
    if (_undo.length > 20) _undo.removeAt(0);
  }

  List<Widget> _editorTools(Size area) {
    final matches = _preset.controls.where((c) => c.id == _selectedControl);
    final control = matches.isEmpty ? null : matches.first;

    final rect = control == null ? null : _rect(control, area);
    var resizeWidth = rect?.width ?? 0.0;
    var resizeHeight = rect?.height ?? 0.0;
    return [
      if (control != null)
        Positioned(
          left: (rect!.right - 24).clamp(0, area.width - 48),
          top: (rect.bottom - 24).clamp(0, area.height - 48),
          child: Semantics(
            label: '拖动调整大小',
            child: GestureDetector(
              key: const ValueKey('control-resize-handle'),
              behavior: HitTestBehavior.opaque,
              onScaleStart: (_) {
                _rememberEdit();
                resizeWidth = rect.width;
                resizeHeight = rect.height;
              },
              onScaleUpdate: (event) => setState(() {
                if (event.pointerCount != 1) return;
                resizeWidth += event.focalPointDelta.dx / _editorScale;
                resizeHeight += event.focalPointDelta.dy / _editorScale;
                final round =
                    control.joystick ||
                    control.shape == 'circle' ||
                    control.shape == 'auto' && (control.aspect - 1).abs() < .02;
                final height = resizeHeight.clamp(
                  48.0,
                  math.min(area.width, area.height) * .48,
                );
                control.size = (height / math.min(area.width, area.height))
                    .clamp(.10, .48);
                if (!round) {
                  control.aspect =
                      (resizeWidth.clamp(48.0, area.width) / height).clamp(
                        .5,
                        3,
                      );
                }
              }),
              child: SizedBox(
                width: 48,
                height: 48,
                child: Center(
                  child: Container(
                    width: 24,
                    height: 24,
                    decoration: BoxDecoration(
                      color: Theme.of(context).colorScheme.primaryContainer,
                      borderRadius: BorderRadius.circular(6),
                    ),
                    child: const Icon(Icons.open_in_full, size: 16),
                  ),
                ),
              ),
            ),
          ),
        ),
    ];
  }

  Widget _buildEditor(Size area) {
    final theme = Theme.of(context);
    final selected = _preset.controls.where((c) => c.id == _selectedControl);
    final control = selected.isEmpty ? null : selected.first;
    void nudge(double x, double y) {
      if (control == null) return;
      _rememberEdit();
      setState(() {
        control.x = (control.x + x / area.width).clamp(0, 1);
        control.y = (control.y + y / area.height).clamp(0, 1);
      });
    }

    Widget action(String label, IconData icon, VoidCallback? onPressed) =>
        Tooltip(
          message: label,
          child: TextButton.icon(
            onPressed: onPressed,
            icon: Icon(icon, size: 20),
            label: Text(label),
          ),
        );
    final dock = Theme(
      data: theme.copyWith(
        textTheme: theme.textTheme.copyWith(
          titleLarge: theme.textTheme.titleLarge?.copyWith(fontSize: 16),
          titleMedium: theme.textTheme.titleMedium?.copyWith(fontSize: 14),
          bodyLarge: theme.textTheme.bodyLarge?.copyWith(fontSize: 13),
          bodyMedium: theme.textTheme.bodyMedium?.copyWith(fontSize: 13),
        ),
        listTileTheme: const ListTileThemeData(
          dense: true,
          minTileHeight: 48,
          contentPadding: EdgeInsets.zero,
        ),
      ),
      child: Material(
        key: const ValueKey('editor-dock'),
        color: theme.colorScheme.surfaceContainer,
        child: Column(
          children: [
            Row(
              children: [
                IconButton(
                  tooltip: '取消编辑',
                  onPressed: _exit,
                  icon: const Icon(Icons.close, size: 20),
                ),
                Expanded(
                  child: TextButton.icon(
                    onPressed: _saveDraft,
                    icon: const Icon(Icons.check, size: 20),
                    label: const Text('保存布局'),
                  ),
                ),
              ],
            ),
            Expanded(
              child: _editorProperties && control != null
                  ? Builder(
                      key: ValueKey('properties-${control.id}'),
                      builder: (ctx) => _controlFields(ctx, control),
                    )
                  : ListView(
                      padding: const EdgeInsets.symmetric(horizontal: 8),
                      children: [
                        action('添加按键', Icons.add, () {
                          if (_draft!.controls.length >= 64) {
                            _snack('每套预设最多64个控件');
                            return;
                          }
                          _rememberEdit();
                          final added = GameControl(
                            id: GamePreset.newId(),
                            label: '新按键',
                            x: .5,
                            y: .5,
                          );
                          setState(() {
                            _draft!.controls.add(added);
                            _selectedControl = added.id;
                          });
                        }),
                        action(
                          '撤销',
                          Icons.undo,
                          _undo.isEmpty
                              ? null
                              : () => setState(() {
                                  _draft = _undo.removeLast();
                                  if (!_draft!.controls.any(
                                    (c) => c.id == _selectedControl,
                                  )) {
                                    _selectedControl = null;
                                  }
                                }),
                        ),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            IconButton(
                              tooltip: '放大画布',
                              icon: const Icon(Icons.zoom_in),
                              onPressed: () {
                                final scale = _editorView.value
                                    .getMaxScaleOnAxis();
                                if (scale < 3) {
                                  final factor = (3 / scale).clamp(1.0, 1.25);
                                  _editorView.value = Matrix4.copy(
                                    _editorView.value,
                                  )..scaleByDouble(factor, factor, 1, 1);
                                }
                              },
                            ),
                            IconButton(
                              tooltip: '适应画布',
                              icon: const Icon(Icons.fit_screen),
                              onPressed: () =>
                                  _editorView.value = Matrix4.identity(),
                            ),
                          ],
                        ),
                        if (control != null) ...[
                          Text(
                            control.label.isEmpty ? '未命名按键' : control.label,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                          action('属性', Icons.tune, () => _editControl(control)),
                          Wrap(
                            alignment: WrapAlignment.center,
                            children: [
                              IconButton(
                                tooltip: '向左微调',
                                onPressed: () => nudge(-1, 0),
                                icon: const Icon(Icons.arrow_back),
                              ),
                              IconButton(
                                tooltip: '向右微调',
                                onPressed: () => nudge(1, 0),
                                icon: const Icon(Icons.arrow_forward),
                              ),
                              IconButton(
                                tooltip: '向上微调',
                                onPressed: () => nudge(0, -1),
                                icon: const Icon(Icons.arrow_upward),
                              ),
                              IconButton(
                                tooltip: '向下微调',
                                onPressed: () => nudge(0, 1),
                                icon: const Icon(Icons.arrow_downward),
                              ),
                            ],
                          ),
                          Text(
                            '位置 ${(control.x * 100).round()}%, ${(control.y * 100).round()}%',
                          ),
                        ] else
                          const Text('轻点选中按键'),
                        const SizedBox(height: 12),
                        const Text('拖动按键移动，角标调整大小。空白处双指缩放、单指移动画布。'),
                      ],
                    ),
            ),
          ],
        ),
      ),
    );
    final preview = Expanded(
      child: Padding(
        padding: const EdgeInsets.all(8),
        child: ClipRect(
          key: const ValueKey('editor-viewport'),
          child: LayoutBuilder(
            builder: (_, constraints) {
              _editorFitScale = math.min(
                constraints.maxWidth / area.width,
                constraints.maxHeight / area.height,
              );
              return InteractiveViewer(
                transformationController: _editorView,
                minScale: 1,
                maxScale: 3,
                child: Center(
                  child: FittedBox(
                    fit: BoxFit.contain,
                    child: SizedBox(
                      width: area.width,
                      height: area.height,
                      child: Material(
                        color: theme.colorScheme.surface,
                        child: Stack(
                          key: const ValueKey('game-controls'),
                          children: [
                            for (final item in _preset.controls)
                              Positioned.fromRect(
                                rect: _rect(item, area),
                                child: _control(item, _rect(item, area)),
                              ),
                            ..._editorTools(area),
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
      ),
    );
    return Material(
      color: theme.colorScheme.surfaceContainerLow,
      child: Row(
        children: [
          preview,
          SizedBox(width: area.width < 650 ? 176 : 208, child: dock),
        ],
      ),
    );
  }

  Rect _rect(GameControl control, Size area) {
    final short = math.min(area.width, area.height);
    final h = (short * control.size).clamp(48.0, area.height);
    final circle = control.joystick || control.shape == 'circle';
    final w = (h * (circle ? 1 : control.aspect)).clamp(48.0, area.width);
    final top = (control.y * area.height - h / 2).clamp(
      0.0,
      math.max(0.0, area.height - h),
    );
    var rect = Rect.fromLTWH(
      (control.x * area.width - w / 2).clamp(
        0.0,
        math.max(0.0, area.width - w),
      ),
      top.toDouble(),
      w.toDouble(),
      h,
    );
    if (_editing) return rect;
    // Reserve only the fixed toolbar targets, leaving the other top buttons in one row.
    final toolbarWidth = widget.onExit == null ? 64.0 : 56.0;
    final overlapsToolbar = widget.desktop && !_editing
        ? rect.overlaps(Rect.fromLTWH(area.width / 2 - 56, 0, 112, 56))
        : rect.overlaps(Rect.fromLTWH(area.width / 2 - 28, 0, 56, 56)) ||
              rect.overlaps(
                Rect.fromLTWH(area.width - toolbarWidth, 0, toolbarWidth, 56),
              );
    if (overlapsToolbar) {
      rect = Rect.fromLTWH(rect.left, 60, rect.width, rect.height);
    }
    return rect;
  }

  double _cornerRadius(GameControl control, Rect rect) {
    if (control.joystick || control.shape == 'circle') {
      return math.min(rect.width, rect.height) / 2;
    }
    if (control.shape == 'rect') return 0;
    if (control.shape == 'rounded') {
      return math.min(rect.width, rect.height) * control.cornerRadius;
    }
    return (control.aspect - 1).abs() < .02 ? rect.height / 2 : 18;
  }

  Widget _control(GameControl control, Rect rect) {
    final colors = Theme.of(context).colorScheme;
    final selected = _selectedControl == control.id;
    Widget face(bool pressed, Offset knob) => AnimatedContainer(
      duration: const Duration(milliseconds: 65),
      decoration: BoxDecoration(
        color:
            (pressed ? colors.primaryContainer : colors.surfaceContainerHighest)
                .withValues(alpha: widget.desktop && !_editing ? _opacity : 1),
        borderRadius: BorderRadius.circular(_cornerRadius(control, rect)),
        border: Border.all(
          color: selected
              ? colors.primary
              : pressed
              ? colors.primary
              : colors.outlineVariant,
          width: selected ? 2 : 1,
        ),
      ),
      child: control.joystick
          ? Stack(
              alignment: Alignment.center,
              children: [
                for (int i = 0; i < 4; i++)
                  Align(
                    alignment: [
                      const Alignment(0, -.8),
                      const Alignment(-.8, 0),
                      const Alignment(0, .8),
                      const Alignment(.8, 0),
                    ][i],
                    child: Text(
                      gameKeyName(control.directions[i]),
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ),
                Transform.translate(
                  offset: knob * (rect.width * .28),
                  child: Container(
                    width: rect.width * .37,
                    height: rect.height * .37,
                    decoration: BoxDecoration(
                      color: colors.primary.withValues(
                        alpha: pressed ? .8 : .45,
                      ),
                      shape: BoxShape.circle,
                    ),
                    child: _label(control, colors.onPrimary),
                  ),
                ),
              ],
            )
          : Padding(
              padding: const EdgeInsets.all(8),
              child: _label(
                control,
                pressed ? colors.onPrimaryContainer : colors.onSurface,
              ),
            ),
    );
    if (_editing) {
      return GestureDetector(
        onTap: () {
          setState(() => _selectedControl = control.id);
        },
        onScaleStart: (_) {
          _rememberEdit();
          setState(() => _selectedControl = control.id);
        },
        onScaleUpdate: (event) => setState(() {
          if (event.pointerCount != 1) return;
          final area = _area!;
          control.x =
              (control.x + event.focalPointDelta.dx / _editorScale / area.width)
                  .clamp(0, 1);
          control.y =
              (control.y +
                      event.focalPointDelta.dy / _editorScale / area.height)
                  .clamp(0, 1);
        }),
        child: face(false, Offset.zero),
      );
    }
    return _GameTouchControl(
      key: ValueKey('${control.id}-$_epoch'),
      control: control,
      enabled: _canInput,
      onBindings: (bindings) => _input.update(control.id, bindings),
      onMouseMove: control.binding < 0 ? _moveMouse : null,
      onFeedback: _feedback,
      face: face,
    );
  }

  Widget _label(GameControl control, Color color) {
    Widget text() => Center(
      child: Text(
        control.label.isEmpty ? gameKeyName(control.binding) : control.label,
        textAlign: TextAlign.center,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.titleMedium?.copyWith(color: color),
      ),
    );
    return control.image == null
        ? text()
        : Image.file(
            File(control.image!),
            fit: BoxFit.contain,
            cacheWidth: 256,
            errorBuilder: (_, _, _) => text(),
          );
  }

  Size? _area;
  double _editorFitScale = 1;
  double get _editorScale =>
      _editorFitScale * _editorView.value.getMaxScaleOnAxis();
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final body = SafeArea(
      child: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
          ? Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(_error!),
                    TextButton(onPressed: _exit, child: const Text('返回')),
                  ],
                ),
              ),
            )
          : LayoutBuilder(
              builder: (context, constraints) {
                final area = constraints.biggest;
                _area = area;
                if (_editing) return _buildEditor(area);
                return Stack(
                  key: const ValueKey('game-controls'),
                  fit: StackFit.expand,
                  children: [
                    if (_captureCamera || _editing)
                      Positioned.fill(
                        child: Listener(
                          key: const ValueKey('game-canvas'),
                          behavior: HitTestBehavior.opaque,
                          onPointerDown: _cameraDown,
                          onPointerMove: _cameraMove,
                          onPointerUp: (e) => _endCanvasPointer(e.pointer),
                          onPointerCancel: (e) => _endCanvasPointer(e.pointer),
                          child: Center(
                            child: IgnorePointer(
                              child: widget.desktop && !_editing
                                  ? const SizedBox.shrink()
                                  : Text(
                                      _editing
                                          ? '轻点选中 · 拖动移动 · 角标缩放 · 属性修改映射'
                                          : '单指滑动移动视角 · 双指按住并移动可拖拽（鼠标左键）',
                                      style: Theme.of(context)
                                          .textTheme
                                          .bodySmall
                                          ?.copyWith(
                                            color: widget.onExit == null
                                                ? colors.onSurfaceVariant
                                                : Colors.white,
                                            shadows: widget.onExit == null
                                                ? null
                                                : const [
                                                    Shadow(
                                                      color: Colors.black,
                                                      blurRadius: 6,
                                                    ),
                                                  ],
                                          ),
                                    ),
                            ),
                          ),
                        ),
                      ),
                    for (final control in _preset.controls)
                      Positioned.fromRect(
                        rect: _rect(control, area),
                        child: _control(control, _rect(control, area)),
                      ),
                    if (widget.desktop && !_editing)
                      Positioned(
                        top: 2,
                        left: area.width / 2 - 52,
                        child: Row(
                          children: [
                            IconButton.filledTonal(
                              tooltip: '退出按键映射',
                              onPressed: _exit,
                              icon: const Icon(Icons.close),
                            ),
                            const SizedBox(width: 8),
                            IconButton.filledTonal(
                              tooltip: '按键映射设置',
                              onPressed: widget.onSettings ?? openSettings,
                              icon: const Icon(Icons.settings_outlined),
                            ),
                          ],
                        ),
                      )
                    else
                      Positioned(
                        top: 2,
                        left: area.width / 2 - 24,
                        child: IconButton.filledTonal(
                          tooltip: _editing ? '取消编辑' : '退出游戏模式',
                          onPressed: _exit,
                          icon: const Icon(Icons.close),
                        ),
                      ),
                    if (!widget.desktop)
                      Positioned(
                        top: 2,
                        right: 8,
                        child: IconButton.filledTonal(
                          tooltip: '游戏设置与预设',
                          onPressed: openSettings,
                          icon: const Icon(Icons.settings_outlined),
                        ),
                      ),
                    if (!_editing &&
                        (!widget.sender.ready ||
                            !widget.sender.supportsGameKeys ||
                            widget.connection.state != SessionState.connected))
                      Positioned(
                        bottom: 8,
                        left: 16,
                        right: 16,
                        child: Material(
                          color: colors.errorContainer,
                          borderRadius: BorderRadius.circular(12),
                          child: Padding(
                            padding: const EdgeInsets.all(12),
                            child: Text(
                              widget.onExit == null
                                  ? '控制已停止，所有按键已释放。请返回触控板重新连接。'
                                  : '控制已停止，所有按键已释放。请退出游戏模式，检查电脑端支持并重新连接。',
                              style: TextStyle(color: colors.onErrorContainer),
                            ),
                          ),
                        ),
                      ),
                  ],
                );
              },
            ),
    );
    return PopScope(
      canPop: !widget.inputEnabled || (!_editing && widget.onExit == null),
      onPopInvokedWithResult: (popped, _) {
        if (!popped && widget.inputEnabled) _exit();
      },
      child: widget.desktop
          ? (_loading || _error != null ? AbsorbPointer(child: body) : body)
          : Scaffold(
              backgroundColor: widget.onExit == null
                  ? colors.surface
                  : Colors.transparent,
              body: body,
            ),
    );
  }
}

class _GameTouchControl extends StatefulWidget {
  final GameControl control;
  final bool enabled;
  final ValueChanged<Set<int>> onBindings;
  final VoidCallback onFeedback;
  final Widget Function(bool pressed, Offset knob) face;
  final ValueChanged<Offset>? onMouseMove;
  const _GameTouchControl({
    super.key,
    required this.control,
    required this.enabled,
    required this.onBindings,
    required this.onFeedback,
    required this.face,
    this.onMouseMove,
  });
  @override
  State<_GameTouchControl> createState() => _GameTouchControlState();
}

class _GameTouchControlState extends State<_GameTouchControl> {
  final Set<int> _pointers = {};
  Offset _knob = Offset.zero;
  Set<int> _bindings = {};
  void _update(Set<int> bindings) {
    if (_bindings.length != bindings.length ||
        !_bindings.containsAll(bindings)) {
      _bindings = bindings;
      widget.onBindings(bindings);
      if (bindings.isNotEmpty) widget.onFeedback();
    }
  }

  void _position(Offset local, Size area) {
    final center = Offset(area.width / 2, area.height / 2),
        delta = (local - center) / (area.shortestSide * .36);
    _knob = delta.distance > 1 ? delta / delta.distance : delta;
    _update(
      GameInputState.joystick(_knob.dx, _knob.dy, widget.control.directions),
    );
  }

  void _up(int pointer) {
    if (!mounted) return;
    if (!_pointers.remove(pointer)) return;
    if (_pointers.isEmpty) {
      _knob = Offset.zero;
      _update({});
    }
    setState(() {});
  }

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) => Semantics(
      label: widget.control.joystick
          ? '拖动方向摇杆'
          : '${widget.control.label}，${gameKeyName(widget.control.binding)}',
      button: !widget.control.joystick,
      child: Listener(
        behavior: HitTestBehavior.opaque,
        onPointerDown: (event) {
          if (!mounted ||
              !widget.enabled ||
              widget.control.joystick && _pointers.isNotEmpty) {
            return;
          }
          _pointers.add(event.pointer);
          if (widget.control.joystick) {
            _position(event.localPosition, constraints.biggest);
          } else {
            _update({widget.control.binding});
          }
          setState(() {});
        },
        onPointerMove: (event) {
          if (mounted &&
              widget.enabled &&
              !widget.control.joystick &&
              _pointers.contains(event.pointer) &&
              event.pointer == _pointers.first) {
            widget.onMouseMove?.call(event.delta);
          }
          if (mounted &&
              widget.enabled &&
              widget.control.joystick &&
              _pointers.contains(event.pointer)) {
            setState(() => _position(event.localPosition, constraints.biggest));
          }
        },
        onPointerUp: (event) => _up(event.pointer),
        onPointerCancel: (event) => _up(event.pointer),
        child: Opacity(
          opacity: widget.enabled ? 1 : .45,
          child: widget.face(_pointers.isNotEmpty, _knob),
        ),
      ),
    ),
  );
}
