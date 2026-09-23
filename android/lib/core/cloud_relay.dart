import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import 'package:crypto/crypto.dart' as crypto_lib;
import 'package:cryptography/cryptography.dart';

const int cloudRelayPartSize = 8 * 1024 * 1024;
const int cloudRelayGcmTagSize = 16;
const int cloudRelayGcmNonceSize = 12;
const int cloudRelayDefaultTtlHours = 168;

class CloudRelayException implements Exception {
  final int statusCode;
  final String message;

  const CloudRelayException(this.statusCode, this.message);

  @override
  String toString() => 'CloudRelayException($statusCode): $message';
}

class CloudRelaySettings {
  final bool enabled;
  final String endpoint;
  final String deviceToken;
  final String relayEncryptionKey;

  const CloudRelaySettings({
    this.enabled = false,
    this.endpoint = '',
    this.deviceToken = '',
    this.relayEncryptionKey = '',
  });

  bool get isConfigured {
    final uri = Uri.tryParse(endpoint.trim());
    return enabled &&
        uri != null &&
        (uri.scheme == 'https' || uri.scheme == 'http') &&
        uri.host.isNotEmpty &&
        deviceToken.trim().isNotEmpty &&
        relayEncryptionKey.trim().isNotEmpty;
  }

  CloudRelaySettings copyWith({
    bool? enabled,
    String? endpoint,
    String? deviceToken,
    String? relayEncryptionKey,
  }) {
    return CloudRelaySettings(
      enabled: enabled ?? this.enabled,
      endpoint: endpoint ?? this.endpoint,
      deviceToken: deviceToken ?? this.deviceToken,
      relayEncryptionKey: relayEncryptionKey ?? this.relayEncryptionKey,
    );
  }

  Map<String, dynamic> toJson() => {
    'enabled': enabled,
    'endpoint': endpoint,
    'deviceToken': deviceToken,
    'relayEncryptionKey': relayEncryptionKey,
  };

  factory CloudRelaySettings.fromJson(Map<dynamic, dynamic> json) {
    return CloudRelaySettings(
      enabled: json['enabled'] == true,
      endpoint: '${json['endpoint'] ?? ''}'.trim(),
      deviceToken: '${json['deviceToken'] ?? ''}'.trim(),
      relayEncryptionKey: '${json['relayEncryptionKey'] ?? ''}'.trim(),
    );
  }
}

class CloudRelayManifest {
  final int version;
  final String transferId;
  final String senderRelayDeviceId;
  final String receiverRelayDeviceId;
  final int createdAt;
  final int expiresAt;
  final int partSize;
  final int partCount;
  final int ciphertextSize;
  final String encryption;
  final String metadataCiphertext;
  final String metadataNonce;
  final String metadataTag;

  const CloudRelayManifest({
    required this.version,
    required this.transferId,
    required this.senderRelayDeviceId,
    required this.receiverRelayDeviceId,
    required this.createdAt,
    required this.expiresAt,
    required this.partSize,
    required this.partCount,
    required this.ciphertextSize,
    required this.encryption,
    required this.metadataCiphertext,
    required this.metadataNonce,
    required this.metadataTag,
  });

  factory CloudRelayManifest.fromJson(Map<dynamic, dynamic> json) {
    int integer(String key) => (json[key] as num?)?.toInt() ?? 0;
    return CloudRelayManifest(
      version: integer('version'),
      transferId: '${json['transferId'] ?? ''}',
      senderRelayDeviceId: '${json['senderRelayDeviceId'] ?? ''}',
      receiverRelayDeviceId: '${json['receiverRelayDeviceId'] ?? ''}',
      createdAt: integer('createdAt'),
      expiresAt: integer('expiresAt'),
      partSize: integer('partSize'),
      partCount: integer('partCount'),
      ciphertextSize: integer('ciphertextSize'),
      encryption: '${json['encryption'] ?? ''}',
      metadataCiphertext: '${json['metadataCiphertext'] ?? ''}',
      metadataNonce: '${json['metadataNonce'] ?? ''}',
      metadataTag: '${json['metadataTag'] ?? ''}',
    );
  }
}

class CloudRelayFileMetadata {
  final String fileName;
  final String mimeType;
  final int fileSize;
  final int modifiedTime;
  final String sha256;

  const CloudRelayFileMetadata({
    required this.fileName,
    required this.mimeType,
    required this.fileSize,
    required this.modifiedTime,
    required this.sha256,
  });

