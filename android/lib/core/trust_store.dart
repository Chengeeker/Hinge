import 'dart:convert';
import 'dart:io';

import 'device_model.dart';

class TrustedDevice {
  final String deviceId;
  final String name;
  final String publicKey;
  final int pairedAt;
  final int lastSeen;
  final DeviceTrustState trustState;

  const TrustedDevice({
    required this.deviceId,
    required this.name,
    this.publicKey = '',
    required this.pairedAt,
    required this.lastSeen,
    this.trustState = DeviceTrustState.trusted,
  });

  Map<String, dynamic> toJson() => {
    'deviceId': deviceId,
    'name': name,
    'publicKey': publicKey,
    'pairedAt': pairedAt,
    'lastSeen': lastSeen,
    // Windows' former WinUI client used System.Text.Json's numeric enum
    // representation. Keep that format on the shared desktop path so an
    // existing trust_store.json remains readable by both clients.
    'trustState': Platform.isWindows ? trustState.index : trustState.name,
  };

  factory TrustedDevice.fromJson(Map<String, dynamic> json) {
    final rawTrustState = json['trustState'];
    final trustState = rawTrustState is num
        ? (rawTrustState.toInt() >= 0 &&
                  rawTrustState.toInt() < DeviceTrustState.values.length
              ? DeviceTrustState.values[rawTrustState.toInt()]
              : DeviceTrustState.trusted)
        : DeviceTrustState.values.firstWhere(
            (e) => e.name == rawTrustState,
            orElse: () => DeviceTrustState.trusted,
          );
    return TrustedDevice(
      deviceId: json['deviceId'] as String,
      name: json['name'] as String,
      publicKey: json['publicKey'] as String? ?? '',
      pairedAt: json['pairedAt'] as int? ?? 0,
      lastSeen: json['lastSeen'] as int? ?? 0,
      trustState: trustState,
    );
  }
}

class TrustStore {
  final String? _customPath;
  final Map<String, TrustedDevice> _trustedDevices = {};

  TrustStore([this._customPath]) {
    _load();
  }

  File _getStorageFile() {
    final custom = _customPath;
    if (custom != null) {
      return File(custom);
    }
    final home = Platform.isWindows
        ? (Platform.environment['LOCALAPPDATA'] ??
              Platform.environment['APPDATA'] ??
              Directory.systemTemp.path)
        : (Platform.environment['HOME'] ?? Directory.systemTemp.path);
    final dir = Directory(Platform.isWindows ? '$home/Hinge' : '$home/.hinge');
    if (!dir.existsSync()) {
      dir.createSync(recursive: true);
    }
    return File('${dir.path}/trust_store.json');
  }

  bool isTrusted(String deviceId) {
    final dev = _trustedDevices[deviceId];
    return dev != null && dev.trustState == DeviceTrustState.trusted;
  }

  TrustedDevice? getDevice(String deviceId) => _trustedDevices[deviceId];

  List<TrustedDevice> getAllTrustedDevices() => _trustedDevices.values.toList();

  void addOrUpdate(TrustedDevice device) {
    _trustedDevices[device.deviceId] = device;
    _save();
  }

  bool revoke(String deviceId) {
    if (_trustedDevices.remove(deviceId) != null) {
      _save();
      return true;
    }
    return false;
  }

  void _load() {
    final file = _getStorageFile();
    if (!file.existsSync()) return;
    try {
      final content = file.readAsStringSync();
      final list = jsonDecode(content) as List;
      _trustedDevices.clear();
      for (final item in list) {
        final dev = TrustedDevice.fromJson(item as Map<String, dynamic>);
        _trustedDevices[dev.deviceId] = dev;
      }
    } catch (_) {}
  }

  void _save() {
    final file = _getStorageFile();
    try {
      final list = _trustedDevices.values.map((d) => d.toJson()).toList();
      file.writeAsStringSync(jsonEncode(list));
    } catch (_) {}
  }
}
