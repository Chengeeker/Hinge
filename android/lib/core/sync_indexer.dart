import 'dart:io';

import 'package:crypto/crypto.dart';

import 'sync_model.dart';

class SyncIndexer {
  static Future<SyncManifestMessage> generateManifest(
    String rootPath, [
    String folderId = 'default',
    bool computeHashOnDemand = false,
  ]) async {
    final manifest = SyncManifestMessage(
      folderId: folderId,
      timestamp: DateTime.now().millisecondsSinceEpoch,
      entries: [],
    );

    final dir = Directory(rootPath);
    if (!dir.existsSync()) {
      return manifest;
    }

    final normalizedRoot = rootPath
        .replaceAll('\\', '/')
        .replaceAll(RegExp(r'/+$'), '');
    final entities = dir.listSync(recursive: true, followLinks: false);

    for (final entity in entities) {
      if (entity is File) {
        final path = entity.path.replaceAll('\\', '/');
        final relative = path.startsWith('$normalizedRoot/')
            ? path.substring(normalizedRoot.length + 1)
            : path;

        final stat = entity.statSync();
        String hash = '';
        if (computeHashOnDemand) {
          final bytes = await entity.readAsBytes();
          hash = sha256.convert(bytes).toString().toLowerCase();
        }

        manifest.entries.add(
          SyncFileEntry(
            relativePath: relative,
            size: stat.size,
            modifiedTime: stat.modified.millisecondsSinceEpoch,
            sha256: hash,
          ),
        );
      }
    }

    return manifest;
  }

  static SyncDifference calculateDifference(
    SyncManifestMessage localManifest,
    SyncManifestMessage remoteManifest,
  ) {
    final needPull = <SyncFileEntry>[];
    final needPush = <SyncFileEntry>[];
    final upToDate = <SyncFileEntry>[];

    final localMap = {
      for (final e in localManifest.entries) e.relativePath.toLowerCase(): e,
    };
    final remoteMap = {
      for (final e in remoteManifest.entries) e.relativePath.toLowerCase(): e,
    };

    // Check remote entries against local
    for (final entry in remoteManifest.entries) {
      final key = entry.relativePath.toLowerCase();
      final local = localMap[key];

      if (local == null) {
        // Remote has it, local does not -> need pull
        needPull.add(entry);
      } else {
        // Tier 1: Fast metadata match (Size and MTime within 1s tolerance)
        final metadataMatch =
            local.size == entry.size &&
            (local.modifiedTime - entry.modifiedTime).abs() < 1000;

        // Tier 2: Hash match if both hashes are non-empty
        final hashMatch =
            local.sha256.isNotEmpty &&
            entry.sha256.isNotEmpty &&
            local.sha256.toLowerCase() == entry.sha256.toLowerCase() &&
            local.size == entry.size;

        if (metadataMatch || hashMatch) {
          upToDate.add(local);
        } else {
          // Last-Write-Wins conflict arbitration
          if (entry.modifiedTime > local.modifiedTime) {
            needPull.add(entry);
          } else if (local.modifiedTime > entry.modifiedTime) {
            needPush.add(local);
          } else {
            // Deterministic tie-break by SHA-256
            if (entry.sha256.compareTo(local.sha256) > 0) {
              needPull.add(entry);
            } else {
              needPush.add(local);
            }
          }
        }
      }
    }

    // Check local entries not in remote -> need push
    for (final local in localManifest.entries) {
      final key = local.relativePath.toLowerCase();
      if (!remoteMap.containsKey(key)) {
        needPush.add(local);
      }
    }

    return SyncDifference(
      needPull: needPull,
      needPush: needPush,
      upToDate: upToDate,
    );
  }
}
