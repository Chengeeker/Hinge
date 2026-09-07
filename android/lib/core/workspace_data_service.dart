import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

import 'device_model.dart';
import 'protocol_frame.dart';
import 'pairing_manager.dart';
import 'session_manager.dart';
import 'transfer_manager.dart';

class StorageInfo {
  final int totalBytes;
  final int usedBytes;
  final int freeBytes;

  const StorageInfo({
    required this.totalBytes,
    required this.usedBytes,
    required this.freeBytes,
  });

  double get usedRatio => totalBytes <= 0 ? 0 : usedBytes / totalBytes;

  Map<String, dynamic> toJson() => {
    'totalBytes': totalBytes,
    'usedBytes': usedBytes,
    'freeBytes': freeBytes,
  };

  factory StorageInfo.fromJson(Map<dynamic, dynamic> json) {
    return StorageInfo(
      totalBytes: (json['totalBytes'] as num?)?.toInt() ?? 0,
      usedBytes: (json['usedBytes'] as num?)?.toInt() ?? 0,
      freeBytes: (json['freeBytes'] as num?)?.toInt() ?? 0,
    );
  }
}

class CalendarEvent {
  final String id;
  final String title;
  final DateTime start;
  final DateTime? end;
  final String location;
  final bool allDay;

  const CalendarEvent({
    required this.id,
    required this.title,
    required this.start,
    this.end,
    this.location = '',
    this.allDay = false,
  });

  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'start': start.millisecondsSinceEpoch,
    'end': end?.millisecondsSinceEpoch,
    'location': location,
    'allDay': allDay,
  };

  factory CalendarEvent.fromJson(Map<dynamic, dynamic> json) {
    return CalendarEvent(
      id: '${json['id'] ?? ''}',
      title: '${json['title'] ?? '未命名日程'}',
      start: DateTime.fromMillisecondsSinceEpoch(
        (json['start'] as num?)?.toInt() ?? 0,
      ),
      end: (json['end'] as num?) == null
          ? null
          : DateTime.fromMillisecondsSinceEpoch((json['end'] as num).toInt()),
      location: '${json['location'] ?? ''}',
      allDay: json['allDay'] == true,
    );
  }
}

class PhotoItem {
  final String id;
  final String name;
  final String uri;
  final int takenAt;
  final int width;
  final int height;
  final int sizeBytes;

  const PhotoItem({
    required this.id,
    required this.name,
    required this.uri,
    this.takenAt = 0,
    this.width = 0,
    this.height = 0,
    this.sizeBytes = 0,
  });

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'uri': uri,
    'takenAt': takenAt,
    'width': width,
    'height': height,
    'sizeBytes': sizeBytes,
  };

  factory PhotoItem.fromJson(Map<dynamic, dynamic> json) {
    return PhotoItem(
      id: '${json['id'] ?? ''}',
      name: '${json['name'] ?? '未命名图片'}',
      uri: '${json['uri'] ?? ''}',
      takenAt: (json['takenAt'] as num?)?.toInt() ?? 0,
      width: (json['width'] as num?)?.toInt() ?? 0,
      height: (json['height'] as num?)?.toInt() ?? 0,
      sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
    );
  }
}

class PhotoPage {
  final List<PhotoItem> items;
  final int total;

  const PhotoPage({required this.items, required this.total});

  factory PhotoPage.fromJson(Map<dynamic, dynamic> json) {
    final rawItems = json['items'];
    return PhotoPage(
      items: rawItems is List
          ? rawItems.whereType<Map>().map(PhotoItem.fromJson).toList()
          : const <PhotoItem>[],
      total: (json['total'] as num?)?.toInt() ?? 0,
    );
  }
}

class PhotoAlbum {
  final String id;
  final String name;
  final int count;
  final int totalSizeBytes;
  final String coverUri;
  final int latestTakenAt;
  final String relativePath;