  Map<String, dynamic> toJson() => {
    'fileName': fileName,
    'mimeType': mimeType,
    'fileSize': fileSize,
    'modifiedTime': modifiedTime,
    'sha256': sha256,
  };

  factory CloudRelayFileMetadata.fromJson(Map<dynamic, dynamic> json) {
    return CloudRelayFileMetadata(
      fileName: '${json['fileName'] ?? ''}',
      mimeType: '${json['mimeType'] ?? 'application/octet-stream'}',
      fileSize: (json['fileSize'] as num?)?.toInt() ?? -1,
      modifiedTime: (json['modifiedTime'] as num?)?.toInt() ?? 0,
      sha256: '${json['sha256'] ?? ''}',
    );
  }
}

class CloudRelayReceipt {
  final String transferId;
  final String status;

  const CloudRelayReceipt({required this.transferId, required this.status});

  factory CloudRelayReceipt.fromJson(Map<dynamic, dynamic> json) {
    return CloudRelayReceipt(
      transferId: '${json['transferId'] ?? ''}',
      status: '${json['status'] ?? ''}',
    );
  }
}

class CloudRelayProgress {
  final String transferId;
  final String fileName;
  final int bytesTransferred;
  final int totalBytes;

  const CloudRelayProgress({
    required this.transferId,
    required this.fileName,
    required this.bytesTransferred,
    required this.totalBytes,
  });

  double get percentage => totalBytes <= 0
      ? 0
      : bytesTransferred / totalBytes * 100;
}

class CloudRelayCrypto {
  static final AesGcm _aes = AesGcm.with256bits();

  static String generateEncryptionKey() {
    final random = Random.secure();
    return _encodeBase64Url(
      List<int>.generate(32, (_) => random.nextInt(256)),
    );
  }

  static String computeRelayDeviceId(
    String relayEncryptionKey,
    String hingeDeviceId,
  ) {
    final digest = crypto_lib.Hmac(crypto_lib.sha256, _decodeKey(relayEncryptionKey)).convert(
      utf8.encode('Hinge-Relay-Device-v1|${hingeDeviceId.trim()}'),
    );
    return _encodeBase64Url(digest.bytes);
  }

  static List<int> deriveTransferKey({
    required String relayEncryptionKey,
    required String transferId,
    required String senderRelayDeviceId,
    required String receiverRelayDeviceId,
  }) {
    final salt = crypto_lib.sha256.convert(utf8.encode(transferId)).bytes;
    final info = utf8.encode(
      'Hinge-Cloud-Relay-v1|$senderRelayDeviceId|$receiverRelayDeviceId',
    );
    return _hkdfSha256(_decodeKey(relayEncryptionKey), salt, info, 32);
  }

  static Future<List<int>> encryptPart({
    required List<int> plaintext,
    required List<int> transferKey,
    required String transferId,
    required String senderRelayDeviceId,
    required String receiverRelayDeviceId,
    required int partNumber,
  }) async {
    final box = await _aes.encrypt(
      plaintext,
      secretKey: SecretKey(transferKey),
      nonce: _partNonce(transferId, partNumber),
      aad: _partAssociatedData(
        transferId,
        senderRelayDeviceId,
        receiverRelayDeviceId,
        partNumber,
        plaintext.length,
      ),
    );
    return <int>[...box.cipherText, ...box.mac.bytes];
  }

  static Future<List<int>> decryptPart({
    required List<int> encrypted,
    required List<int> transferKey,
    required String transferId,
    required String senderRelayDeviceId,
    required String receiverRelayDeviceId,
    required int partNumber,
    required int plaintextLength,
  }) async {
    if (encrypted.length != plaintextLength + cloudRelayGcmTagSize) {
      throw const FormatException('Cloud Relay encrypted part length is invalid.');
    }
    final box = SecretBox(
      encrypted.sublist(0, plaintextLength),
      nonce: _partNonce(transferId, partNumber),
      mac: Mac(encrypted.sublist(plaintextLength)),
    );
    return _aes.decrypt(
      box,
      secretKey: SecretKey(transferKey),
      aad: _partAssociatedData(
        transferId,
        senderRelayDeviceId,
        receiverRelayDeviceId,
        partNumber,
        plaintextLength,
      ),
    );
  }

