import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';

import 'protocol_frame.dart';
import 'session_manager.dart';
import 'transfer_model.dart';

class _IncomingFileContext {
  final FileOfferMessage offer;
  final String tempFilePath;
  final String finalFilePath;
  RandomAccessFile? file;
  int bytesReceived;
  Future<void> writeQueue = Future.value();

  _IncomingFileContext({
    required this.offer,
    required this.tempFilePath,
    required this.finalFilePath,
    required this.file,
    required this.bytesReceived,
  });
}

class TransferManager {
  static const int _fileChunkSize = 512 * 1024;
  final String _defaultDirectory;
  String _downloadDirectory;
  String _imageDirectory;
  String _videoDirectory;
  final Map<String, _IncomingFileContext> _incomingTransfers = {};

  Future<String?> Function(FileOfferMessage offer)? chooseReceiveDirectory;

  final StreamController<TextTransferMessage> _textController =
      StreamController<TextTransferMessage>.broadcast();
  final StreamController<TransferProgress> _progressController =
      StreamController<TransferProgress>.broadcast();
  final StreamController<String> _fileReceivedController =
      StreamController<String>.broadcast();

  Stream<TextTransferMessage> get textStream => _textController.stream;
  Stream<TransferProgress> get progressStream => _progressController.stream;
  Stream<String> get fileReceivedStream => _fileReceivedController.stream;

  TransferManager([String? downloadDirectory])
    : _defaultDirectory = downloadDirectory ?? _getDefaultDownloadDir(),
      _downloadDirectory = downloadDirectory ?? _getDefaultDownloadDir(),
      _imageDirectory = downloadDirectory ?? _getDefaultDownloadDir(),
      _videoDirectory = downloadDirectory ?? _getDefaultDownloadDir() {
    _ensureDirectory(_downloadDirectory);
    _ensureDirectory(_imageDirectory);
    _ensureDirectory(_videoDirectory);
  }

  String get downloadDirectory => _downloadDirectory;
  String get imageDirectory => _imageDirectory;
  String get videoDirectory => _videoDirectory;

  void configureStorage({
    String? imagePath,
    String? videoPath,
    String? filePath,
  }) {
    _imageDirectory = _pathOrDefault(imagePath, _defaultDirectory);
    _videoDirectory = _pathOrDefault(videoPath, _defaultDirectory);
    _downloadDirectory = _pathOrDefault(filePath, _defaultDirectory);
    _ensureDirectory(_imageDirectory);
    _ensureDirectory(_videoDirectory);
    _ensureDirectory(_downloadDirectory);
  }

  static String _getDefaultDownloadDir() {
    final home = Platform.isWindows
        ? (Platform.environment['LOCALAPPDATA'] ??
              Platform.environment['APPDATA'] ??
              Directory.systemTemp.path)
        : (Platform.environment['HOME'] ?? Directory.systemTemp.path);
    return Platform.isWindows
        ? '$home/Hinge/downloads'
        : '$home/.hinge/downloads';
  }

  Future<void> sendText(
    SessionConnection conn,
    String content, [
    String type = 'text',
  ]) async {
    final msg = TextTransferMessage(
      transferId: _generateUuid(),
      type: type,
      content: content,
      timestamp: DateTime.now().millisecondsSinceEpoch ~/ 1000,
    );
    conn.sendJson(MessageType.textMessage, msg.toJson());
  }

