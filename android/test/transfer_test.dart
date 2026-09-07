import 'dart:async';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/transfer_manager.dart';
import 'package:hinge/core/transfer_model.dart';
import 'package:hinge/core/trust_store.dart';

void main() {
  group('Transfer Core Tests', () {
    test('UUID to bytes roundtrip', () {
      const uuid = 'c85d7b5f-519b-4e12-8e10-3b0222a7f05a';
      final bytes = TransferManager.uuidToBytes(uuid);
      expect(bytes.length, equals(16));

      final back = TransferManager.bytesToUuid(bytes);
      expect(back, equals(uuid));
    });

    test('Loopback text transfer sends and receives message', () async {
      const port = 52890;
      final serverId = const DeviceIdentity(
        deviceId: 'server-id',
        name: 'Server',
      );
      final store = TrustStore();

      final server = SessionManager(
        localIdentity: serverId,
        trustStore: store,
        listenPort: port,
      );
      await server.startListener();

      final receiverTransfer = TransferManager();
      final textCompleter = Completer<TextTransferMessage>();

      receiverTransfer.textStream.listen((msg) {
        if (!textCompleter.isCompleted) {
          textCompleter.complete(msg);
        }
      });

      server.onClientConnected.listen((conn) {
        conn.frames.listen((frame) {
          receiverTransfer.handleIncomingFrame(conn, frame);
        });
      });

      final clientId = const DeviceIdentity(
        deviceId: 'client-id',
        name: 'Client',
      );
      final client = SessionManager(
        localIdentity: clientId,
        trustStore: store,
        listenPort: port + 1,
      );

      final clientConn = await client.connectToPeer(
        InternetAddress.loopbackIPv4,
        port,
      );
      final senderTransfer = TransferManager();

      await senderTransfer.sendText(
        clientConn,
        'Hello cross-device world from Flutter 🚀',
        'text',
      );

      final received = await textCompleter.future.timeout(
        const Duration(seconds: 3),
      );
      expect(
        received.content,
        equals('Hello cross-device world from Flutter 🚀'),
      );
      expect(received.type, equals('text'));

      client.dispose();
      server.dispose();
      receiverTransfer.dispose();
      senderTransfer.dispose();
    });

    test('Loopback file transfer streams chunks, verifies SHA-256 and saves file', () async {
      const port = 52895;
      final serverId = const DeviceIdentity(
        deviceId: 'server-id',
        name: 'Server',
      );
      final store = TrustStore();

      final server = SessionManager(
        localIdentity: serverId,
        trustStore: store,
        listenPort: port,
      );
      await server.startListener();

      final downloadDir =
          '${Directory.systemTemp.path}/dl_${DateTime.now().millisecondsSinceEpoch}';
      final receiverTransfer = TransferManager(downloadDir);
      final fileCompleter = Completer<String>();

      receiverTransfer.fileReceivedStream.listen((path) {
        if (!fileCompleter.isCompleted) {
          fileCompleter.complete(path);
        }
      });

      server.onClientConnected.listen((conn) {
        conn.frames.listen((frame) {
          receiverTransfer.handleIncomingFrame(conn, frame);
        });
      });

      final clientId = const DeviceIdentity(
        deviceId: 'client-id',
        name: 'Client',
      );
      final client = SessionManager(
        localIdentity: clientId,
        trustStore: store,
        listenPort: port + 1,
      );

      final clientConn = await client.connectToPeer(
        InternetAddress.loopbackIPv4,
        port,
      );
      final senderTransfer = TransferManager();

      // Create a 128KB test file (spans multiple 64KB chunks)
      final samplePath =
          '${Directory.systemTemp.path}/test_send_${DateTime.now().millisecondsSinceEpoch}.bin';
      final rand = Random.secure();
      final sampleBytes = Uint8List(128 * 1024);
      for (int i = 0; i < sampleBytes.length; i++) {
        sampleBytes[i] = rand.nextInt(256);
      }
      File(samplePath).writeAsBytesSync(sampleBytes);

      try {
        final transferId = await senderTransfer.sendFile(
          clientConn,
          samplePath,
        );
        expect(transferId, isNotEmpty);

        final receivedPath = await fileCompleter.future.timeout(
          const Duration(seconds: 5),
        );
        final receivedFile = File(receivedPath);
        expect(receivedFile.existsSync(), isTrue);

        final receivedBytes = receivedFile.readAsBytesSync();
        expect(receivedBytes.length, equals(sampleBytes.length));
        expect(receivedBytes, equals(sampleBytes));
      } finally {
        client.dispose();
        server.dispose();
        receiverTransfer.dispose();
        senderTransfer.dispose();

        if (File(samplePath).existsSync()) {
          try {
            File(samplePath).deleteSync();
          } catch (_) {}
        }
        final dir = Directory(downloadDir);
        if (dir.existsSync()) {
          try {
            dir.deleteSync(recursive: true);
          } catch (_) {}
        }
      }
    });
  });
}
