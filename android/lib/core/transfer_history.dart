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
  final String? _storagePath;
  List<TransferHistoryRecord> _records = <TransferHistoryRecord>[];

  TransferHistoryStore([this._storagePath]) {
    _records = _readRecords();
  }

  List<TransferHistoryRecord> get records => List.unmodifiable(_records);

  void upsert(TransferHistoryRecord record) {
    if (record.id.trim().isEmpty || record.fileName.trim().isEmpty) return;
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

  int clearFinished() {
    final before = _records.length;
    _records.removeWhere((record) => record.canDelete);
    final removed = before - _records.length;
    if (removed == 0) return 0;
    _saveRecords();
    notifyListeners();
    return removed;
  }

  List<TransferHistoryRecord> _readRecords() {
    final path = _storagePath?.trim();
    if (path == null || path.isEmpty) return <TransferHistoryRecord>[];
    try {
      final file = File(path);
      if (!file.existsSync()) return <TransferHistoryRecord>[];
      final decoded = jsonDecode(file.readAsStringSync());
      if (decoded is! List) return <TransferHistoryRecord>[];
      return decoded
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
        const JsonEncoder.withIndent('  ')
            .convert(_records.map((record) => record.toJson()).toList()),
        flush: true,
      );
    } catch (_) {
      // History is optional; never let a write failure break a transfer.
    }
  }
}