  const PhotoAlbum({
    required this.id,
    required this.name,
    required this.count,
    this.totalSizeBytes = 0,
    required this.coverUri,
    this.latestTakenAt = 0,
    this.relativePath = '',
  });

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'count': count,
    'totalSizeBytes': totalSizeBytes,
    'coverUri': coverUri,
    'latestTakenAt': latestTakenAt,
    'relativePath': relativePath,
  };

  factory PhotoAlbum.fromJson(Map<dynamic, dynamic> json) {
    return PhotoAlbum(
      id: '${json['id'] ?? ''}',
      name: '${json['name'] ?? '未命名相册'}',
      count: (json['count'] as num?)?.toInt() ?? 0,
      totalSizeBytes: (json['totalSizeBytes'] as num?)?.toInt() ?? 0,
      coverUri: '${json['coverUri'] ?? ''}',
      latestTakenAt: (json['latestTakenAt'] as num?)?.toInt() ?? 0,
      relativePath: '${json['relativePath'] ?? ''}',
    );
  }
}

class RemoteFileItem {
  final String id;
  final String name;
  final String mimeType;
  final int sizeBytes;
  final int modifiedAt;
  final String uri;

  const RemoteFileItem({
    required this.id,
    required this.name,
    required this.mimeType,
    this.sizeBytes = 0,
    this.modifiedAt = 0,
    this.uri = '',
  });

  factory RemoteFileItem.fromJson(Map<dynamic, dynamic> json) {
    return RemoteFileItem(
      id: '${json['id'] ?? ''}',
      name: '${json['name'] ?? '未命名文件'}',
      mimeType: '${json['mimeType'] ?? 'application/octet-stream'}',
      sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
      modifiedAt: (json['modifiedAt'] as num?)?.toInt() ?? 0,
      uri: '${json['uri'] ?? ''}',
    );
  }
}

class StorageEntry {
  final String id;
  final String name;
  final String relativePath;
  final String mimeType;
  final int sizeBytes;
  final int modifiedAt;
  final String uri;
  final bool isDirectory;
  final String category;

  const StorageEntry({
    required this.id,
    required this.name,
    required this.relativePath,
    required this.mimeType,
    this.sizeBytes = 0,
    this.modifiedAt = 0,
    this.uri = '',
    this.isDirectory = false,
    this.category = '',
  });

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'relativePath': relativePath,
    'mimeType': mimeType,
    'sizeBytes': sizeBytes,
    'modifiedAt': modifiedAt,
    'uri': uri,
    'isDirectory': isDirectory,
    'category': category,
  };

  factory StorageEntry.fromJson(Map<dynamic, dynamic> json) {
    return StorageEntry(
      id: (json['id'] ?? '').toString(),
      name: (json['name'] ?? '未命名文件').toString(),
      relativePath: (json['relativePath'] ?? '').toString(),
      mimeType: (json['mimeType'] ?? 'application/octet-stream').toString(),
      sizeBytes: (json['sizeBytes'] as num?)?.toInt() ?? 0,
      modifiedAt: (json['modifiedAt'] as num?)?.toInt() ?? 0,
      uri: (json['uri'] ?? '').toString(),
      isDirectory: json['isDirectory'] == true,
      category: (json['category'] ?? '').toString(),
    );
  }
}

class StorageDirectoryPage {
  final List<StorageEntry> items;
  final int total;

  const StorageDirectoryPage({required this.items, required this.total});

  factory StorageDirectoryPage.fromJson(Map<dynamic, dynamic> json) {
    final rawItems = json['items'];
    return StorageDirectoryPage(
      items: rawItems is List
          ? rawItems.whereType<Map>().map(StorageEntry.fromJson).toList()
          : const [],
      total: (json['total'] as num?)?.toInt() ?? 0,
    );
  }
}

class WorkspaceNote {
  final String id;
  final String title;
  final String content;
  final int updatedAt;
  final bool pinned;

  const WorkspaceNote({
    required this.id,
    required this.title,
    required this.content,
    required this.updatedAt,
    this.pinned = false,
  });

