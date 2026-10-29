import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/protocol_frame.dart';
import 'package:hinge/core/remote_input_model.dart';
import 'package:hinge/core/remote_input_sender.dart';
import 'package:hinge/core/session_manager.dart';

class _Peer extends Fake implements SessionConnection {
  final states = StreamController<SessionState>.broadcast();
  int ordinaryFrames = 0;
  @override
  Stream<SessionState> get stateStream => states.stream;
  @override
  void sendFrame(MessageType type, dynamic payload) {
    ordinaryFrames++;
  }
}

void main() {
  test('desktop sender bypasses business frames and preserves UTF8 text across bounded chunks', () async {
    final peer = _Peer();
    final events = <RemoteInputEvent>[];
    final sender = RemoteInputSender(
      peer,
      desktopTransport: (bytes) {
        expect(bytes.length, lessThanOrEqualTo(4096));
        events.add(RemoteInputEvent.tryParse(bytes)!);
      },
    );
    sender.startControl();
    sender.sendMouseMove(10, 20);
    expect(events, isEmpty);
    sender.setDesktopReady(true);
    sender.sendMouseMove(10, 20);
    final text = List.filled(2048, '界🙂').join();
    sender.sendTextInput(text);
    expect(events.first.actionType, RemoteActionType.mouseMove);
    expect(events.skip(1).map((event) => event.textPayload).join(), text);
    expect(peer.ordinaryFrames, 0);
    expect(sender.isEncrypted, isTrue);
    final count = events.length;
    sender.sendMouseMove(5, 6, relative: true);
    expect(events.length, count);
    sender.setDesktopReady(true, relativeMouse: true);
    sender.sendMouseMove(5, 6, relative: true);
    expect(events.last.flags, RemoteInputEvent.relativeMouseFlag);
    sender.sendMouseMove(5, 6);
    expect(events.last.flags, 0);
    sender.setDesktopReady(false);
    final before = events.length;
    sender.sendMouseMove(1, 1);
    expect(events.length, before);
    sender.dispose();
    await peer.states.close();
  });
}
