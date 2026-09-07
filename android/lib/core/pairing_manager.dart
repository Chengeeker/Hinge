import 'dart:convert';
import 'dart:math';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';

import 'device_identity_manager.dart';
import 'device_model.dart';
import 'trust_store.dart';

class PairRequestMessage {
  final String initiatorDeviceId;
  final String initiatorName;
  final String receiverDeviceId;
  final String salt;
  final String initiatorNonce;
  final String initiatorPublicKey;
  final int timestamp;

  const PairRequestMessage({
    required this.initiatorDeviceId,
    required this.initiatorName,
    required this.receiverDeviceId,
    required this.salt,
    this.initiatorNonce = '',
    this.initiatorPublicKey = '',
    required this.timestamp,
  });

  Map<String, dynamic> toJson() => {
    'initiatorDeviceId': initiatorDeviceId,
    'initiatorName': initiatorName,
    'receiverDeviceId': receiverDeviceId,
    'salt': salt,
    'initiatorNonce': initiatorNonce,
    'initiatorPublicKey': initiatorPublicKey,
    'timestamp': timestamp,
  };

  factory PairRequestMessage.fromJson(Map<String, dynamic> json) {
    return PairRequestMessage(
      initiatorDeviceId: json['initiatorDeviceId'] as String,
      initiatorName: json['initiatorName'] as String,
      receiverDeviceId: json['receiverDeviceId'] as String,
      salt: json['salt'] as String,
      initiatorNonce: json['initiatorNonce'] as String? ?? '',
      initiatorPublicKey: json['initiatorPublicKey'] as String? ?? '',
      timestamp: json['timestamp'] as int? ?? 0,
    );
  }
}

class PairConfirmMessage {
  final String initiatorDeviceId;
  final String receiverDeviceId;
  final String receiverName;
  final String receiverNonce;
  final String receiverPublicKey;
  final String sasCode;
  final bool accepted;
  final int timestamp;

  const PairConfirmMessage({
    required this.initiatorDeviceId,
    required this.receiverDeviceId,
    this.receiverName = '',
    this.receiverNonce = '',
    this.receiverPublicKey = '',
    this.sasCode = '',
    required this.accepted,
    required this.timestamp,
  });

  Map<String, dynamic> toJson() => {
    'initiatorDeviceId': initiatorDeviceId,
    'receiverDeviceId': receiverDeviceId,
    'receiverName': receiverName,
    'receiverNonce': receiverNonce,
    'receiverPublicKey': receiverPublicKey,
    'sasCode': sasCode,
    'accepted': accepted,
    'timestamp': timestamp,
  };

  factory PairConfirmMessage.fromJson(Map<String, dynamic> json) {
    return PairConfirmMessage(
      initiatorDeviceId: json['initiatorDeviceId'] as String,
      receiverDeviceId: json['receiverDeviceId'] as String,
      receiverName: json['receiverName'] as String? ?? '',
      receiverNonce: json['receiverNonce'] as String? ?? '',
      receiverPublicKey: json['receiverPublicKey'] as String? ?? '',
      sasCode: json['sasCode'] as String? ?? '',
      accepted: json['accepted'] as bool? ?? false,
      timestamp: json['timestamp'] as int? ?? 0,
    );
  }
}

class PairingManager {
  final DeviceIdentity _localIdentity;
  final TrustStore _trustStore;

  PairingManager({required this._localIdentity, required this._trustStore});

  /// Backward-compatible legacy deterministic PIN derivation (prototype).
  static int derivePin(String initiatorId, String receiverId, String salt) {
    final input = '$initiatorId:$receiverId:$salt';
    final bytes = utf8.encode(input);
    final digest = sha256.convert(bytes);
    final byteData = ByteData.sublistView(
      Uint8List.fromList(digest.bytes.sublist(0, 4)),
    );
    final val = byteData.getUint32(0, Endian.big);
    return (val % 900000) + 100000;
  }

