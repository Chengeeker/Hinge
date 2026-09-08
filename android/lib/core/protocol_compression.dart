import 'dart:io';
import 'dart:typed_data';

import 'protocol_frame.dart';

/// Optional compression for large JSON/control payloads.
///
/// The envelope matches windows/Hinge.Core/ProtocolCompression.cs:
/// version(1) + flags(1) + inner message type(2, big-endian) +
/// uncompressed length(4, big-endian) + zlib payload.
class ProtocolCompression {
  static const String capability = 'control-compression-zlib-v1';
  static const int envelopeVersion = 1;
  static const int envelopeHeaderSize = 8;
  static const int _minimumPayloadSize = 2048;

  static const Set<MessageType> _compressibleTypes = {
    MessageType.textMessage,
    MessageType.syncManifestRequest,
    MessageType.syncManifestResponse,
    MessageType.syncPullRequest,
    MessageType.clipboardEvent,
    MessageType.notificationEvent,
    MessageType.notificationAction,
    MessageType.toolCommand,
    MessageType.toolResult,
  };

  static bool isCompressible(MessageType type, int payloadLength) =>
      payloadLength >= _minimumPayloadSize && _compressibleTypes.contains(type);

  static Uint8List? tryCompress(MessageType type, Uint8List payload) {
    if (!isCompressible(type, payload.length) ||
        payload.length > ProtocolFrame.maxPayloadSize) {
      return null;
    }

    try {
      final compressed = Uint8List.fromList(
        ZLibCodec(level: 1).encode(payload),
      );
      if (compressed.length + envelopeHeaderSize >= payload.length) {
        return null;
      }

      final envelope = Uint8List(envelopeHeaderSize + compressed.length);
      final data = ByteData.sublistView(envelope);
      envelope[0] = envelopeVersion;
      envelope[1] = 0;
      data.setUint16(2, type.value, Endian.big);
      data.setUint32(4, payload.length, Endian.big);
      envelope.setRange(envelopeHeaderSize, envelope.length, compressed);
      return envelope;
    } on Object {
      // Compression is an optimization. A provider/runtime failure must not
      // interrupt the ordinary uncompressed protocol path.
      return null;
    }
  }

  static CompressedControlPayload? tryDecompress(Uint8List envelope) {
    if (envelope.length <= envelopeHeaderSize ||
        envelope[0] != envelopeVersion ||
        envelope[1] != 0) {
      return null;
    }

    final data = ByteData.sublistView(envelope);
    final type = MessageType.fromValue(data.getUint16(2, Endian.big));
    final expectedLength = data.getUint32(4, Endian.big);
    if (!isCompressible(type, expectedLength) ||
        expectedLength > ProtocolFrame.maxPayloadSize) {
      return null;
    }

    try {
      final decoded = Uint8List.fromList(
        ZLibCodec().decode(envelope.sublist(envelopeHeaderSize)),
      );
      if (decoded.length != expectedLength) return null;
      return CompressedControlPayload(type: type, payload: decoded);
    } on Object {
      return null;
    }
  }
}

class CompressedControlPayload {
  final MessageType type;
  final Uint8List payload;

  const CompressedControlPayload({required this.type, required this.payload});
}