  Future<String> sendFile(
    SessionConnection conn,
    String filePath, {
    String? mimeType,
    String? fileNameOverride,
    void Function(TransferProgress)? onProgress,
    bool precomputeHash = true,
  }) async {
    final file = File(filePath);
    if (!file.existsSync()) {
      throw FileSystemException('File not found', filePath);
    }

    final fileName = fileNameOverride ?? file.uri.pathSegments.last;
    final fileSize = file.lengthSync();
    final transferId = _generateUuid();

    // Compute SHA-256 incrementally so opening a large video does not load the
    // whole file into the Dart heap or destabilize the session connection.
    var sha256Hash = precomputeHash
        ? (await sha256.bind(file.openRead()).first).toString()
        : '';

    final offer = FileOfferMessage(
      transferId: transferId,
      fileName: fileName,
      fileSize: fileSize,
      mimeType: mimeType ?? _mimeTypeForFileName(fileName),
      modifiedTime: file.lastModifiedSync().millisecondsSinceEpoch ~/ 1000,
      sha256: sha256Hash,
    );

    final completer = Completer<FileAcceptMessage>();
    late final StreamSubscription<ProtocolFrame> sub;

    sub = conn.frames.listen((frame) {
      if (frame.type == MessageType.fileAccept) {
        final text = utf8.decode(frame.payload);
        final accept = FileAcceptMessage.fromJson(
          jsonDecode(text) as Map<String, dynamic>,
        );
        if (accept.transferId == transferId && !completer.isCompleted) {
          completer.complete(accept);
        }
      } else if (frame.type == MessageType.fileReject) {
        if (!completer.isCompleted) {
          completer.completeError(Exception('File transfer rejected by peer'));
        }
      }
    });

    try {
      conn.sendJson(MessageType.fileOffer, offer.toJson());
      final accept = await completer.future.timeout(
        const Duration(seconds: 30),
      );
      if (!accept.accepted) {
        throw Exception('Peer rejected file transfer: ${accept.reason}');
      }

      // Send chunks starting from accept.offset. Media previews skip the
      // initial full-file hash scan and calculate the digest while streaming.
      int offset = accept.offset;
      const chunkSize = _fileChunkSize;
      final raf = await file.open(mode: FileMode.read);

      if (sha256Hash.isEmpty && offset > 0) {
        sha256Hash = (await sha256.bind(file.openRead()).first).toString();
      }
      final digestController = sha256Hash.isEmpty
          ? StreamController<Digest>(sync: true)
          : null;
      final digestFuture = digestController?.stream.first;
      final hashSink = digestController == null
          ? null
          : sha256.startChunkedConversion(digestController.sink);

      if (offset > 0 && offset < fileSize) {
        await raf.setPosition(offset);
      }

      int chunkIndex = 0;
      int bytesSent = offset;
      final transferIdBytes = uuidToBytes(transferId);

      while (bytesSent < fileSize) {
        final chunkData = await raf.read(chunkSize);
        if (chunkData.isEmpty) break;

        final payload = Uint8List(28 + chunkData.length);
        final byteData = ByteData.sublistView(payload);

        payload.setRange(0, 16, transferIdBytes);
        byteData.setUint32(16, chunkIndex, Endian.big);
        byteData.setInt64(20, bytesSent, Endian.big);
        payload.setRange(28, payload.length, chunkData);

        hashSink?.add(chunkData);

        conn.sendFrame(MessageType.fileChunk, payload);

        bytesSent += chunkData.length;
        chunkIndex++;

        final prog = TransferProgress(
          transferId: transferId,
          fileName: fileName,
          bytesTransferred: bytesSent,
          totalBytes: fileSize,
          state: TransferState.transferring,
        );
        onProgress?.call(prog);
        _progressController.add(prog);
      }

      await raf.close();

      if (hashSink != null) {
        hashSink.close();
        sha256Hash = (await digestFuture!).toString();
        await digestController!.close();
      }

      // Send FILE_COMPLETE
      final completeMsg = FileCompleteMessage(
        transferId: transferId,
        sha256: sha256Hash,
        success: true,
      );
      conn.sendJson(MessageType.fileComplete, completeMsg.toJson());

      final finalProg = TransferProgress(
        transferId: transferId,
        fileName: fileName,
        bytesTransferred: fileSize,
        totalBytes: fileSize,
        state: TransferState.completed,
      );
      onProgress?.call(finalProg);
      _progressController.add(finalProg);

      return transferId;
    } finally {
      await sub.cancel();
    }
  }

  Future<String> sendBytes(
    SessionConnection conn, {
    required String fileName,
    required Uint8List bytes,
    String? mimeType,
    void Function(TransferProgress)? onProgress,
  }) async {
    final safeName = (fileName.trim().isEmpty ? '图片' : fileName.trim())
        .split(Platform.pathSeparator)
        .last
        .split('/')
        .last;
    final tempPath =
        '${Directory.systemTemp.path}${Platform.pathSeparator}'
        'hinge_${_generateUuid()}_$safeName';
    final tempFile = File(tempPath);
    await tempFile.writeAsBytes(bytes, flush: true);
    try {
      return await sendFile(
        conn,
        tempPath,
        mimeType: mimeType,
        fileNameOverride: safeName,
        onProgress: onProgress,
      );
    } finally {
      try {
        if (tempFile.existsSync()) tempFile.deleteSync();
      } catch (_) {}
    }
  }

  Future<void> handleIncomingFrame(
    SessionConnection conn,
    ProtocolFrame frame,
  ) async {
    if (frame.type == MessageType.textMessage) {
      final text = utf8.decode(frame.payload);
      final msg = TextTransferMessage.fromJson(
        jsonDecode(text) as Map<String, dynamic>,
      );
      _textController.add(msg);
    } else if (frame.type == MessageType.fileOffer) {
      final text = utf8.decode(frame.payload);
      final offer = FileOfferMessage.fromJson(
        jsonDecode(text) as Map<String, dynamic>,
      );
      await _handleFileOffer(conn, offer);
    } else if (frame.type == MessageType.fileChunk) {
      await _handleFileChunk(frame.payload);
    } else if (frame.type == MessageType.fileComplete) {
      final text = utf8.decode(frame.payload);
      final complete = FileCompleteMessage.fromJson(
        jsonDecode(text) as Map<String, dynamic>,
      );
      await _handleFileComplete(conn, complete);
    }
  }

