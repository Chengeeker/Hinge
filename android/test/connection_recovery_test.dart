import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/trust_store.dart';

// Match native connect's contract so the test can disconnect a ready session
// during the UI's duplicate-resolution delay.
class _ReadySessionManager extends SessionManager {
  int attempts = 0;
  SessionConnection? firstConnection;
  final Completer<void>? firstResultGate;

  _ReadySessionManager({
    required super.localIdentity,
    required super.trustStore,
    this.firstResultGate,
  }) : super(listenPort: 0);

  @override
  Future<SessionConnection> connectToPeer(
    InternetAddress address, [
    int port = 52831,
    String? remotePairingCode,
  ]) async {
    attempts++;
    final connection = await super.connectToPeer(
      address,
      port,
      remotePairingCode,
    );
    if (!await connection.waitUntilReady()) throw StateError('test handshake');
    if (attempts == 1) {
      firstConnection = connection;
      await firstResultGate?.future;
    }
    return connection;
  }
}

void main() {
  for (final holdDirectResult in [false, true]) {
    testWidgets(
      holdDirectResult
          ? 'watcher and direct result preserve the same winning session'
          : 'automatic recovery reconnects without connection popups',
      (tester) async {
        final directory = (await tester.runAsync(
          () => Directory.systemTemp.createTemp('hinge-session-recovery-'),
        ))!;
        const phoneIdentity = DeviceIdentity(
          deviceId: 'recovery-phone',
          name: 'Phone',
        );
        const desktopIdentity = DeviceIdentity(
          deviceId: 'recovery-desktop',
          name: 'Recovery Windows',
        );
        final trust = TrustStore('${directory.path}/trust.json');
        trust.addOrUpdate(
          const TrustedDevice(
            deviceId: 'recovery-desktop',
            name: 'Recovery Windows',
            pairedAt: 1,
            lastSeen: 1,
          ),
        );
        final registry = DeviceRegistry();
        final discovery = DiscoveryService(
          localIdentity: phoneIdentity,
          registry: registry,
        );
        final resultGate = holdDirectResult ? Completer<void>() : null;
        final phone = _ReadySessionManager(
          firstResultGate: resultGate,
          localIdentity: phoneIdentity,
          trustStore: trust,
        );
        final desktop = SessionManager(
          localIdentity: desktopIdentity,
          trustStore: trust,
          listenPort: 0,
        );
        Future<void> cleanUp() async {
          await tester.pumpWidget(const SizedBox.shrink());
          phone.dispose();
          desktop.dispose();
          discovery.dispose();
          await tester.pump(const Duration(seconds: 9));
          await tester.runAsync(() => directory.delete(recursive: true));
        }

        try {
          await tester.runAsync(desktop.startListener);
          final advertisement = DiscoveryMessage(
            deviceId: desktopIdentity.deviceId,
            name: desktopIdentity.name,
            platform: 'windows',
            port: desktop.listeningPort,
            timestamp: 1,
          );
          registry.upsertDevice(advertisement, '127.0.0.1');
          await tester.pumpWidget(
            HingeApp(
              identity: phoneIdentity,
              discoveryService: discovery,
              trustStore: trust,
              sessionManager: phone,
              persistentDataDirectory: directory.path,
            ),
          );
          await tester.pump(const Duration(seconds: 2));
          for (var i = 0; i < 20; i++) {
            await tester.runAsync(
              () => Future<void>.delayed(const Duration(milliseconds: 10)),
            );
            await tester.pump();
            if (phone.firstConnection != null) break;
          }
          expect(phone.attempts, 1);
          expect(phone.firstConnection?.isReady, isTrue);
          await tester.pump();
          if (resultGate != null) {
            // The watcher can select the direct session before connect returns it.
            await tester.pump(const Duration(milliseconds: 50));
            await tester.pump(const Duration(milliseconds: 30));
            resultGate.complete();
            await tester.runAsync(() => Future<void>.delayed(Duration.zero));
            await tester.pump();
            expect(phone.firstConnection!.isReady, isTrue);
            expect(
              phone.connectionForDevice(desktopIdentity.deviceId),
              same(phone.firstConnection),
            );
            return;
          }
          phone.firstConnection!.dispose(manual: false);
          // Deliver disconnect before letting the pending 20 ms UI delay end.
          await tester.pump();
          await tester.pump(const Duration(milliseconds: 50));
          expect(phone.connectionForDevice(desktopIdentity.deviceId), isNull);
          expect(
            registry.devices.single.connectionState,
            isNot(DeviceConnectionState.connected),
          );
          expect(find.text('已连接 Recovery Windows'), findsNothing);

          // A fresh discovery packet should lead to a real retry, not a stale
          // non-null connection being announced as connected every five seconds.
          // The reconnect throttle uses DateTime.now(), not the widget fake clock.
          await tester.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 5100)),
          );
          registry.upsertDevice(advertisement, '127.0.0.1');
          await tester.pump();
          await tester.pump(const Duration(seconds: 6));
          for (var i = 0; i < 20; i++) {
            await tester.runAsync(
              () => Future<void>.delayed(const Duration(milliseconds: 10)),
            );
            await tester.pump(const Duration(milliseconds: 10));
          }
          expect(phone.attempts, greaterThanOrEqualTo(2));
          expect(
            phone.connectionForDevice(desktopIdentity.deviceId)?.isReady,
            isTrue,
          );
          expect(find.text('Recovery Windows 已连接'), findsNothing);
          expect(find.text('已连接 Recovery Windows'), findsNothing);
          expect(find.text('正在自动连接历史设备：Recovery Windows…'), findsNothing);
        } finally {
          if (resultGate != null && !resultGate.isCompleted) {
            resultGate.complete();
          }
          await cleanUp();
        }
      },
    );
  }
}
