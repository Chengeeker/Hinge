import 'dart:convert';
import 'dart:typed_data';

enum RemoteActionType {
  mouseMove(0x01),
  mouseDown(0x02),
  mouseUp(0x03),
  mouseClick(0x04),
  mouseDoubleClick(0x05),
  mouseScroll(0x06),
  keyDown(0x10),
  keyUp(0x11),
  textInput(0x12),
  unknown(0x00);

  final int value;
  const RemoteActionType(this.value);

  static RemoteActionType fromValue(int val) {
    return RemoteActionType.values.firstWhere(
      (e) => e.value == val,
      orElse: () => RemoteActionType.unknown,
    );
  }
}

enum RemoteMouseButton {
  left(0),
  right(1),
  middle(2);

  final int value;
  const RemoteMouseButton(this.value);
}

class RemoteInputEvent {
  static const int headerSize = 16;

  final RemoteActionType actionType;
  final int buttonOrKey;
  final int flags;
  final int deltaX;
  final int deltaY;
  final int wheelOrData;
  final String? textPayload;

  RemoteInputEvent({
    required this.actionType,
    this.buttonOrKey = 0,
    this.flags = 0,
    this.deltaX = 0,
    this.deltaY = 0,
    this.wheelOrData = 0,
    this.textPayload,
  });

  Uint8List serialize() {
    Uint8List? textBytes;
    int totalSize = headerSize;
    if (actionType == RemoteActionType.textInput &&
        textPayload != null &&
        textPayload!.isNotEmpty) {
      textBytes = Uint8List.fromList(utf8.encode(textPayload!));
      totalSize += textBytes.length;
    }

    final data = ByteData(totalSize);
    data.setUint8(0, actionType.value);
    data.setUint8(1, buttonOrKey);
    data.setUint16(2, flags, Endian.big);
    data.setInt32(4, deltaX, Endian.big);
    data.setInt32(8, deltaY, Endian.big);
    data.setInt32(12, wheelOrData, Endian.big);

    final bytes = data.buffer.asUint8List();
    if (textBytes != null) {
      bytes.setRange(headerSize, totalSize, textBytes);
    }
    return bytes;
  }

  static RemoteInputEvent? tryParse(Uint8List bytes) {
    if (bytes.length < headerSize) return null;

    final data = ByteData.sublistView(bytes);
    final action = RemoteActionType.fromValue(data.getUint8(0));
    final button = data.getUint8(1);
    final flags = data.getUint16(2, Endian.big);
    final dx = data.getInt32(4, Endian.big);
    final dy = data.getInt32(8, Endian.big);
    final wheel = data.getInt32(12, Endian.big);

    String? text;
    if (action == RemoteActionType.textInput && bytes.length > headerSize) {
      text = utf8.decode(bytes.sublist(headerSize));
    }

    return RemoteInputEvent(
      actionType: action,
      buttonOrKey: button,
      flags: flags,
      deltaX: dx,
      deltaY: dy,
      wheelOrData: wheel,
      textPayload: text,
    );
  }
}
