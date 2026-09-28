import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';

enum TransferHistoryDirection { send, receive }

enum TransferHistoryStatus {
  queued,
  transferring,
  awaitingPickup,
  completed,
  failed,
  cancelled,
}

@immutable
class TransferHistoryRecord {
  final String id;
  final String transferId;
  final String deviceId;
  final String deviceName;
  final String fileName;
  final String localFilePath;
  final TransferHistoryDirection direction;
  final TransferHistoryStatus status;
  final int bytesTransferred;
  final int totalBytes;
  final DateTime createdAt;
  final DateTime updatedAt;
  final String error;

  const TransferHistoryRecord({
    required this.id,
    this.transferId = '',
    this.deviceId = '',
    this.deviceName = '',
    required this.fileName,
    this.localFilePath = '',
    required this.direction,
    required this.status,
    this.bytesTransferred = 0,
    this.totalBytes = 0,
    required this.createdAt,
    required this.updatedAt,
    this.error = '',
  });

  bool get canDelete =>
      status == TransferHistoryStatus.awaitingPickup ||
      status == TransferHistoryStatus.completed ||
      status == TransferHistoryStatus.failed ||
      status == TransferHistoryStatus.cancelled;

  bool get isInProgress => !canDelete;

  double get progress =>
      totalBytes <= 0 ? 0 : (bytesTransferred / totalBytes).clamp(0.0, 1.0);

  String get statusText => switch (status) {
    TransferHistoryStatus.queued =>
      error.isEmpty
          ? (direction == TransferHistoryDirection.send ? '等待发送' : '等待接收')
          : '等待重试 · $error',
    TransferHistoryStatus.transferring =>
      '${direction == TransferHistoryDirection.send ? '正在发送' : '正在接收'}'
          '${totalBytes > 0 ? ' · ${(progress * 100).round()}%' : ''}',
    TransferHistoryStatus.awaitingPickup => '已上传 Cloud Relay · 等待设备接收',
    TransferHistoryStatus.completed =>
      direction == TransferHistoryDirection.send ? '发送完成' : '接收完成',
    TransferHistoryStatus.failed => error.isEmpty ? '传输失败' : '失败 · $error',
    TransferHistoryStatus.cancelled => '已取消',
  };

  String get directionText =>
      direction == TransferHistoryDirection.send ? '发送' : '接收';

  TransferHistoryRecord copyWith({
    String? transferId,
    String? deviceId,
    String? deviceName,
    String? fileName,
    String? localFilePath,
    TransferHistoryDirection? direction,
    TransferHistoryStatus? status,
    int? bytesTransferred,
    int? totalBytes,
    DateTime? updatedAt,
    String? error,
  }) => TransferHistoryRecord(
    id: id,
    transferId: transferId ?? this.transferId,
    deviceId: deviceId ?? this.deviceId,
    deviceName: deviceName ?? this.deviceName,
    fileName: fileName ?? this.fileName,
    localFilePath: localFilePath ?? this.localFilePath,
    direction: direction ?? this.direction,
    status: status ?? this.status,
    bytesTransferred: bytesTransferred ?? this.bytesTransferred,
    totalBytes: totalBytes ?? this.totalBytes,
    createdAt: createdAt,
    updatedAt: updatedAt ?? this.updatedAt,
    error: error ?? this.error,
  );

  Map<String, Object?> toJson() => {
    'id': id,
    'transferId': transferId,
    'deviceId': deviceId,
    'deviceName': deviceName,
    'fileName': fileName,
    'localFilePath': localFilePath,
    'direction': direction.name,
    'status': status.name,
    'bytesTransferred': bytesTransferred,
    'totalBytes': totalBytes,
    'createdAt': createdAt.toIso8601String(),
    'updatedAt': updatedAt.toIso8601String(),
    'error': error,
  };

  factory TransferHistoryRecord.fromJson(Map<String, dynamic> json) {
    final now = DateTime.now();
    return TransferHistoryRecord(
      id: '${json['id'] ?? ''}',
      transferId: '${json['transferId'] ?? ''}',
      deviceId: '${json['deviceId'] ?? ''}',
      deviceName: '${json['deviceName'] ?? ''}',
      fileName: '${json['fileName'] ?? ''}',
      localFilePath: '${json['localFilePath'] ?? ''}',
      direction: TransferHistoryDirection.values.firstWhere(
        (value) => value.name == json['direction'],
        orElse: () => TransferHistoryDirection.receive,
      ),
      status: TransferHistoryStatus.values.firstWhere(
        (value) => value.name == json['status'],
        orElse: () => TransferHistoryStatus.failed,
      ),
      bytesTransferred: (json['bytesTransferred'] as num?)?.toInt() ?? 0,
      totalBytes: (json['totalBytes'] as num?)?.toInt() ?? 0,
      createdAt: DateTime.tryParse('${json['createdAt'] ?? ''}') ?? now,
      updatedAt: DateTime.tryParse('${json['updatedAt'] ?? ''}') ?? now,
      error: '${json['error'] ?? ''}',
    );
  }
}

