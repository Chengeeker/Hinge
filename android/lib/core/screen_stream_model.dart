import 'dart:convert';
import 'dart:typed_data';

enum ScreenStreamSubtype {
  controlRequest(0x01),
  controlResponse(0x02),
  frameConfig(0x10),
  keyFrame(0x11),
  interFrame(0x12),
  jpegFrame(0x13),
  unknown(0x00);

  final int value;
  const ScreenStreamSubtype(this.value);

  static ScreenStreamSubtype fromValue(int val) {
    return ScreenStreamSubtype.values.firstWhere(
      (e) => e.value == val,
      orElse: () => ScreenStreamSubtype.unknown,
    );
  }
}

class ScreenStreamFlags {
  static const int none = 0x00;
  static const int endOfFrame = 0x01;
  static const int keyFrame = 0x02;
  static const int configFrame = 0x04;
}

class ScreenStreamConfig {
  final int width;
  final int height;
  final int fps;
  final int bitrate;
  final String codec;
  final bool nativeResolution;

  const ScreenStreamConfig({
    this.width = 1080,
    this.height = 1920,
    this.fps = 60,
    this.bitrate = 8000000,
    this.codec = 'H264',
    this.nativeResolution = false,
  });

  Map<String, dynamic> toJson() => {
    'width': width,
    'height': height,
    'fps': fps,
    'bitrate': bitrate,
    'codec': codec,
  };
}

class ScreenStreamControlMessage {
  final String? action;
  final String? status;
  final int streamId;
  final int width;
  final int height;
  final int fps;
  final int bitrate;
  final String? codec;
  final String? reason;
  final bool nativeResolution;

  ScreenStreamControlMessage({
    this.action,
    this.status,
    this.streamId = 0,
    this.width = 0,
    this.height = 0,
    this.fps = 0,
    this.bitrate = 0,
    this.codec,
    this.reason,
    this.nativeResolution = false,
  });

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{};
    if (action != null) map['action'] = action;
    if (status != null) map['status'] = status;
    if (streamId != 0) map['streamId'] = streamId;
    if (width != 0) map['width'] = width;
    if (height != 0) map['height'] = height;
    if (fps != 0) map['fps'] = fps;
    if (bitrate != 0) map['bitrate'] = bitrate;
    if (codec != null) map['codec'] = codec;
    if (reason != null) map['reason'] = reason;
    if (nativeResolution) map['nativeResolution'] = true;
    return map;
  }

  String serialize() => jsonEncode(toJson());

  static ScreenStreamControlMessage? fromJson(String jsonStr) {
    try {
      final map = jsonDecode(jsonStr) as Map<String, dynamic>;
      return ScreenStreamControlMessage(
        action: map['action'] as String?,
        status: map['status'] as String?,
        streamId: (map['streamId'] as num?)?.toInt() ?? 0,
        width: (map['width'] as num?)?.toInt() ?? 0,
        height: (map['height'] as num?)?.toInt() ?? 0,
        fps: (map['fps'] as num?)?.toInt() ?? 0,
        bitrate: (map['bitrate'] as num?)?.toInt() ?? 0,
        codec: map['codec'] as String?,
        reason: map['reason'] as String?,
        nativeResolution: map['nativeResolution'] == true,
      );
    } catch (_) {
      return null;
    }
  }
}

class ScreenStreamPacket {
  static const int headerSize = 20;

  final ScreenStreamSubtype subtype;
  final int flags;
  final int streamId;
  final int sequenceNumber;
  final int payloadLength;
  final int timestampUs;
  final Uint8List payload;

  ScreenStreamPacket({
    required this.subtype,
    this.flags = ScreenStreamFlags.none,
    this.streamId = 0,
    this.sequenceNumber = 0,
    int? payloadLength,
    this.timestampUs = 0,
    Uint8List? payload,
  }) : payload = payload ?? Uint8List(0),
       payloadLength = payloadLength ?? (payload?.length ?? 0);

  Uint8List serialize() {
    final totalSize = headerSize + payload.length;
    final bytes = Uint8List(totalSize);
    final bd = ByteData.sublistView(bytes);

    bd.setUint8(0, subtype.value);
    bd.setUint8(1, flags);
    bd.setUint16(2, streamId, Endian.big);
    bd.setUint32(4, sequenceNumber, Endian.big);
    bd.setUint32(8, payload.length, Endian.big);
    bd.setUint64(12, timestampUs, Endian.big);

    if (payload.isNotEmpty) {
      bytes.setRange(headerSize, totalSize, payload);
    }

    return bytes;
  }

  static ScreenStreamPacket? tryParse(Uint8List data) {
    if (data.length < headerSize) return null;

    final bd = ByteData.sublistView(data);
    final subtypeVal = bd.getUint8(0);
    final flags = bd.getUint8(1);
    final streamId = bd.getUint16(2, Endian.big);
    final sequenceNumber = bd.getUint32(4, Endian.big);
    final payloadLength = bd.getUint32(8, Endian.big);
    final timestampUs = bd.getUint64(12, Endian.big);

    final availablePayload = data.length - headerSize;
    if (availablePayload < payloadLength) return null;

    final payloadBytes = Uint8List(payloadLength);
    if (payloadLength > 0) {
      payloadBytes.setRange(
        0,
        payloadLength,
        data.sublist(headerSize, headerSize + payloadLength),
      );
    }

    return ScreenStreamPacket(
      subtype: ScreenStreamSubtype.fromValue(subtypeVal),
      flags: flags,
      streamId: streamId,
      sequenceNumber: sequenceNumber,
      payloadLength: payloadLength,
      timestampUs: timestampUs,
      payload: payloadBytes,
    );
  }
}