  static Future<Map<String, String>> encryptMetadata({
    required CloudRelayFileMetadata metadata,
    required List<int> transferKey,
    required String transferId,
    required String senderRelayDeviceId,
    required String receiverRelayDeviceId,
  }) async {
    final box = await _aes.encrypt(
      utf8.encode(jsonEncode(metadata.toJson())),
      secretKey: SecretKey(transferKey),
      nonce: _metadataNonce(transferId),
      aad: _metadataAssociatedData(
        transferId,
        senderRelayDeviceId,
        receiverRelayDeviceId,
      ),
    );
    return {
      'ciphertext': _encodeBase64Url(box.cipherText),
      'nonce': _encodeBase64Url(box.nonce),
      'tag': _encodeBase64Url(box.mac.bytes),
    };
  }

  static Future<CloudRelayFileMetadata> decryptMetadata({
    required CloudRelayManifest manifest,
    required String relayEncryptionKey,
  }) async {
    final transferKey = deriveTransferKey(
      relayEncryptionKey: relayEncryptionKey,
      transferId: manifest.transferId,
      senderRelayDeviceId: manifest.senderRelayDeviceId,
      receiverRelayDeviceId: manifest.receiverRelayDeviceId,
    );
    final box = SecretBox(
      _decodeBase64Url(manifest.metadataCiphertext),
      nonce: _decodeBase64Url(manifest.metadataNonce),
      mac: Mac(_decodeBase64Url(manifest.metadataTag)),
    );
    final plaintext = await _aes.decrypt(
      box,
      secretKey: SecretKey(transferKey),
      aad: _metadataAssociatedData(
        manifest.transferId,
        manifest.senderRelayDeviceId,
        manifest.receiverRelayDeviceId,
      ),
    );
    final decoded = jsonDecode(utf8.decode(plaintext));
    if (decoded is! Map) {
      throw const FormatException('Cloud Relay metadata is invalid.');
    }
    return CloudRelayFileMetadata.fromJson(decoded);
  }

  static List<int> _partNonce(String transferId, int partNumber) {
    final digest = crypto_lib.sha256.convert(utf8.encode(transferId)).bytes;
    final result = Uint8List(cloudRelayGcmNonceSize);
    result.setRange(0, 8, digest);
    ByteData.view(result.buffer).setUint32(8, partNumber, Endian.big);
    return result;
  }

  static List<int> _metadataNonce(String transferId) {
    final digest = crypto_lib.sha256.convert(
      utf8.encode('Hinge-Cloud-Relay-v1|metadata|$transferId'),
    ).bytes;
    return digest.sublist(0, cloudRelayGcmNonceSize);
  }

  static List<int> _partAssociatedData(
    String transferId,
    String senderRelayDeviceId,
    String receiverRelayDeviceId,
    int partNumber,
    int plaintextLength,
  ) {
    return utf8.encode(
      'Hinge-Cloud-Relay-v1|$transferId|$senderRelayDeviceId|'
      '$receiverRelayDeviceId|$partNumber|$plaintextLength',
    );
  }

  static List<int> _metadataAssociatedData(
    String transferId,
    String senderRelayDeviceId,
    String receiverRelayDeviceId,
  ) {
    return utf8.encode(
      'Hinge-Cloud-Relay-v1|metadata|$transferId|'
      '$senderRelayDeviceId|$receiverRelayDeviceId',
    );
  }

  static List<int> _hkdfSha256(
    List<int> ikm,
    List<int> salt,
    List<int> info,
    int length,
  ) {
    final prk = crypto_lib.Hmac(crypto_lib.sha256, salt).convert(ikm).bytes;
    final output = <int>[];
    var previous = <int>[];
    var counter = 1;
    while (output.length < length) {
      previous = crypto_lib.Hmac(crypto_lib.sha256, prk).convert([
        ...previous,
        ...info,
        counter,
      ]).bytes;
      output.addAll(previous);
      counter++;
    }
    return output.sublist(0, length);
  }

  static List<int> _decodeKey(String value) {
    final key = _decodeBase64Url(value);
    if (key.length != 32) {
      throw const FormatException('Relay encryption key must be exactly 32 bytes.');
    }
    return key;
  }

  static String _encodeBase64Url(List<int> bytes) =>
      base64Url.encode(bytes).replaceAll('=', '');

