import 'dart:convert';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

class GameControl {
  final String id;
  String label;
  String? image;
  double x, y, size, aspect, cornerRadius;
  int binding;
  bool joystick;
  String shape;
  List<int> directions;
  GameControl({
    required this.id,
    required this.label,
    required this.x,
    required this.y,
    this.size = .15,
    this.aspect = 1,
    this.shape = 'auto',
    this.cornerRadius = .12,
    this.binding = 0,
    this.joystick = false,
    this.image,
    List<int>? directions,
  }) : directions = directions ?? [87, 65, 83, 68];
  Map<String, dynamic> toJson() => {
    'id': id,
    'label': label,
    'x': x,
    'y': y,
    'size': size,
    'aspect': aspect,
    'shape': shape,
    'cornerRadius': cornerRadius,
    'binding': binding,
    'joystick': joystick,
    'image': image,
    'directions': directions,
  };
  factory GameControl.fromJson(Map<String, dynamic> json) {
    double number(String key, double fallback, double min, double max) {
      final value = json[key];
      return value is num && value.toDouble().isFinite
          ? value.toDouble().clamp(min, max)
          : fallback;
    }

    int key(dynamic value) =>
        value is int &&
            (value == 0 ||
                value >= 8 && value <= 254 ||
                value >= -3 && value <= -1)
        ? value
        : 0;
    final image = json['image'];
    final directions = json['directions'];
    final shapeValue = json['shape'];
    final shape =
        shapeValue is String &&
            const ['auto', 'circle', 'rounded', 'rect'].contains(shapeValue)
        ? shapeValue
        : 'auto';
    return GameControl(
      id: json['id'] is String
          ? (json['id'] as String).substring(
              0,
              math.min(80, (json['id'] as String).length),
            )
          : 'control',
      label: (json['label'] is String ? json['label'] as String : '')
          .split('')
          .take(64)
          .join(),
      x: number('x', .5, 0, 1),
      y: number('y', .5, 0, 1),
      size: number('size', .15, .10, .48),
      aspect: shape == 'circle' ? 1 : number('aspect', 1, .5, 3),
      shape: shape,
      cornerRadius: shape == 'circle' ? .5 : number('cornerRadius', .12, 0, .5),
      binding: key(json['binding']),
      joystick: json['joystick'] == true,
      image:
          image is String &&
              image.contains('/game-controls/') &&
              image.endsWith('.png')
          ? image
          : null,
      directions: directions is List && directions.length == 4
          ? directions.map(key).map((k) => k < 8 ? 0 : k).toList()
          : [87, 65, 83, 68],
    );
  }
}

class GamePreset {
  String id, name;
  List<GameControl> controls;
  GamePreset(this.id, this.name, this.controls);
  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'controls': controls.map((c) => c.toJson()).toList(),
  };
  GamePreset copy({String? id, String? name}) {
    final result = GamePreset.fromJson(toJson());
    result.id = id ?? this.id;
    result.name = name ?? this.name;
    return result;
  }

  factory GamePreset.fromJson(Map<String, dynamic> json) {
    if (json['id'] is! String ||
        json['name'] is! String ||
        json['controls'] is! List) {
      throw const FormatException('无效预设');
    }
    final controls = (json['controls'] as List)
        .take(64)
        .map((e) => GameControl.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
    if (controls.map((c) => c.id).toSet().length != controls.length) {
      throw const FormatException('重复控件');
    }
    return GamePreset(
      json['id'] as String,
      (json['name'] as String).split('').take(40).join(),
      controls,
    );
  }
  static String newId() =>
      '${DateTime.now().microsecondsSinceEpoch}-${math.Random.secure().nextInt(1 << 30)}';
  static GamePreset basic() {
    GameControl button(
      String label,
      int key,
      double x,
      double y, {
      double size = .14,
      double aspect = 1,
    }) => GameControl(
      id: newId(),
      label: label,
      binding: key,
      x: x,
      y: y,
      size: size,
      aspect: aspect,
    );
    return GamePreset(newId(), '基础游戏布局', [
      button('地图', 77, .15, .20, size: .28, aspect: 1.1),
      GameControl(
        id: newId(),
        label: '移动',
        x: .16,
        y: .78,
        size: .34,
        joystick: true,
      ),
      button('F1', 112, .61, .09, size: .12),
      button('F2', 113, .70, .09, size: .12),
      button('F3', 114, .79, .09, size: .12),
      button('+', 0, .88, .09, size: .12),
      for (int i = 0; i < 4; i++)
        button(
          '${i + 1}',
          49 + i,
          i.isEven ? .80 : .93,
          i < 2 ? .27 : .46,
          size: .20,
          aspect: .5,
        ),
      button('Q', 81, .73, .65),
      button('E', 69, .73, .87),
      button('F', 70, .30, .87),
      button('左键', -1, .84, .77, size: .22),
      button('空格', 32, .95, .63),
      button('右键', -2, .95, .88),
    ]);
  }
}