  Future<void> _handleFileOffer(
    SessionConnection conn,
    FileOfferMessage offer,
  ) async {
    // Sanitize filename to prevent path traversal
    final safeName = offer.fileName
        .split(Platform.pathSeparator)
        .last
        .split('/')
        .last;
    var directory = _directoryForOffer(offer);
    if (chooseReceiveDirectory != null && Platform.isWindows) {
      directory = (await chooseReceiveDirectory!(offer))?.trim() ?? '';
      if (directory.isEmpty) {
        conn.sendJson(MessageType.fileReject, {
          'transferId': offer.transferId,
          'reason': '用户取消了保存位置选择',
        });
        return;
      }
    }
    try {
      await Directory(directory).create(recursive: true);
    } catch (error) {
      conn.sendJson(MessageType.fileReject, {
        'transferId': offer.transferId,
        'reason': '保存目录不可用：$error',
      });
      return;
    }
    final finalPath = '$directory${Platform.pathSeparator}$safeName';
    final tempPath = '$finalPath.part';

    final tempFile = File(tempPath);
    int existingBytes = 0;
    if (tempFile.existsSync()) {
      existingBytes = tempFile.lengthSync();
      if (existingBytes > offer.fileSize) {
        tempFile.deleteSync();
        existingBytes = 0;
      }
    }

    final raf = await tempFile.open(mode: FileMode.append);
    _incomingTransfers[offer.transferId] = _IncomingFileContext(
      offer: offer,
      tempFilePath: tempPath,
      finalFilePath: finalPath,
      file: raf,
      bytesReceived: existingBytes,
    );

    final accept = FileAcceptMessage(
      transferId: offer.transferId,
      accepted: true,
      offset: existingBytes,
    );
    conn.sendJson(MessageType.fileAccept, accept.toJson());
  }

  Future<void> _handleFileChunk(Uint8List payload) async {
    if (payload.length < 28) return;

    final transferIdBytes = Uint8List.fromList(payload.sublist(0, 16));
    final transferId = bytesToUuid(transferIdBytes);

    final context = _incomingTransfers[transferId];
    if (context == null || context.file == null) return;

    final dataLength = payload.length - 28;
    final chunkData = Uint8List.fromList(payload.sublist(28));
    context.writeQueue = context.writeQueue.then((_) async {
      if (context.file != null) {
        await context.file!.writeFrom(chunkData);
      }
    });
    await context.writeQueue;

    context.bytesReceived += dataLength;

    final prog = TransferProgress(
      transferId: transferId,
      fileName: context.offer.fileName,
      bytesTransferred: context.bytesReceived,
      totalBytes: context.offer.fileSize,
      state: TransferState.transferring,
    );
    _progressController.add(prog);
  }

  Future<void> _handleFileComplete(
    SessionConnection conn,
    FileCompleteMessage complete,
  ) async {
    final context = _incomingTransfers.remove(complete.transferId);
    if (context == null || context.file == null) return;

    await context.writeQueue;
    if (context.file != null) {
      await context.file!.flush();
      await context.file!.close();
      context.file = null;
    }

    // Verify SHA-256
    final tempFile = File(context.tempFilePath);
    final fileBytes = await tempFile.readAsBytes();
    final localHash = sha256.convert(fileBytes).toString();

    final expectedHash = complete.sha256.trim().isNotEmpty
        ? complete.sha256
        : context.offer.sha256;
    if (expectedHash.isNotEmpty &&
        localHash.toLowerCase() == expectedHash.toLowerCase()) {
      final finalFile = File(context.finalFilePath);
      if (finalFile.existsSync()) {
        finalFile.deleteSync();
      }
      tempFile.renameSync(context.finalFilePath);

      final prog = TransferProgress(
        transferId: complete.transferId,
        fileName: context.offer.fileName,
        bytesTransferred: context.offer.fileSize,
        totalBytes: context.offer.fileSize,
        state: TransferState.completed,
      );
      _progressController.add(prog);
      _fileReceivedController.add(context.finalFilePath);
    } else {
      if (tempFile.existsSync()) tempFile.deleteSync();
      final prog = TransferProgress(
        transferId: complete.transferId,
        fileName: context.offer.fileName,
        bytesTransferred: 0,
        totalBytes: context.offer.fileSize,
        state: TransferState.failed,
      );
      _progressController.add(prog);
    }
  }

  static Uint8List uuidToBytes(String uuid) {
    final clean = uuid.replaceAll('-', '');
    final bytes = Uint8List(16);
    for (int i = 0; i < 16; i++) {
      bytes[i] = int.parse(clean.substring(i * 2, i * 2 + 2), radix: 16);
    }
    return bytes;
  }

