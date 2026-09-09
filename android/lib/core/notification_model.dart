import 'dart:convert';

class NotificationEventMessage {
  final String notificationId;
  final String packageName;
  final String appName;
  final String title;
  final String content;

  /// Optional source discriminator for specialized relays such as SMS/MMS.
  /// Generic notifications keep this empty for backwards compatibility.
  final String source;
  final bool isVerificationCode;
  final String? verificationCode;
  final int timestamp;
  final bool canReply;
  final List<String> actions;

  NotificationEventMessage({
    String? notificationId,
    required this.packageName,
    this.appName = '',
    this.title = '',
    this.content = '',
    this.source = '',
    this.isVerificationCode = false,
    this.verificationCode,
    int? timestamp,
    this.canReply = false,
    List<String>? actions,
  }) : notificationId =
           notificationId ?? DateTime.now().microsecondsSinceEpoch.toString(),
       timestamp = timestamp ?? DateTime.now().millisecondsSinceEpoch,
       actions = actions ?? [];

  Map<String, dynamic> toJson() => {
    'notificationId': notificationId,
    'packageName': packageName,
    'appName': appName,
    'title': title,
    'content': content,
    if (source.isNotEmpty) 'source': source,
    if (isVerificationCode) 'isVerificationCode': true,
    if (verificationCode != null && verificationCode!.isNotEmpty)
      'verificationCode': verificationCode,
    'timestamp': timestamp,
    'canReply': canReply,
    'actions': actions,
  };

  String serialize() => jsonEncode(toJson());

  static NotificationEventMessage? fromJson(String jsonStr) {
    try {
      final map = jsonDecode(jsonStr) as Map<String, dynamic>;
      return NotificationEventMessage(
        notificationId: map['notificationId'] as String?,
        packageName: map['packageName'] as String? ?? '',
        appName: map['appName'] as String? ?? '',
        title: map['title'] as String? ?? '',
        content: map['content'] as String? ?? '',
        source: map['source'] as String? ?? '',
        isVerificationCode: map['isVerificationCode'] as bool? ?? false,
        verificationCode: map['verificationCode'] as String?,
        timestamp: (map['timestamp'] as num?)?.toInt(),
        canReply: map['canReply'] as bool? ?? false,
        actions: (map['actions'] as List<dynamic>?)
            ?.map((e) => e.toString())
            .toList(),
      );
    } catch (_) {
      return null;
    }
  }
}

class NotificationActionMessage {
  final String notificationId;
  final String actionKey;
  final String? replyText;
  final int timestamp;

  NotificationActionMessage({
    required this.notificationId,
    required this.actionKey,
    this.replyText,
    int? timestamp,
  }) : timestamp = timestamp ?? DateTime.now().millisecondsSinceEpoch;

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{
      'notificationId': notificationId,
      'actionKey': actionKey,
      'timestamp': timestamp,
    };
    if (replyText != null) map['replyText'] = replyText;
    return map;
  }

  String serialize() => jsonEncode(toJson());

  static NotificationActionMessage? fromJson(String jsonStr) {
    try {
      final map = jsonDecode(jsonStr) as Map<String, dynamic>;
      return NotificationActionMessage(
        notificationId: map['notificationId'] as String? ?? '',
        actionKey: map['actionKey'] as String? ?? '',
        replyText: map['replyText'] as String?,
        timestamp: (map['timestamp'] as num?)?.toInt(),
      );
    } catch (_) {
      return null;
    }
  }
}

class NotificationFilter {
  final Set<String> whitelistPackages = {};
  final Set<String> blacklistPackages = {};
  Duration debounceWindow = const Duration(seconds: 2);
  final Map<String, DateTime> _dedupeCache = {};

  bool shouldAllow(NotificationEventMessage msg) {
    if (msg.packageName.trim().isEmpty) return false;

    // 1. Blacklist check
    if (blacklistPackages.contains(msg.packageName)) return false;

    // 2. Whitelist check (if any configured)
    if (whitelistPackages.isNotEmpty &&
        !whitelistPackages.contains(msg.packageName)) {
      return false;
    }

    // 3. Debounce deduplication check
    final key = '${msg.packageName}:${msg.title}:${msg.content}';
    final now = DateTime.now();

    if (_dedupeCache.length > 200) {
      _dedupeCache.removeWhere(
        (_, time) => now.difference(time) > debounceWindow,
      );
    }

    final lastTime = _dedupeCache[key];
    if (lastTime != null && now.difference(lastTime) < debounceWindow) {
      return false; // Suppress duplicate
    }

    _dedupeCache[key] = now;
    return true;
  }
}