  WorkspaceNote copyWith({
    String? title,
    String? content,
    int? updatedAt,
    bool? pinned,
  }) {
    return WorkspaceNote(
      id: id,
      title: title ?? this.title,
      content: content ?? this.content,
      updatedAt: updatedAt ?? this.updatedAt,
      pinned: pinned ?? this.pinned,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'content': content,
    'updatedAt': updatedAt,
    'pinned': pinned,
  };

  factory WorkspaceNote.fromJson(Map<dynamic, dynamic> json) {
    return WorkspaceNote(
      id: '${json['id'] ?? ''}',
      title: '${json['title'] ?? ''}',
      content: '${json['content'] ?? ''}',
      updatedAt: (json['updatedAt'] as num?)?.toInt() ?? 0,
      pinned: json['pinned'] == true,
    );
  }
}

class WorkspaceTask {
  final String id;
  final String title;
  final bool completed;
  final int? dueAt;
  final int updatedAt;

  const WorkspaceTask({
    required this.id,
    required this.title,
    this.completed = false,
    this.dueAt,
    required this.updatedAt,
  });

  WorkspaceTask copyWith({
    String? title,
    bool? completed,
    int? dueAt,
    int? updatedAt,
  }) {
    return WorkspaceTask(
      id: id,
      title: title ?? this.title,
      completed: completed ?? this.completed,
      dueAt: dueAt ?? this.dueAt,
      updatedAt: updatedAt ?? this.updatedAt,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'title': title,
    'completed': completed,
    'dueAt': dueAt,
    'updatedAt': updatedAt,
  };

  factory WorkspaceTask.fromJson(Map<dynamic, dynamic> json) {
    return WorkspaceTask(
      id: '${json['id'] ?? ''}',
      title: '${json['title'] ?? ''}',
      completed: json['completed'] == true,
      dueAt: (json['dueAt'] as num?)?.toInt(),
      updatedAt: (json['updatedAt'] as num?)?.toInt() ?? 0,
    );
  }
}

class WorkspaceDataService {
  static const MethodChannel _channel = MethodChannel('hinge/platform');

  Future<dynamic> _invoke(String method, [dynamic arguments]) async {
    try {
      return await _channel.invokeMethod<dynamic>(method, arguments);
    } on MissingPluginException {
      return null;
    }
  }

  Future<StorageInfo?> readStorage() async {
    final raw = await _invoke('storageInfo');
    if (raw is Map) return StorageInfo.fromJson(raw);
    return null;
  }

  Future<bool> hapticFeedbackEnabled() async {
    final raw = await _invoke('hapticFeedbackEnabled');
    return raw is bool ? raw : true;
  }

  Future<void> setHapticFeedbackEnabled(bool enabled) async {
    await _invoke('setHapticFeedbackEnabled', {'enabled': enabled});
  }

  Future<bool> hasNotificationPermission() async {
    return await _invoke('hasNotificationPermission') == true;
  }

  Future<bool> requestNotificationPermission() async {
    return await _invoke('requestNotificationPermission') == true;
  }

  Future<bool> persistentNotificationEnabled() async {
    final raw = await _invoke('persistentNotificationEnabled');
    return raw is bool ? raw : true;
  }

  Future<void> setPersistentNotificationEnabled(bool enabled) async {
    await _invoke('setPersistentNotificationEnabled', {'enabled': enabled});
  }

  Future<Map<String, dynamic>> keepAliveStatus() async {
    final raw = await _invoke('keepAliveStatus');
    if (raw is! Map) return const {};
    return raw.map((key, value) => MapEntry('$key', value));
  }

  Future<bool> openNotificationSettings() async {
    return await _invoke('openNotificationSettings') == true;
  }

  Future<bool> openBatteryOptimizationSettings() async {
    return await _invoke('openBatteryOptimizationSettings') == true;
  }

  Future<bool> openBackgroundProtectionSettings() async {
    return await _invoke('openBackgroundProtectionSettings') == true;
  }

  Future<bool> openProjectUrl() async {
    return await _invoke('openProjectUrl') == true;
  }

  Future<bool> showFileReceivedNotification(String path) async {
    final raw = await _invoke('showFileReceivedNotification', {'path': path});
    return raw == true;
  }

  Future<List<Map<String, String>>> defaultAppOptions(String type) async {
    final raw = await _invoke('defaultAppOptions', {'type': type});
    if (raw is! List) return const <Map<String, String>>[];
    return raw
        .whereType<Map>()
        .map(
          (item) => <String, String>{
            'packageName': '${item['packageName'] ?? ''}',
            'label': '${item['label'] ?? item['packageName'] ?? ''}',
            'iconBase64': '${item['iconBase64'] ?? ''}',
          },
        )
        .where((item) => item['packageName']!.isNotEmpty)
        .toList(growable: false);
  }