// A binding belongs to all fingers holding it, so one lifted finger cannot release another's key.
class GameInputState {
  final void Function(int binding, bool down) emit;
  final Map<Object, Set<int>> _sources = {};
  GameInputState(this.emit);
  Set<int> get held => _sources.values.expand((s) => s).toSet();
  void update(Object source, Set<int> bindings) {
    final before = held;
    bindings = bindings.where((k) => k != 0).toSet();
    if (bindings.isEmpty) {
      _sources.remove(source);
    } else {
      _sources[source] = bindings;
    }
    final after = held;
    for (final key in before.difference(after)) {
      emit(key, false);
    }
    for (final key in after.difference(before)) {
      emit(key, true);
    }
  }

  void releaseAll() {
    final before = held;
    _sources.clear();
    for (final key in before) {
      emit(key, false);
    }
  }

  static Set<int> joystick(double dx, double dy, List<int> directions) {
    if (math.sqrt(dx * dx + dy * dy) < .18) return {};
    final maxAxis = math.max(dx.abs(), dy.abs());
    return {
      if (dy < -maxAxis * .42) directions[0],
      if (dx < -maxAxis * .42) directions[1],
      if (dy > maxAxis * .42) directions[2],
      if (dx > maxAxis * .42) directions[3],
    }..remove(0);
  }
}

class GamePresetStore {
  static const channel = MethodChannel('hinge/game_controls');
  static final ValueNotifier<int> syncRevision = ValueNotifier(0);
  List<GamePreset> presets = [];
  String? selected;
  GamePresetStore();
  Future<void> load() async {
    final raw = await channel.invokeMethod<String>('load');
    if (raw == null) {
      presets = [GamePreset.basic()];
      selected = presets.first.id;
      return;
    }
    _decode(raw);
  }

  factory GamePresetStore.fromJsonString(String raw) {
    final store = GamePresetStore();
    store._decode(raw);
    return store;
  }

  void _decode(String raw) {
    if (utf8.encode(raw).length > 1024 * 1024) {
      throw const FormatException('预设数据过大');
    }
    final json = jsonDecode(raw) as Map<String, dynamic>;
    final list = json['presets'];
    if (list is! List || list.isEmpty || list.length > 32) {
      throw const FormatException('无效预设列表');
    }
    presets = list
        .map((e) => GamePreset.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
    if (presets.map((p) => p.id).toSet().length != presets.length) {
      throw const FormatException('重复预设');
    }
    final savedSelection = json['selected'] as String?;
    selected = presets.any((preset) => preset.id == savedSelection)
        ? savedSelection
        : presets.first.id;
  }

  GamePreset get current =>
      presets.firstWhere((p) => p.id == selected, orElse: () => presets.first);
  String toJsonString() => jsonEncode({
    'version': 1,
    'selected': selected,
    'presets': presets.map((p) => p.toJson()).toList(),
  });

  Future<void> save() {
    if (presets.isEmpty || presets.length > 32) {
      throw const FormatException('最多保存32套预设');
    }
    return _saveJson(toJsonString());
  }

  Future<void> replaceWithJson(String raw) async {
    final incoming = GamePresetStore.fromJsonString(raw);
    await _saveJson(incoming.toJsonString());
    presets = incoming.presets;
    selected = incoming.selected;
    syncRevision.value++;
  }

  Future<void> _saveJson(String json) async {
    if (utf8.encode(json).length > 1024 * 1024) {
      throw const FormatException('预设数据过大');
    }
    await channel.invokeMethod<void>('save', json);
  }
}

String gameKeyName(int key) {
  if (key == 0) return '未设置';
  if (key >= 48 && key <= 90) return String.fromCharCode(key);
  if (key >= 112 && key <= 135) return 'F${key - 111}';
  if (key >= 96 && key <= 105) return '数字小键盘 ${key - 96}';
  return const {
        -1: '鼠标左键',
        -2: '鼠标右键',
        -3: '鼠标中键',
        8: '退格',
        9: 'Tab',
        13: 'Enter',
        16: 'Shift',
        17: 'Ctrl',
        18: 'Alt',
        19: 'Pause',
        20: 'Caps Lock',
        27: 'Esc',
        32: 'Space',
        33: 'Page Up',
        34: 'Page Down',
        35: 'End',
        36: 'Home',
        37: '←',
        38: '↑',
        39: '→',
        40: '↓',
        44: 'Print Screen',
        45: 'Insert',
        46: 'Delete',
        91: '左 Win',
        92: '右 Win',
        106: '小键盘 *',
        107: '小键盘 +',
        109: '小键盘 -',
        110: '小键盘 .',
        111: '小键盘 /',
        144: 'Num Lock',
        160: '左 Shift',
        161: '右 Shift',
        162: '左 Ctrl',
        163: '右 Ctrl',
        164: '左 Alt',
        165: '右 Alt',
        186: ';',
        187: '=',
        188: ',',
        189: '-',
        190: '.',
        191: '/',
        192: '`',
        219: '[',
        220: '\\',
        221: ']',
        222: "'",
      }[key] ??
      'VK $key';
}
