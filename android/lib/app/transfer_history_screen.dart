import 'package:flutter/material.dart';

import '../core/transfer_history.dart';

class TransferHistoryScreen extends StatelessWidget {
  final TransferHistoryStore store;
  final Future<String> Function(String path)? openFile;

  const TransferHistoryScreen({super.key, required this.store, this.openFile});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('文件传输记录'),
        actions: [
          AnimatedBuilder(
            animation: store,
            builder: (context, _) => IconButton(
              tooltip: '清空本机记录',
              onPressed: store.records.isNotEmpty
                  ? () => _confirmClear(context)
                  : null,
              icon: const Icon(Icons.delete_sweep_outlined),
            ),
          ),
        ],
      ),
      body: AnimatedBuilder(
        animation: store,
        builder: (context, _) {
          final records = store.records;
          if (records.isEmpty) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(
                      Icons.history_rounded,
                      size: 56,
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                    const SizedBox(height: 12),
                    Text(
                      '暂无文件传输记录',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 6),
                    Text(
                      '通过局域网或 Cloud Relay 收发文件后，记录会显示在这里。',
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            );
          }
          return ListView.separated(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
            itemCount: records.length,
            separatorBuilder: (_, _) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final record = records[index];
              final canLocateFile =
                  record.localFilePath.isNotEmpty ||
                  (record.direction == TransferHistoryDirection.receive &&
                      record.status == TransferHistoryStatus.completed);
              return _TransferHistoryTile(
                record: record,
                onOpen: openFile != null && canLocateFile
                    ? () => _openRecord(context, record)
                    : null,
                onDelete: record.canDelete
                    ? () => store.delete(record.id)
                    : null,
              );
            },
          );
        },
      ),
    );
  }

  Future<void> _confirmClear(BuildContext context) async {
    final recordCount = store.records.length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清空传输记录？'),
        content: Text('将从本机列表中移除 $recordCount 条记录。此操作不会取消正在进行的传输，也不会删除已接收的文件。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('清空记录'),
          ),
        ],
      ),
    );
    if (confirmed == true) store.clearAll();
  }

  Future<void> _openRecord(
    BuildContext context,
    TransferHistoryRecord record,
  ) async {
    final path = record.localFilePath.trim();
    if (path.isEmpty) {
      _showMessage(context, '这是旧记录，未保存本机文件位置，无法直接打开。');
      return;
    }
    final opener = openFile;
    if (opener == null) return;

    String result;
    try {
      result = await opener(path);
    } catch (_) {
      result = 'failed';
    }
    if (!context.mounted) return;
    switch (result) {
      case 'opened':
        return;
      case 'missing':
        _showMessage(context, '文件已不存在或已被删除。');
      default:
        _showMessage(context, '无法打开文件，请检查“默认应用”设置或文件访问权限。');
    }
  }

  void _showMessage(BuildContext context, String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }
}

class _TransferHistoryTile extends StatelessWidget {
  final TransferHistoryRecord record;
  final VoidCallback? onOpen;
  final VoidCallback? onDelete;

  const _TransferHistoryTile({
    required this.record,
    this.onOpen,
    this.onDelete,
  });

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final active = record.isInProgress;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onOpen,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    record.direction == TransferHistoryDirection.send
                        ? Icons.upload_rounded
                        : Icons.download_rounded,
                    color: scheme.primary,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      record.fileName,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  if (onOpen != null)
                    Icon(
                      Icons.open_in_new_rounded,
                      size: 18,
                      color: scheme.onSurfaceVariant,
                    ),
                  if (onDelete != null)
                    IconButton(
                      tooltip: '删除记录',
                      visualDensity: VisualDensity.compact,
                      onPressed: onDelete,
                      icon: const Icon(Icons.delete_outline_rounded),
                    ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                [
                  record.directionText,
                  if (record.deviceName.isNotEmpty) record.deviceName,
                  record.statusText,
                  _formatTime(record.updatedAt),
                ].join(' · '),
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: record.status == TransferHistoryStatus.failed
                      ? scheme.error
                      : active
                      ? scheme.primary
                      : scheme.onSurfaceVariant,
                ),
              ),
              if (record.status == TransferHistoryStatus.transferring &&
                  record.totalBytes > 0) ...[
                const SizedBox(height: 12),
                LinearProgressIndicator(value: record.progress),
                const SizedBox(height: 4),
                Align(
                  alignment: Alignment.centerRight,
                  child: Text(
                    '${_formatBytes(record.bytesTransferred)} / ${_formatBytes(record.totalBytes)}',
                    style: Theme.of(context).textTheme.labelSmall,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  static String _formatTime(DateTime value) {
    final local = value.toLocal();
    final month = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    return '$month-$day $hour:$minute';
  }

  static String _formatBytes(int value) {
    if (value < 1024) return '$value B';
    if (value < 1024 * 1024) return '${(value / 1024).toStringAsFixed(1)} KB';
    if (value < 1024 * 1024 * 1024) {
      return '${(value / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(value / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }
}