  Future<String?> defaultApp(String type) async {
    final raw = await _invoke('defaultApp', {'type': type});
    return raw is String && raw.isNotEmpty ? raw : null;
  }

  Future<bool> setDefaultApp(String type, String? packageName) async {
    final raw = await _invoke('setDefaultApp', {
      'type': type,
      'packageName': packageName ?? '',
    });
    return raw == true;
  }

  Future<void> openAppSettings() async {
    await _invoke('openAppSettings');
  }

  Future<int?> loadSystemAccentColor() async {
    final raw = await _invoke('systemAccentColor');
    return raw is int ? raw.toUnsigned(32) : null;
  }

  /// Android's wallpaper-derived Material roles for both brightness modes.
  /// Keeping the roles native avoids rebuilding a second, unrelated palette
  /// from a single accent color in Flutter.
  Future<Map<String, int>> loadSystemDynamicColors() async {
    final raw = await _invoke('systemDynamicColors');
    if (raw is! Map) return const {};
    final colors = <String, int>{};
    raw.forEach((key, value) {
      if (value is num) {
        colors['$key'] = value.toInt().toUnsigned(32);
      }
    });
    return colors;
  }

  Future<Map<String, String>> loadStoragePaths() async {
    final raw = await _invoke('storagePaths');
    if (raw is Map) {
      return raw.map((key, value) => MapEntry('$key', '$value'));
    }
    return const {};
  }

  Future<bool> hasCalendarPermission() async {
    return await _invoke('hasCalendarPermission') == true;
  }

  Future<bool> requestCalendarPermission() async {
    return await _invoke('requestCalendarPermission') == true;
  }

  Future<List<CalendarEvent>> loadCalendar() async {
    final raw = await _invoke('calendarEvents');
    if (raw is! List) return const [];
    return raw.whereType<Map>().map(CalendarEvent.fromJson).toList();
  }

  Future<bool> hasPhotosPermission() async {
    return await _invoke('hasPhotosPermission') == true;
  }

  Future<bool> requestPhotosPermission() async {
    return await _invoke('requestPhotosPermission') == true;
  }

  Future<bool> hasMediaPermission() async {
    return await _invoke('hasMediaPermission') == true;
  }

  Future<bool> requestMediaPermission() async {
    return await _invoke('requestMediaPermission') == true;
  }

  Future<bool> hasAllFilesAccess() async {
    final raw = await _invoke('hasAllFilesAccess');
    // Desktop/widget-test has no Android method channel. Do not block those
    // environments on an Android-only special permission.
    return raw is bool ? raw : true;
  }

  Future<bool> openAllFilesAccessSettings() async {
    return await _invoke('openAllFilesAccessSettings') == true;
  }

  Future<List<PhotoItem>> loadPhotos({String? albumId}) async {
    final raw = await _invoke(
      'photos',
      albumId == null ? null : {'albumId': albumId},
    );
    if (raw is! List) return const [];
    return raw.whereType<Map>().map(PhotoItem.fromJson).toList();
  }

  Future<PhotoPage> loadPhotoPage({
    String? albumId,
    int offset = 0,
    int limit = 200,
  }) async {
    final raw = await _invoke('photosPage', {
      ...?(albumId == null ? null : {'albumId': albumId}),
      'offset': offset,
      'limit': limit,
    });
    return raw is Map
        ? PhotoPage.fromJson(raw)
        : const PhotoPage(items: [], total: 0);
  }

  Future<List<PhotoAlbum>> loadPhotoAlbums() async {
    final raw = await _invoke('photoAlbums');
    if (raw is! List) return const [];
    return raw.whereType<Map>().map(PhotoAlbum.fromJson).toList();
  }

  Future<Uint8List?> loadPhotoBytes(String uri) async {
    final raw = await _invoke('photoBytes', {'uri': uri});
    return raw is Uint8List ? raw : null;
  }

  Future<Uint8List?> loadPhotoThumbnailBytes(String uri) async {
    final raw = await _invoke('photoThumbnailBytes', {'uri': uri});
    return raw is Uint8List ? raw : null;
  }

