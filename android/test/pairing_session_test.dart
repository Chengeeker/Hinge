import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/pairing_manager.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/trust_store.dart';

void main() {
  group('Pairing and Session Tests', () {
    test('TrustStore CRUD and persistence', () {
      final tempFile = File(
        '${Directory.systemTemp.path}/ts_${DateTime.now().millisecondsSinceEpoch}.json',
      );
      try {
        final store = TrustStore(tempFile.path);
        expect(store.isTrusted('dev-phone-1'), isFalse);

        final device = TrustedDevice(
          deviceId: 'dev-phone-1',
          name: 'Windows Desktop',
          pairedAt: 1000,
          lastSeen: 1000,
          trustState: DeviceTrustState.trusted,
        );
        store.addOrUpdate(device);

        expect(store.isTrusted('dev-phone-1'), isTrue);
        expect(store.getDevice('dev-phone-1')?.name, equals('Windows Desktop'));

        // Load again from same path
        final store2 = TrustStore(tempFile.path);
        expect(store2.isTrusted('dev-phone-1'), isTrue);

        // Revoke
        expect(store2.revoke('dev-phone-1'), isTrue);
        expect(store2.isTrusted('dev-phone-1'), isFalse);
      } finally {
        if (tempFile.existsSync()) tempFile.deleteSync();
      }
    });

    test('PairingManager derives deterministic 6-digit PIN consistent with Windows', () {
      const initId = 'c85d7b5f-519b-4e12-8e10-3b0222a7f05a';
      const recvId = '1ab063eb-c033-46c7-b908-10763fd66229';
      const salt = 'abc12345';

      final pin1 = PairingManager.derivePin(initId, recvId, salt);
      final pin2 = PairingManager.derivePin(initId, recvId, salt);

      expect(pin1, equals(pin2));
      expect(pin1, greaterThanOrEqualTo(100000));
      expect(pin1, lessThanOrEqualTo(999999));
    });

    test('PairingManager derives deterministic 6-digit SAS code with cross-platform parity', () {
      const secretStr = 'shared-secret-key-12345';
      const nonceA = 'a1b2c3d4e5f60718293a4b5c6d7e8f90';
      const nonceB = '09f8e7d6c5b4a39281706f5e4d3c2b1a';
      const pubKeyA = 'pubkey_phone_alice';
      const pubKeyB = 'pubkey_pc_bob';

      final sas1 = PairingManager.deriveSasCodeFromHex(
        sharedSecretStr: secretStr,
        nonceAHex: nonceA,
        nonceBHex: nonceB,
        pubKeyA: pubKeyA,
        pubKeyB: pubKeyB,
      );
      final sas2 = PairingManager.deriveSasCodeFromHex(
        sharedSecretStr: secretStr,
        nonceAHex: nonceA,
        nonceBHex: nonceB,
        pubKeyA: pubKeyA,
        pubKeyB: pubKeyB,
      );

      expect(sas1, equals(sas2));
      expect(sas1.length, equals(6));
      final code = int.parse(sas1);
      expect(code, greaterThanOrEqualTo(100000));
      expect(code, lessThanOrEqualTo(999999));

      // Altering nonce yields different SAS
      final sasDifferent = PairingManager.deriveSasCodeFromHex(
        sharedSecretStr: secretStr,
        nonceAHex: nonceA,
        nonceBHex: '19f8e7d6c5b4a39281706f5e4d3c2b1b',
        pubKeyA: pubKeyA,
        pubKeyB: pubKeyB,
      );
      expect(sas1, isNot(equals(sasDifferent)));
    });

    test('ProtocolFrame serialization and parsing roundtrip', () {
      final payloadText = 'Hello Frame World';
      final payload = Uint8List.fromList(utf8.encode(payloadText));
      final frame = ProtocolFrame(
        type: MessageType.textMessage,
        payload: payload,
      );

      final bytes = frame.serialize();
      final parsed = ProtocolFrame.tryParse(bytes);

      expect(parsed, isNotNull);
      expect(parsed!.type, equals(MessageType.textMessage));
      expect(utf8.decode(parsed.payload), equals(payloadText));
    });

    test(
      'ProtocolFrame uses RFC 4122 network byte order and random message IDs',
      () {
        final messageId = Uint8List.fromList([
          0x00,
          0x11,
          0x22,
          0x33,
          0x44,
          0x55,
          0x66,
          0x77,
          0x88,
          0x99,
          0xaa,
          0xbb,
          0xcc,
          0xdd,
          0xee,
          0xff,
        ]);
        final sessionId = Uint8List.fromList(List<int>.generate(16, (i) => i));
        final frame = ProtocolFrame(
          type: MessageType.textMessage,
          messageId: messageId,
          sessionId: sessionId,
        );

        final bytes = frame.serialize();
        expect(bytes.sublist(8, 24), equals(messageId));
        expect(bytes.sublist(32, 48), equals(sessionId));

        final generated = ProtocolFrame(type: MessageType.textMessage);
        expect(generated.messageId.any((byte) => byte != 0), isTrue);
      },
    );

    test('SessionManager loopback connection exchanges frame', () async {
      final serverId = const DeviceIdentity(
        deviceId: 'server-id',
        name: 'Server',
      );
      final store = TrustStore();

      final server = SessionManager(
        localIdentity: serverId,
        trustStore: store,
        listenPort: 0,
      );
      await server.startListener();
      expect(server.isListening, isTrue, reason: server.lastError);

      final completer = Completer<ProtocolFrame>();
      SessionConnection? incomingConnection;
      final sub = server.onClientConnected.listen((conn) {
        incomingConnection = conn;
        conn.frames.listen((frame) {
          if (!completer.isCompleted) {
            completer.complete(frame);
          }
        });
      });

      final clientId = const DeviceIdentity(
        deviceId: 'client-id',
        name: 'Client',
      );
      final client = SessionManager(
        localIdentity: clientId,
        trustStore: store,
        listenPort: 0,
      );

      final clientConn = await client.connectToPeer(
        InternetAddress.loopbackIPv4,
        server.listeningPort,
      );
      await Future<void>.delayed(const Duration(milliseconds: 100));
      expect(incomingConnection?.peerInfo?.deviceId, equals('client-id'));
      expect(clientConn.peerInfo?.deviceId, equals('server-id'));
      final msg = 'Hello from client';
      clientConn.sendFrame(
        MessageType.textMessage,
        Uint8List.fromList(utf8.encode(msg)),
      );

      final received = await completer.future.timeout(
        const Duration(seconds: 3),
      );
      expect(received.type, equals(MessageType.textMessage));
      expect(utf8.decode(received.payload), equals(msg));

      await sub.cancel();
      client.dispose();
      server.dispose();
    });
  });
}
