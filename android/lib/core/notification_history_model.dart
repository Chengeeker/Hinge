class NotificationHistoryApplication {
  final String packageName;
  final String appName;
  final int count;
  final String iconBase64;

  const NotificationHistoryApplication({
    required this.packageName,
    required this.appName,
    this.count = 0,
    this.iconBase64 = '',
  });

  factory NotificationHistoryApplication.fromJson(Map<dynamic, dynamic> json) {
    return NotificationHistoryApplication(
      packageName: '${json['packageName'] ?? ''}',
      appName: '${json['appName'] ?? json['packageName'] ?? ''}',
      count: (json['count'] as num?)?.toInt() ?? 0,
      iconBase64: '${json['iconBase64'] ?? ''}',
    );
  }
}

class NotificationHistoryItem {
  final String id;
  final String packageName;
  final String appName;
  final String title;
  final String content;
  final int timestamp;
  final String category;
  final bool ongoing;
  final String notificationKey;
  final String iconBase64;

  const NotificationHistoryItem({
    required this.id,
    required this.packageName,
    required this.appName,
    required this.title,
    required this.content,
    required this.timestamp,
    this.category = '',
    this.ongoing = false,
    this.notificationKey = '',
    this.iconBase64 = '',
  });

  factory NotificationHistoryItem.fromJson(Map<dynamic, dynamic> json) {
    return NotificationHistoryItem(
      id: '${json['id'] ?? ''}',
      packageName: '${json['packageName'] ?? ''}',
      appName: '${json['appName'] ?? json['packageName'] ?? ''}',
      title: '${json['title'] ?? ''}',
      content: '${json['content'] ?? ''}',
      timestamp: (json['timestamp'] as num?)?.toInt() ?? 0,
      category: '${json['category'] ?? ''}',
      ongoing: json['ongoing'] == true,
      notificationKey: '${json['notificationKey'] ?? ''}',
      iconBase64: '${json['iconBase64'] ?? ''}',
    );
  }
}

class NotificationHistoryPage {
  final List<NotificationHistoryItem> items;
  final int total;
  final bool accessEnabled;
  final bool enabled;
  final List<NotificationHistoryApplication> applications;

  const NotificationHistoryPage({
    required this.items,
    required this.total,
    required this.accessEnabled,
    required this.enabled,
    required this.applications,
  });

  factory NotificationHistoryPage.fromJson(Map<dynamic, dynamic> json) {
    final rawItems = json['items'];
    final rawApplications = json['applications'];
    return NotificationHistoryPage(
      items: rawItems is List
          ? rawItems
                .whereType<Map>()
                .map(NotificationHistoryItem.fromJson)
                .toList()
          : const <NotificationHistoryItem>[],
      total: (json['total'] as num?)?.toInt() ?? 0,
      accessEnabled: json['access'] == true,
      enabled: json['enabled'] == true,
      applications: rawApplications is List
          ? rawApplications
                .whereType<Map>()
                .map(NotificationHistoryApplication.fromJson)
                .where((item) => item.packageName.isNotEmpty)
                .toList()
          : const <NotificationHistoryApplication>[],
    );
  }
}
