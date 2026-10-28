import 'dart:convert';
import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';

class RemoteInputCipher {
  final Uint8List challenge;
  late SecretKey _sendKey, _receiveKey;
  int _sent = 0, _received = 0;
  final _aes = AesGcm.with256bits();
  RemoteInputCipher._(this.challenge);
  static Uint8List parseToken(String token) {
    if (!token.trim().startsWith('HRC1-')) {
      throw const FormatException('请输入电脑端的远控密钥');
    }
    final bytes = base64Decode(token.trim().substring(5));
    if (bytes.length != 32) throw const FormatException('远控密钥格式不正确');
    return bytes;
  }

  static Future<RemoteInputCipher> create(
    List<int> key,
    Uint8List challenge, {
    bool server = false,
  }) async {
    if (key.length != 32 || challenge.length != 32) {
      throw const FormatException('Invalid control key');
    }
    final result = RemoteInputCipher._(Uint8List.fromList(challenge));
    final hkdf = Hkdf(hmac: Hmac.sha256(), outputLength: 32);
    Future<SecretKey> derive(String direction) => hkdf.deriveKey(
      secretKey: SecretKey(key),
      nonce: challenge,
      info: utf8.encode('HingeRemoteInput/v1/$direction'),
    );
    result._sendKey = await derive(server ? 'server' : 'client');
    result._receiveKey = await derive(server ? 'client' : 'server');
    return result;
  }

  Future<Uint8List> encrypt(Uint8List plain) async {
    final header = ByteData(9)
      ..setUint8(0, 1)
      ..setUint64(1, ++_sent, Endian.big);
    final bytes = header.buffer.asUint8List();
    final nonce = Uint8List(12)..setRange(4, 12, bytes, 1);
    final box = await _aes.encrypt(
      plain,
      secretKey: _sendKey,
      nonce: nonce,
      aad: [...bytes, ...challenge],
    );
    return Uint8List.fromList([...bytes, ...box.cipherText, ...box.mac.bytes]);
  }

  Future<Uint8List?> decrypt(Uint8List bytes) async {
    if (bytes.length < 41 || bytes.length > 16425 || bytes[0] != 1) return null;
    final sequence = ByteData.sublistView(bytes).getUint64(1, Endian.big);
    if (sequence <= _received) return null;
    final nonce = Uint8List(12)..setRange(4, 12, bytes, 1);
    try {
      final plain = await _aes.decrypt(
        SecretBox(
          bytes.sublist(9, bytes.length - 16),
          nonce: nonce,
          mac: Mac(bytes.sublist(bytes.length - 16)),
        ),
        secretKey: _receiveKey,
        aad: [...bytes.take(9), ...challenge],
      );
      _received = sequence;
      return Uint8List.fromList(plain);
    } on SecretBoxAuthenticationError {
      return null;
    }
  }
}