  Future<Uint8List?> loadPhotoPreviewBytes(String uri) async {
    final raw = await _invoke('photoPreviewBytes', {'uri': uri});
    return raw is Uint8List ? raw : null;
  }

  Future<String?> copyUriToCache(String uri, String fileName) async {
    final raw = await _invoke('copyUriToCache', {
      'uri': uri,
      'fileName': fileName,
    });
    return raw is String && raw.isNotEmpty ? raw : null;
  }

  Future<int> deleteFiles(List<String> uris) async {
    final raw = await _invoke('deleteFiles', {'uris': uris});
    return raw is num ? raw.toInt() : 0;
  }

  Future<List<RemoteFileItem>> loadFiles() async {
    final raw = await _invoke('files');
    if (raw is! List) return const [];
    return raw.whereType<Map>().map(RemoteFileItem.fromJson).toList();
  }

  Future<List<StorageEntry>> loadStorageDirectory({
    String category = 'storage',
    String path = '',
    bool forceRefresh = false,
  }) async {
    final raw = await _invoke('storageDirectory', {
      'category': category,
      'path': path,
      'forceRefresh': forceRefresh,
    });
    if (raw is! List) return const [];
    return raw.whereType<Map>().map(StorageEntry.fromJson).toList();
  }

  Future<StorageDirectoryPage> loadStorageDirectoryPage({
    String category = 'storage',
    String path = '',
    int offset = 0,
    int limit = 200,
    bool forceRefresh = false,
  }) async {
    final raw = await _invoke('storageDirectoryPage', {
      'category': category,
      'path': path,
      'offset': offset,
      'limit': limit,
      'forceRefresh': forceRefresh,
    });
    if (raw is Map) return StorageDirectoryPage.fromJson(raw);
    return const StorageDirectoryPage(items: [], total: 0);
  }

  Future<List<WorkspaceNote>> loadNotes() async {
    final raw = await _invoke('loadNotes');
    if (raw is List) {
      return raw.whereType<Map>().map(WorkspaceNote.fromJson).toList();
    }
    return _loadLocalList('notes', WorkspaceNote.fromJson);
  }

  Future<void> saveNote(WorkspaceNote note) async {
    final nativeResult = await _invoke('saveNote', note.toJson());
    if (nativeResult != null) return;
    await _saveLocalList('notes', note.toJson());
  }

  Future<void> deleteNote(String id) async {
    final nativeResult = await _invoke('deleteNote', id);
    if (nativeResult != null) return;
    final notes = await loadNotes();
    await _writeLocalList(
      'notes',
      notes
          .where((note) => note.id != id)
          .map((note) => note.toJson())
          .toList(),
    );
  }

  Future<List<WorkspaceTask>> loadTasks() async {
    final raw = await _invoke('loadTasks');
    if (raw is List) {
      return raw.whereType<Map>().map(WorkspaceTask.fromJson).toList();
    }
    return _loadLocalList('tasks', WorkspaceTask.fromJson);
  }

  Future<void> saveTask(WorkspaceTask task) async {
    final nativeResult = await _invoke('saveTask', task.toJson());
    if (nativeResult != null) return;
    await _saveLocalList('tasks', task.toJson());
  }

  Future<void> deleteTask(String id) async {
    final nativeResult = await _invoke('deleteTask', id);
    if (nativeResult != null) return;
    final tasks = await loadTasks();
    await _writeLocalList(
      'tasks',
      tasks
          .where((task) => task.id != id)
          .map((task) => task.toJson())
          .toList(),
    );
  }

  static String newId() =>
      '${DateTime.now().microsecondsSinceEpoch}-${DateTime.now().millisecond}';

  static String _localRoot() {
    final home = Platform.isWindows
        ? (Platform.environment['LOCALAPPDATA'] ??
              Platform.environment['APPDATA'] ??
              Directory.systemTemp.path)
        : (Platform.environment['HOME'] ?? Directory.systemTemp.path);
    return Platform.isWindows ? '$home/Hinge' : '$home/.hinge';
  }

