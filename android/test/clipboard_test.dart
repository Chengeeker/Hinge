import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/clipboard_adapter.dart';
import 'package:hinge/core/clipboard_manager.dart';
import 'package:hinge/core/clipboard_model.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/lru_ring_cache.dart';
import 'package:hinge/core/protocol_frame.dart';

void main() {
  group('Clipboard Core Tests', () {
    test('LruRingCache evicts oldest when exceeding 100 items', () {
      final cache = LruRingCache(100);

      for (int i = 1; i <= 100; i++) {
        expect(cache.add('event-$i'), isTrue);
      }

      expect(cache.count, equals(100));
      expect(cache.contains('event-1'), isTrue);
      expect(cache.contains('event-100'), isTrue);

      // Add 101th item -> should evict event-1
      expect(cache.add('event-101'), isTrue);
      expect(cache.count, equals(100));
      expect(cache.contains('event-1'), isFalse);
      expect(cache.contains('event-2'), isTrue);
      expect(cache.contains('event-101'), isTrue);

      // Adding duplicate should return false and not increase count
      expect(cache.add('event-2'), isFalse);
      expect(cache.count, equals(100));
    });

    test('ClipboardEventMessage serialization roundtrip', () {
      final msg = ClipboardEventMessage(
        eventId: 'test-event-id-123',
        originDeviceId: 'dev-phone-456',
        timestamp: 1788533000000,
        contentType: 'text/plain',
        content: 'Testing clipboard sync across devices!',
      );

      final json = msg.toJson();
      final parsed = ClipboardEventMessage.fromJson(json);

      expect(parsed.eventId, equals(msg.eventId));
      expect(parsed.originDeviceId, equals(msg.originDeviceId));
      expect(parsed.timestamp, equals(msg.timestamp));
      expect(parsed.contentType, equals(msg.contentType));
      expect(parsed.content, equals(msg.content));
    });

    test(
      'ClipboardManager drops self-originated events (Anti-Loop Rule 1)',
      () async {
        const localId = DeviceIdentity(
          deviceId: 'local-phone-1',
          name: 'My Phone',
        );
        final adapter = MockClipboardAdapter();
        final manager = ClipboardManager(
          localIdentity: localId,
          adapter: adapter,
        );

        final selfMsg = ClipboardEventMessage(
          eventId: 'evt-self',
          originDeviceId: 'local-phone-1', // Self
          timestamp: 1000,
          content: 'Echo text',
        );

        final frame = ProtocolFrame(
          type: MessageType.clipboardEvent,
          payload: Uint8List.fromList(
            utf8.encode(jsonEncode(selfMsg.toJson())),
          ),
        );

        final handled = await manager.handleIncomingFrame(null, frame);

        expect(handled, isFalse);
        expect(await adapter.getText(), isNull);

        manager.dispose();
      },
    );

    test(
      'ClipboardManager drops duplicate events (Anti-Loop Rule 2)',
      () async {
        const localId = DeviceIdentity(
          deviceId: 'local-phone-1',
          name: 'My Phone',
        );
        final adapter = MockClipboardAdapter();
        final manager = ClipboardManager(
          localIdentity: localId,
          adapter: adapter,
        );

        final msg = ClipboardEventMessage(
          eventId: 'evt-duplicate',
          originDeviceId: 'remote-pc-2',
          timestamp: 1000,
          content: 'First sync',
        );

        final frame = ProtocolFrame(
          type: MessageType.clipboardEvent,
          payload: Uint8List.fromList(utf8.encode(jsonEncode(msg.toJson()))),
        );

        // 1st receive: handled and applied
        final handled1 = await manager.handleIncomingFrame(null, frame);
        expect(handled1, isTrue);
        expect(await adapter.getText(), equals('First sync'));

        // 2nd receive: dropped
        final handled2 = await manager.handleIncomingFrame(null, frame);
        expect(handled2, isFalse);

        manager.dispose();
      },
    );

    test('ClipboardManager URL recognition and handoff stream', () async {
      expect(
        ClipboardManager.isUrl('https://github.com/Chengeeker/Hinge'),
        isTrue,
      );
      expect(ClipboardManager.isUrl('http://192.168.1.5:8080'), isTrue);
      expect(ClipboardManager.isUrl('Not a url'), isFalse);
      expect(ClipboardManager.isUrl(''), isFalse);

      const localId = DeviceIdentity(
        deviceId: 'local-phone-1',
        name: 'My Phone',
      );
      final adapter = MockClipboardAdapter();
      final manager = ClipboardManager(
        localIdentity: localId,
        adapter: adapter,
      );

      final completer = Completer<String>();
      manager.urlHandoffStream.listen((url) {
        if (!completer.isCompleted) completer.complete(url);
      });

      final msg = ClipboardEventMessage(
        eventId: 'evt-url-1',
        originDeviceId: 'remote-pc-2',
        timestamp: 1000,
        content: 'https://flutter.dev/docs',
      );

      final frame = ProtocolFrame(
        type: MessageType.clipboardEvent,
        payload: Uint8List.fromList(utf8.encode(jsonEncode(msg.toJson()))),
      );

      await manager.handleIncomingFrame(null, frame);

      final handoffUrl = await completer.future.timeout(
        const Duration(seconds: 1),
      );
      expect(handoffUrl, equals('https://flutter.dev/docs'));
      manager.dispose();
    });
  });
}