  static List<int> _decodeBase64Url(String value) {
    final normalized = value.trim();
    final padded = normalized.padRight(
      normalized.length + ((4 - normalized.length % 4) % 4),
      '=',
    );
    return base64Url.decode(padded);
  }
}

class CloudRelayClient {
  final HttpClient _httpClient;

  CloudRelayClient([HttpClient? httpClient])
    : _httpClient = httpClient ?? HttpClient() {
    _httpClient.connectionTimeout = const Duration(seconds: 15);
  }

  Future<bool> checkHealth(String endpoint) async {
    final response = await _request('GET', endpoint, '/v1/health');
    return response.statusCode >= 200 && response.statusCode < 300;
  }

  Future<({String relayDeviceId, String deviceToken})> register({
    required String endpoint,
    required String adminToken,
    required String relayDeviceId,
    required String deviceName,
    required String platform,
    required String clientVersion,
  }) async {
    final response = await _request(
      'POST',
      endpoint,
      '/v1/register',
      bearer: adminToken,
      body: jsonEncode({
        'relayDeviceId': relayDeviceId,
        'name': deviceName,
        'platform': platform,
        'clientVersion': clientVersion,
      }),
    );
    final json = _decodeJson(response);
    return (
      relayDeviceId: '${json['relayDeviceId'] ?? relayDeviceId}',
      deviceToken: '${json['deviceToken'] ?? ''}',
    );
  }

  Future<bool> isPeerRegistered({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String peerHingeDeviceId,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final peerRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      peerHingeDeviceId,
    );
    final response = await _request(
      'GET',
      settings.endpoint,
      '/v1/devices/${Uri.encodeComponent(peerRelay)}',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
    );
    return _decodeJson(response)['registered'] == true;
  }

  Future<({String transferId, String uploadId, int partCount})> createTransfer({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String peerHingeDeviceId,
    required String transferId,
    required int partCount,
    required int expiresAt,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final peerRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      peerHingeDeviceId,
    );
    final response = await _request(
      'POST',
      settings.endpoint,
      '/v1/transfers',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
      body: jsonEncode({
        'transferId': transferId,
        'receiverRelayDeviceId': peerRelay,
        'partSize': cloudRelayPartSize,
        'partCount': partCount,
        'expiresAt': expiresAt,
      }),
    );
    final json = _decodeJson(response);
    return (
      transferId: '${json['transferId'] ?? transferId}',
      uploadId: '${json['uploadId'] ?? ''}',
      partCount: (json['partCount'] as num?)?.toInt() ?? partCount,
    );
  }

  Future<String> uploadPart({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String transferId,
    required String uploadId,
    required int partNumber,
    required List<int> encryptedPart,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'PUT',
      settings.endpoint,
      '/v1/transfers/${Uri.encodeComponent(transferId)}/parts/$partNumber'
      '?uploadId=${Uri.encodeQueryComponent(uploadId)}',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
      bodyBytes: encryptedPart,
      contentType: 'application/octet-stream',
    );
    return '${_decodeJson(response)['etag'] ?? ''}';
  }

  Future<void> completeTransfer({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required CloudRelayManifest manifest,
    required String uploadId,
    required List<({int partNumber, String etag})> parts,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'POST',
      settings.endpoint,
      '/v1/transfers/${Uri.encodeComponent(manifest.transferId)}/complete',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
      body: jsonEncode({
        'uploadId': uploadId,
        'parts': parts
            .map((part) => {'partNumber': part.partNumber, 'etag': part.etag})
            .toList(),
        'manifest': {
          'version': manifest.version,
          'transferId': manifest.transferId,
          'senderRelayDeviceId': manifest.senderRelayDeviceId,
          'receiverRelayDeviceId': manifest.receiverRelayDeviceId,
          'createdAt': manifest.createdAt,
          'expiresAt': manifest.expiresAt,
          'partSize': manifest.partSize,
          'partCount': manifest.partCount,
          'ciphertextSize': manifest.ciphertextSize,
          'encryption': manifest.encryption,
          'metadataCiphertext': manifest.metadataCiphertext,
          'metadataNonce': manifest.metadataNonce,
          'metadataTag': manifest.metadataTag,
        },
      }),
    );
    _ensureSuccess(response);
  }

  Future<List<CloudRelayManifest>> inbox({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'GET',
      settings.endpoint,
      '/v1/inbox',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
    );
    final items = _decodeJson(response)['items'];
    return items is List
        ? items.whereType<Map>().map(CloudRelayManifest.fromJson).toList()
        : <CloudRelayManifest>[];
  }