  Future<List<T>> _loadLocalList<T>(
    String name,
    T Function(Map<dynamic, dynamic>) parser,
  ) async {
    final file = File('${_localRoot()}/$name.json');
    if (!file.existsSync()) return <T>[];
    try {
      final raw = jsonDecode(await file.readAsString());
      if (raw is! List) return <T>[];
      return raw.whereType<Map>().map(parser).toList();
    } catch (_) {
      return <T>[];
    }
  }

  Future<void> _saveLocalList(String name, Map<String, dynamic> item) async {
    final current = await _loadLocalList<Map<String, dynamic>>(
      name,
      (json) => Map<String, dynamic>.from(json),
    );
    final id = item['id'];
    final index = current.indexWhere((entry) => entry['id'] == id);
    if (index >= 0) {
      current[index] = item;
    } else {
      current.add(item);
    }
    await _writeLocalList(name, current);
  }

  Future<void> _writeLocalList(
    String name,
    List<Map<String, dynamic>> items,
  ) async {
    final file = File('${_localRoot()}/$name.json');
    await file.parent.create(recursive: true);
    await file.writeAsString(jsonEncode(items));
  }
}

class WorkspaceCommandRouter {
  final SessionManager sessionManager;
  final WorkspaceDataService dataService;
  final PairingManager pairingManager;
  final TransferManager transferManager;
  StreamSubscription<SessionConnection>? _connectionsSubscription;
  final List<StreamSubscription<ProtocolFrame>> _frameSubscriptions = [];
  final Map<SessionConnection, Future<void>> _connectionQueues = {};

  WorkspaceCommandRouter({
    required this.sessionManager,
    required this.dataService,
    required this.pairingManager,
    required this.transferManager,
  });

  void start() {
    _connectionsSubscription ??= sessionManager.onConnectionCreated.listen(
      _watchConnection,
    );
  }

  void _watchConnection(SessionConnection connection) {
    final subscription = connection.frames.listen((frame) {
      // A single ordered queue per connection prevents several expensive
      // ContentResolver queries from racing on the same TCP stream. Control
      // responses remain paired with their command IDs on the Windows side.
      final previous = _connectionQueues[connection] ?? Future<void>.value();
      final next = previous
          .catchError((_) {})
          .then((_) => _handleFrame(connection, frame));
      _connectionQueues[connection] = next;
      unawaited(
        next.whenComplete(() {
          if (identical(_connectionQueues[connection], next)) {
            _connectionQueues.remove(connection);
          }
        }),
      );
    });
    _frameSubscriptions.add(subscription);
  }

  Future<void> _handleFrame(
    SessionConnection connection,
    ProtocolFrame frame,
  ) async {
    if (frame.type != MessageType.toolCommand) return;

    try {
      final command =
          jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
      final commandId = '${command['commandId'] ?? ''}';
      final name = '${command['command'] ?? ''}';
      final payload = command['payload'] is Map
          ? Map<String, dynamic>.from(command['payload'] as Map)
          : <String, dynamic>{};
      final result = await _execute(connection, name, payload);
      connection.sendJson(MessageType.toolResult, {
        'commandId': commandId,
        'success': true,
        'payload': result,
      });
    } catch (error) {
      String commandId = '';
      try {
        final command = jsonDecode(utf8.decode(frame.payload)) as Map;
        commandId = '${command['commandId'] ?? ''}';
      } catch (_) {}
      connection.sendJson(MessageType.toolResult, {
        'commandId': commandId,
        'success': false,
        'error': '$error',
      });
    }
  }

