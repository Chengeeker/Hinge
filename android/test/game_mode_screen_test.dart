import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/game_mode_screen.dart';
import 'package:hinge/core/game_controls.dart';
import 'package:hinge/core/remote_input_model.dart';
import 'package:hinge/core/remote_input_sender.dart';
import 'package:hinge/core/session_manager.dart';

class _Connection extends Fake implements SessionConnection {
  final states = StreamController<SessionState>.broadcast();
  @override
  SessionState get state => SessionState.connected;
  @override
  Stream<SessionState> get stateStream => states.stream;
}

class _Sender extends RemoteInputSender {
  final events = <String>[];
  _Sender(super.connection) {
    ready = true;
    supportsGameKeys = true;
  }
  @override
  void sendKey(int key, bool down) => events.add('$key:$down');
  @override
  void sendMouseMove(int dx, int dy, {bool relative = false}) =>
      events.add('move');
  @override
  void sendMouseDown(RemoteMouseButton button) =>
      events.add('mouseDown:${button.value}');
  @override
  void sendMouseUp(RemoteMouseButton button) =>
      events.add('mouseUp:${button.value}');
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'game multitouch holds joystick and buttons, settings releases keys and edits without input',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 486);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final messenger = tester.binding.defaultBinaryMessenger;
      String? saved;
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (call.method == 'load') return saved;
        if (call.method == 'save') {
          saved = call.arguments as String;
          return null;
        }
        return null;
      });
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => true,
      );
      addTearDown(() {
        messenger.setMockMethodCallHandler(GamePresetStore.channel, null);
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/platform'),
          null,
        );
      });
      final conn = _Connection();
      final sender = _Sender(conn);
      const captureKey = ValueKey('game-capture');
      await tester.pumpWidget(
        RepaintBoundary(
          key: captureKey,
          child: MaterialApp(
            home: GameModeScreen(sender: sender, connection: conn),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byTooltip('退出游戏模式'), findsOneWidget);
      expect(find.text('地图'), findsOneWidget);
      expect(tester.takeException(), isNull);
      final canvas = tester.getRect(find.byKey(const ValueKey('game-canvas')));
      final firstDrag = await tester.startGesture(canvas.center, pointer: 20);
      final secondDrag = await tester.startGesture(
        canvas.center + const Offset(40, 0),
        pointer: 21,
      );
      await secondDrag.moveBy(const Offset(0, -24));
      await firstDrag.moveBy(const Offset(0, -24));
      await tester.pump();
      expect(
        sender.events,
        contains('mouseDown:${RemoteMouseButton.left.value}'),
      );
      expect(sender.events, contains('move'));
      await secondDrag.up();
      await tester.pump();
      expect(
        sender.events,
        contains('mouseUp:${RemoteMouseButton.left.value}'),
      );
      final moveCount = sender.events.where((event) => event == 'move').length;
      await firstDrag.moveBy(const Offset(0, -24));
      expect(sender.events.where((event) => event == 'move').length, moveCount);
      await firstDrag.up();
      await tester.pump();
      await tester.runAsync(() async {
        final boundary = tester.renderObject<RenderRepaintBoundary>(
          find.byKey(captureKey),
        );
        final image = await boundary.toImage();
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        await File('../tmp/game-mode-preview.png')
            .writeAsBytes(bytes!.buffer.asUint8List());
        image.dispose();
      });
      final joystick = tester.getCenter(find.text('移动'));
      final stick = await tester.startGesture(joystick, pointer: 1);
      await stick.moveBy(const Offset(50, -50));
      final q = await tester.startGesture(
        tester.getCenter(find.text('Q')),
        pointer: 2,
      );
      final space = await tester.startGesture(
        tester.getCenter(find.text('空格')),
        pointer: 3,
      );
      await tester.pump();
      expect(
        sender.events,
        containsAll(['87:true', '68:true', '81:true', '32:true']),
      );
      await q.up();
      await stick.up();
      await tester.pump();
      expect(sender.events, containsAll(['81:false', '87:false', '68:false']));
      expect(sender.events.where((e) => e == '32:false'), isEmpty);
      await tester.tap(find.byTooltip('游戏设置与预设'));
      await tester.pumpAndSettle();
      expect(sender.events, contains('32:false'));
      expect(find.text('视角灵敏度 3.0'), findsOneWidget);
      final sensitivity = tester.widget<Slider>(find.byType(Slider));
      expect(sensitivity.min, 1.5);
      expect(sensitivity.max, 5);
      await space.up();
      await tester.tap(find.text('编辑布局'));
      await tester.pumpAndSettle();
      sender.events.clear();
      await tester.drag(find.text('Q'), const Offset(-30, -10));
      await tester.pump();
      expect(sender.events, isEmpty);
      await tester.tap(find.text('保存布局'));
      await tester.pumpAndSettle();
      expect(saved, isNotNull);
      expect((jsonDecode(saved!)['presets'] as List).length, 1);
      expect(find.byTooltip('游戏设置与预设'), findsOneWidget);
      sender.events.clear();
      final pressed = await tester.startGesture(
        tester.getCenter(find.text('E')),
        pointer: 4,
      );
      await tester.pump();
      expect(sender.events, contains('69:true'));
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await tester.pump();
      expect(sender.events, contains('69:false'));
      await pressed.up();
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pump();
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
      await conn.states.close();
      sender.dispose();
      expect(tester.takeException(), isNull);
    },
  );
}