/// Local-only history, separate from the active send queue and received files.
/// Persistence is best effort so a storage issue cannot interrupt transfers.
class TransferHistoryStore extends ChangeNotifier {
  static const int _maxRecords = 100;
  static const int _maxSuppressedTransferIds = 1000;
  final String? _storagePath;
  List<TransferHistoryRecord> _records = <TransferHistoryRecord>[];
  final Set<String> _suppressedTransferIds = <String>{};

  TransferHistoryStore([this._storagePath]) {
    _records = _readRecords();
  }

  List<TransferHistoryRecord> get records => List.unmodifiable(_records);

  void upsert(TransferHistoryRecord record) {
    if (record.id.trim().isEmpty || record.fileName.trim().isEmpty) return;
    // Clearing the local list must not touch the native queue or socket. Keep
    // a tombstone for in-flight IDs so their later progress/completion events
    // do not repopulate the list the user just cleared.
    if (_suppressedTransferIds.contains(record.id)) return;
    final index = _records.indexWhere((item) => item.id == record.id);
    if (index >= 0) {
      _records[index] = record;
    } else {
      _records.add(record);
    }
    _records.sort((a, b) => b.createdAt.compareTo(a.createdAt));
    if (_records.length > _maxRecords) {
      _records = _records.take(_maxRecords).toList(growable: true);
    }
    _saveRecords();
    notifyListeners();
  }

  bool delete(String id) {
    final before = _records.length;
    _records.removeWhere((record) => record.id == id && record.canDelete);
    if (_records.length == before) return false;
    _saveRecords();
    notifyListeners();
    return true;
  }

  int clearAll() {
    final removed = _records.length;
    if (removed == 0) return 0;
    _suppressedTransferIds.addAll(
      _records
          .where((record) => record.isInProgress)
          .map((record) => record.id),
    );
    while (_suppressedTransferIds.length > _maxSuppressedTransferIds) {
      _suppressedTransferIds.remove(_suppressedTransferIds.first);
    }
    _records.clear();
    _saveRecords();
    notifyListeners();
    return removed;
  }

  /// Reconciles persisted UI state against the tasks still owned by Android's
  /// foreground service. A Flutter/activity restart does not necessarily mean
  /// that service died, so only missing tasks are marked interrupted.
  void reconcileAfterStartup(Set<String> activeTransferHistoryIds) {
    final now = DateTime.now();
    var changed = false;
    for (var index = 0; index < _records.length; index++) {
      final record = _records[index];
      final nativeQueueEntry =
          record.status == TransferHistoryStatus.queued &&
          record.id.startsWith('native-');
      if (record.status != TransferHistoryStatus.transferring &&
          !nativeQueueEntry) {
        continue;
      }
      if (activeTransferHistoryIds.contains(record.id)) continue;
      _records[index] = record.copyWith(
        status: TransferHistoryStatus.failed,
        updatedAt: now,
        error: '应用重启后传输已中断，可重新发送',
      );
      changed = true;
    }
    if (changed) {
      _saveRecords();
      notifyListeners();
    }
  }

  List<TransferHistoryRecord> _readRecords() {
    final path = _storagePath?.trim();
    if (path == null || path.isEmpty) return <TransferHistoryRecord>[];
    try {
      final file = File(path);
      if (!file.existsSync()) return <TransferHistoryRecord>[];
      final decoded = jsonDecode(file.readAsStringSync());
      final List<dynamic> rawRecords;
      if (decoded is List) {
        // Keep reading history files written by earlier app versions.
        rawRecords = decoded;
      } else if (decoded is Map && decoded['records'] is List) {
        rawRecords = decoded['records'] as List<dynamic>;
        final suppressed = decoded['suppressedTransferIds'];
        if (suppressed is List) {
          _suppressedTransferIds.addAll(
            suppressed
                .whereType<Object>()
                .map((value) => '$value'.trim())
                .where((value) => value.isNotEmpty),
          );
        }
      } else {
        return <TransferHistoryRecord>[];
      }
      return rawRecords
          .whereType<Map>()
          .map(
            (item) => TransferHistoryRecord.fromJson(
              item.map((key, value) => MapEntry('$key', value)),
            ),
          )
          .where((record) => record.id.isNotEmpty && record.fileName.isNotEmpty)
          .toList()
        ..sort((a, b) => b.createdAt.compareTo(a.createdAt));
    } catch (_) {
      return <TransferHistoryRecord>[];
    }
  }

  void _saveRecords() {
    final path = _storagePath?.trim();
    if (path == null || path.isEmpty) return;
    try {
      final file = File(path);
      file.parent.createSync(recursive: true);
      file.writeAsStringSync(
        const JsonEncoder.withIndent('  ').convert({
          'records': _records.map((record) => record.toJson()).toList(),
          'suppressedTransferIds': _suppressedTransferIds.toList(),
        }),
        flush: true,
      );
    } catch (_) {
      // History is optional; never let a write failure break a transfer.
    }
  }
}
