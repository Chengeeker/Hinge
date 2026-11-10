import 'package:hinge/core/remote_input_cipher.dart';

import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/touchpad_screen.dart';
import 'package:hinge/app/desktop_gesture_surface.dart';
import 'package:hinge/app/desktop_video_view.dart';
import 'package:hinge/app/game_mode_screen.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/game_controls.dart';
import 'package:hinge/core/remote_input_model.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/device_identity_manager.dart';

class _Connection extends Fake implements SessionConnection {
  final frameController = StreamController<ProtocolFrame>.broadcast();
  final stateController = StreamController<SessionState>.broadcast();
  final events = <RemoteInputEvent>[];
  @override
  String get remoteAddress => '192.0.2.1';
  @override
  SessionPeerInfo? get peerInfo =>
      const SessionPeerInfo(deviceId: 'pc', name: 'PC', platform: 'windows');
  @override
  DeviceIdentity get localIdentity =>
      const DeviceIdentity(deviceId: 'phone', name: 'Phone');
  @override
  void sendJson(MessageType type, Map<String, dynamic> json) {
    if (type == MessageType.desktopControl) {
      frameController.add(
        ProtocolFrame(
          type: type,
          payload: Uint8List.fromList(
            utf8.encode(
              jsonEncode({
                'action': 'description',
                'version': 1,
                'enabled': true,
                'port': 50000,
                'requestId': json['requestId'],
              }),
            ),
          ),
        ),
      );
    }
  }

  @override
  SessionState get state => SessionState.connected;

  @override
  Stream<ProtocolFrame> get frames => frameController.stream;

  @override
  Stream<SessionState> get stateStream => stateController.stream;

  static Uint8List get key => Uint8List.fromList(List.filled(32, 1));
  RemoteInputCipher? cipher;
  Future<void> queue = Future.value();
  @override
  void sendFrame(MessageType type, Uint8List payload) {
    if (type != MessageType.secureRemoteInput) return;
    queue = queue.then((_) async {
      final plain = await cipher!.decrypt(payload);
      if (plain == null) return;
      final event = RemoteInputEvent.tryParse(plain);
      if (event == null) return;
      events.add(event);
      if (event.actionType == RemoteActionType.controlStart) {
        final accepted = await cipher!.encrypt(
          RemoteInputEvent(
            actionType: RemoteActionType.controlAccepted,
            flags: 1,
          ).serialize(),
        );
        frameController.add(
          ProtocolFrame(type: MessageType.secureRemoteInput, payload: accepted),
        );
      }
    });
  }

  Future<void> accept() async {
    final challenge = Uint8List.fromList(List.filled(32, 2));
    cipher = await RemoteInputCipher.create(key, challenge, server: true);
    final bytes = Uint8List(48)
      ..[0] = 0x31
      ..setRange(16, 48, challenge);
    frameController.add(
      ProtocolFrame(type: MessageType.remoteInput, payload: bytes),
    );
  }
}

