enum TransferState {
  idle,
  offering,
  waitingAccept,
  transferring,
  verifying,
  completed,
  failed,
  cancelled,
}

class TextTransferMessage {
  final String transferId;
  final String type; // "text" or "url"
  final String content;
  final int timestamp;

  const TextTransferMessage({
    required this.transferId,
    this.type = 'text',
    required this.content,
    required this.timestamp,
  });

  Map<String, dynamic> toJson() => {
    'transferId': transferId,
    'type': type,
    'content': content,
    'timestamp': timestamp,
  };

  factory TextTransferMessage.fromJson(Map<String, dynamic> json) {
    return TextTransferMessage(
      transferId: json['transferId'] as String,
      type: json['type'] as String? ?? 'text',
      content: json['content'] as String? ?? '',
      timestamp: json['timestamp'] as int? ?? 0,
    );
  }
}

class FileOfferMessage {
  final String transferId;
  final String fileName;
  final int fileSize;
  final String mimeType;
  final int modifiedTime;
  final String sha256;
  final String destinationPath;

  const FileOfferMessage({
    required this.transferId,
    required this.fileName,
    required this.fileSize,
    this.mimeType = 'application/octet-stream',
    required this.modifiedTime,
    required this.sha256,
    this.destinationPath = '',
  });

  Map<String, dynamic> toJson() => {
    'transferId': transferId,
    'fileName': fileName,
    'fileSize': fileSize,
    'mimeType': mimeType,
    'modifiedTime': modifiedTime,
    'sha256': sha256,
    'destinationPath': destinationPath,
  };

  factory FileOfferMessage.fromJson(Map<String, dynamic> json) {
    return FileOfferMessage(
      transferId: json['transferId'] as String,
      fileName: json['fileName'] as String,
      fileSize: json['fileSize'] as int,
      mimeType: json['mimeType'] as String? ?? 'application/octet-stream',
      modifiedTime: json['modifiedTime'] as int? ?? 0,
      sha256: json['sha256'] as String? ?? '',
      destinationPath: json['destinationPath'] as String? ?? '',
    );
  }
}

class FileAcceptMessage {
  final String transferId;
  final bool accepted;
  final int offset;
  final String reason;

  const FileAcceptMessage({
    required this.transferId,
    this.accepted = true,
    this.offset = 0,
    this.reason = '',
  });

  Map<String, dynamic> toJson() => {
    'transferId': transferId,
    'accepted': accepted,
    'offset': offset,
    'reason': reason,
  };

  factory FileAcceptMessage.fromJson(Map<String, dynamic> json) {
    return FileAcceptMessage(
      transferId: json['transferId'] as String,
      accepted: json['accepted'] as bool? ?? true,
      offset: json['offset'] as int? ?? 0,
      reason: json['reason'] as String? ?? '',
    );
  }
}

class FileCompleteMessage {
  final String transferId;
  final String sha256;
  final bool success;
  final String error;

  const FileCompleteMessage({
    required this.transferId,
    required this.sha256,
    this.success = true,
    this.error = '',
  });

  Map<String, dynamic> toJson() => {
    'transferId': transferId,
    'sha256': sha256,
    'success': success,
    'error': error,
  };

  factory FileCompleteMessage.fromJson(Map<String, dynamic> json) {
    return FileCompleteMessage(
      transferId: json['transferId'] as String,
      sha256: json['sha256'] as String? ?? '',
      success: json['success'] as bool? ?? true,
      error: json['error'] as String? ?? '',
    );
  }
}

class TransferProgress {
  final String transferId;
  final String fileName;
  final int bytesTransferred;
  final int totalBytes;
  final TransferState state;

  double get percentage =>
      totalBytes > 0 ? (bytesTransferred / totalBytes) * 100.0 : 0.0;

  const TransferProgress({
    required this.transferId,
    required this.fileName,
    required this.bytesTransferred,
    required this.totalBytes,
    required this.state,
  });
}
