import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/game_controls.dart';
import 'package:hinge/core/remote_input_cipher.dart';

void main() {
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
    expect(GamePresetStore.fromJsonString(store.toJsonString()).selected, preset.id);
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
    print(
      'REMOTE_INPUT_VECTOR=${envelope.map((b) => b.toRadixString(16).padLeft(2, '0')).join()}',
    );
  });
}