  Future<List<int>> downloadPart({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String transferId,
    required int partNumber,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'GET',
      settings.endpoint,
      '/v1/transfers/${Uri.encodeComponent(transferId)}/parts/$partNumber',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
    );
    _ensureSuccess(response);
    return response.bytes;
  }

  Future<void> acknowledge({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String transferId,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'POST',
      settings.endpoint,
      '/v1/transfers/${Uri.encodeComponent(transferId)}/ack',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
      body: '{}',
    );
    _ensureSuccess(response);
  }

  Future<List<CloudRelayReceipt>> receipts({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'GET',
      settings.endpoint,
      '/v1/receipts',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
    );
    final items = _decodeJson(response)['items'];
    return items is List
        ? items.whereType<Map>().map(CloudRelayReceipt.fromJson).toList()
        : <CloudRelayReceipt>[];
  }

  Future<void> deleteReceipt({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String transferId,
  }) async {
    final localRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final response = await _request(
      'DELETE',
      settings.endpoint,
      '/v1/receipts/${Uri.encodeComponent(transferId)}',
      bearer: settings.deviceToken,
      relayDeviceId: localRelay,
    );
    _ensureSuccess(response);
  }

  Future<_CloudRelayHttpResponse> _request(
    String method,
    String endpoint,
    String path, {
    String? bearer,
    String? relayDeviceId,
    String? body,
    List<int>? bodyBytes,
    String? contentType,
  }) async {
    final base = Uri.tryParse(endpoint.trim());
    if (base == null || base.host.isEmpty || (base.scheme != 'https' && base.scheme != 'http')) {
      throw const FormatException('Cloud Relay endpoint must be an HTTP(S) URL.');
    }
    final uri = base.replace(
      path: '${base.path.replaceFirst(RegExp(r'\/$'), '')}$path',
      query: null,
    );
    final request = await _httpClient.openUrl(method, uri);
    request.headers.set(HttpHeaders.acceptHeader, 'application/json');
    if (bearer != null && bearer.trim().isNotEmpty) {
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer ${bearer.trim()}');
    }
    if (relayDeviceId != null) {
      request.headers.set('X-Hinge-Relay-Device', relayDeviceId);
    }
    if (bodyBytes != null) {
      request.headers.contentType = ContentType(
        (contentType ?? 'application/octet-stream').split('/').first,
        (contentType ?? 'application/octet-stream').split('/').skip(1).join('/'),
      );
      request.contentLength = bodyBytes.length;
      request.add(bodyBytes);
    } else if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(body);
    }
    final response = await request.close();
    final bytes = await response.fold<List<int>>(<int>[], (buffer, chunk) {
      buffer.addAll(chunk);
      return buffer;
    });
    return _CloudRelayHttpResponse(response.statusCode, bytes);
  }

  Map<String, dynamic> _decodeJson(_CloudRelayHttpResponse response) {
    _ensureSuccess(response);
    final decoded = jsonDecode(utf8.decode(response.bytes));
    return decoded is Map ? Map<String, dynamic>.from(decoded) : <String, dynamic>{};
  }

  void _ensureSuccess(_CloudRelayHttpResponse response) {
    if (response.statusCode >= 200 && response.statusCode < 300) return;
    var message = utf8.decode(response.bytes, allowMalformed: true).trim();
    try {
      final decoded = jsonDecode(message);
      if (decoded is Map && decoded['error'] != null) message = '${decoded['error']}';
    } catch (_) {}
    throw CloudRelayException(
      response.statusCode,
      message.isEmpty ? 'Cloud Relay request failed.' : message.substring(0, min(400, message.length)),
    );
  }

  void dispose() => _httpClient.close(force: true);
}

class CloudRelayTransferService {
  final CloudRelayClient client;

  const CloudRelayTransferService(this.client);

