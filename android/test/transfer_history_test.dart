import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/transfer_history_screen.dart';
import 'package:hinge/core/transfer_history.dart';

TransferHistoryRecord record({
  String id = 'transfer-1',
  TransferHistoryStatus status = TransferHistoryStatus.completed,
}) {
  final now = DateTime.utc(2026, 9, 25, 12);
  return TransferHistoryRecord(
    id: id,
    transferId: id,
    deviceName: 'Test PC',
    fileName: 'setup.exe',
    direction: TransferHistoryDirection.send,
    status: status,
    bytesTransferred: 30 * 1024 * 1024,
    totalBytes: 30 * 1024 * 1024,
    createdAt: now,
    updatedAt: now,
  );
}

void main() {
  test('persists history and keeps unfinished records when clearing', () {
    final directory = Directory.systemTemp.createTempSync(
      'hinge-history-test-',
    );
    final path = '${directory.path}${Platform.pathSeparator}history.json';
    final store = TransferHistoryStore(path);
    store.upsert(record());
    store.upsert(
      record(id: 'active-1', status: TransferHistoryStatus.transferring),
    );
    store.dispose();

    final restored = TransferHistoryStore(path);
    expect(restored.records, hasLength(2));
    expect(restored.clearFinished(), 1);
    expect(restored.records.single.id, 'active-1');
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
}
