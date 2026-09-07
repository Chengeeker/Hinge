import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/remote_input_model.dart';

void main() {
  group('Remote Input Core Tests', () {
    test('RemoteInputEvent mouseMove binary serialization roundtrip', () {
      final ev = RemoteInputEvent(
        actionType: RemoteActionType.mouseMove,
        deltaX: -15,
        deltaY: 42,
      );

      final bytes = ev.serialize();
      expect(bytes.length, equals(16));

      final parsed = RemoteInputEvent.tryParse(bytes);
      expect(parsed, isNotNull);
      expect(parsed!.actionType, equals(RemoteActionType.mouseMove));
      expect(parsed.deltaX, equals(-15));
      expect(parsed.deltaY, equals(42));
    });

    test('RemoteInputEvent mouseClick binary serialization roundtrip', () {
      final ev = RemoteInputEvent(
        actionType: RemoteActionType.mouseClick,
        buttonOrKey: RemoteMouseButton.right.value,
      );

      final bytes = ev.serialize();
      expect(bytes.length, equals(16));

      final parsed = RemoteInputEvent.tryParse(bytes);
      expect(parsed, isNotNull);
      expect(parsed!.actionType, equals(RemoteActionType.mouseClick));
      expect(parsed.buttonOrKey, equals(RemoteMouseButton.right.value));
    });

    test('RemoteInputEvent mouseScroll binary serialization roundtrip', () {
      final ev = RemoteInputEvent(
        actionType: RemoteActionType.mouseScroll,
        wheelOrData: -120,
      );

      final bytes = ev.serialize();
      expect(bytes.length, equals(16));

      final parsed = RemoteInputEvent.tryParse(bytes);
      expect(parsed, isNotNull);
      expect(parsed!.actionType, equals(RemoteActionType.mouseScroll));
      expect(parsed.wheelOrData, equals(-120));
    });

    test('RemoteInputEvent textInput binary serialization roundtrip', () {
      const text = 'Hello from Flutter Remote Touchpad 🚀';
      final ev = RemoteInputEvent(
        actionType: RemoteActionType.textInput,
        textPayload: text,
      );

      final bytes = ev.serialize();
      expect(bytes.length, greaterThan(16));

      final parsed = RemoteInputEvent.tryParse(bytes);
      expect(parsed, isNotNull);
      expect(parsed!.actionType, equals(RemoteActionType.textInput));
      expect(parsed.textPayload, equals(text));
    });

    test('RemoteInputEvent rejects truncated bytes', () {
      final truncated = Uint8List(12);
      final parsed = RemoteInputEvent.tryParse(truncated);
      expect(parsed, isNull);
    });
  });
}