  Future<String> sendFile({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String peerHingeDeviceId,
    required String filePath,
    String? fileName,
    String? mimeType,
    void Function(CloudRelayProgress progress)? onProgress,
  }) async {
    final file = File(filePath);
    if (!file.existsSync()) throw FileSystemException('File not found', filePath);
    if (!settings.isConfigured) throw const FormatException('Cloud Relay is not configured.');

    final stat = await file.stat();
    final transferId = _newUuid();
    final senderRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      localHingeDeviceId,
    );
    final receiverRelay = CloudRelayCrypto.computeRelayDeviceId(
      settings.relayEncryptionKey,
      peerHingeDeviceId,
    );
    final partCount = max(
      1,
      (stat.size + cloudRelayPartSize - 1) ~/ cloudRelayPartSize,
    );
    final sha256Hash = (await crypto_lib.sha256.bind(file.openRead()).first).toString();
    final transferKey = CloudRelayCrypto.deriveTransferKey(
      relayEncryptionKey: settings.relayEncryptionKey,
      transferId: transferId,
      senderRelayDeviceId: senderRelay,
      receiverRelayDeviceId: receiverRelay,
    );
    final metadata = CloudRelayFileMetadata(
      fileName: _safeFileName(fileName ?? file.uri.pathSegments.last),
      mimeType: mimeType ?? 'application/octet-stream',
      fileSize: stat.size,
      modifiedTime: stat.modified.millisecondsSinceEpoch ~/ 1000,
      sha256: sha256Hash,
    );
    final encryptedMetadata = await CloudRelayCrypto.encryptMetadata(
      metadata: metadata,
      transferKey: transferKey,
      transferId: transferId,
      senderRelayDeviceId: senderRelay,
      receiverRelayDeviceId: receiverRelay,
    );
    final createdAt = DateTime.now().millisecondsSinceEpoch ~/ 1000;
    final expiresAt = createdAt + cloudRelayDefaultTtlHours * 3600;
    final session = await client.createTransfer(
      settings: settings,
      localHingeDeviceId: localHingeDeviceId,
      peerHingeDeviceId: peerHingeDeviceId,
      transferId: transferId,
      partCount: partCount,
      expiresAt: expiresAt,
    );
    final parts = <({int partNumber, String etag})>[];
    final raf = await file.open(mode: FileMode.read);
    var transferred = 0;
    try {
      for (var partNumber = 1; partNumber <= partCount; partNumber++) {
        final plaintext = await raf.read(cloudRelayPartSize);
        final encrypted = await CloudRelayCrypto.encryptPart(
          plaintext: plaintext,
          transferKey: transferKey,
          transferId: transferId,
          senderRelayDeviceId: senderRelay,
          receiverRelayDeviceId: receiverRelay,
          partNumber: partNumber,
        );
        final etag = await client.uploadPart(
          settings: settings,
          localHingeDeviceId: localHingeDeviceId,
          transferId: transferId,
          uploadId: session.uploadId,
          partNumber: partNumber,
          encryptedPart: encrypted,
        );
        parts.add((partNumber: partNumber, etag: etag));
        transferred += plaintext.length;
        onProgress?.call(CloudRelayProgress(
          transferId: transferId,
          fileName: metadata.fileName,
          bytesTransferred: transferred,
          totalBytes: stat.size,
        ));
      }
    } finally {
      await raf.close();
    }
    await client.completeTransfer(
      settings: settings,
      localHingeDeviceId: localHingeDeviceId,
      uploadId: session.uploadId,
      parts: parts,
      manifest: CloudRelayManifest(
        version: 1,
        transferId: transferId,
        senderRelayDeviceId: senderRelay,
        receiverRelayDeviceId: receiverRelay,
        createdAt: createdAt,
        expiresAt: expiresAt,
        partSize: cloudRelayPartSize,
        partCount: partCount,
        ciphertextSize: stat.size + partCount * cloudRelayGcmTagSize,
        encryption: 'AES-256-GCM',
        metadataCiphertext: encryptedMetadata['ciphertext']!,
        metadataNonce: encryptedMetadata['nonce']!,
        metadataTag: encryptedMetadata['tag']!,
      ),
    );
    return transferId;
  }

  Future<List<String>> receiveInbox({
    required CloudRelaySettings settings,
    required String localHingeDeviceId,
    required String downloadDirectory,
    Set<String>? allowedSenderRelayDeviceIds,
    void Function(CloudRelayProgress progress)? onProgress,
  }) async {
    final manifests = await client.inbox(
      settings: settings,
      localHingeDeviceId: localHingeDeviceId,
    );
    final paths = <String>[];
    for (final manifest in manifests) {
      if (allowedSenderRelayDeviceIds != null &&
          !allowedSenderRelayDeviceIds.contains(manifest.senderRelayDeviceId)) {
        continue;
      }
      final metadata = await CloudRelayCrypto.decryptMetadata(
        manifest: manifest,
        relayEncryptionKey: settings.relayEncryptionKey,
      );
      if (metadata.fileSize < 0 || metadata.sha256.length != 64) {
        throw const FormatException('Cloud Relay file metadata is invalid.');
      }
      final safeName = _safeFileName(metadata.fileName.isEmpty
          ? 'Hinge-${manifest.transferId}.bin'
          : metadata.fileName);
      final directory = Directory(downloadDirectory);
      await directory.create(recursive: true);
      final temporary = File('${directory.path}/.${manifest.transferId}.part');
      final raf = await temporary.open(mode: FileMode.writeOnly);
      var written = 0;
      try {
        for (var partNumber = 1; partNumber <= manifest.partCount; partNumber++) {
          final partLength = partNumber == manifest.partCount
              ? manifest.ciphertextSize -
                    (partNumber - 1) * (manifest.partSize + cloudRelayGcmTagSize) -
                    cloudRelayGcmTagSize
              : manifest.partSize;
          if (partLength < 0 || partLength > cloudRelayPartSize) {
            throw const FormatException('Cloud Relay manifest range is invalid.');
          }
          final encrypted = await client.downloadPart(
            settings: settings,
            localHingeDeviceId: localHingeDeviceId,
            transferId: manifest.transferId,
            partNumber: partNumber,
          );
          final key = CloudRelayCrypto.deriveTransferKey(
            relayEncryptionKey: settings.relayEncryptionKey,
            transferId: manifest.transferId,
            senderRelayDeviceId: manifest.senderRelayDeviceId,
            receiverRelayDeviceId: manifest.receiverRelayDeviceId,
          );
          final plaintext = await CloudRelayCrypto.decryptPart(
            encrypted: encrypted,
            transferKey: key,
            transferId: manifest.transferId,
            senderRelayDeviceId: manifest.senderRelayDeviceId,
            receiverRelayDeviceId: manifest.receiverRelayDeviceId,
            partNumber: partNumber,
            plaintextLength: partLength,
          );
          await raf.writeFrom(plaintext);
          written += plaintext.length;
          onProgress?.call(CloudRelayProgress(
            transferId: manifest.transferId,
            fileName: safeName,
            bytesTransferred: written,
            totalBytes: metadata.fileSize,
          ));
        }
      } finally {
        await raf.close();
      }
      final actualHash = (await crypto_lib.sha256.bind(temporary.openRead()).first).toString();
      if (written != metadata.fileSize || actualHash.toLowerCase() != metadata.sha256.toLowerCase()) {
        try {
          await temporary.delete();
        } catch (_) {}
        throw const FormatException('Cloud Relay plaintext SHA-256 verification failed.');
      }
      final destination = _uniquePath('${directory.path}/$safeName');
      await temporary.rename(destination);
      await client.acknowledge(
        settings: settings,
        localHingeDeviceId: localHingeDeviceId,
        transferId: manifest.transferId,
      );
      paths.add(destination);
    }
    return paths;
  }

  static String _safeFileName(String value) {
    final normalized = value.split(RegExp(r'[\\/]')).last.trim();
    if (normalized.isEmpty || normalized == '.' || normalized == '..') return 'Hinge-file';
    final cleaned = normalized.replaceAll(RegExp(r'[\u0000-\u001f\u007f]'), '');
    if (cleaned.isEmpty) return 'Hinge-file';
    return cleaned.substring(0, min(160, cleaned.length));
  }

  static String _uniquePath(String path) {
    final file = File(path);
    if (!file.existsSync()) return path;
    final extension = path.contains('.') ? path.substring(path.lastIndexOf('.')) : '';
    final base = extension.isEmpty ? path : path.substring(0, path.length - extension.length);
    for (var index = 2; index < 10000; index++) {
      final candidate = '$base ($index)$extension';
      if (!File(candidate).existsSync()) return candidate;
    }
    return '$base-${_newUuid()}$extension';
  }

  static String _newUuid() {
    final bytes = List<int>.generate(16, (_) => Random.secure().nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    final hex = bytes.map((value) => value.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-'
        '${hex.substring(16, 20)}-${hex.substring(20)}';
  }
}

class _CloudRelayHttpResponse {
  final int statusCode;
  final List<int> bytes;

  const _CloudRelayHttpResponse(this.statusCode, this.bytes);
}
