import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/protocol_compression.dart';
import 'package:hinge/core/protocol_frame.dart';

void main() {
  test('large control payload round-trips through the zlib envelope', () {
    final payload = Uint8List.fromList(
      utf8.encode(
        List.filled(200, '{"name":"photo","type":"image/jpeg"}').join(),
      ),
    );

    final envelope = ProtocolCompression.tryCompress(
      MessageType.toolResult,
      payload,
    );

    expect(envelope, isNotNull);
    expect(envelope!.length, lessThan(payload.length));

    final restored = ProtocolCompression.tryDecompress(envelope);
    expect(restored, isNotNull);
    expect(restored!.type, MessageType.toolResult);
    expect(restored.payload, payload);
  });

  test('small and binary payloads stay uncompressed', () {
    expect(
      ProtocolCompression.tryCompress(
        MessageType.toolResult,
        Uint8List.fromList(utf8.encode('small control payload')),
      ),
      isNull,
    );
    expect(
      ProtocolCompression.tryCompress(MessageType.fileChunk, Uint8List(4096)),
      isNull,
    );
  });

  test('corrupt envelope is rejected', () {
    expect(
      ProtocolCompression.tryDecompress(
        Uint8List(ProtocolCompression.envelopeHeaderSize + 1),
      ),
      isNull,
    );
  });
}
