import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/desktop_video_view.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/session_manager.dart';

class _Peer extends Fake implements SessionConnection {
  final bool enabled;
  final bool reply;
  _Peer(this.enabled, {this.reply = true});
  final _frames = StreamController<ProtocolFrame>.broadcast();
  final _states = StreamController<SessionState>.broadcast();
  @override
  Stream<ProtocolFrame> get frames => _frames.stream;
  @override
  Stream<SessionState> get stateStream => _states.stream;
  @override
  SessionState get state => SessionState.connected;
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
    if (!reply) return;
    _frames.add(
      ProtocolFrame(
        type: MessageType.desktopControl,
        payload: Uint8List.fromList(
          utf8.encode(
            jsonEncode({
              'action': 'description',
              'version': 1,
              'enabled': enabled,
              'port': 50000,
              'requestId': json['requestId'],
            }),
          ),
        ),
      ),
    );
  }

  Future<void> close() async {
    await _frames.close();
    await _states.close();
  }
}

void main() {
  final binding = TestWidgetsFlutterBinding.ensureInitialized();
  final calls = <MethodCall>[];
  setUp(() {
    calls.clear();
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video'),
      (call) async {
        calls.add(call);
        if (call.method == 'start') return {'sessionId': 7, 'textureId': 123};
        return null;
      },
    );
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video/events'),
      (_) async => null,
    );
  });
  tearDown(() {
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video'),
      null,
    );
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video/events'),
      null,
    );
  });
  void event(Map<String, Object> value) {
    binding.defaultBinaryMessenger.handlePlatformMessage(
      'hinge/desktop_video/events',
      const StandardMethodCodec().encodeSuccessEnvelope({
        'sessionId': 7,
        ...value,
      }),
      (_) {},
    );
  }

  testWidgets(
    'replaced native session stop clears its old view without ending the new view',
    (tester) async {
      final peer = _Peer(true), first = <bool>[], second = <bool>[];
      var next = 6;
      binding.defaultBinaryMessenger.setMockMethodCallHandler(
        const MethodChannel('hinge/desktop_video'),
        (call) async {
          calls.add(call);
          if (call.method == 'start') {
            return {'sessionId': ++next, 'textureId': 100 + next};
          }
          return null;
        },
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Column(
            children: [
              Expanded(
                child: DesktopVideoView(
                  connection: peer,
                  zoomEnabled: false,
                  onReadyChanged: first.add,
                ),
              ),
              Expanded(
                child: DesktopVideoView(
                  connection: peer,
                  zoomEnabled: false,
                  onReadyChanged: second.add,
                ),
              ),
            ],
          ),
        ),
      );
      await tester.pumpAndSettle();
      event({'state': 'ready'});
      event({'sessionId': 8, 'state': 'ready'});
      await tester.pump();
      await tester.pump();
      expect(first, [true]);
      expect(second, [true]);
      event({'state': 'stopped'});
      await tester.pump();
      await tester.pump();
      expect(first, [true, false]);
      expect(second, [true]);
      expect(find.byType(Texture), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await peer.close();
    },
  );

  testWidgets('leaving during description cancels its listener and timeout', (
    tester,
  ) async {
    final peer = _Peer(true, reply: false);
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: (_) {},
        ),
      ),
    );
    expect(peer._frames.hasListener, isTrue);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    expect(peer._frames.hasListener, isFalse);
    await tester.pump(const Duration(seconds: 9));
    expect(tester.takeException(), isNull);
    await peer.close();
  });

  testWidgets(
    'network recovery is bounded even if every retry briefly becomes ready',
    (tester) async {
      final peer = _Peer(true);
      await tester.pumpWidget(
        MaterialApp(
          home: DesktopVideoView(
            connection: peer,
            zoomEnabled: false,
            onReadyChanged: (_) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      for (final delay in [500, 1500, 4000]) {
        event({'state': 'ready'});
        await tester.pump();
        event({'state': 'error', 'message': 'network', 'retryable': true});
        event({'state': 'stopped'});
        await tester.pump();
        await tester.pump(Duration(milliseconds: delay));
        await tester.pumpAndSettle();
      }
      expect(calls.where((call) => call.method == 'start').length, 4);
      event({'state': 'ready'});
      await tester.pump();
      event({'state': 'error', 'message': 'network', 'retryable': true});
      event({'state': 'stopped'});
      await tester.pump();
      await tester.pump(const Duration(seconds: 20));
      expect(calls.where((call) => call.method == 'start').length, 4);
      expect(find.text('network'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await peer.close();
    },
  );

  testWidgets('identity failures do not retry or reopen verification', (
    tester,
  ) async {
    final peer = _Peer(true);
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    event({
      'state': 'error',
      'message': 'identity refused',
      'retryable': false,
    });
    event({'state': 'stopped'});
    await tester.pump();
    await tester.pump(const Duration(seconds: 20));
    expect(calls.where((call) => call.method == 'start').length, 1);
    expect(find.text('identity refused'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await peer.close();
  });

  testWidgets(
    'background cancels pending recovery and foreground resumes it once',
    (tester) async {
      final peer = _Peer(true);
      await tester.pumpWidget(
        MaterialApp(
          home: DesktopVideoView(
            connection: peer,
            zoomEnabled: false,
            onReadyChanged: (_) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      event({'state': 'ready'});
      await tester.pump();
      event({'state': 'error', 'message': 'network', 'retryable': true});
      event({'state': 'stopped'});
      await tester.pump();
      binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await tester.pump(const Duration(seconds: 20));
      expect(calls.where((call) => call.method == 'start').length, 1);
      binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pumpAndSettle();
      expect(calls.where((call) => call.method == 'start').length, 2);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await peer.close();
    },
  );

  testWidgets(
    'native onStop arriving before Flutter pause preserves foreground recovery',
    (tester) async {
      final peer = _Peer(true);
      await tester.pumpWidget(
        MaterialApp(
          home: DesktopVideoView(
            connection: peer,
            zoomEnabled: false,
            onReadyChanged: (_) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      event({'state': 'ready'});
      await tester.pump();
      event({'state': 'stopped'});
      await tester.pump();
      binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
      await tester.pump(const Duration(seconds: 12));
      expect(calls.where((call) => call.method == 'start').length, 1);
      binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await tester.pumpAndSettle();
      expect(calls.where((call) => call.method == 'start').length, 2);
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      await peer.close();
    },
  );

  testWidgets('late native stop completion cannot clear resumed readiness', (
    tester,
  ) async {
    final peer = _Peer(true), ready = <bool>[];
    final oldStop = Completer<void>();
    var next = 6;
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video'),
      (call) async {
        calls.add(call);
        if (call.method == 'start') {
          return {'sessionId': ++next, 'textureId': 100 + next};
        }
        if (call.method == 'stop' && call.arguments['sessionId'] == 7) {
          await oldStop.future;
        }
        return null;
      },
    );
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: ready.add,
        ),
      ),
    );
    await tester.pumpAndSettle();
    event({'state': 'connecting'});
    await tester.pumpAndSettle();
    expect(find.text('身份已验证，正在接收首帧…'), findsOneWidget);
    expect(ready, isEmpty);
    event({'state': 'ready'});
    await tester.pumpAndSettle();
    binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();
    binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();
    event({'sessionId': 8, 'state': 'ready'});
    await tester.pumpAndSettle();
    expect(ready.last, isTrue);
    final count = ready.length;
    oldStop.complete();
    await tester.pumpAndSettle();
    expect(ready.length, count);
    expect(ready.last, isTrue);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await peer.close();
  });

  testWidgets('rapid peer replacement starts only the latest connection', (
    tester,
  ) async {
    final a = _Peer(true), b = _Peer(true), c = _Peer(true);
    final oldStop = Completer<void>();
    var next = 6;
    binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('hinge/desktop_video'),
      (call) async {
        calls.add(call);
        if (call.method == 'start') {
          return {'sessionId': ++next, 'textureId': 100 + next};
        }
        if (call.method == 'stop' && call.arguments['sessionId'] == 7) {
          await oldStop.future;
        }
        return null;
      },
    );
    Widget view(_Peer peer) => MaterialApp(
      home: DesktopVideoView(
        connection: peer,
        zoomEnabled: false,
        onReadyChanged: (_) {},
      ),
    );
    await tester.pumpWidget(view(a));
    await tester.pumpAndSettle();
    await tester.pumpWidget(view(b));
    await tester.pump();
    await tester.pumpWidget(view(c));
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'start').length, 2);
    oldStop.complete();
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'start').length, 2);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await a.close();
    await b.close();
    await c.close();
  });

  testWidgets('background during first-frame wait resumes the connection', (
    tester,
  ) async {
    final peer = _Peer(true);
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    event({'state': 'connecting'});
    await tester.pumpAndSettle();
    event({'state': 'stopped'});
    await tester.pumpAndSettle();
    binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
    await tester.pump();
    binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();
    expect(calls.where((call) => call.method == 'start').length, 2);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await peer.close();
  });

  testWidgets('peer stop closes verification without a late confirmation', (
    tester,
  ) async {
    final peer = _Peer(true);
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    event({'state': 'verify', 'code': '0123 4567 89AB CDEF'});
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    event({'state': 'stopped'});
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsNothing);
    expect(calls.where((call) => call.method == 'confirm'), isEmpty);
    expect(find.byType(Texture), findsNothing);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await peer.close();
  });

  testWidgets('disabled desktop never starts native capture connection', (
    tester,
  ) async {
    final peer = _Peer(false);
    await tester.pumpWidget(
      MaterialApp(
        home: DesktopVideoView(
          connection: peer,
          zoomEnabled: false,
          onReadyChanged: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('开启桌面共享'), findsOneWidget);
    expect(calls.where((call) => call.method == 'start'), isEmpty);
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await peer.close();
  });

  testWidgets(
    'identity confirmation and native presentation stats retain session ownership',
    (tester) async {
      final peer = _Peer(true), readiness = <bool>[];
      await tester.pumpWidget(
        MaterialApp(
          home: DesktopVideoView(
            connection: peer,
            zoomEnabled: false,
            onReadyChanged: readiness.add,
          ),
        ),
      );
      await tester.pumpAndSettle();
      final start = calls.singleWhere((call) => call.method == 'start');
      expect(start.arguments['host'], '192.0.2.1');
      event({'state': 'verify', 'code': '0123 4567 89AB CDEF'});
      await tester.pumpAndSettle();
      expect(find.textContaining('0123 4567 89AB CDEF'), findsOneWidget);
      expect(readiness, isEmpty);
      await tester.tap(find.text('身份码一致'));
      await tester.pumpAndSettle();
      final confirm = calls.singleWhere((call) => call.method == 'confirm');
      expect(confirm.arguments, {'sessionId': 7, 'allow': true});
      event({'state': 'ready'});
      await tester.pump();
      await tester.pump();
      var otherReports = 0;
      void otherObserver(List<FrameTiming> _) {
        otherReports++;
      }

      SchedulerBinding.instance.addTimingsCallback(otherObserver);
      addTearDown(
        () => SchedulerBinding.instance.removeTimingsCallback(otherObserver),
      );
      final rendered = List.generate(61, (i) {
        final start = 1000000 + i * 16667;
        return FrameTiming(
          vsyncStart: start,
          buildStart: start,
          buildFinish: start + 1000,
          rasterStart: start + 1000,
          rasterFinish: start + 4000,
          rasterFinishWallTime: start + 4000,
        );
      });
      binding.platformDispatcher.onReportTimings?.call(rendered);
      event({
        'state': 'stats',
        'timeMs': 1000,
        'consumed': 10,
        'presented': 10,
        'received': 10,
        'decoded': 10,
      });
      await tester.pump();
      event({
        'state': 'stats',
        'timeMs': 2000,
        'consumed': 55,
        'textureSampling': true,
        'maxTextureGapMs': 80.0,
        'presented': 70,
        'received': 70,
        'decoded': 70,
        'pending': 2,
        'decoderName': 'c2.vendor.avc.decoder',
        'decoderMode': 'hardware',
        'maxVideoGapMs': 50.0,
        'displayHz': 120.0,
      });
      await tester.pump();
      expect(readiness, [true]);
      await tester.pumpAndSettle();
      expect(find.textContaining('视频输出 60 FPS'), findsOneWidget);
      expect(find.textContaining('纹理取帧 45 FPS'), findsOneWidget);
      expect(find.textContaining('视频最大间隔 50.0 ms'), findsOneWidget);
      expect(
        find.textContaining('屏幕 120 Hz · 界面重建 P95 4.0 ms'),
        findsOneWidget,
      );
      expect(
        find.textContaining('硬解 · 待解码 2 · c2.vendor.avc.decoder'),
        findsOneWidget,
      );
      await tester.pumpWidget(const SizedBox());
      await tester.pump();
      expect(calls.last.method, 'stop');
      expect(calls.last.arguments['sessionId'], 7);
      binding.platformDispatcher.onReportTimings?.call(rendered);
      expect(otherReports, 2);
      await peer.close();
    },
  );
}
