import 'dart:math';
import 'dart:typed_data';

enum MessageType {
  deviceDiscovery(0x0001),
  pairRequest(0x0002),
  pairConfirm(0x0003),
  sessionInit(0x0010),
  sessionAck(0x0011),
  heartbeatPing(0x0012),
  heartbeatPong(0x0013),
  textMessage(0x0020),
  fileOffer(0x0030),
  fileAccept(0x0031),
  fileReject(0x0032),
  fileChunk(0x0033),
  fileComplete(0x0034),
  syncManifestRequest(0x0035),
  syncManifestResponse(0x0036),
  syncPullRequest(0x0037),
  clipboardEvent(0x0040),
  remoteInput(0x0050),
  screenStream(0x0060),
  notificationEvent(0x0070),
  notificationAction(0x0071),
  toolCommand(0x0080),
  toolResult(0x0081),
  unknown(0x0000);

  final int value;
  const MessageType(this.value);

  static MessageType fromValue(int val) {
    return MessageType.values.firstWhere(
      (e) => e.value == val,
      orElse: () => MessageType.unknown,
    );
  }
}

class ProtocolFrame {
  static const List<int> magicBytes = [0x4F, 0x53, 0x50, 0x31]; // "OSP1"
  static const int headerSize = 52;
  static const int maxPayloadSize = 16 * 1024 * 1024;

  final int version;
  final MessageType type;
  final Uint8List messageId;
  final int timestamp;
  final Uint8List sessionId;
  final Uint8List payload;

  ProtocolFrame({
    this.version = 1,
    required this.type,
    Uint8List? messageId,
    int? timestamp,
    Uint8List? sessionId,
    Uint8List? payload,
  }) : messageId = _validateUuid(
         messageId ?? _generateUuidBytes(),
         'messageId',
       ),
       timestamp = timestamp ?? (DateTime.now().millisecondsSinceEpoch ~/ 1000),
       sessionId = _validateUuid(sessionId ?? Uint8List(16), 'sessionId'),
       payload = payload ?? Uint8List(0);

  Uint8List serialize() {
    if (payload.length > maxPayloadSize) {
      throw ArgumentError('Payload exceeds the $maxPayloadSize byte limit.');
    }

    final totalSize = headerSize + payload.length;
    final buffer = Uint8List(totalSize);
    final byteData = ByteData.sublistView(buffer);

    buffer.setRange(0, 4, magicBytes);
    byteData.setUint16(4, version, Endian.big);
    byteData.setUint16(6, type.value, Endian.big);
    buffer.setRange(8, 24, messageId);
    byteData.setInt64(24, timestamp, Endian.big);
    buffer.setRange(32, 48, sessionId);
    byteData.setUint32(48, payload.length, Endian.big);

    if (payload.isNotEmpty) {
      buffer.setRange(headerSize, totalSize, payload);
    }

    return buffer;
  }

  static ProtocolFrame? tryParse(Uint8List data) {
    if (data.length < headerSize) return null;

    if (data[0] != magicBytes[0] ||
        data[1] != magicBytes[1] ||
        data[2] != magicBytes[2] ||
        data[3] != magicBytes[3]) {
      return null;
    }

    final byteData = ByteData.sublistView(data);
    final version = byteData.getUint16(4, Endian.big);
    final typeVal = byteData.getUint16(6, Endian.big);
    final type = MessageType.fromValue(typeVal);
    final msgId = Uint8List.fromList(data.sublist(8, 24));
    final ts = byteData.getInt64(24, Endian.big);
    final sessId = Uint8List.fromList(data.sublist(32, 48));
    final payloadLen = byteData.getUint32(48, Endian.big);

    if (payloadLen > maxPayloadSize) return null;
    if (data.length < headerSize + payloadLen) return null;

    final payload = Uint8List.fromList(
      data.sublist(headerSize, headerSize + payloadLen),
    );

    return ProtocolFrame(
      version: version,
      type: type,
      messageId: msgId,
      timestamp: ts,
      sessionId: sessId,
      payload: payload,
    );
  }

  static Uint8List _validateUuid(Uint8List value, String name) {
    if (value.length != 16) {
      throw ArgumentError('$name must contain exactly 16 bytes.');
    }
    return value;
  }

  static Uint8List _generateUuidBytes() {
    final random = Random.secure();
    final bytes = Uint8List.fromList(
      List<int>.generate(16, (_) => random.nextInt(256)),
    );
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    return bytes;
  }
}
