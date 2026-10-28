import 'dart:async';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/remote_input_cipher.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/remote_input_model.dart';
import 'package:hinge/core/remote_input_sender.dart';
import 'package:hinge/core/session_manager.dart';

class _Connection extends Fake implements SessionConnection {
  final incoming = StreamController<ProtocolFrame>.broadcast();
  final states = StreamController<SessionState>.broadcast();
  final outgoing = <ProtocolFrame>[];
  @override
  SessionState get state => SessionState.connected;
  @override
  Stream<ProtocolFrame> get frames => incoming.stream;
  @override
  Stream<SessionState> get stateStream => states.stream;
  @override
  void sendFrame(MessageType type, Uint8List payload) {
    outgoing.add(ProtocolFrame(type: type, payload: payload));
  }
}

void main() {
  test(
    'allows opt-in LAN plaintext only after PC marks the session unencrypted',
    () async {
      final connection = _Connection();
      final sender = RemoteInputSender(connection);
      sender.startControl();
      expect(
        RemoteInputEvent.tryParse(connection.outgoing.single.payload)
            ?.actionType,
        RemoteActionType.controlProbe,
      );

      connection.incoming.add(
        ProtocolFrame(
          type: MessageType.remoteInput,
          payload: RemoteInputEvent(
            actionType: RemoteActionType.controlAccepted,
            flags: 3,
          ).serialize(),
        ),
      );
      await Future<void>.delayed(Duration.zero);
      expect(sender.ready, isTrue);
      expect(sender.isEncrypted, isFalse);
      expect(sender.status, contains('局域网未加密'));

      sender.sendKey(87, true);
      expect(connection.outgoing.last.type, MessageType.remoteInput);
      expect(
        RemoteInputEvent.tryParse(connection.outgoing.last.payload)?.actionType,
        RemoteActionType.keyDown,
      );
      sender.dispose();
      await connection.incoming.close();
      await connection.states.close();
    },
  );

  test(
    'does not fall back to plaintext when PC sends an encryption challenge',
    () async {
      final connection = _Connection();
      final sender = RemoteInputSender(connection);
      sender.startControl();
      connection.incoming.add(
        ProtocolFrame(
          type: MessageType.remoteInput,
          payload: Uint8List(48)..[0] = 0x31,
        ),
      );
      await Future<void>.delayed(Duration.zero);
      expect(sender.ready, isFalse);
      expect(sender.status, contains('要求加密'));
      expect(connection.outgoing, hasLength(1));
      sender.dispose();
      await connection.incoming.close();
      await connection.states.close();
    },
  );

  test(
    'requests a key only after the encrypted PC sends a challenge',
    () async {
      final connection = _Connection();
      final key = Uint8List.fromList(List.filled(32, 7));
      var requested = false;
      final sender = RemoteInputSender(
        connection,
        onEncryptionRequired: () async {
          requested = true;
          return key;
        },
      );
      sender.startControl();
      final challenge = Uint8List.fromList(List.filled(32, 2));
      final payload = Uint8List(48)..[0] = 0x31;
      payload.setRange(16, 48, challenge);
      connection.incoming.add(
        ProtocolFrame(type: MessageType.remoteInput, payload: payload),
      );
      await Future<void>.delayed(const Duration(milliseconds: 20));
      expect(requested, isTrue);
      expect(connection.outgoing.last.type, MessageType.secureRemoteInput);
      final server = await RemoteInputCipher.create(
        key,
        challenge,
        server: true,
      );
      final start = await server.decrypt(connection.outgoing.last.payload);
      expect(
        RemoteInputEvent.tryParse(start!)?.actionType,
        RemoteActionType.controlStart,
      );
      sender.dispose();
      await connection.incoming.close();
      await connection.states.close();
    },
  );
}
