import 'package:hinge/core/remote_input_cipher.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/touchpad_screen.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/game_controls.dart';
import 'package:hinge/core/remote_input_model.dart';
import 'package:hinge/core/session_manager.dart';

class _Connection extends Fake implements SessionConnection {
  final frameController = StreamController<ProtocolFrame>.broadcast();
  final stateController = StreamController<SessionState>.broadcast();
  final events = <RemoteInputEvent>[];

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