  Future<dynamic> _execute(
    SessionConnection connection,
    String command,
    Map<String, dynamic> payload,
  ) async {
    switch (command) {
      case 'getDeviceSummary':
        return (await dataService.readStorage())?.toJson() ??
            <String, dynamic>{};
      case 'deviceInfo':
        // 连接建立后再次从 Android 原生层读取一次，避免首轮广播使用旧身份缓存。
        final info = await const MethodChannel('hinge/platform')
            .invokeMethod<Map<dynamic, dynamic>>('deviceInfo');
        return info == null
            ? <String, dynamic>{}
            : Map<String, dynamic>.from(info);
      case 'calendarEvents':
        return (await dataService.loadCalendar())
            .map((event) => event.toJson())
            .toList();
      case 'photoAlbums':
        return (await dataService.loadPhotoAlbums())
            .map((album) => album.toJson())
            .toList();
      case 'photos':
        return (await dataService.loadPhotos(
          albumId: payload['albumId']?.toString(),
        )).map((photo) => photo.toJson()).toList();
      case 'photosPage':
        final page = await dataService.loadPhotoPage(
          albumId: payload['albumId']?.toString(),
          offset: (payload['offset'] as num?)?.toInt() ?? 0,
          limit: (payload['limit'] as num?)?.toInt() ?? 200,
        );
        return <String, dynamic>{
          'items': page.items.map((photo) => photo.toJson()).toList(),
          'total': page.total,
        };
      case 'photoBytes':
        final bytes = await dataService.loadPhotoBytes(
          '${payload['uri'] ?? ''}',
        );
        return bytes == null ? null : base64Encode(bytes);
      case 'photoThumbnailBytes':
        final bytes = await dataService.loadPhotoThumbnailBytes(
          '${payload['uri'] ?? ''}',
        );
        return bytes == null ? null : base64Encode(bytes);
      case 'photoPreviewBytes':
        final bytes = await dataService.loadPhotoPreviewBytes(
          '${payload['uri'] ?? ''}',
        );
        return bytes == null ? null : base64Encode(bytes);
      case 'sendMediaToComputer':
        final uri = '${payload['uri'] ?? ''}'.trim();
        final fileName = '${payload['fileName'] ?? '手机文件'}'.trim();
        if (uri.isEmpty) throw ArgumentError('媒体地址为空');
        // 大视频不能塞进 ToolResult 的 base64 JSON。后台通过现有的分块
        // FILE_OFFER/FILE_CHUNK 通道发送，命令本身立即返回，避免 10 秒命令
        // 超时把正在传输的会话误判成断线。
        unawaited(
          _sendMediaToComputer(
            connection,
            uri,
            fileName,
            '${payload['mimeType'] ?? ''}'.trim(),
          ),
        );
        return <String, dynamic>{'started': true, 'fileName': fileName};
      case 'pairDevice':
        final deviceId = '${payload['deviceId'] ?? ''}';
        if (deviceId.isEmpty) throw ArgumentError('缺少配对设备 ID');
        pairingManager.saveTrustedPeer(
          deviceId,
          '${payload['name'] ?? '未命名设备'}',
          publicKey: '${payload['publicKey'] ?? ''}',
        );
        return <String, dynamic>{'paired': true};
      case 'files':
        return (await dataService.loadFiles())
            .map(
              (file) => {
                'id': file.id,
                'name': file.name,
                'mimeType': file.mimeType,
                'sizeBytes': file.sizeBytes,
                'modifiedAt': file.modifiedAt,
                'uri': file.uri,
              },
            )
            .toList();
      case 'browseFiles':
        return (await dataService.loadStorageDirectory(
          category: (payload['category'] ?? 'storage').toString(),
          path: (payload['path'] ?? '').toString(),
          forceRefresh: payload['forceRefresh'] == true,
        )).map((entry) => entry.toJson()).toList();
      case 'browseFilesPage':
        final page = await dataService.loadStorageDirectoryPage(
          category: (payload['category'] ?? 'storage').toString(),
          path: (payload['path'] ?? '').toString(),
          offset: (payload['offset'] as num?)?.toInt() ?? 0,
          limit: (payload['limit'] as num?)?.toInt() ?? 200,
          forceRefresh: payload['forceRefresh'] == true,
        );
        return <String, dynamic>{
          'items': page.items.map((entry) => entry.toJson()).toList(),
          'total': page.total,
        };
      case 'deleteFiles':
        final uris =
            (payload['uris'] is List ? payload['uris'] as List : const [])
                .map((uri) => '$uri')
                .where((uri) => uri.trim().isNotEmpty)
                .toList();
        return <String, dynamic>{
          'deleted': await dataService.deleteFiles(uris),
        };
      case 'loadNotes':
        return (await dataService.loadNotes())
            .map((note) => note.toJson())
            .toList();
      case 'saveNote':
        await dataService.saveNote(WorkspaceNote.fromJson(payload));
        return <String, dynamic>{'saved': true};
      case 'deleteNote':
        await dataService.deleteNote('${payload['id'] ?? ''}');
        return <String, dynamic>{'deleted': true};
      case 'loadTasks':
        return (await dataService.loadTasks())
            .map((task) => task.toJson())
            .toList();
      case 'saveTask':
        await dataService.saveTask(WorkspaceTask.fromJson(payload));
        return <String, dynamic>{'saved': true};
      case 'deleteTask':
        await dataService.deleteTask('${payload['id'] ?? ''}');
        return <String, dynamic>{'deleted': true};
      default:
        throw UnsupportedError('不支持的工作区命令：$command');
    }
  }

