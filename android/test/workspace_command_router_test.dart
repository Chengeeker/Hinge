import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/pairing_manager.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/transfer_manager.dart';
import 'package:hinge/core/trust_store.dart';
import 'package:hinge/core/workspace_data_service.dart';

void main() {
  test('router handles commands on a session that existed before start', () async {
    final tempDirectory = await Directory.systemTemp.createTemp(
      'hinge-workspace-router-',
    );
    final serverIdentity = const DeviceIdentity(
      deviceId: 'router-server',
      name: 'Router Server',
    );
    final clientIdentity = const DeviceIdentity(
      deviceId: 'router-client',
      name: 'Router Client',
    );
    final serverTrustStore = TrustStore('${tempDirectory.path}/server.json');
    final clientTrustStore = TrustStore('${tempDirectory.path}/client.json');
    final server = SessionManager(
      localIdentity: serverIdentity,
      trustStore: serverTrustStore,
      listenPort: 0,
    );
    final client = SessionManager(
      localIdentity: clientIdentity,
      trustStore: clientTrustStore,
      listenPort: 0,
    );
    final pairingManager = PairingManager(
      localIdentity: serverIdentity,
      trustStore: serverTrustStore,
    );
    final transferManager = TransferManager(tempDirectory.path);
    WorkspaceCommandRouter? router;
    StreamSubscription<ProtocolFrame>? frameSubscription;

    try {
      await server.startListener();
      final clientConnection = await client.connectToPeer(
        InternetAddress.loopbackIPv4,
        server.listeningPort,
      );
      final deadline = DateTime.now().add(const Duration(seconds: 3));
      while (server.connectionForDevice(clientIdentity.deviceId) == null &&
          DateTime.now().isBefore(deadline)) {
        await Future<void>.delayed(const Duration(milliseconds: 10));
      }
      expect(server.connectionForDevice(clientIdentity.deviceId), isNotNull);

      router = WorkspaceCommandRouter(
        sessionManager: server,
        dataService: WorkspaceDataService(),
        pairingManager: pairingManager,
        transferManager: transferManager,
      );
      router.start();

      final response = Completer<Map<String, dynamic>>();
      frameSubscription = clientConnection.frames.listen((frame) {
        if (frame.type != MessageType.toolResult || response.isCompleted) {
          return;
        }
        response.complete(
          jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>,
        );
      });
      clientConnection.sendJson(MessageType.toolCommand, {
        'commandId': 'existing-session-command',
        'command': 'unsupported-router-test-command',
        'payload': <String, dynamic>{},
      });

      final result = await response.future.timeout(const Duration(seconds: 3));
      expect(result['commandId'], 'existing-session-command');
      expect(result['success'], isFalse);
      expect(result['error'], contains('unsupported-router-test-command'));
    } finally {
      await frameSubscription?.cancel();
      router?.dispose();
      client.dispose();
      server.dispose();
      await tempDirectory.delete(recursive: true);
    }
  });
}