void main() {
  for (final size in [const Size(800, 360), const Size(568, 256)]) {
    testWidgets(
      'fullscreen mapping preserves mouse gestures and video with cutout ($size)',
      (tester) async {
        tester.view.physicalSize = size;
        tester.view.devicePixelRatio = 1;
        tester.view.viewPadding = const FakeViewPadding(
          left: 24,
          top: 6,
          bottom: 8,
        );
        tester.view.padding = const FakeViewPadding(
          left: 24,
          top: 6,
          bottom: 8,
        );
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        addTearDown(tester.view.resetViewPadding);
        addTearDown(tester.view.resetPadding);
        final connection = _Connection();
        final calls = <MethodCall>[];
        final inputs = <RemoteInputEvent>[];
        final messenger = tester.binding.defaultBinaryMessenger;
        const channel = MethodChannel('hinge/desktop_video');
        messenger.setMockMethodCallHandler(channel, (call) async {
          calls.add(call);
          if (call.method == 'start') {
            return {'sessionId': 77, 'textureId': 123};
          }
          if (call.method == 'sendInput') {
            inputs.add(
              RemoteInputEvent.tryParse(call.arguments['data'] as Uint8List)!,
            );
            return true;
          }
          return null;
        });
        for (final channel in [
          const MethodChannel('hinge/desktop_video/events'),
          GamePresetStore.channel,
          const MethodChannel('hinge/platform'),
        ]) {
          messenger.setMockMethodCallHandler(channel, (_) async => null);
        }
        addTearDown(() async {
          await tester.pumpWidget(const SizedBox());
          await connection.frameController.close();
          await connection.stateController.close();
          for (final channel in [
            channel,
            const MethodChannel('hinge/desktop_video/events'),
            GamePresetStore.channel,
            const MethodChannel('hinge/platform'),
          ]) {
            messenger.setMockMethodCallHandler(channel, null);
          }
        });
        await tester.pumpWidget(
          MaterialApp(
            home: TouchpadScreen(
              peerName: 'PC',
              connection: connection,
              showDesktop: true,
            ),
          ),
        );
        await tester.pumpAndSettle();
        for (final value in [
          {'state': 'format', 'width': 1920, 'height': 1080},
          {
            'state': 'ready',
            'canControl': true,
            'relativeMouse': true,
            'absoluteMouse': true,
          },
        ]) {
          messenger.handlePlatformMessage(
            'hinge/desktop_video/events',
            const StandardMethodCodec().encodeSuccessEnvelope({
              'sessionId': 77,
              ...value,
            }),
            (_) {},
          );
        }
        await tester.pumpAndSettle();
        final texture = tester.element(find.byType(Texture));
        final videoRect = tester.getRect(find.byType(Texture));
        expect(
          tester
              .widget<DesktopVideoView>(find.byType(DesktopVideoView))
              .directTouch,
          false,
          reason: 'unconfigured desktop defaults to mouse pointer movement',
        );
        expect(find.text('按住拖拽'), findsNothing);
        expect(find.text('松开左键'), findsNothing);
        Future<void> tools() async {
          await tester.tap(
            find.byTooltip('按键映射设置').evaluate().isNotEmpty
                ? find.byTooltip('按键映射设置')
                : find.text('操作'),
          );
          await tester.pumpAndSettle();
        }

        Future<void> closeTools() async {
          await tester.tap(find.byTooltip('关闭工具栏'));
          await tester.pumpAndSettle();
        }

        Finder button(String label) => find.descendant(
          of: find.byKey(const ValueKey('game-controls')),
          matching: find.text(label),
        );
        List<RemoteInputEvent> events(RemoteActionType type) =>
            inputs.where((e) => e.actionType == type).toList();
        await tools();
        await tester.tap(find.widgetWithText(SwitchListTile, '启用按键映射'));
        await tester.pumpAndSettle();
        expect(find.widgetWithText(SwitchListTile, '按住左键拖拽'), findsNothing);
        await tester.scrollUntilVisible(
          find.text('屏幕触控'),
          140,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('屏幕触控'));
        await tester.pumpAndSettle();
        await closeTools();
        expect(
          tester.getRect(find.byKey(const ValueKey('game-controls'))),
          Rect.fromLTWH(24, 6, size.width - 24, size.height - 14),
          reason:
              'mapping uses the full screen while video keeps its own bounds',
        );
        expect(tester.getRect(find.byType(Texture)), videoRect);
        expect(
          find.byKey(const ValueKey('desktop-control-rail-left')),
          findsNothing,
        );
        expect(
          find.byKey(const ValueKey('desktop-control-rail-right')),
          findsNothing,
        );
        expect(find.byTooltip('退出按键映射'), findsOneWidget);
        expect(find.byTooltip('按键映射设置'), findsOneWidget);
        expect(find.byKey(const ValueKey('game-canvas')), findsNothing);
        expect(tester.getRect(button('Esc')).left, lessThan(videoRect.left));
        expect(
          tester.getRect(button('右键')).right,
          greaterThan(videoRect.right),
        );
        inputs.clear();
        await tester.tap(button('Esc'));
        await tester.pump();
        expect(events(RemoteActionType.keyDown).single.buttonOrKey, 0x1B);
        expect(events(RemoteActionType.keyUp).single.buttonOrKey, 0x1B);
        final center = tester
            .getRect(find.byType(DesktopGestureSurface))
            .center;
        inputs.clear();
        await tester.tapAt(center);
        await tester.pump();
        expect(
          events(RemoteActionType.mouseMove).single.flags,
          RemoteInputEvent.absoluteMouseFlag,
        );
        expect(
          events(RemoteActionType.mouseClick).single.buttonOrKey,
          RemoteMouseButton.left.value,
        );
        inputs.clear();
        final directDrag = await tester.startGesture(
          center + const Offset(40, 0),
          pointer: 2001,
        );
        await tester.pump(const Duration(milliseconds: 460));
        await directDrag.moveBy(const Offset(30, 0));
        await directDrag.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseDown).length, 1);
        expect(events(RemoteActionType.mouseUp).length, 1);
        expect(events(RemoteActionType.mouseClick), isEmpty);
        await tools();
        await tester.scrollUntilVisible(
          find.text('鼠标指针'),
          140,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('鼠标指针'));
        await tester.pumpAndSettle();
        await closeTools();
        inputs.clear();
        final pointerDrag = await tester.startGesture(center, pointer: 2002);
        await tester.pump(const Duration(milliseconds: 460));
        await pointerDrag.moveBy(const Offset(15, 5));
        await pointerDrag.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseDown).length, 1);
        expect(events(RemoteActionType.mouseUp).length, 1);
        expect(events(RemoteActionType.mouseMove).single.flags, 0);
        inputs.clear();
        final left = await tester.startGesture(
          tester.getCenter(button('左键')),
          pointer: 2003,
        );
        await left.moveBy(const Offset(120, -10));
        await left.up();
        await tester.pump();
        expect(
          events(RemoteActionType.mouseMove).single.deltaX,
          closeTo(360, 1),
        );
        expect(events(RemoteActionType.mouseMove).single.flags, 0);
        expect(events(RemoteActionType.mouseDown).single.buttonOrKey, 0);
        expect(events(RemoteActionType.mouseUp).single.buttonOrKey, 0);
        expect(events(RemoteActionType.mouseClick), isEmpty);
        await tools();
        await tester.scrollUntilVisible(
          find.text('屏幕触控'),
          140,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('屏幕触控'));
        await tester.pumpAndSettle();
        await closeTools();
        inputs.clear();
        final right = await tester.startGesture(
          tester.getCenter(button('右键')),
          pointer: 2004,
        );
        final rightDrag = await tester.startGesture(center, pointer: 2005);
        await tester.pump(const Duration(milliseconds: 460));
        await rightDrag.moveBy(const Offset(10, 5));
        await right.up();
        await rightDrag.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseDown).single.buttonOrKey, 1);
        expect(events(RemoteActionType.mouseUp).single.buttonOrKey, 1);
        expect(
          events(RemoteActionType.mouseMove).single.flags,
          0,
          reason: 'held right button must not jump to an absolute coordinate',
        );
        expect(events(RemoteActionType.mouseClick), isEmpty);
        inputs.clear();
        final ctrl = await tester.startGesture(
          tester.getCenter(button('Ctrl')),
          pointer: 2006,
        );
        await tester.tapAt(center);
        await ctrl.up();
        await tester.pump();
        expect(events(RemoteActionType.keyDown).single.buttonOrKey, 0x11);
        expect(events(RemoteActionType.keyUp).single.buttonOrKey, 0x11);
        expect(events(RemoteActionType.mouseClick).length, 1);
        await tools();
        await tester.scrollUntilVisible(
          find.text('鼠标指针'),
          140,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('鼠标指针'));
        await tester.pumpAndSettle();
        await closeTools();
        inputs.clear();
        final ownDrag = await tester.startGesture(center, pointer: 2007);
        await tester.pump(const Duration(milliseconds: 460));
        final mappedHold = await tester.startGesture(
          tester.getCenter(button('左键')),
          pointer: 2008,
        );
        await ownDrag.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseDown).length, 1);
        expect(
          events(RemoteActionType.mouseUp),
          isEmpty,
          reason: 'mapped hold still owns the left button',
        );
        await mappedHold.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseUp).length, 1);
        inputs.clear();
        final scrollA = await tester.startGesture(
          center - const Offset(80, 0),
          pointer: 2009,
        );
        final scrollB = await tester.startGesture(
          center + const Offset(80, 0),
          pointer: 2010,
        );
        await scrollA.moveBy(const Offset(0, 28));
        await scrollB.moveBy(const Offset(0, 28));
        await scrollA.up();
        await scrollB.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseScroll), isNotEmpty);
        expect(events(RemoteActionType.mouseDown), isEmpty);
        expect(events(RemoteActionType.mouseClick), isEmpty);
        await tools();
        await tester.scrollUntilVisible(
          find.widgetWithText(SwitchListTile, '游戏视角'),
          140,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(SwitchListTile, '游戏视角'));
        await tester.pumpAndSettle();
        await closeTools();
        inputs.clear();
        await tester.tapAt(center);
        await tester.pump();
        expect(
          inputs,
          isEmpty,
          reason: 'camera tapping must not accidentally shoot',
        );
        final camera = await tester.startGesture(center, pointer: 2011);
        await camera.moveBy(const Offset(20, 0));
        await camera.up();
        await tester.pump();
        expect(
          events(RemoteActionType.mouseMove).single.flags,
          RemoteInputEvent.relativeMouseFlag,
        );
        inputs.clear();
        final cameraA = await tester.startGesture(
          center - const Offset(80, 0),
          pointer: 2012,
        );
        final cameraB = await tester.startGesture(
          center + const Offset(80, 0),
          pointer: 2013,
        );
        await tester.pump();
        expect(
          events(RemoteActionType.mouseDown),
          isEmpty,
          reason: 'second finger down is not a drag yet',
        );
        await cameraA.moveBy(const Offset(30, 0));
        await tester.pump();
        expect(events(RemoteActionType.mouseDown).length, 1);
        await cameraA.up();
        await cameraB.up();
        await tester.pump();
        expect(events(RemoteActionType.mouseUp).length, 1);
        await tester.tap(find.byTooltip('退出按键映射'));
        await tester.pumpAndSettle();
        expect(find.byKey(const ValueKey('game-controls')), findsNothing);
        expect(
          find.byKey(const ValueKey('desktop-control-rail-left')),
          findsOneWidget,
        );
        expect(
          find.byKey(const ValueKey('desktop-control-rail-right')),
          findsOneWidget,
        );
        expect(tester.getRect(find.byType(Texture)), videoRect);
        expect(tester.element(find.byType(Texture)), same(texture));
        expect(calls.where((c) => c.method == 'start').length, 1);
        expect(calls.where((c) => c.method == 'stop'), isEmpty);
      },
    );
  }
  for (final portrait in [false, true]) {
    testWidgets(
      'desktop mapping uses shared presets and releases controls (portrait=$portrait)',
      (tester) async {
        tester.view.physicalSize = portrait
            ? const Size(390, 844)
            : const Size(800, 360);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final connection = _Connection();
        final calls = <MethodCall>[], systemCalls = <MethodCall>[];
        final inputs = <RemoteInputEvent>[];
        var loads = 0;
        final preset = GamePreset.basic();
        preset.controls.firstWhere((c) => c.label == 'Q')
          ..label = '技能'
          ..binding = 0x54;
        final store = GamePresetStore()
          ..presets = [preset]
          ..selected = preset.id;
        final messenger = tester.binding.defaultBinaryMessenger;
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video'),
          (call) async {
            calls.add(call);
            if (call.method == 'start') {
              return {'sessionId': 77, 'textureId': 123};
            }
            if (call.method == 'sendInput') {
              inputs.add(
                RemoteInputEvent.tryParse(call.arguments['data'] as Uint8List)!,
              );
              return true;
            }
            return null;
          },
        );
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video/events'),
          (_) async => null,
        );
        messenger.setMockMethodCallHandler(GamePresetStore.channel, (
          call,
        ) async {
          if (call.method == 'loadDesktopSelection') return preset.id;
          if (call.method == 'load') {
            loads++;
            return store.toJsonString();
          }
          return null;
        });
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/platform'),
          (_) async => false,
        );
        messenger.setMockMethodCallHandler(SystemChannels.platform, (
          call,
        ) async {
          systemCalls.add(call);
          return null;
        });
        addTearDown(() async {
          await tester.pumpWidget(const SizedBox());
          await tester.pump();
          await connection.frameController.close();
          await connection.stateController.close();
          for (final channel in [
            const MethodChannel('hinge/desktop_video'),
            const MethodChannel('hinge/desktop_video/events'),
            GamePresetStore.channel,
            const MethodChannel('hinge/platform'),
            SystemChannels.platform,
          ]) {
            messenger.setMockMethodCallHandler(channel, null);
          }
        });
        void ready(bool relative) => messenger.handlePlatformMessage(
          'hinge/desktop_video/events',
          const StandardMethodCodec().encodeSuccessEnvelope({
            'sessionId': 77,
            'state': 'ready',
            'canControl': true,
            'relativeMouse': relative,
            'absoluteMouse': true,
          }),
          (_) {},
        );
        Future<void> openTools() async {
          if (find.byTooltip('桌面操作').evaluate().isNotEmpty) {
            await tester.tap(find.byTooltip('桌面操作'));
          } else {
            await tester.tap(
              find.byTooltip('按键映射设置').evaluate().isNotEmpty
                  ? find.byTooltip('按键映射设置')
                  : find.text('操作'),
            );
          }
          await tester.pumpAndSettle();
        }

        final mapping = find.widgetWithText(SwitchListTile, '启用按键映射');
        await tester.pumpWidget(
          MaterialApp(
            home: TouchpadScreen(
              peerName: 'PC',
              connection: connection,
              showDesktop: true,
            ),
          ),
        );
        await tester.pumpAndSettle();
        ready(true);
        await tester.pumpAndSettle();
        final texture = tester.element(find.byType(Texture));
        await openTools();
        await tester.tap(mapping);
        await tester.pumpAndSettle();
        expect(tester.widget<SwitchListTile>(mapping).value, true);
        expect(find.text('切换操控方案'), findsOneWidget);
        expect(find.text('编辑当前方案'), findsOneWidget);
        expect(find.text('其他设置'), findsOneWidget);
        await tester.ensureVisible(find.widgetWithText(SwitchListTile, '游戏视角'));
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(SwitchListTile, '游戏视角'));
        await tester.pumpAndSettle();
        await tester.tap(find.byTooltip('关闭工具栏'));
        await tester.pumpAndSettle();
        if (portrait) {
          expect(
            systemCalls.any(
              (c) =>
                  c.method == 'SystemChrome.setPreferredOrientations' &&
                  (c.arguments as List).contains(
                    'DeviceOrientation.landscapeLeft',
                  ),
            ),
            true,
          );
          tester.view.physicalSize = const Size(800, 360);
          await tester.pumpAndSettle();
        }
        expect(find.byTooltip('退出游戏模式'), findsNothing);
        expect(find.text('技能'), findsOneWidget);
        Rect controlRect(String label) => tester.getRect(
          find.byWidgetPredicate(
            (widget) =>
                widget is Semantics &&
                (widget.properties.label?.startsWith('$label，') ?? false),
          ),
        );
        expect(controlRect('+').overlaps(controlRect('1')), false);
        expect(controlRect('+').overlaps(controlRect('2')), false);
        expect(find.byTooltip('游戏设置与预设'), findsNothing);
        expect(loads, 1);
        expect(tester.element(find.byType(Texture)), same(texture));
        await openTools();
        await tester.ensureVisible(find.text('其他设置'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('其他设置'));
        await tester.pumpAndSettle();
        expect(find.text('映射设置'), findsOneWidget);
        expect(find.text('触控反馈'), findsOneWidget);
        expect(find.text('按键不透明度 55%'), findsOneWidget);
        expect(
          find.text('此模式控制电脑键鼠；电脑画面仍需在电脑屏幕查看。部分游戏或管理员窗口可能不接受系统模拟输入。'),
          findsNothing,
        );
        await tester.binding.handlePopRoute();
        await tester.pumpAndSettle();
        await openTools();
        await tester.ensureVisible(find.text('切换操控方案'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('切换操控方案'));
        await tester.pumpAndSettle();
        expect(find.text('操控方案'), findsOneWidget);
        expect(find.text('桌面精简布局'), findsOneWidget);
        await tester.binding.handlePopRoute();
        await tester.pumpAndSettle();
        final canvas = tester.getRect(
          find.byKey(const ValueKey('game-canvas')),
        );
        final camera = await tester.startGesture(canvas.center, pointer: 1020);
        await camera.moveBy(const Offset(8, 4));
        await camera.up();
        await tester.pump();
        expect(
          inputs.any(
            (e) =>
                e.actionType == RemoteActionType.mouseMove &&
                e.flags == RemoteInputEvent.relativeMouseFlag,
          ),
          true,
        );
        final skill = await tester.startGesture(
          tester.getCenter(find.text('技能')),
          pointer: 1021,
        );
        final stick = await tester.startGesture(
          tester.getCenter(find.text('移动')),
          pointer: 1022,
        );
        await stick.moveBy(const Offset(50, -50));
        await tester.pump();
        expect(
          inputs
              .where((e) => e.actionType == RemoteActionType.keyDown)
              .map((e) => e.buttonOrKey),
          containsAll([0x54, 87, 68]),
        );
        await openTools();
        expect(
          inputs
              .where((e) => e.actionType == RemoteActionType.keyUp)
              .map((e) => e.buttonOrKey),
          containsAll([0x54, 87, 68]),
        );
        final pausedCount = inputs.length;
        await skill.moveBy(const Offset(2, 0));
        await stick.moveBy(const Offset(2, 0));
        await tester.pump();
        expect(inputs.length, pausedCount);
        await skill.up();
        await stick.up();
        await tester.tap(mapping);
        await tester.pumpAndSettle();
        expect(tester.widget<SwitchListTile>(mapping).value, false);
        await tester.tap(find.byTooltip('关闭工具栏'));
        await tester.pumpAndSettle();
        expect(find.byKey(const ValueKey('game-canvas')), findsNothing);
        expect(
          find.byType(GameModeScreen, skipOffstage: false),
          findsOneWidget,
        );
        await openTools();
        await tester.tap(mapping);
        await tester.pumpAndSettle();
        await tester.tap(find.byTooltip('关闭工具栏'));
        await tester.pumpAndSettle();
        expect(loads, 1, reason: 'toggling uses the same preset/edit state');
        final held = await tester.startGesture(
          tester.getCenter(find.text('技能')),
          pointer: 1023,
        );
        await tester.pump();
        await tester.binding.handlePopRoute();
        await tester.pumpAndSettle();
        await held.up();
        expect(find.byKey(const ValueKey('game-canvas')), findsNothing);
        expect(
          find.text('退出全屏'),
          findsOneWidget,
          reason: 'back closes mapping, not the desktop',
        );
        await openTools();
        await tester.tap(mapping);
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(FilledButton, '仅观看'));
        await tester.pumpAndSettle();
        expect(tester.widget<SwitchListTile>(mapping).value, false);
        expect(tester.widget<SwitchListTile>(mapping).onChanged, isNull);
        await tester.tap(find.widgetWithText(FilledButton, '仅观看'));
        await tester.pumpAndSettle();
        await tester.tap(mapping);
        await tester.pumpAndSettle();
        await tester.tap(find.byTooltip('关闭工具栏'));
        await tester.pumpAndSettle();
        final revoked = await tester.startGesture(
          tester.getCenter(find.text('技能')),
          pointer: 1024,
        );
        await tester.pump();
        final ups = inputs
            .where(
              (e) =>
                  e.actionType == RemoteActionType.keyUp &&
                  e.buttonOrKey == 0x54,
            )
            .length;
        ready(false);
        await tester.pumpAndSettle();
        await revoked.up();
        expect(find.byKey(const ValueKey('game-canvas')), findsNothing);
        expect(
          inputs
              .where(
                (e) =>
                    e.actionType == RemoteActionType.keyUp &&
                    e.buttonOrKey == 0x54,
              )
              .length,
          greaterThan(ups),
        );
        await openTools();
        expect(tester.widget<SwitchListTile>(mapping).onChanged, isNull);
        await tester.tap(find.byTooltip('关闭工具栏'));
        await tester.pumpAndSettle();
        expect(calls.where((c) => c.method == 'start').length, 1);
        expect(calls.where((c) => c.method == 'stop'), isEmpty);
        expect(tester.element(find.byType(Texture)), same(texture));
        expect(tester.takeException(), isNull);
      },
    );
  }

  for (final size in [
    const Size(320, 760),
    const Size(800, 360),
    const Size(844, 390),
    const Size(568, 256),
  ]) {
    testWidgets(
      'desktop tools fit $size with large text and viewing permission',
      (tester) async {
        tester.view.physicalSize = size;
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final connection = _Connection();
        final messenger = tester.binding.defaultBinaryMessenger;
        final calls = <MethodCall>[];
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video'),
          (call) async {
            calls.add(call);
            if (call.method == 'start') {
              return {'sessionId': 77, 'textureId': 123};
            }
            return null;
          },
        );
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video/events'),
          (_) async => null,
        );
        addTearDown(() async {
          await tester.pumpWidget(const SizedBox());
          await connection.frameController.close();
          await connection.stateController.close();
          messenger.setMockMethodCallHandler(
            const MethodChannel('hinge/desktop_video'),
            null,
          );
          messenger.setMockMethodCallHandler(
            const MethodChannel('hinge/desktop_video/events'),
            null,
          );
        });
        await tester.pumpWidget(
          MaterialApp(
            builder: (ctx, child) => MediaQuery(
              data: MediaQuery.of(ctx)
                  .copyWith(textScaler: const TextScaler.linear(1.6)),
              child: child!,
            ),
            home: TouchpadScreen(
              peerName: 'PC',
              connection: connection,
              showDesktop: true,
            ),
          ),
        );
        await tester.pumpAndSettle();
        messenger.handlePlatformMessage(
          'hinge/desktop_video/events',
          const StandardMethodCodec().encodeSuccessEnvelope({
            'sessionId': 77,
            'state': 'ready',
            'canControl': false,
          }),
          (_) {},
        );
        await tester.pumpAndSettle();
        if (size.width < size.height) {
          await tester.tap(find.byTooltip('桌面操作'));
        } else {
          await tester.tap(find.text('操作'));
        }
        await tester.pumpAndSettle();
        expect(find.text('仅观看 · 鼠标和键盘输入已暂停'), findsOneWidget);
        final exit = tester.getRect(find.widgetWithText(FilledButton, '退出远控'));
        final watch = tester.getRect(find.widgetWithText(FilledButton, '仅观看'));
        final header = find.byWidgetPredicate(
          (widget) =>
              widget is Text &&
              widget.data == '操作' &&
              widget.style?.fontSize == (size.width > size.height ? 18 : 22),
        );
        expect(header, findsOneWidget);
        expect(exit.height, greaterThanOrEqualTo(48));
        if (size.width > size.height) {
          expect(
            exit.top,
            closeTo(watch.top, .1),
            reason: 'all three primary actions must fit one landscape row',
          );
          expect(exit.width, lessThan(108));
          final context = tester.element(
            find.widgetWithText(FilledButton, '退出远控'),
          );
          expect(
            MediaQuery.textScalerOf(context).scale(10),
            16,
            reason: 'keep system large-text scaling',
          );
          expect(Theme.of(context).textTheme.bodyLarge?.fontSize, 13);
        } else {
          expect(
            exit.size,
            const Size(108, 96),
            reason: 'portrait layout stays unchanged',
          );
          expect(watch.top, greaterThan(exit.top));
        }
        await tester.scrollUntilVisible(
          find.widgetWithText(ListTile, '键盘'),
          200,
          scrollable: find.byType(Scrollable).last,
        );
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(ListTile, '键盘'));
        await tester.pumpAndSettle();
        expect(tester.widget<TextField>(find.byType(TextField)).enabled, false);
        expect(calls.where((call) => call.method == 'sendInput'), isEmpty);
        expect(tester.takeException(), isNull);
      },
    );
  }
  testWidgets(
    'desktop controls keep one texture and gate mapping by capability',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 486);
      tester.view.devicePixelRatio = 1;
      tester.view.viewPadding = const FakeViewPadding(left: 48);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetViewPadding);
      final connection = _Connection();
      final calls = <MethodCall>[];
      final inputs = <RemoteInputEvent>[];
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/desktop_video'),
        (call) async {
          calls.add(call);
          if (call.method == 'start') {
            return {'sessionId': 77, 'textureId': 123};
          }
          if (call.method == 'sendInput') {
            inputs.add(
              RemoteInputEvent.tryParse(call.arguments['data'] as Uint8List)!,
            );
            return true;
          }
          return null;
        },
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/desktop_video/events'),
        (_) async => null,
      );
      messenger.setMockMethodCallHandler(
        GamePresetStore.channel,
        (_) async => null,
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => true,
      );
      addTearDown(() {
        for (final channel in [
          const MethodChannel('hinge/desktop_video'),
          const MethodChannel('hinge/desktop_video/events'),
          GamePresetStore.channel,
          const MethodChannel('hinge/platform'),
        ]) {
          messenger.setMockMethodCallHandler(channel, null);
        }
      });
      await tester.pumpWidget(
        MaterialApp(
          home: TouchpadScreen(
            peerName: 'PC',
            connection: connection,
            showDesktop: true,
          ),
        ),
      );
      await tester.pumpAndSettle();
      void ready(bool relative, {bool systemActions = false}) =>
          messenger.handlePlatformMessage(
            'hinge/desktop_video/events',
            const StandardMethodCodec().encodeSuccessEnvelope({
              'sessionId': 77,
              'state': 'ready',
              'canControl': true,
              'relativeMouse': relative,
              'absoluteMouse': true,
              'systemActions': systemActions,
            }),
            (_) {},
          );
      ready(false);
      await connection.accept();
      await connection.queue;
      await tester.pumpAndSettle();
      expect(find.text('退出全屏'), findsOneWidget);
      expect(find.byTooltip('全屏显示桌面'), findsNothing);
      final leftRail = tester.getRect(
        find.byKey(const ValueKey('desktop-control-rail-left')),
      );
      final screen = tester.getRect(
        find.byKey(const ValueKey('touchpad-surface')),
      );
      final rightRail = tester.getRect(
        find.byKey(const ValueKey('desktop-control-rail-right')),
      );
      expect(leftRail.width, closeTo(rightRail.width, 0.1));
      expect(leftRail.right, closeTo(screen.left, 0.1));
      expect(screen.right, closeTo(rightRail.left, 0.1));
      expect(
        screen.center.dx,
        closeTo(tester.view.physicalSize.width / 2, 0.1),
      );
      expect(screen.height, 486);
      await tester.tap(find.text('右键'));
      await tester.pump();
      expect(inputs.last.buttonOrKey, RemoteMouseButton.right.value);
      await tester.tap(find.text('左键'));
      await tester.pump();
      expect(inputs.last.buttonOrKey, RemoteMouseButton.left.value);
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      expect(find.text('游戏模式'), findsNothing);
      expect(
        tester
            .widget<SwitchListTile>(
              find.widgetWithText(SwitchListTile, '启用按键映射'),
            )
            .onChanged,
        isNull,
      );
      expect(
        tester
            .widget<SwitchListTile>(
              find.widgetWithText(SwitchListTile, '显示鼠标按键'),
            )
            .value,
        true,
      );
      Navigator.of(tester.element(find.byType(SwitchListTile).first)).pop();
      await tester.pumpAndSettle();
      ready(true, systemActions: true);
      await tester.pumpAndSettle();
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      expect(find.text('游戏模式'), findsNothing);
      expect(find.byTooltip('游戏模式'), findsNothing);
      expect(find.byIcon(Icons.sports_esports), findsNothing);
      final panel = find.byType(SegmentedButton<bool>);
      expect(tester.widget<SegmentedButton<bool>>(panel).selected, {false});
      await tester.tap(find.text('屏幕触控').last);
      await tester.pumpAndSettle();
      expect(tester.widget<SegmentedButton<bool>>(panel).selected, {true});
      expect(
        calls.lastWhere((c) => c.method == 'saveInputMode').arguments['direct'],
        true,
      );
      await tester.tap(find.text('鼠标指针').last);
      await tester.pumpAndSettle();
      expect(tester.widget<SegmentedButton<bool>>(panel).selected, {false});
      expect(
        calls.lastWhere((c) => c.method == 'saveInputMode').arguments['direct'],
        false,
      );
      await tester.tap(find.widgetWithText(FilledButton, '仅观看'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(SwitchListTile, '按住左键拖拽'), findsNothing);
      await tester.tap(find.widgetWithText(FilledButton, '仅观看'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(SwitchListTile, '显示鼠标按键'));
      await tester.pumpAndSettle();
      Navigator.of(tester.element(find.byType(SwitchListTile).first)).pop();
      await tester.pumpAndSettle();
      expect(find.text('右键'), findsNothing);
      expect(
        tester
            .getRect(find.byKey(const ValueKey('touchpad-surface')))
            .center
            .dx,
        closeTo(540, .1),
      );
      await tester.tap(find.text('显示').last);
      await tester.pumpAndSettle();
      final beforeZoom = inputs.length;
      await tester.tap(find.text('200%'));
      await tester.pumpAndSettle();
      expect(inputs.length, beforeZoom);
      expect(calls.where((c) => c.method == 'start').length, 1);
      final gesture = find.byType(DesktopGestureSurface);
      expect(tester.widget<DesktopGestureSurface>(gesture).viewScale, 2);
      await tester.tap(find.byTooltip('关闭工具栏'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('键盘').last);
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.widgetWithText(OutlinedButton, 'Enter'),
        160,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(OutlinedButton, 'Enter'));
      await tester.pump();
      expect(inputs[inputs.length - 2].actionType, RemoteActionType.keyDown);
      expect(inputs.last.actionType, RemoteActionType.keyUp);
      expect(inputs.last.buttonOrKey, 0x0D);
      await tester.tap(find.byTooltip('关闭工具栏'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.widgetWithText(ListTile, '系统快捷操作'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, '系统快捷操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.widgetWithText(ListTile, '关机电脑'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, '关机电脑'));
      await tester.pumpAndSettle();
      expect(find.text('关机电脑？'), findsOneWidget);
      await tester.tap(find.text('取消'));
      await tester.pumpAndSettle();
      expect(
        inputs.where(
          (e) => e.actionType == RemoteActionType.desktopSystemAction,
        ),
        isEmpty,
      );
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.widgetWithText(ListTile, '系统快捷操作'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, '系统快捷操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.widgetWithText(ListTile, '关机电脑'),
        180,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ListTile, '关机电脑'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('确认关机'));
      await tester.pumpAndSettle();
      final power = inputs
          .where((e) => e.actionType == RemoteActionType.desktopSystemAction)
          .single;
      expect(power.buttonOrKey, 3);
      expect(power.serialize().length, 16);
      expect(
        tester
            .widget<DesktopVideoView>(find.byType(DesktopVideoView))
            .allowRecovery,
        false,
      );
      for (final value in [
        {'state': 'error', 'message': 'power request', 'retryable': true},
        {'state': 'stopped'},
      ]) {
        messenger.handlePlatformMessage(
          'hinge/desktop_video/events',
          const StandardMethodCodec().encodeSuccessEnvelope({
            'sessionId': 77,
            ...value,
          }),
          (_) {},
        );
      }
      await tester.pumpAndSettle();
      await tester.pump(const Duration(seconds: 5));
      expect(calls.where((c) => c.method == 'start').length, 1);
      await tester.tap(find.text('重新连接'));
      await tester.pumpAndSettle();
      ready(true, systemActions: true);
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<DesktopVideoView>(find.byType(DesktopVideoView))
            .allowRecovery,
        true,
      );
      expect(find.byType(Texture), findsOneWidget);
      expect(calls.where((call) => call.method == 'start').length, 2);
      expect(calls.where((call) => call.method == 'stop'), isEmpty);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await connection.frameController.close();
      await connection.stateController.close();
    },
  );
  testWidgets(
    'desktop portrait keeps fullscreen and real zoom without rotation or game',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final connection = _Connection();
      final calls = <MethodCall>[];
      final systemCalls = <MethodCall>[];
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/desktop_video'),
        (call) async {
          calls.add(call);
          if (call.method == 'start') {
            return {'sessionId': 77, 'textureId': 123};
          }
          return null;
        },
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/desktop_video/events'),
        (_) async => null,
      );
      messenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        systemCalls.add(call);
        return null;
      });
      addTearDown(() async {
        await tester.pumpWidget(const SizedBox.shrink());
        await connection.frameController.close();
        await connection.stateController.close();
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video'),
          null,
        );
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video/events'),
          null,
        );
        messenger.setMockMethodCallHandler(SystemChannels.platform, null);
      });
      await tester.pumpWidget(
        MaterialApp(
          home: TouchpadScreen(
            peerName: 'PC',
            connection: connection,
            showDesktop: true,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byTooltip('全屏显示桌面'), findsOneWidget);
      expect(find.byTooltip('切换横屏'), findsNothing);
      expect(find.byTooltip('切换竖屏'), findsNothing);
      expect(find.byTooltip('游戏模式'), findsNothing);
      expect(find.byIcon(Icons.sports_esports), findsNothing);
      expect(find.byTooltip('虚拟键盘输入'), findsOneWidget);
      await tester.tap(find.byTooltip('缩放/平移画面'));
      await tester.pumpAndSettle();
      expect(find.byTooltip('切回鼠标操作'), findsOneWidget);
      await tester.tap(find.byTooltip('全屏显示桌面'));
      await tester.pumpAndSettle();
      expect(find.text('退出全屏'), findsOneWidget);
      expect(
        systemCalls.any(
          (call) =>
              call.method == 'SystemChrome.setPreferredOrientations' &&
              (call.arguments as List).contains(
                'DeviceOrientation.landscapeLeft',
              ),
        ),
        isTrue,
      );
      await tester.tap(find.text('退出全屏'));
      await tester.pumpAndSettle();
      expect(find.byTooltip('全屏显示桌面'), findsOneWidget);
      expect(find.byTooltip('游戏模式'), findsNothing);
      expect(calls.where((call) => call.method == 'start').length, 1);
      expect(calls.where((call) => call.method == 'stop'), isEmpty);
      expect(tester.takeException(), isNull);
    },
  );
  for (final failWithException in [false, true]) {
    testWidgets(
      'late desktop input ${failWithException ? 'error' : 'refusal'} cannot end a replacement session',
      (tester) async {
        final connection = _Connection(), readiness = <bool>[];
        final pending = Completer<bool>();
        final calls = <MethodCall>[];
        var session = 6;
        final messenger = tester.binding.defaultBinaryMessenger;
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video'),
          (call) async {
            calls.add(call);
            if (call.method == 'start') {
              return {'sessionId': ++session, 'textureId': 123};
            }
            if (call.method == 'sendInput') {
              if (call.arguments['sessionId'] == 7) return pending.future;
              return true;
            }
            return null;
          },
        );
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/desktop_video/events'),
          (_) async => null,
        );
        addTearDown(() {
          messenger.setMockMethodCallHandler(
            const MethodChannel('hinge/desktop_video'),
            null,
          );
          messenger.setMockMethodCallHandler(
            const MethodChannel('hinge/desktop_video/events'),
            null,
          );
        });
        void event(int id, Map<String, Object> value) {
          messenger.handlePlatformMessage(
            'hinge/desktop_video/events',
            const StandardMethodCodec().encodeSuccessEnvelope({
              'sessionId': id,
              ...value,
            }),
            (_) {},
          );
        }

        await tester.pumpWidget(
          MaterialApp(
            home: TouchpadScreen(
              peerName: 'PC',
              connection: connection,
              showDesktop: true,
              onControlStateChanged: readiness.add,
            ),
          ),
        );
        await tester.pumpAndSettle();
        event(7, {'state': 'ready', 'canControl': true});
        await tester.pump();
        await tester.pump();
        await tester.pumpAndSettle();
        await tester.tap(find.text('左键'));
        await tester.pump();
        expect(
          calls.where(
            (call) =>
                call.method == 'sendInput' && call.arguments['sessionId'] == 7,
          ),
          isNotEmpty,
          reason: calls
              .map((call) => '${call.method}: ${call.arguments}')
              .join('\n'),
        );
        event(7, {'state': 'error', 'message': 'network', 'retryable': true});
        event(7, {'state': 'stopped'});
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 500));
        await tester.pumpAndSettle();
        expect(session, 8);
        event(8, {'state': 'ready', 'canControl': true});
        await tester.pump();
        await tester.pump();
        expect(readiness.last, true);
        if (failWithException) {
          pending.completeError(PlatformException(code: 'old-session'));
        } else {
          pending.complete(false);
        }
        await tester.pump();
        await tester.pump();
        expect(readiness.last, true);
        await tester.tap(find.text('左键'));
        await tester.pump();
        expect(
          calls.where(
            (call) =>
                call.method == 'sendInput' && call.arguments['sessionId'] == 8,
          ),
          isNotEmpty,
        );
        await tester.pumpWidget(const SizedBox());
        await tester.pump();
        await connection.frameController.close();
        await connection.stateController.close();
      },
    );
  }
  testWidgets(
    'portrait touchpad opens game mode directly and requests landscape',
    (tester) async {
      tester.view.physicalSize = const Size(500, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final calls = <MethodCall>[];
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        calls.add(call);
        return null;
      });
      messenger.setMockMethodCallHandler(GamePresetStore.channel, (call) async {
        if (call.method == 'load') return null;
        return null;
      });
      messenger.setMockMethodCallHandler(
        const MethodChannel('hinge/platform'),
        (_) async => true,
      );
      addTearDown(() {
        messenger.setMockMethodCallHandler(SystemChannels.platform, null);
        messenger.setMockMethodCallHandler(GamePresetStore.channel, null);
        messenger.setMockMethodCallHandler(
          const MethodChannel('hinge/platform'),
          null,
        );
      });
      final connection = _Connection();
      await tester.pumpWidget(
        MaterialApp(
          home: TouchpadScreen(
            peerName: 'PC',
            connection: connection,
            controlKey: _Connection.key,
          ),
        ),
      );
      await connection.accept();
      await tester.pumpAndSettle();
      expect(find.text('灵敏度 3.0×'), findsOneWidget);
      expect(find.byTooltip('游戏模式'), findsOneWidget);
      await tester.tap(find.byTooltip('游戏模式'));
      await tester.pumpAndSettle();
      expect(find.byTooltip('退出游戏模式'), findsOneWidget);
      expect(
        calls.any(
          (call) =>
              call.method == 'SystemChrome.setPreferredOrientations' &&
              (call.arguments as List).contains(
                'DeviceOrientation.landscapeLeft',
              ),
        ),
        isTrue,
      );
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pump();
      await connection.frameController.close();
      await connection.stateController.close();
    },
  );

  testWidgets('manual rotation is restored on leaving the touchpad', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(500, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final calls = <MethodCall>[];
    final messenger = tester.binding.defaultBinaryMessenger;
    messenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
      calls.add(call);
      return null;
    });
    addTearDown(
      () => messenger.setMockMethodCallHandler(SystemChannels.platform, null),
    );
    final connection = _Connection();
    await tester.pumpWidget(
      MaterialApp(
        home: TouchpadScreen(
          peerName: 'PC',
          connection: connection,
          controlKey: _Connection.key,
        ),
      ),
    );
    await tester.tap(find.byTooltip('切换横屏'));
    await tester.pump();
    await tester.pump();
    expect(calls.last.method, 'SystemChrome.setPreferredOrientations');
    await tester.pump();
    expect(calls.last.arguments, [
      'DeviceOrientation.landscapeLeft',
      'DeviceOrientation.landscapeRight',
    ]);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump();
    await tester.pump();
    expect(calls.last.method, 'SystemChrome.setPreferredOrientations');
    await tester.pump();
    expect(calls.last.arguments, isEmpty);
    await connection.frameController.close();
    await connection.stateController.close();
  });

  testWidgets('landscape splits clicks and keeps sliding as pointer movement', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1000, 500);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final connection = _Connection();
    await tester.pumpWidget(
      MaterialApp(
        home: TouchpadScreen(
          peerName: 'PC',
          connection: connection,
          controlKey: _Connection.key,
        ),
      ),
    );
    final area = tester.getRect(find.byKey(const ValueKey('touchpad-surface')));
    await tester.pump();
    expect(area.top, inInclusiveRange(125, 140));
    await tester.pump();
    expect(area.height, inInclusiveRange(350, 380));
    await tester.pump();
    expect(find.textContaining('左侧轻点'), findsOneWidget);

    final pending = await tester.startGesture(area.center, pointer: 100);
    await connection.accept();
    await tester.pump();
    await pending.up();
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseClick),
      isFalse,
    );

    await tester.tapAt(Offset(area.left + area.width * 0.25, area.center.dy));
    await tester.tapAt(Offset(area.left + area.width * 0.75, area.center.dy));
    final clicks = connection.events
        .where((e) => e.actionType == RemoteActionType.mouseClick)
        .toList();
    await tester.pump();
    expect(clicks.map((e) => e.buttonOrKey), [0, 1]);

    connection.events.clear();
    await tester.dragFrom(
      Offset(area.left + area.width * 0.75, area.center.dy),
      const Offset(-60, 10),
    );
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseMove),
      isTrue,
    );
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseClick),
      isFalse,
    );

    connection.events.clear();
    final first = await tester.startGesture(
      Offset(area.center.dx - 40, area.center.dy),
      pointer: 101,
    );
    final second = await tester.startGesture(
      Offset(area.center.dx + 40, area.center.dy),
      pointer: 102,
    );
    await first.moveBy(const Offset(0, -40));
    await second.moveBy(const Offset(0, -40));
    await first.up();
    await second.up();
    await tester.pump();
    expect(
      connection.events.any(
        (e) => e.actionType == RemoteActionType.mouseScroll,
      ),
      isTrue,
    );
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseClick),
      isFalse,
    );

    connection.events.clear();
    final left = Offset(area.left + area.width * 0.25, area.center.dy);
    await tester.tapAt(left);
    final drag = await tester.startGesture(left, pointer: 103);
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseDown),
      isTrue,
    );
    tester.view.physicalSize = const Size(500, 1000);
    await tester.pump();
    await tester.pump();
    expect(
      connection.events.any((e) => e.actionType == RemoteActionType.mouseUp),
      isTrue,
    );
    await drag.cancel();

    connection.events.clear();
    final portraitArea = tester.getRect(
      find.byKey(const ValueKey('touchpad-surface')),
    );
    await tester.tapAt(
      Offset(
        portraitArea.left + portraitArea.width * 0.75,
        portraitArea.center.dy,
      ),
    );
    final portraitClicks = connection.events.where(
      (e) => e.actionType == RemoteActionType.mouseClick,
    );
    await tester.pump();
    expect(portraitClicks.single.buttonOrKey, RemoteMouseButton.left.value);

    await tester.pumpWidget(const SizedBox.shrink());
    await connection.frameController.close();
    await connection.stateController.close();
  });
}
