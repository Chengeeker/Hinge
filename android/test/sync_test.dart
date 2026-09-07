import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/sync_indexer.dart';
import 'package:hinge/core/sync_model.dart';

void main() {
  group('Sync & Backup Core Tests', () {
    test('SyncIndexer generateManifest scans files and subdirectories', () async {
      final tempDir =
          '${Directory.systemTemp.path}/sync_test_${DateTime.now().millisecondsSinceEpoch}';
      final dir = Directory(tempDir);
      dir.createSync(recursive: true);

      try {
        final rootFile = File('$tempDir/root.txt');
        rootFile.writeAsStringSync('Root file content');

        final subDir = Directory('$tempDir/sub');
        subDir.createSync(recursive: true);
        final subFile = File('$tempDir/sub/child.bin');
        subFile.writeAsBytesSync([10, 20, 30, 40, 50]);

        final manifestWithHash = await SyncIndexer.generateManifest(
          tempDir,
          'vault-1',
          true,
        );

        expect(manifestWithHash.folderId, equals('vault-1'));
        expect(manifestWithHash.entries.length, equals(2));

        final rootEntry = manifestWithHash.entries.firstWhere(
          (e) => e.relativePath == 'root.txt',
        );
        expect(rootEntry.size, greaterThan(0));
        expect(rootEntry.sha256.length, equals(64));

        final subEntry = manifestWithHash.entries.firstWhere(
          (e) => e.relativePath == 'sub/child.bin',
        );
        expect(subEntry.size, equals(5));
        expect(subEntry.sha256.length, equals(64));

        // Test fast zero-IO metadata scan
        final fastManifest = await SyncIndexer.generateManifest(
          tempDir,
          'vault-1',
          false,
        );
        expect(fastManifest.entries.length, equals(2));
        expect(fastManifest.entries[0].sha256, isEmpty);
        expect(fastManifest.entries[1].sha256, isEmpty);
      } finally {
        if (dir.existsSync()) {
          try {
            dir.deleteSync(recursive: true);
          } catch (_) {}
        }
      }
    });

    test('SyncIndexer calculateDifference correctly sorts needPull, needPush, upToDate', () {
      final localManifest = SyncManifestMessage(
        folderId: 'folder-1',
        timestamp: 1000,
        entries: [
          // Identical
          SyncFileEntry(
            relativePath: 'common.txt',
            size: 100,
            modifiedTime: 1000,
            sha256: 'hash_same',
          ),
          // Local is newer
          SyncFileEntry(
            relativePath: 'local_newer.txt',
            size: 200,
            modifiedTime: 2000,
            sha256: 'hash_local_2',
          ),
          // Local only
          SyncFileEntry(
            relativePath: 'local_only.txt',
            size: 50,
            modifiedTime: 1000,
            sha256: 'hash_local_only',
          ),
          // Remote newer (older on local)
          SyncFileEntry(
            relativePath: 'remote_newer.txt',
            size: 100,
            modifiedTime: 1200,
            sha256: 'hash_local_old',
          ),
        ],
      );

      final remoteManifest = SyncManifestMessage(
        folderId: 'folder-1',
        timestamp: 1000,
        entries: [
          // Identical
          SyncFileEntry(
            relativePath: 'common.txt',
            size: 100,
            modifiedTime: 1000,
            sha256: 'hash_same',
          ),
          // Remote is older
          SyncFileEntry(
            relativePath: 'local_newer.txt',
            size: 180,
            modifiedTime: 1500,
            sha256: 'hash_remote_old',
          ),
          // Remote is newer
          SyncFileEntry(
            relativePath: 'remote_newer.txt',
            size: 300,
            modifiedTime: 3000,
            sha256: 'hash_remote_new',
          ),
          // Remote only
          SyncFileEntry(
            relativePath: 'remote_only.txt',
            size: 80,
            modifiedTime: 1000,
            sha256: 'hash_remote_only',
          ),
        ],
      );

      final diff = SyncIndexer.calculateDifference(
        localManifest,
        remoteManifest,
      );

      // UpToDate: common.txt
      expect(diff.upToDate.length, equals(1));
      expect(diff.upToDate[0].relativePath, equals('common.txt'));

      // NeedPull: remote_newer.txt and remote_only.txt
      expect(diff.needPull.length, equals(2));
      expect(
        diff.needPull.any((e) => e.relativePath == 'remote_newer.txt'),
        isTrue,
      );
      expect(
        diff.needPull.any((e) => e.relativePath == 'remote_only.txt'),
        isTrue,
      );

      // NeedPush: local_newer.txt and local_only.txt
      expect(diff.needPush.length, equals(2));
      expect(
        diff.needPush.any((e) => e.relativePath == 'local_newer.txt'),
        isTrue,
      );
      expect(
        diff.needPush.any((e) => e.relativePath == 'local_only.txt'),
        isTrue,
      );
    });

    test('SyncManifestMessage JSON serialization roundtrip', () {
      final manifest = SyncManifestMessage(
        folderId: 'sync-vault',
        timestamp: 1788500000000,
        entries: [
          SyncFileEntry(
            relativePath: 'docs/spec.md',
            size: 2048,
            modifiedTime: 1788499000000,
            sha256: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
          ),
        ],
      );

      final json = manifest.toJson();
      final parsed = SyncManifestMessage.fromJson(json);

      expect(parsed.folderId, equals(manifest.folderId));
      expect(parsed.timestamp, equals(manifest.timestamp));
      expect(parsed.entries.length, equals(1));
      expect(parsed.entries[0].relativePath, equals('docs/spec.md'));
      expect(parsed.entries[0].size, equals(2048));
      expect(parsed.entries[0].sha256, equals(manifest.entries[0].sha256));
    });
  });
}
