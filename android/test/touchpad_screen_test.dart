import 'package:hinge/core/remote_input_cipher.dart';

import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/touchpad_screen.dart';
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
  testWidgets(
    'desktop game overlay keeps one texture/session and releases held keys on exit',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 486);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
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
      void ready(bool relative) => messenger.handlePlatformMessage(
        'hinge/desktop_video/events',
        const StandardMethodCodec().encodeSuccessEnvelope({
          'sessionId': 77,
          'state': 'ready',
          'canControl': true,
          'relativeMouse': relative,
        }),
        (_) {},
      );
      ready(false);
      await tester.pumpAndSettle();
      expect(find.text('退出全屏'), findsOneWidget);
      expect(find.byTooltip('全屏显示桌面'), findsNothing);
      expect(
        tester.getRect(find.byKey(const ValueKey('touchpad-surface'))).height,
        486,
      );
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      expect(find.text('游戏模式'), findsNothing);
      expect(
        tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value,
        false,
      );
      Navigator.of(tester.element(find.byType(SwitchListTile))).pop();
      await tester.pumpAndSettle();
      ready(true);
      await tester.pumpAndSettle();
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('游戏模式'),
        140,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('游戏模式'));
      await tester.pumpAndSettle();
      expect(find.byTooltip('退出游戏模式'), findsOneWidget);
      expect(find.byType(Texture), findsOneWidget);
      expect(calls.where((call) => call.method == 'start').length, 1);
      expect(calls.where((call) => call.method == 'stop'), isEmpty);
      final canvas = tester.getRect(find.byKey(const ValueKey('game-canvas')));
      final camera = await tester.startGesture(canvas.center, pointer: 10);
      await camera.moveBy(const Offset(30, 10));
      await camera.up();
      await tester.pump();
      expect(
        inputs.where(
          (input) =>
              input.actionType == RemoteActionType.mouseMove &&
              input.flags == RemoteInputEvent.relativeMouseFlag,
        ),
        isNotEmpty,
      );
      final held = await tester.startGesture(
        tester.getCenter(find.text('Q')),
        pointer: 11,
      );
      await tester.pump();
      expect(
        inputs.where(
          (input) =>
              input.actionType == RemoteActionType.keyDown &&
              input.buttonOrKey == 81,
        ),
        isNotEmpty,
      );
      await tester.tap(find.byTooltip('退出游戏模式'));
      await tester.pumpAndSettle();
      await held.up();
      await tester.pump();
      expect(
        inputs.where(
          (input) =>
              input.actionType == RemoteActionType.keyUp &&
              input.buttonOrKey == 81,
        ),
        isNotEmpty,
      );
      expect(find.byTooltip('退出游戏模式'), findsNothing);
      expect(find.byType(Texture), findsOneWidget);
      expect(calls.where((call) => call.method == 'start').length, 1);
      await tester.tap(find.text('操作'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('游戏模式'),
        140,
        scrollable: find.byType(Scrollable).last,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('游戏模式'));
      await tester.pumpAndSettle();
      final lostCapabilityHold = await tester.startGesture(
        tester.getCenter(find.text('Q')),
        pointer: 12,
      );
      await tester.pump();
      ready(false);
      await tester.pumpAndSettle();
      expect(
        inputs
            .where(
              (input) =>
                  input.actionType == RemoteActionType.keyUp &&
                  input.buttonOrKey == 81,
            )
            .length,
        2,
      );
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      await lostCapabilityHold.up();
      expect(find.byTooltip('退出游戏模式'), findsNothing);
      expect(find.byType(Texture), findsOneWidget);
      expect(calls.where((call) => call.method == 'stop'), isEmpty);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await connection.frameController.close();
      await connection.stateController.close();
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
        await tester.tap(find.text('操作'));
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.text('鼠标左键'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('鼠标左键'));
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
        await tester.tap(find.text('操作'));
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.text('鼠标左键'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('鼠标左键'));
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
