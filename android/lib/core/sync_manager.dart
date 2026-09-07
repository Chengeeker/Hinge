import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'directory_watcher.dart';
import 'protocol_frame.dart';
import 'session_manager.dart';
import 'sync_indexer.dart';
import 'sync_model.dart';
import 'transfer_manager.dart';

class SyncManager {
  final String syncFolderPath;
  final TransferManager transferManager;
  final DirectoryWatcher? watcher;

  final StreamController<SyncDifference> _diffController =
      StreamController<SyncDifference>.broadcast();
  final StreamController<String> _syncCompletedController =
      StreamController<String>.broadcast();

  Stream<SyncDifference> get diffStream => _diffController.stream;
  Stream<String> get syncCompletedStream => _syncCompletedController.stream;

  StreamSubscription<String>? _watcherSub;

  SyncManager({
    required this.syncFolderPath,
    required this.transferManager,
    this.watcher,
  }) {
    final dir = Directory(syncFolderPath);
    if (!dir.existsSync()) {
      dir.createSync(recursive: true);
    }

    if (watcher != null) {
      _watcherSub = watcher!.changeStream.listen(_onDirectoryChanged);
      watcher!.start();
    }
  }

  void _onDirectoryChanged(String path) {
    // Directory change detected
  }

  Future<void> requestManifest(
    SessionConnection conn, [
    String folderId = 'default',
  ]) async {
    final req = SyncManifestMessage(
      folderId: folderId,
      timestamp: DateTime.now().millisecondsSinceEpoch,
      entries: [],
    );
    conn.sendJson(MessageType.syncManifestRequest, req.toJson());
  }

  Future<void> sendManifest(
    SessionConnection conn, [
    String folderId = 'default',
  ]) async {
    final manifest = await SyncIndexer.generateManifest(
      syncFolderPath,
      folderId,
    );
    conn.sendJson(MessageType.syncManifestResponse, manifest.toJson());
  }

  Future<SyncDifference?> handleIncomingFrame(
    SessionConnection conn,
    ProtocolFrame frame,
  ) async {
    switch (frame.type) {
      case MessageType.syncManifestRequest:
        try {
          final json =
              jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
          final req = SyncManifestMessage.fromJson(json);
          await sendManifest(
            conn,
            req.folderId.isEmpty ? 'default' : req.folderId,
          );
        } catch (_) {}
        return null;

      case MessageType.syncManifestResponse:
        try {
          final json =
              jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
          final remoteManifest = SyncManifestMessage.fromJson(json);

          final localManifest = await SyncIndexer.generateManifest(
            syncFolderPath,
            remoteManifest.folderId,
          );
          final diff = SyncIndexer.calculateDifference(
            localManifest,
            remoteManifest,
          );

          _diffController.add(diff);
          _syncCompletedController.add(remoteManifest.folderId);
          return diff;
        } catch (_) {
          return null;
        }

      case MessageType.syncPullRequest:
        try {
          final json =
              jsonDecode(utf8.decode(frame.payload)) as Map<String, dynamic>;
          final pullReq = SyncPullRequestMessage.fromJson(json);

          for (final rel in pullReq.relativePaths) {
            final localPath = '$syncFolderPath/${rel.replaceAll('\\', '/')}';
            final file = File(localPath);
            if (file.existsSync()) {
              await transferManager.sendFile(conn, localPath);
            }
          }
        } catch (_) {}
        return null;

      default:
        return null;
    }
  }

  void dispose() {
    _watcherSub?.cancel();
    _diffController.close();
    _syncCompletedController.close();
    watcher?.dispose();
  }
}
