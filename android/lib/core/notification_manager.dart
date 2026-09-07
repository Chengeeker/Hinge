import 'dart:async';
import 'dart:convert';

import 'notification_model.dart';
import 'protocol_frame.dart';
import 'session_manager.dart';

class NotificationManager {
  final NotificationFilter filter;
  final List<SessionConnection> _connections = [];
  final _notifController =
      StreamController<NotificationEventMessage>.broadcast();
  final _actionController =
      StreamController<NotificationActionMessage>.broadcast();

  Stream<NotificationEventMessage> get notificationStream =>
      _notifController.stream;
  Stream<NotificationActionMessage> get actionStream =>
      _actionController.stream;

  NotificationManager({NotificationFilter? filter})
    : filter = filter ?? NotificationFilter();

  void registerConnection(SessionConnection conn) {
    if (!_connections.contains(conn)) {
      _connections.add(conn);
    }
  }

  void unregisterConnection(SessionConnection conn) {
    _connections.remove(conn);
  }

  Future<bool> dispatchNotification(NotificationEventMessage notif) async {
    if (!filter.shouldAllow(notif)) {
      return false; // Filtered or debounced
    }

    final jsonStr = notif.serialize();
    final bytes = utf8.encode(jsonStr);

    for (final conn in List<SessionConnection>.of(_connections)) {
      if (await conn.waitUntilReady()) {
        conn.sendFrame(MessageType.notificationEvent, bytes);
      }
    }

    _notifController.add(notif);
    return true;
  }

  void handleIncomingFrame(SessionConnection conn, ProtocolFrame frame) {
    if (frame.type != MessageType.notificationEvent &&
        frame.type != MessageType.notificationAction) {
      return;
    }

    final jsonStr = utf8.decode(frame.payload, allowMalformed: true);

    if (frame.type == MessageType.notificationEvent) {
      final notif = NotificationEventMessage.fromJson(jsonStr);
      if (notif != null && filter.shouldAllow(notif)) {
        _notifController.add(notif);
      }
    } else if (frame.type == MessageType.notificationAction) {
      final action = NotificationActionMessage.fromJson(jsonStr);
      if (action != null) {
        _actionController.add(action);
      }
    }
  }

  void dispose() {
    _connections.clear();
    _notifController.close();
    _actionController.close();
  }
}
