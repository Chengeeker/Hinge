import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/transfer_history_screen.dart';
import 'package:hinge/core/transfer_history.dart';

TransferHistoryRecord record({
  String id = 'transfer-1',
  TransferHistoryStatus status = TransferHistoryStatus.completed,
  TransferHistoryDirection direction = TransferHistoryDirection.send,
  String localFilePath = '',
}) {
  final now = DateTime.utc(2026, 9, 25, 12);
  return TransferHistoryRecord(
    id: id,
    transferId: id,
    deviceName: 'Test PC',
    fileName: 'setup.exe',
    localFilePath: localFilePath,
    direction: direction,
    status: status,
    bytesTransferred: 30 * 1024 * 1024,
    totalBytes: 30 * 1024 * 1024,
    createdAt: now,
    updatedAt: now,
  );
}

void main() {
  test(
    'clearing all rows suppresses later events without cancelling tasks',
    () {
      final directory = Directory.systemTemp.createTempSync(
        'hinge-history-test-',
      );
      final path = '${directory.path}${Platform.pathSeparator}history.json';
      final store = TransferHistoryStore(path);
      final active = record(
        id: 'lan-active-1',
        status: TransferHistoryStatus.transferring,
      );
      store.upsert(record());
      store.upsert(active);
      expect(store.clearAll(), 2);
      expect(store.records, isEmpty);
      store.dispose();

      final restored = TransferHistoryStore(path);
      expect(restored.records, isEmpty);
      restored.upsert(
        active.copyWith(
          bytesTransferred: 20 * 1024 * 1024,
          updatedAt: DateTime.now(),
        ),
      );
      restored.upsert(
        active.copyWith(
          status: TransferHistoryStatus.completed,
          updatedAt: DateTime.now(),
        ),
      );
      expect(restored.records, isEmpty);
      restored.upsert(record(id: 'new-transfer'));
      expect(restored.records.single.id, 'new-transfer');
      restored.dispose();
      directory.deleteSync(recursive: true);
    },
  );

  test(
    'startup reconciliation marks stale work interrupted, keeps live work',
    () {
      final store = TransferHistoryStore();
      store.upsert(
        record(id: 'lan-stale', status: TransferHistoryStatus.transferring),
      );
      store.upsert(
        record(id: 'lan-live', status: TransferHistoryStatus.transferring),
      );
      store.upsert(
        record(id: 'native-queued', status: TransferHistoryStatus.queued),
      );
      store.upsert(
        record(id: 'native-missing', status: TransferHistoryStatus.queued),
      );

      store.reconcileAfterStartup({'lan-live', 'native-queued'});

      final byId = {for (final item in store.records) item.id: item};
      expect(byId['lan-stale']!.status, TransferHistoryStatus.failed);
      expect(byId['lan-stale']!.error, contains('应用重启后'));
      expect(byId['lan-live']!.status, TransferHistoryStatus.transferring);
      expect(byId['native-queued']!.status, TransferHistoryStatus.queued);
      expect(byId['native-missing']!.status, TransferHistoryStatus.failed);
      store.dispose();
    },
  );

  test('reads history stored in the legacy JSON array format', () {
    final directory = Directory.systemTemp.createTempSync(
      'hinge-history-legacy-test-',
    );
    final path = '${directory.path}${Platform.pathSeparator}history.json';
    File(path).writeAsStringSync(jsonEncode([record().toJson()]));

    final store = TransferHistoryStore(path);
    expect(store.records, hasLength(1));
    expect(store.records.single.id, 'transfer-1');
    expect(store.records.single.localFilePath, isEmpty);
    store.dispose();
    directory.deleteSync(recursive: true);
  });

  test('persists the local file path and keeps it through copyWith', () {
    final directory = Directory.systemTemp.createTempSync(
      'hinge-history-path-test-',
    );
    final path = '${directory.path}${Platform.pathSeparator}history.json';
    final stored = record(
      direction: TransferHistoryDirection.receive,
      localFilePath: '/storage/emulated/0/Download/Hinge/setup.exe',
    );
    final store = TransferHistoryStore(path);
    store.upsert(stored);
    store.dispose();

    final restored = TransferHistoryStore(path);
    expect(restored.records.single.localFilePath, stored.localFilePath);
    expect(
      restored.records.single
          .copyWith(status: TransferHistoryStatus.failed)
          .localFilePath,
      stored.localFilePath,
    );
    restored.dispose();
    directory.deleteSync(recursive: true);
  });

  testWidgets(
    'shows a record with progress and allows deleting finished rows',
    (tester) async {
      final store = TransferHistoryStore();
      store.upsert(
        record(id: 'active-1', status: TransferHistoryStatus.transferring),
      );
      store.upsert(record());
      await tester.pumpWidget(
        MaterialApp(home: TransferHistoryScreen(store: store)),
      );

      expect(find.text('setup.exe'), findsNWidgets(2));
      expect(find.textContaining('正在发送 · 100%'), findsOneWidget);
      expect(find.byTooltip('清空本机记录'), findsOneWidget);
      expect(find.byTooltip('删除记录'), findsOneWidget);

      await tester.tap(find.byTooltip('删除记录'));
      await tester.pump();
      expect(store.records.map((item) => item.id), contains('active-1'));
      expect(
        store.records.map((item) => item.id),
        isNot(contains('transfer-1')),
      );
      store.dispose();
    },
  );

  testWidgets('tapping a received record opens its saved local file', (
    tester,
  ) async {
    final store = TransferHistoryStore();
    const filePath = '/storage/emulated/0/Download/Hinge/setup.apk';
    store.upsert(
      record(
        direction: TransferHistoryDirection.receive,
        localFilePath: filePath,
      ),
    );
    String? openedPath;
    await tester.pumpWidget(
      MaterialApp(
        home: TransferHistoryScreen(
          store: store,
          openFile: (path) async {
            openedPath = path;
            return 'opened';
          },
        ),
      ),
    );

    await tester.tap(find.text('setup.exe'));
    await tester.pumpAndSettle();
    expect(openedPath, filePath);
    store.dispose();
  });

  testWidgets('shows a clear message when a saved file has been deleted', (
    tester,
  ) async {
    final store = TransferHistoryStore();
    store.upsert(
      record(
        direction: TransferHistoryDirection.receive,
        localFilePath: '/storage/emulated/0/Download/Hinge/deleted.apk',
      ),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: TransferHistoryScreen(
          store: store,
          openFile: (_) async => 'missing',
        ),
      ),
    );

    await tester.tap(find.text('setup.exe'));
    await tester.pumpAndSettle();
    expect(find.text('文件已不存在或已被删除。'), findsOneWidget);
    store.dispose();
  });
}
