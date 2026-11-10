import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/services.dart';
import 'package:hinge/core/game_controls.dart';
import 'package:hinge/core/remote_input_cipher.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  test(
    'preset import appends with a fresh identity and rolls back failed writes',
    () async {
      final original = GamePreset.basic();
      final store = GamePresetStore()
        ..presets = [original]
        ..selected = original.id;
      final messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      var fail = false;
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (fail) throw PlatformException(code: 'disk-full');
        return null;
      });
      addTearDown(
        () => messenger.setMockMethodCallHandler(GamePresetStore.channel, null),
      );
      await store.importPreset(jsonEncode(original.toJson()));
      expect(store.presets.length, 2);
      expect(store.current.id, isNot(original.id));
      expect(store.presets.first, same(original));
      expect(
        store.current.controls.first.toJson(),
        original.controls.first.toJson(),
      );
      final selected = store.selected;
      fail = true;
      await expectLater(
        store.importPreset(jsonEncode(original.toJson())),
        throwsA(isA<PlatformException>()),
      );
      expect(store.presets.length, 2);
      expect(store.selected, selected);
      await expectLater(
        store.importPreset(
          jsonEncode({
            ...original.toJson(),
            'controls': List.generate(
              65,
              (_) => original.controls.first.toJson(),
            ),
          }),
        ),
        throwsFormatException,
      );
      await expectLater(
        store.importPreset(
          jsonEncode({
            ...original.toJson(),
            'controls': [
              original.controls.first.toJson(),
              original.controls.first.toJson(),
            ],
          }),
        ),
        throwsFormatException,
      );
      expect(store.presets.length, 2);
    },
  );
  test(
    'desktop selection persists without changing the shared game selection',
    () async {
      final game = GamePreset.basic(),
          custom = GamePreset.basic()..name = '自定义';
      var raw =
          (GamePresetStore()
                ..presets = [game, custom]
                ..selected = game.id)
              .toJsonString();
      String? desktopSelection;
      final messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (call.method == 'load') return raw;
        if (call.method == 'loadDesktopSelection') return desktopSelection;
        if (call.method == 'saveDesktop') {
          raw = call.arguments['json'] as String;
          desktopSelection = call.arguments['selected'] as String;
        }
        return null;
      });
      addTearDown(
        () => messenger.setMockMethodCallHandler(GamePresetStore.channel, null),
      );
      final store = GamePresetStore(desktop: true);
      await store.load();
      expect(store.current.id, GamePreset.desktopId);
      expect(store.current.controls.length, 8);
      expect(store.current.controls.any((c) => c.joystick), false);
      expect(
        store.current.controls.map((c) => c.binding),
        containsAll([-1, -2, 0x11, 0x12]),
      );
      expect(
        GamePresetStore.fromJsonString(raw).presets.length,
        2,
        reason: 'loading does not write presets',
      );
      await store.save();
      store.selected = custom.id;
      await store.save();
      final reload = GamePresetStore(desktop: true);
      await reload.load();
      expect(reload.current.id, custom.id);
      final regular = GamePresetStore();
      await regular.load();
      expect(regular.current.id, game.id);
      expect(
        regular.presets.firstWhere((p) => p.id == custom.id).toJson(),
        custom.toJson(),
      );
      expect(
        regular.presets.where((p) => p.id == GamePreset.desktopId).length,
        1,
      );
    },
  );
  test('joystick dead zone and diagonals', () {
    expect(GameInputState.joystick(.1, -.1, [87, 65, 83, 68]), isEmpty);
    expect(GameInputState.joystick(.8, -.9, [87, 65, 83, 68]), {87, 68});
    expect(GameInputState.joystick(-1, .05, [87, 65, 83, 68]), {65});
  });
  test('shared binding stays held until last finger releases', () {
    final events = <String>[];
    final input = GameInputState((key, down) => events.add('$key:$down'));
    input.update('stick', {87, 68});
    input.update('button', {87});
    input.update('stick', {65});
    expect(input.held, {87, 65});
    expect(events, ['87:true', '68:true', '68:false', '65:true']);
    input.update('button', {});
    input.releaseAll();
    input.releaseAll();
    expect(events, [
      '87:true',
      '68:true',
      '68:false',
      '65:true',
      '87:false',
      '65:false',
    ]);
  });
  test('presets deep copy and validate positions and mappings', () {
    final preset = GamePreset.basic();
    final copy = preset.copy(id: 'copy', name: '第二套');
    copy.controls.first.label = '地图2';
    copy.controls[1].directions[0] = 38;
    expect(preset.controls.first.label, '地图');
    expect(preset.controls[1].directions[0], 87);
    expect(copy.id, 'copy');
    expect(copy.name, '第二套');
    final c = GameControl.fromJson({
      'id': 'x',
      'x': double.nan,
      'y': 9,
      'size': -1,
      'binding': 999,
      'image': '/sdcard/a.png',
    });
    expect(c.x, .5);
    expect(c.y, 1);
    expect(c.size, .10);
    expect(c.binding, 0);
    expect(c.image, isNull);
    expect(
      preset.controls
          .where((c) => ['1', '2', '3', '4'].contains(c.label))
          .map((c) => c.binding),
      [49, 50, 51, 52],
    );
    expect(
      preset.controls
          .where((c) => ['1', '2', '3', '4'].contains(c.label))
          .map((c) => c.aspect),
      everyElement(.5),
    );
    expect(preset.controls.singleWhere((c) => c.joystick).size, .34);
    expect(
      preset.controls
          .where((c) => ['F1', 'F2', 'F3'].contains(c.label))
          .map((c) => c.binding),
      [112, 113, 114],
    );
    final store = GamePresetStore.fromJsonString(
      jsonEncode({
        'version': 1,
        'selected': preset.id,
        'presets': [preset.toJson()],
      }),
    );
    expect(
      GamePresetStore.fromJsonString(store.toJsonString()).selected,
      preset.id,
    );
  });
  test('encrypted input rejects tampering replay reflection and old sessions', () async {
    final key = List<int>.generate(32, (i) => i);
    final challenge = Uint8List.fromList(List<int>.generate(32, (i) => i + 32));
    final client = await RemoteInputCipher.create(key, challenge);
    final server = await RemoteInputCipher.create(key, challenge, server: true);
    final plain = Uint8List(16)
      ..[0] = 0x10
      ..[1] = 87;
    final envelope = await client.encrypt(plain);
    final bad = Uint8List.fromList(envelope)..[10] ^= 1;
    expect(await server.decrypt(bad), isNull);
    expect(await server.decrypt(envelope), plain);
    expect(await server.decrypt(envelope), isNull);
    expect(await client.decrypt(envelope), isNull);
    final next = await RemoteInputCipher.create(
      key,
      Uint8List(32),
      server: true,
    );
    expect(await next.decrypt(envelope), isNull);
    expect(await client.decrypt(await server.encrypt(plain)), plain);
    // ignore: avoid_print
    print(
      'REMOTE_INPUT_VECTOR=${envelope.map((b) => b.toRadixString(16).padLeft(2, '0')).join()}',
    );
  });
}
