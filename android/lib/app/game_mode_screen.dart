import 'dart:async';
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
  const GameModeScreen({
    super.key,
    required this.sender,
    required this.connection,
    this.restoreOrientations = const [],
  });
  @override
  State<GameModeScreen> createState() => _GameModeScreenState();
}

class _GameModeScreenState extends State<GameModeScreen>
    with WidgetsBindingObserver {
  final _store = GamePresetStore();
  late final GameInputState _input;
  StreamSubscription<SessionState>? _subscription;
  bool _loading = true, _editing = false, _suspended = false, _haptics = true;
  String? _error;
  GamePreset? _draft;
  String? _selectedControl;
  int _epoch = 0;
  Orientation? _orientation;
  final Map<int, Offset> _camera = {};
  double _remainderX = 0, _remainderY = 0, _sensitivity = 3;
  bool _canvasDragging = false;
  bool _externalSyncPending = false;
  Offset? _canvasCentroid;
  GamePreset get _preset => _draft ?? _store.current;
  bool get _canInput =>
      !_loading &&
      _error == null &&
      !_editing &&
      !_suspended &&
      widget.sender.ready &&
      widget.connection.state == SessionState.connected;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    GamePresetStore.syncRevision.addListener(_externalPresetSync);
    unawaited(
      SystemChrome.setPreferredOrientations([
        DeviceOrientation.landscapeLeft,
        DeviceOrientation.landscapeRight,
      ]),
    );
    _input = GameInputState((key, down) {
      if (key > 0) {
        widget.sender.sendKey(key, down);
      } else {
        final button = RemoteMouseButton.values[-key - 1];
        down
            ? widget.sender.sendMouseDown(button)
            : widget.sender.sendMouseUp(button);
      }
    });
    widget.sender.addListener(_connectionChanged);
    _subscription = widget.connection.stateStream.listen(
      (_) => _connectionChanged(),
    );
    unawaited(
      SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
    );
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
        widget.connection.state != SessionState.connected) {
      _release();
    }
    setState(() {});
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
    _canvasCentroid = null;
    _remainderX = _remainderY = 0;
    _epoch++;
  }

  void _endCanvasPointer(int pointer) {
    if (!_camera.containsKey(pointer)) return;
    if (_canvasDragging) {
      _input.update('canvas-drag', {});
      _camera.clear();
      _canvasDragging = false;
      _canvasCentroid = null;
      _remainderX = _remainderY = 0;
    } else {
      _camera.remove(pointer);
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
    if (!_suspended) {
      unawaited(
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
      );
    }
  }

  @override
  void dispose() {
    _release();
    GamePresetStore.syncRevision.removeListener(_externalPresetSync);
    widget.sender.removeListener(_connectionChanged);
    unawaited(_subscription?.cancel());
    WidgetsBinding.instance.removeObserver(this);
    unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge));
    unawaited(
      SystemChrome.setPreferredOrientations(widget.restoreOrientations),
    );
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

  Future<void> _settings() async {
    _release();
    setState(() => _suspended = true);
    try {
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        useSafeArea: true,
        builder: (sheetContext) => StatefulBuilder(
          builder: (ctx, refresh) => SizedBox(
            height: MediaQuery.sizeOf(ctx).height * .9,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        '游戏预设',
                        style: Theme.of(ctx).textTheme.titleLarge,
                      ),
                    ),
                    TextButton(
                      onPressed: () {
                        Navigator.pop(sheetContext);
                        setState(() {
                          _draft = _store.current.copy();
                          _selectedControl = null;
                          _editing = true;
                        });
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
                        if (action == 'delete') {
                          if (_store.presets.length == 1) {
                            _snack('至少保留一套预设');
                            return;
                          }
                          if (!await _confirm('删除“${preset.name}”？')) return;
                          final index = _store.presets.indexOf(preset),
                              oldSelected = _store.selected;
                          _store.presets.removeAt(index);
                          if (!await _saveStore()) {
                            _store.presets.insert(index, preset);
                            _store.selected = oldSelected;
                          }
                        } else {
                          if (action == 'copy' && _store.presets.length >= 32) {
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
                        PopupMenuItem(value: 'rename', child: Text('重命名')),
                        PopupMenuItem(value: 'copy', child: Text('复制为新预设')),
                        PopupMenuItem(value: 'delete', child: Text('删除')),
                      ],
                    ),
                  ),
                OutlinedButton.icon(
                  icon: const Icon(Icons.add),
                  label: const Text('从基础布局新建'),
                  onPressed: () async {
                    if (_store.presets.length >= 32) {
                      _snack('最多保存32套预设');
                      return;
                    }
                    final name = await _askName('新游戏布局');
                    if (name == null || !mounted) return;
                    final preset = GamePreset.basic()..name = name;
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
                const Divider(),
                SwitchListTile(
                  title: const Text('触控反馈'),
                  value: _haptics,
                  onChanged: (value) {
                    setState(() => _haptics = value);
                    refresh(() {});
                  },
                ),
                Text('视角灵敏度 ${_sensitivity.toStringAsFixed(1)}'),
                Slider(
                  value: _sensitivity,
                  min: 1.5,
                  max: 5,
                  divisions: 14,
                  onChanged: (value) {
                    setState(() => _sensitivity = value);
                    refresh(() {});
                  },
                ),
                const Text('此模式控制电脑键鼠；电脑画面仍需在电脑屏幕查看。部分游戏或管理员窗口可能不接受系统模拟输入。'),
              ],
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
    final text = TextEditingController(text: control.label);
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (ctx, refresh) => SizedBox(
          height: MediaQuery.sizeOf(ctx).height * .92,
          child: ListView(
            padding: EdgeInsets.fromLTRB(
              20,
              12,
              20,
              12 + MediaQuery.viewInsetsOf(ctx).bottom,
            ),
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
                    onPressed: () => Navigator.pop(ctx),
                    icon: const Icon(Icons.check),
                  ),
                ],
              ),
              TextField(
                controller: text,
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
              if (!control.joystick) ...[
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
                      Navigator.pop(ctx);
                    },
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
    await Future<void>.delayed(Duration.zero);
    text.dispose();
  }

  Future<void> _saveDraft() async {
    final index = _store.presets.indexWhere((p) => p.id == _draft!.id),
        old = _store.current;
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
      Navigator.pop(context);
    }
  }

  Rect _rect(GameControl control, Size area) {
    final short = math.min(area.width, area.height);
    final h = (short * control.size).clamp(48.0, area.height);
    final w = (h * (control.joystick ? 1 : control.aspect)).clamp(
      48.0,
      area.width,
    );
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
    // Reserve only the fixed toolbar targets, leaving the other top buttons in one row.
    if (rect.overlaps(Rect.fromLTWH(area.width / 2 - 28, 0, 56, 56)) ||
        rect.overlaps(Rect.fromLTWH(area.width - 64, 0, 64, 56))) {
      rect = Rect.fromLTWH(rect.left, 60, rect.width, rect.height);
    }
    return rect;
  }

  Widget _control(GameControl control, Rect rect) {
    final colors = Theme.of(context).colorScheme;
    final selected = _selectedControl == control.id;
    Widget face(bool pressed, Offset knob) => AnimatedContainer(
      duration: const Duration(milliseconds: 65),
      decoration: BoxDecoration(
        color: pressed
            ? colors.primaryContainer
            : colors.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(
          control.joystick || control.aspect == 1 ? rect.height / 2 : 18,
        ),
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
          _editControl(control);
        },
        onPanStart: (_) => setState(() => _selectedControl = control.id),
        onPanUpdate: (event) => setState(() {
          final area = _area!;
          control.x = (control.x + event.delta.dx / area.width).clamp(0, 1);
          control.y = (control.y + event.delta.dy / area.height).clamp(0, 1);
        }),
        child: face(false, Offset.zero),
      );
    }
    return _GameTouchControl(
      key: ValueKey('${control.id}-$_epoch'),
      control: control,
      enabled: _canInput,
      onBindings: (bindings) => _input.update(control.id, bindings),
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
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return PopScope(
      canPop: !_editing,
      onPopInvokedWithResult: (popped, _) {
        if (!popped) _exit();
      },
      child: Scaffold(
        backgroundColor: colors.surface,
        body: SafeArea(
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
                        TextButton(
                          onPressed: () => Navigator.pop(context),
                          child: const Text('返回'),
                        ),
                      ],
                    ),
                  ),
                )
              : LayoutBuilder(
                  builder: (context, constraints) {
                    final area = constraints.biggest;
                    _area = area;
                    return Stack(
                      children: [
                        Positioned.fill(
                          child: Listener(
                            key: const ValueKey('game-canvas'),
                            behavior: HitTestBehavior.opaque,
                            onPointerDown: (event) {
                              if (!_canInput) return;
                              _camera[event.pointer] = event.localPosition;
                              if (!_canvasDragging && _camera.length == 2) {
                                _canvasDragging = true;
                                _canvasCentroid =
                                    _camera.values.reduce((a, b) => a + b) /
                                    _camera.length.toDouble();
                                _input.update('canvas-drag', {-1});
                                _feedback();
                              } else if (_canvasDragging) {
                                _canvasCentroid =
                                    _camera.values.reduce((a, b) => a + b) /
                                    _camera.length.toDouble();
                              }
                            },
                            onPointerMove: (event) {
                              if (!_canInput ||
                                  !_camera.containsKey(event.pointer)) {
                                return;
                              }
                              final previous = _camera[event.pointer]!;
                              _camera[event.pointer] = event.localPosition;
                              final centroid = _canvasDragging
                                  ? _camera.values.reduce((a, b) => a + b) /
                                        _camera.length.toDouble()
                                  : event.localPosition;
                              final delta =
                                  ((_canvasDragging
                                      ? centroid - (_canvasCentroid ?? centroid)
                                      : centroid - previous)) *
                                  _sensitivity;
                              if (_canvasDragging) _canvasCentroid = centroid;
                              _remainderX += delta.dx;
                              _remainderY += delta.dy;
                              final dx = _remainderX.truncate(),
                                  dy = _remainderY.truncate();
                              _remainderX -= dx;
                              _remainderY -= dy;
                              if (dx != 0 || dy != 0) {
                                widget.sender.sendMouseMove(dx, dy);
                              }
                            },
                            onPointerUp: (e) => _endCanvasPointer(e.pointer),
                            onPointerCancel: (e) =>
                                _endCanvasPointer(e.pointer),
                            child: Center(
                              child: IgnorePointer(
                                child: Text(
                                  _editing
                                      ? '拖动控件调整位置 · 轻点修改按键和外观'
                                      : '单指滑动移动视角 · 双指按住并移动可拖拽（鼠标左键）',
                                  style: Theme.of(context).textTheme.bodySmall
                                      ?.copyWith(
                                        color: colors.onSurfaceVariant,
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
                        Positioned(
                          top: 2,
                          left: area.width / 2 - 24,
                          child: IconButton.filledTonal(
                            tooltip: _editing ? '取消编辑' : '退出游戏模式',
                            onPressed: _exit,
                            icon: const Icon(Icons.close),
                          ),
                        ),
                        if (_editing) ...[
                          Positioned(
                            top: 2,
                            left: 8,
                            child: TextButton.icon(
                              onPressed: () {
                                if (_draft!.controls.length >= 64) {
                                  _snack('每套预设最多64个控件');
                                  return;
                                }
                                final control = GameControl(
                                  id: GamePreset.newId(),
                                  label: '新按键',
                                  x: .5,
                                  y: .5,
                                );
                                setState(() {
                                  _draft!.controls.add(control);
                                  _selectedControl = control.id;
                                });
                                _editControl(control);
                              },
                              icon: const Icon(Icons.add),
                              label: const Text('添加按键'),
                            ),
                          ),
                          Positioned(
                            top: 2,
                            right: 8,
                            child: FilledButton.icon(
                              onPressed: _saveDraft,
                              icon: const Icon(Icons.save_outlined),
                              label: const Text('保存布局'),
                            ),
                          ),
                        ] else
                          Positioned(
                            top: 2,
                            right: 8,
                            child: IconButton.filledTonal(
                              tooltip: '游戏设置与预设',
                              onPressed: _settings,
                              icon: const Icon(Icons.settings_outlined),
                            ),
                          ),
                        if (!_editing &&
                            (!widget.sender.ready ||
                                widget.connection.state !=
                                    SessionState.connected))
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
                                  '控制已停止，所有按键已释放。请返回触控板重新连接。',
                                  style: TextStyle(
                                    color: colors.onErrorContainer,
                                  ),
                                ),
                              ),
                            ),
                          ),
                      ],
                    );
                  },
                ),
        ),
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
  const _GameTouchControl({
    super.key,
    required this.control,
    required this.enabled,
    required this.onBindings,
    required this.onFeedback,
    required this.face,
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