  /// Deterministic 6-digit Short Authentication String (SAS) derivation:
  /// HMAC_SHA256(Secret, "Hinge-SAS-v1" || NonceA || NonceB || PubKeyA || PubKeyB)
  static String deriveSasCode({
    required List<int> sharedSecret,
    required List<int> nonceA,
    required List<int> nonceB,
    String pubKeyA = '',
    String pubKeyB = '',
  }) {
    final prefix = utf8.encode('Hinge-SAS-v1');
    final pA = utf8.encode(pubKeyA);
    final pB = utf8.encode(pubKeyB);

    final context = <int>[...prefix, ...nonceA, ...nonceB, ...pA, ...pB];

    final hmac = Hmac(sha256, sharedSecret);
    final digest = hmac.convert(context);
    final byteData = ByteData.sublistView(
      Uint8List.fromList(digest.bytes.sublist(0, 4)),
    );
    final truncatedInt = byteData.getUint32(0, Endian.big);
    final code = (truncatedInt % 900000) + 100000;
    return code.toString().padLeft(6, '0');
  }

  static String deriveSasCodeFromHex({
    required String sharedSecretStr,
    required String nonceAHex,
    required String nonceBHex,
    String pubKeyA = '',
    String pubKeyB = '',
  }) {
    final secret = utf8.encode(sharedSecretStr);
    final nA = _parseHex(nonceAHex);
    final nB = _parseHex(nonceBHex);
    return deriveSasCode(
      sharedSecret: secret,
      nonceA: nA,
      nonceB: nB,
      pubKeyA: pubKeyA,
      pubKeyB: pubKeyB,
    );
  }

  static List<int> _parseHex(String hex) {
    final result = <int>[];
    for (var i = 0; i < hex.length - 1; i += 2) {
      result.add(int.parse(hex.substring(i, i + 2), radix: 16));
    }
    return result;
  }

  PairRequestMessage createPairRequest(
    String receiverDeviceId, {
    String? publicKey,
  }) {
    final rand = Random.secure();
    final nonceBytes = List<int>.generate(16, (_) => rand.nextInt(256));
    final nonceHex = nonceBytes
        .map((b) => b.toRadixString(16).padLeft(2, '0'))
        .join();

    return PairRequestMessage(
      initiatorDeviceId: _localIdentity.deviceId,
      initiatorName: _localIdentity.name,
      receiverDeviceId: receiverDeviceId,
      salt: nonceHex,
      initiatorNonce: nonceHex,
      initiatorPublicKey: publicKey ?? _localIdentity.publicKey,
      timestamp: DateTime.now().millisecondsSinceEpoch ~/ 1000,
    );
  }

  PairConfirmMessage createPairConfirm(
    PairRequestMessage request, {
    required bool accepted,
    List<int>? sharedSecret,
  }) {
    final rand = Random.secure();
    final receiverNonceBytes = List<int>.generate(16, (_) => rand.nextInt(256));
    final receiverNonceHex = receiverNonceBytes
        .map((b) => b.toRadixString(16).padLeft(2, '0'))
        .join();

    String sasCode = '';
    if (accepted) {
      final secret =
          sharedSecret ??
          sha256
              .convert(
                utf8.encode(
                  '${request.initiatorDeviceId}:${_localIdentity.deviceId}',
                ),
              )
              .bytes;
      final nAHex = request.initiatorNonce.isNotEmpty
          ? request.initiatorNonce
          : request.salt.padRight(32, '0').substring(0, 32);
      final nA = _parseHex(nAHex);
      sasCode = deriveSasCode(
        sharedSecret: secret,
        nonceA: nA,
        nonceB: receiverNonceBytes,
        pubKeyA: request.initiatorPublicKey,
        pubKeyB: _localIdentity.publicKey,
      );
    }

    return PairConfirmMessage(
      initiatorDeviceId: request.initiatorDeviceId,
      receiverDeviceId: _localIdentity.deviceId,
      receiverName: _localIdentity.name,
      receiverNonce: receiverNonceHex,
      receiverPublicKey: _localIdentity.publicKey,
      sasCode: sasCode,
      accepted: accepted,
      timestamp: DateTime.now().millisecondsSinceEpoch ~/ 1000,
    );
  }

  void saveTrustedPeer(
    String peerDeviceId,
    String peerName, {
    String publicKey = '',
  }) {
    _trustStore.addOrUpdate(
      TrustedDevice(
        deviceId: peerDeviceId,
        name: peerName,
        publicKey: publicKey,
        pairedAt: DateTime.now().millisecondsSinceEpoch ~/ 1000,
        lastSeen: DateTime.now().millisecondsSinceEpoch ~/ 1000,
        trustState: DeviceTrustState.trusted,
      ),
    );
  }

  bool isPeerTrusted(String peerDeviceId) {
    return _trustStore.isTrusted(peerDeviceId);
  }
}
