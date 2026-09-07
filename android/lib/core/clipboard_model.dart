/// Clipboard Event Message Model (Phase 4)
class ClipboardEventMessage {
  final String eventId;
  final String originDeviceId;
  final int timestamp;
  final String contentType;
  final String content;

  ClipboardEventMessage({
    required this.eventId,
    required this.originDeviceId,
    required this.timestamp,
    this.contentType = 'text/plain',
    required this.content,
  });

  Map<String, dynamic> toJson() => {
    'eventId': eventId,
    'originDeviceId': originDeviceId,
    'timestamp': timestamp,
    'contentType': contentType,
    'content': content,
  };

  factory ClipboardEventMessage.fromJson(Map<String, dynamic> json) {
    return ClipboardEventMessage(
      eventId: json['eventId'] as String? ?? '',
      originDeviceId: json['originDeviceId'] as String? ?? '',
      timestamp: (json['timestamp'] as num?)?.toInt() ?? 0,
      contentType: json['contentType'] as String? ?? 'text/plain',
      content: json['content'] as String? ?? '',
    );
  }
}
