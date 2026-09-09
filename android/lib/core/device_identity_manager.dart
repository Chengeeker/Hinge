import 'dart:convert';
import 'dart:io';
import 'dart:math';

class DeviceIdentity {
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final String publicKey;

  const DeviceIdentity({
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    this.publicKey = '',
  });

  Map<String, dynamic> toJson() => {
    'deviceId': deviceId,
    'name': name,
    'manufacturer': manufacturer,
    'model': model,
    'publicKey': publicKey,
  };

  factory DeviceIdentity.fromJson(Map<String, dynamic> json) {
    return DeviceIdentity(
      deviceId: json['deviceId'] as String,
      name: json['name'] as String,
      manufacturer: json['manufacturer'] as String? ?? '',
      model: json['model'] as String? ?? '',
      publicKey: json['publicKey'] as String? ?? '',
    );
  }
}

class DeviceIdentityManager {
  final String? _customPath;
  DeviceIdentity? _cachedIdentity;

  DeviceIdentityManager([this._customPath]);

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
    return File('${dir.path}/identity.json');
  }

  DeviceIdentity getOrCreateIdentity([
    String? defaultName,
    String? manufacturer,
    String? model,
  ]) {
    final cached = _cachedIdentity;
    if (cached != null) {
      return cached;
    }

    final file = _getStorageFile();
    if (file.existsSync()) {
      try {
        final content = file.readAsStringSync();
        final json = jsonDecode(content) as Map<String, dynamic>;
        final id = DeviceIdentity.fromJson(json);
        if (id.deviceId.isNotEmpty) {
          final requestedName = defaultName?.trim();
          final requestedManufacturer = manufacturer?.trim() ?? '';
          final requestedModel = model?.trim() ?? '';
          final shouldRefreshAndroidName =
              Platform.isAndroid &&
              requestedName != null &&
              requestedName.isNotEmpty &&
              requestedName != id.name;
          final shouldRefreshHardwareInfo =
              requestedManufacturer.isNotEmpty &&
                  requestedManufacturer != id.manufacturer ||
              requestedModel.isNotEmpty && requestedModel != id.model;
          if (shouldRefreshAndroidName || shouldRefreshHardwareInfo) {
            final updated = DeviceIdentity(
              deviceId: id.deviceId,
              name: shouldRefreshAndroidName ? requestedName : id.name,
              manufacturer: requestedManufacturer.isNotEmpty
                  ? requestedManufacturer
                  : id.manufacturer,
              model: requestedModel.isNotEmpty ? requestedModel : id.model,
              publicKey: id.publicKey,
            );
            try {
              file.writeAsStringSync(jsonEncode(updated.toJson()));
            } catch (_) {}
            _cachedIdentity = updated;
            return updated;
          }
          _cachedIdentity = id;
          return id;
        }
      } catch (_) {
        // Corrupt file, re-generate
      }
    }

    final rand = Random.secure();
    final keyBytes = List<int>.generate(32, (_) => rand.nextInt(256));
    final pubKey = keyBytes
        .map((b) => b.toRadixString(16).padLeft(2, '0'))
        .join();

    final newId = DeviceIdentity(
      deviceId: _generateUuid(),
      name: defaultName ?? Platform.localHostname,
      manufacturer: manufacturer?.trim() ?? '',
      model: model?.trim() ?? '',
      publicKey: pubKey,
    );

    try {
      file.writeAsStringSync(jsonEncode(newId.toJson()));
    } catch (_) {}

    _cachedIdentity = newId;
    return newId;
  }

  static String _generateUuid() {
    final random = Random.secure();
    final values = List<int>.generate(16, (i) => random.nextInt(256));
    // Set UUID version 4
    values[6] = (values[6] & 0x0f) | 0x40;
    // Set variant RFC4122
    values[8] = (values[8] & 0x3f) | 0x80;

    final hex = values.map((b) => b.toRadixString(16).padLeft(2, '0')).toList();
    return '${hex[0]}${hex[1]}${hex[2]}${hex[3]}-'
        '${hex[4]}${hex[5]}-'
        '${hex[6]}${hex[7]}-'
        '${hex[8]}${hex[9]}-'
        '${hex[10]}${hex[11]}${hex[12]}${hex[13]}${hex[14]}${hex[15]}';
  }
}