  static String bytesToUuid(Uint8List bytes) {
    final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20, 32)}';
  }

  static String _generateUuid() {
    final bytes = Uint8List(16);
    final rand = Random.secure();
    for (int i = 0; i < 16; i++) {
      bytes[i] = rand.nextInt(256);
    }
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    return bytesToUuid(bytes);
  }

  String _directoryForOffer(FileOfferMessage offer) {
    final destination = _sharedStorageDirectory(offer.destinationPath);
    if (destination != null) {
      // Windows file-management drops use this inbox as their stable
      // cross-device destination. Keep the three folders predictable on the
      // phone instead of putting every dropped item directly in Download.
      if (_isHingeDropInbox(destination)) {
        return _hingeCategoryDirectory(offer);
      }
      return destination;
    }

    final type = offer.mimeType.toLowerCase();
    if (type.startsWith('image/') ||
        _hasExtension(offer.fileName, const [
          '.jpg',
          '.jpeg',
          '.png',
          '.gif',
          '.webp',
          '.heic',
        ])) {
      return _imageDirectory;
    }
    if (type.startsWith('video/') ||
        _hasExtension(offer.fileName, const [
          '.mp4',
          '.mov',
          '.mkv',
          '.avi',
          '.webm',
        ])) {
      return _videoDirectory;
    }
    return _downloadDirectory;
  }

  static bool _isHingeDropInbox(String path) {
    final normalized = path
        .replaceAll('\\', '/')
        .replaceFirst(RegExp(r'/+$'), '');
    return normalized == '/storage/emulated/0/Download' ||
        normalized == '/storage/emulated/0/Download/Hinge';
  }

  static String _hingeCategoryDirectory(FileOfferMessage offer) {
    final type = offer.mimeType.toLowerCase();
    final isImage =
        type.startsWith('image/') ||
        _hasExtension(offer.fileName, const [
          '.jpg',
          '.jpeg',
          '.png',
          '.gif',
          '.webp',
          '.heic',
          '.heif',
          '.bmp',
          '.tif',
          '.tiff',
        ]);
    final isVideo =
        type.startsWith('video/') ||
        _hasExtension(offer.fileName, const [
          '.mp4',
          '.mov',
          '.mkv',
          '.avi',
          '.webm',
          '.3gp',
          '.m4v',
        ]);
    final category = isImage
        ? '图片'
        : isVideo
        ? '视频'
        : '文件';
    return '/storage/emulated/0/Download/Hinge/$category';
  }

  /// Resolve a protocol destination below Android's shared storage root.
  /// Empty destinations retain the existing user-configured receive folders.
  /// Keeping this boundary here prevents a remote peer from escaping the
  /// shared-storage root with an absolute path or `..` segment.
  static String? _sharedStorageDirectory(String rawPath) {
    final value = rawPath.trim();
    if (value.isEmpty || !Platform.isAndroid) return null;

    var normalized = value.replaceAll('\\', '/').trim();
    normalized = normalized.replaceFirst(RegExp(r'^/+'), '');
    const rootPrefix = 'storage/emulated/0/';
    if (normalized.toLowerCase().startsWith(rootPrefix)) {
      normalized = normalized.substring(rootPrefix.length);
    }
    final segments = normalized
        .split('/')
        .where((segment) => segment.trim().isNotEmpty)
        .toList();
    if (segments.isEmpty ||
        segments.any(
          (segment) =>
              segment == '.' ||
              segment == '..' ||
              segment.contains(':') ||
              segment.contains('\u0000'),
        )) {
      throw const FormatException('目标保存目录无效');
    }
    return '/storage/emulated/0/${segments.join('/')}';
  }

  static bool _hasExtension(String name, List<String> extensions) {
    final lower = name.toLowerCase();
    return extensions.any(lower.endsWith);
  }

  static String _mimeTypeForFileName(String name) {
    final lower = name.toLowerCase();
    if (lower.endsWith('.jpg') || lower.endsWith('.jpeg')) return 'image/jpeg';
    if (lower.endsWith('.png')) return 'image/png';
    if (lower.endsWith('.webp')) return 'image/webp';
    if (lower.endsWith('.mp4')) return 'video/mp4';
    return 'application/octet-stream';
  }

  static void _ensureDirectory(String path) {
    if (path.trim().isEmpty) return;
    try {
      final dir = Directory(path);
      if (!dir.existsSync()) dir.createSync(recursive: true);
    } catch (_) {}
  }

  static String _pathOrDefault(String? path, String fallback) {
    final value = path?.trim() ?? '';
    return value.isEmpty ? fallback : value;
  }

  void dispose() {
    for (final ctx in _incomingTransfers.values) {
      ctx.file?.close();
    }
    _incomingTransfers.clear();
    _textController.close();
    _progressController.close();
    _fileReceivedController.close();
  }
}
