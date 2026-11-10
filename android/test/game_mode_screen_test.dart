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
  for (final viewport in [const Size(800, 360), const Size(568, 256)]) {
    testWidgets('editor keeps tools outside complete canvas $viewport', (
      tester,
    ) async {
      tester.view.physicalSize = viewport;
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final previewFont = Platform.environment['HINGE_EDITOR_PREVIEW_FONT'];
      if (previewFont != null) {
        await tester.runAsync(() async {
          final loader = FontLoader('EditorPreview')
            ..addFont(
              File(previewFont)
                  .readAsBytes()
                  .then((bytes) => ByteData.sublistView(bytes)),
            );
          await loader.load();
          final icons = FontLoader('MaterialIcons')
            ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
          await icons.load();
        });
      }
      final preset = GamePreset.basic();
      final store = GamePresetStore()
        ..presets = [preset]
        ..selected = preset.id;
      String? saved;
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (call.method == 'load') return store.toJsonString();
        if (call.method == 'save') saved = call.arguments as String;
        return null;
      });
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => false,
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
      final key = GlobalKey<GameModeScreenState>();
      const capture = ValueKey('editor-capture');
      await tester.pumpWidget(
        RepaintBoundary(
          key: capture,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            theme: ThemeData(
              fontFamily: previewFont == null ? null : 'EditorPreview',
              colorScheme: ColorScheme.fromSeed(
                seedColor: const Color(0xff8a90c0),
                brightness: Brightness.dark,
              ),
            ),
            home: GameModeScreen(key: key, sender: sender, connection: conn),
          ),
        ),
      );
      await tester.pumpAndSettle();
      key.currentState!.editCurrentPreset();
      await tester.pumpAndSettle();
      final canvasFinder = find.byKey(const ValueKey('game-controls'));
      final canvas = tester.getRect(canvasFinder);
      final dock = tester.getRect(find.byKey(const ValueKey('editor-dock')));
      expect(canvas.right, lessThan(dock.left));
      expect(canvas.width / canvas.height, closeTo(viewport.aspectRatio, .001));
      expect(dock.contains(tester.getCenter(find.text('保存布局'))), isTrue);
      expect(dock.contains(tester.getCenter(find.text('添加按键'))), isTrue);
      for (final control in preset.controls) {
        final face = find.descendant(
          of: canvasFinder,
          matching: find.text(control.label),
        );
        expect(canvas.contains(tester.getCenter(face)), isTrue);
      }
      final plus = find.descendant(of: canvasFinder, matching: find.text('+'));
      await tester.tap(plus);
      await tester.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('control-resize-handle')),
        findsOneWidget,
      );
      if (viewport.width == 800) {
        await tester.runAsync(() async {
          final boundary = tester.renderObject<RenderRepaintBoundary>(
            find.byKey(capture),
          );
          final image = await boundary.toImage(pixelRatio: 2);
          final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
          await File('../tmp/editor-workspace-preview.png')
              .writeAsBytes(bytes!.buffer.asUint8List());
          image.dispose();
        });
      }
      await tester.ensureVisible(find.text('属性'));
      await tester.tap(find.text('属性'));
      await tester.pumpAndSettle();
      expect(dock.contains(tester.getCenter(find.text('编辑按钮'))), isTrue);
      expect(tester.getRect(canvasFinder), canvas);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('完成'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('放大画布'));
      await tester.pumpAndSettle();
      expect(tester.getRect(canvasFinder).width, greaterThan(canvas.width));
      await tester.tap(find.byTooltip('适应画布'));
      await tester.pumpAndSettle();
      expect(tester.getRect(canvasFinder), canvas);
      await tester.tap(find.text('保存布局'));
      await tester.pumpAndSettle();
      expect(
        jsonDecode(saved!)['presets'][0]['controls'],
        preset.toJson()['controls'],
      );
      expect(sender.events, isEmpty);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      sender.dispose();
      await conn.states.close();
    });
  }
  testWidgets(
    'editor selects without a sheet, moves, resizes circles and undoes',
    (tester) async {
      tester.view.physicalSize = const Size(800, 360);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final preset = GamePreset('test', '测试', [
        GameControl(
          id: 'q',
          label: 'Q',
          x: .4,
          y: .45,
          size: .2,
          shape: 'circle',
        ),
      ]);
      final store = GamePresetStore()
        ..presets = [preset]
        ..selected = preset.id;
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(
        GamePresetStore.channel,
        (call) async => call.method == 'load' ? store.toJsonString() : null,
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => false,
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
      final key = GlobalKey<GameModeScreenState>();
      await tester.pumpWidget(
        MaterialApp(
          home: GameModeScreen(key: key, sender: sender, connection: conn),
        ),
      );
      await tester.pumpAndSettle();
      key.currentState!.editCurrentPreset();
      await tester.pumpAndSettle();
      await tester.tap(
        find.descendant(
          of: find.byKey(const ValueKey('game-controls')),
          matching: find.text('Q'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('编辑按钮'), findsNothing);
      expect(find.text('属性'), findsOneWidget);
      expect(sender.events, isEmpty);
      final before = tester.getCenter(
        find.descendant(
          of: find.byKey(const ValueKey('game-controls')),
          matching: find.text('Q'),
        ),
      );
      await tester.drag(
        find.descendant(
          of: find.byKey(const ValueKey('game-controls')),
          matching: find.text('Q'),
        ),
        const Offset(60, 20),
      );
      await tester.pumpAndSettle();
      expect(
        tester
            .getCenter(
              find.descendant(
                of: find.byKey(const ValueKey('game-controls')),
                matching: find.text('Q'),
              ),
            )
            .dx,
        greaterThan(before.dx),
      );
      await tester.tap(find.byTooltip('撤销'));
      await tester.pumpAndSettle();
      expect(
        tester.getCenter(
          find.descendant(
            of: find.byKey(const ValueKey('game-controls')),
            matching: find.text('Q'),
          ),
        ),
        before,
      );
      final face = find
          .ancestor(
            of: find.descendant(
              of: find.byKey(const ValueKey('game-controls')),
              matching: find.text('Q'),
            ),
            matching: find.byType(GestureDetector),
          )
          .first;
      final size = tester.getSize(face);
      await tester.drag(
        find.byKey(const ValueKey('control-resize-handle')),
        const Offset(30, 35),
      );
      await tester.pumpAndSettle();
      final resized = tester.getSize(face);
      expect(resized.height, greaterThan(size.height));
      expect(resized.width, closeTo(resized.height, .001));
      expect(sender.events, isEmpty);
      await tester.pumpWidget(const SizedBox());
      sender.dispose();
      await conn.states.close();
    },
  );
  testWidgets(
    'desktop default leaves the center clear and can edit the same preset',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(
        GamePresetStore.channel,
        (_) async => null,
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => false,
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
      final key = GlobalKey<GameModeScreenState>();
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: GameModeScreen(
              key: key,
              sender: sender,
              connection: conn,
              desktop: true,
              onExit: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(key.currentState!.presetName, '桌面精简布局');
      expect(find.text('地图'), findsNothing);
      expect(find.text('移动'), findsNothing);
      expect(find.byTooltip('退出游戏模式'), findsNothing);
      for (final size in [const Size(800, 360), const Size(568, 256)]) {
        tester.view.physicalSize = size;
        await tester.pumpAndSettle();
        final canvas = tester.getRect(
          find.byKey(const ValueKey('game-controls')),
        );
        final controls = find.byWidgetPredicate(
          (w) => w is Semantics && (w.properties.label?.contains('，') ?? false),
        );
        final rects = controls
            .evaluate()
            .map((e) => tester.getRect(find.byWidget(e.widget)))
            .toList();
        expect(rects.length, 8);
        for (var i = 0; i < rects.length; i++) {
          expect(rects[i].contains(canvas.center), false);
          expect(rects[i].width, greaterThanOrEqualTo(48));
          expect(rects[i].height, greaterThanOrEqualTo(48));
          for (final tooltip in ['退出按键映射', '按键映射设置']) {
            final toolbar = tester.getRect(find.byTooltip(tooltip));
            expect(rects[i].overlaps(toolbar), false);
          }

          for (var j = i + 1; j < rects.length; j++) {
            expect(rects[i].overlaps(rects[j]), false);
          }
        }
      }
      key.currentState!.editCurrentPreset();
      await tester.pumpAndSettle();
      expect(find.text('保存布局'), findsOneWidget);
      unawaited(key.currentState!.openSettings());
      await tester.pumpAndSettle();
      expect(find.text('放弃未保存的布局修改？'), findsOneWidget);
      await tester.tap(find.text('取消'));
      await tester.pumpAndSettle();
      expect(find.text('保存布局'), findsOneWidget);
      expect(find.text('操控方案'), findsNothing);
      await tester.tap(find.text('保存布局'));
      await tester.pumpAndSettle();
      expect(key.currentState!.presetName, '桌面精简布局');
      expect(find.text('保存布局'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      sender.dispose();
      await conn.states.close();
    },
  );
  testWidgets(
    'embedded mapping pauses keys and retains edits without owning system UI',
    (tester) async {
      tester.view.physicalSize = const Size(800, 360);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final messenger = tester.binding.defaultBinaryMessenger;
      final system = <MethodCall>[];
      var loads = 0, exits = 0;
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (call.method == 'load') loads++;
        return null;
      });
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => false,
      );
      messenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        system.add(call);
        return null;
      });
      final conn = _Connection();
      final sender = _Sender(conn);
      var enabled = true, visible = true;
      late StateSetter refresh;
      await tester.pumpWidget(
        MaterialApp(
          home: StatefulBuilder(
            builder: (ctx, rebuild) {
              refresh = rebuild;
              return Visibility(
                visible: visible,
                maintainState: true,
                child: GameModeScreen(
                  sender: sender,
                  connection: conn,
                  inputEnabled: enabled && visible,
                  onExit: () => exits++,
                ),
              );
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      final held = await tester.startGesture(
        tester.getCenter(find.text('Q')),
        pointer: 1030,
      );
      await tester.pump();
      expect(sender.events, contains('81:true'));
      refresh(() => enabled = false);
      await tester.pumpAndSettle();
      expect(sender.events, contains('81:false'));
      await held.up();
      refresh(() => enabled = true);
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('游戏设置与预设'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('编辑布局'));
      await tester.pumpAndSettle();
      expect(find.text('保存布局'), findsOneWidget);
      refresh(() => visible = false);
      await tester.pumpAndSettle();
      expect(find.text('保存布局'), findsNothing);
      refresh(() => visible = true);
      await tester.pumpAndSettle();
      expect(find.text('保存布局'), findsOneWidget);
      expect(loads, 1);
      await tester.tap(find.byTooltip('取消编辑'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('确定'));
      await tester.pumpAndSettle();
      await tester.tap(find.byTooltip('退出游戏模式'));
      await tester.pumpAndSettle();
      expect(exits, 1);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      expect(
        system.where(
          (call) =>
              call.method == 'SystemChrome.setPreferredOrientations' ||
              call.method == 'SystemChrome.setEnabledSystemUIMode',
        ),
        isEmpty,
      );
      sender.dispose();
      await conn.states.close();
      messenger.setMockMethodCallHandler(GamePresetStore.channel, null);
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        null,
      );
      messenger.setMockMethodCallHandler(SystemChannels.platform, null);
    },
  );
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
