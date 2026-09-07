/// Sync File Entry Model (Phase 5)
class SyncFileEntry {
  final String relativePath;
  final int size;
  final int modifiedTime;
  final String sha256;
  final String? platformFileIdentity;

  SyncFileEntry({
    required this.relativePath,
    required this.size,
    required this.modifiedTime,
    required this.sha256,
    this.platformFileIdentity,
  });

  Map<String, dynamic> toJson() => {
    'relativePath': relativePath,
    'size': size,
    'modifiedTime': modifiedTime,
    'sha256': sha256,
    if (platformFileIdentity != null)
      'platformFileIdentity': platformFileIdentity,
  };

  factory SyncFileEntry.fromJson(Map<String, dynamic> json) {
    return SyncFileEntry(
      relativePath: json['relativePath'] as String? ?? '',
      size: (json['size'] as num?)?.toInt() ?? 0,
      modifiedTime: (json['modifiedTime'] as num?)?.toInt() ?? 0,
      sha256: json['sha256'] as String? ?? '',
      platformFileIdentity: json['platformFileIdentity'] as String?,
    );
  }
}

/// Sync Manifest Message Model (Phase 5)
class SyncManifestMessage {
  final String folderId;
  final int timestamp;
  final List<SyncFileEntry> entries;

  SyncManifestMessage({
    required this.folderId,
    required this.timestamp,
    required this.entries,
  });

  Map<String, dynamic> toJson() => {
    'folderId': folderId,
    'timestamp': timestamp,
    'entries': entries.map((e) => e.toJson()).toList(),
  };

  factory SyncManifestMessage.fromJson(Map<String, dynamic> json) {
    final rawEntries = json['entries'] as List<dynamic>? ?? [];
    return SyncManifestMessage(
      folderId: json['folderId'] as String? ?? '',
      timestamp: (json['timestamp'] as num?)?.toInt() ?? 0,
      entries: rawEntries
          .map((e) => SyncFileEntry.fromJson(e as Map<String, dynamic>))
          .toList(),
    );
  }
}

/// Sync Pull Request Message Model (Phase 5)
class SyncPullRequestMessage {
  final String folderId;
  final List<String> relativePaths;

  SyncPullRequestMessage({required this.folderId, required this.relativePaths});

  Map<String, dynamic> toJson() => {
    'folderId': folderId,
    'relativePaths': relativePaths,
  };

  factory SyncPullRequestMessage.fromJson(Map<String, dynamic> json) {
    final rawPaths = json['relativePaths'] as List<dynamic>? ?? [];
    return SyncPullRequestMessage(
      folderId: json['folderId'] as String? ?? '',
      relativePaths: rawPaths.map((e) => e.toString()).toList(),
    );
  }
}

/// Computed difference between local and remote manifests
class SyncDifference {
  final List<SyncFileEntry> needPull;
  final List<SyncFileEntry> needPush;
  final List<SyncFileEntry> upToDate;

  SyncDifference({
    required this.needPull,
    required this.needPush,
    required this.upToDate,
  });
}
