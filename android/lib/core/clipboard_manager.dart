import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'clipboard_adapter.dart';
import 'clipboard_model.dart';
import 'device_identity_manager.dart';
import 'lru_ring_cache.dart';
import 'protocol_frame.dart';
import 'session_manager.dart';

class ClipboardManager {
  final DeviceIdentity localIdentity;
  final ClipboardAdapter adapter;
  final LruRingCache cache;
  final List<SessionConnection> _activeConnections = [];

  final StreamController<ClipboardEventMessage> _clipboardController =
      StreamController<ClipboardEventMessage>.broadcast();
  final StreamController<String> _urlHandoffController =
      StreamController<String>.broadcast();

  StreamSubscription<String>? _adapterSubscription;
  String? _lastAppliedRemoteContent;
  bool autoSync = true;
  bool autoOpenUrls = false;

  Stream<ClipboardEventMessage> get clipboardStream =>
      _clipboardController.stream;
  Stream<String> get urlHandoffStream => _urlHandoffController.stream;

  ClipboardManager({
    required this.localIdentity,
    required this.adapter,
    LruRingCache? cache,
  }) : cache = cache ?? LruRingCache(100) {
    _adapterSubscription = adapter.textStream.listen(_onLocalClipboardChanged);
  }

  void registerConnection(SessionConnection connection) {
    if (!_activeConnections.contains(connection)) {
      _activeConnections.add(connection);
    }
  }

  void unregisterConnection(SessionConnection connection) {
    _activeConnections.remove(connection);
  }

  void _onLocalClipboardChanged(String text) {
    if (!autoSync || text.isEmpty) return;

    // Anti-echo: if the clipboard change matches what we just received from a peer, ignore it
    if (text == _lastAppliedRemoteContent) return;

    final eventId = generateUuid();
    cache.add(eventId);

    final msg = ClipboardEventMessage(
      eventId: eventId,
      originDeviceId: localIdentity.deviceId,
      timestamp: DateTime.now().millisecondsSinceEpoch,
      contentType: 'text/plain',
      content: text,
    );

    broadcastClipboardEvent(msg);
  }

  void broadcastClipboardEvent(ClipboardEventMessage msg) {
    final active = _activeConnections
        .where((c) => c.state == SessionState.connected)
        .toList();

    for (final conn in active) {
      conn.sendJson(MessageType.clipboardEvent, msg.toJson());
    }
  }

  Future<bool> handleIncomingFrame(
    SessionConnection? conn,
    ProtocolFrame frame,
  ) async {
    if (frame.type != MessageType.clipboardEvent) return false;

    ClipboardEventMessage msg;
    try {
      final json =
          jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
      msg = ClipboardEventMessage.fromJson(json);
    } catch (_) {
      return false;
    }

    // 1. Anti-Loop Rule 1: Drop if originating from self
    if (msg.originDeviceId.toLowerCase() ==
        localIdentity.deviceId.toLowerCase()) {
      return false;
    }

    // 2. Anti-Loop Rule 2: Drop if already received (in LRU 100 cache)
    if (cache.contains(msg.eventId)) {
      return false;
    }

    // Add to ring cache
    cache.add(msg.eventId);

    // Apply to local clipboard
    _lastAppliedRemoteContent = msg.content;
    await adapter.setText(msg.content);

    _clipboardController.add(msg);

    // Check for URL handoff
    if (isUrl(msg.content)) {
      _urlHandoffController.add(msg.content.trim());
    }

    return true;
  }

  static bool isUrl(String? text) {
    if (text == null || text.trim().isEmpty) return false;
    final trimmed = text.trim();
    final uri = Uri.tryParse(trimmed);
    if (uri != null &&
        uri.hasScheme &&
        (uri.scheme == 'http' || uri.scheme == 'https')) {
      return uri.host.isNotEmpty;
    }
    return false;
  }

  static String generateUuid() {
    final random = Random.secure();
    final bytes = List<int>.generate(16, (_) => random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
    bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant RFC4122
    final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20, 32)}';
  }

  void dispose() {
    _adapterSubscription?.cancel();
    _clipboardController.close();
    _urlHandoffController.close();
    adapter.dispose();
  }
}