  Future<void> _sendMediaToComputer(
    SessionConnection connection,
    String uri,
    String fileName,
    String mimeType,
  ) async {
    String? cachedPath;
    try {
      cachedPath = await dataService.copyUriToCache(uri, fileName);
      if (cachedPath == null || cachedPath.isEmpty) return;
      await transferManager.sendFile(
        connection,
        cachedPath,
        mimeType: mimeType,
        fileNameOverride: fileName,
        precomputeHash: false,
      );
    } catch (_) {
      // The receiving side will report a timeout or a disconnected session.
    } finally {
      if (cachedPath != null) {
        try {
          final cachedFile = File(cachedPath);
          if (cachedFile.existsSync()) await cachedFile.delete();
        } catch (_) {}
      }
    }
  }

  void dispose() {
    _connectionsSubscription?.cancel();
    _connectionsSubscription = null;
    for (final subscription in _frameSubscriptions) {
      subscription.cancel();
    }
    _frameSubscriptions.clear();
    _connectionQueues.clear();
  }
}

class WorkspaceRemoteClient {
  final SessionManager sessionManager;

  const WorkspaceRemoteClient(this.sessionManager);

  Future<dynamic> invoke(
    Device device,
    String command, [
    Map<String, dynamic> payload = const {},
  ]) async {
    if (device.networkAddresses.isEmpty) {
      throw StateError('设备尚未提供可连接的局域网地址');
    }

    final current = sessionManager.connectionForDevice(device.deviceId);
    if (current != null) {
      return invokeOnConnection(current, command, payload);
    }

    Object? lastError;
    for (final address in device.networkAddresses.toSet()) {
      SessionConnection? connection;
      try {
        connection = await sessionManager.connectToPeer(
          InternetAddress(address),
        );
        return await _invokeOnConnection(
          connection,
          command,
          payload,
          disposeConnection: true,
        );
      } catch (error) {
        lastError = error;
        connection?.dispose();
      }
    }
    throw StateError(lastError == null ? '没有可用的设备地址' : '$lastError');
  }

  Future<dynamic> invokeOnConnection(
    SessionConnection connection,
    String command, [
    Map<String, dynamic> payload = const {},
  ]) {
    return _invokeOnConnection(
      connection,
      command,
      payload,
      disposeConnection: false,
    );
  }

  Future<dynamic> _invokeOnConnection(
    SessionConnection connection,
    String command,
    Map<String, dynamic> payload, {
    required bool disposeConnection,
  }) async {
    final commandId = WorkspaceDataService.newId();
    final completer = Completer<Map<String, dynamic>>();
    late final StreamSubscription<ProtocolFrame> subscription;
    subscription = connection.frames.listen((frame) {
      if (frame.type != MessageType.toolResult) return;
      try {
        final result = jsonDecode(utf8.decode(frame.payload)) as Map;
        if ('${result['commandId'] ?? ''}' != commandId ||
            completer.isCompleted) {
          return;
        }
        completer.complete(Map<String, dynamic>.from(result));
      } catch (error) {
        if (!completer.isCompleted) completer.completeError(error);
      }
    });

    try {
      connection.sendJson(MessageType.toolCommand, {
        'commandId': commandId,
        'command': command,
        'payload': payload,
      });
      final result = await completer.future.timeout(
        const Duration(seconds: 10),
      );
      if (result['success'] != true) {
        throw StateError('${result['error'] ?? '远端操作失败'}');
      }
      return result['payload'];
    } finally {
      await subscription.cancel();
      if (disposeConnection) connection.dispose();
    }
  }
}
