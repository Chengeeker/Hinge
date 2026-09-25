import 'package:flutter/material.dart';

import '../core/transfer_history.dart';

class TransferHistoryScreen extends StatelessWidget {
  final TransferHistoryStore store;

  const TransferHistoryScreen({super.key, required this.store});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('文件传输记录'),
        actions: [
          AnimatedBuilder(
            animation: store,
            builder: (context, _) => IconButton(
              tooltip: '清空已完成记录',
              onPressed: store.records.any((record) => record.canDelete)
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
            itemBuilder: (context, index) => _TransferHistoryTile(
              record: records[index],
              onDelete: records[index].canDelete
                  ? () => store.delete(records[index].id)
                  : null,
            ),
          );
        },
      ),
    );
  }

  Future<void> _confirmClear(BuildContext context) async {
    final finishedCount = store.records
        .where((record) => record.canDelete)
        .length;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清空传输记录？'),
        content: Text(
          '将删除 $finishedCount 条已结束记录，包括完成、失败或等待接收的云中转记录。只删除本机记录，不会取消传输；进行中的任务会保留。',
        ),
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
    if (confirmed == true) store.clearFinished();
  }
}

class _TransferHistoryTile extends StatelessWidget {
  final TransferHistoryRecord record;
  final VoidCallback? onDelete;

  const _TransferHistoryTile({required this.record, this.onDelete});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final active = record.isInProgress;
    return Card(
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
