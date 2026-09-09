import 'dart:async';
import 'dart:io';

import 'package:flutter/services.dart';

import 'notification_manager.dart';
import 'notification_model.dart';
import 'session_manager.dart';

/// Bridges the native Android SMS receiver into the existing notification
/// protocol. The native side only emits events while the user has enabled SMS
/// relay; no SMS history is queried or written to disk by this class.
class SmsRelayManager {
  static const EventChannel _events = EventChannel('hinge/sms/events');
  static const int _maxPendingNotifications = 8;

  final NotificationManager notificationManager;
  StreamSubscription<dynamic>? _subscription;
  final Set<SessionConnection> _connections = <SessionConnection>{};
  final List<NotificationEventMessage> _pendingNotifications =
      <NotificationEventMessage>[];

  SmsRelayManager({required this.notificationManager}) {
    if (Platform.isAndroid) {
      _subscription = _events.receiveBroadcastStream().listen(
        _handleNativeEvent,
        onError: (_, _) {},
      );
    }
  }

  void registerConnection(SessionConnection connection) {
    _connections.add(connection);
    notificationManager.registerConnection(connection);
    _flushPendingNotifications();
  }

  void unregisterConnection(SessionConnection connection) {
    _connections.remove(connection);
    notificationManager.unregisterConnection(connection);
  }

  void _flushPendingNotifications() {
    if (_connections.isEmpty || _pendingNotifications.isEmpty) return;
    final pending = List<NotificationEventMessage>.of(_pendingNotifications);
    _pendingNotifications.clear();
    for (final notification in pending) {
      unawaited(_dispatch(notification));
    }
  }

  void _queue(NotificationEventMessage notification) {
    if (_pendingNotifications.length >= _maxPendingNotifications) {
      _pendingNotifications.removeAt(0);
    }
    _pendingNotifications.add(notification);
  }

  Future<void> _dispatch(NotificationEventMessage notification) async {
    if (_connections.isEmpty) {
      _queue(notification);
      return;
    }
    final delivered = await notificationManager.dispatchNotification(
      notification,
    );
    // A connection can disappear while waitUntilReady() is waiting. Keep the
    // event briefly so a just-completed reconnect can still deliver it.
    if (!delivered && _connections.isEmpty) _queue(notification);
  }

  void _handleNativeEvent(dynamic value) {
    if (value is! Map) return;
    final source = '${value['source'] ?? 'sms'}'.trim().toLowerCase();
    final body = '${value['body'] ?? ''}'.trim();
    final sender = '${value['sender'] ?? ''}'.trim();
    if (body.isEmpty && source != 'mms') return;

    final code = '${value['verificationCode'] ?? ''}'.trim();
    final timestamp = (value['timestamp'] as num?)?.toInt();
    final isVerificationCode =
        value['isVerificationCode'] == true && code.isNotEmpty;
    final title = sender.isEmpty ? (source == 'mms' ? '收到彩信' : '收到短信') : sender;

    final notification = NotificationEventMessage(
      notificationId: '${value['messageId'] ?? ''}'.trim().isEmpty
          ? null
          : '${value['messageId']}',
      packageName: source == 'mms'
          ? 'android.provider.Telephony.MMS'
          : 'android.provider.Telephony.SMS',
      appName: source == 'mms' ? '彩信' : '短信',
      title: title,
      content: body,
      source: source,
      isVerificationCode: isVerificationCode,
      verificationCode: isVerificationCode ? code : null,
      timestamp: timestamp,
      actions: isVerificationCode ? const ['copy_code'] : const [],
    );
    unawaited(_dispatch(notification));
  }

  Future<void> dispose() async {
    await _subscription?.cancel();
    _subscription = null;
    _connections.clear();
    _pendingNotifications.clear();
  }
}
