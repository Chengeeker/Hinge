import 'dart:async';

import 'device_model.dart';
import 'discovery_message.dart';

class _DeviceRecord {
  final Device device;
  DateTime lastSeen;

  _DeviceRecord({required this.device, required this.lastSeen});
}

class DeviceRegistry {
  final Map<String, _DeviceRecord> _records = {};
  final StreamController<List<Device>> _controller =
      StreamController<List<Device>>.broadcast();

  Stream<List<Device>> get devicesStream => _controller.stream;

  List<Device> get devices => _records.values.map((r) => r.device).toList();

  void upsertDevice(DiscoveryMessage message, String remoteAddress) {
    final now = DateTime.now();
    bool changed = false;

    if (_records.containsKey(message.deviceId)) {
      final record = _records[message.deviceId]!;
      record.lastSeen = now;

      final dev = record.device;
      final addressIndex = dev.networkAddresses.indexOf(remoteAddress);
      if (addressIndex < 0) {
        // Keep address order stable. Phones with Wi-Fi, hotspot, or VPN
        // interfaces can announce from more than one address; moving the
        // latest packet to index 0 caused the UI to rebuild on every packet.
        dev.networkAddresses.add(remoteAddress);
        changed = true;
      }
      if (dev.connectionState == DeviceConnectionState.disconnected) {
        // Device re-appeared
        _records[message.deviceId] = _DeviceRecord(
          device: Device(
            deviceId: dev.deviceId,
            name: message.name,
            manufacturer: message.manufacturer,
            model: message.model,
            platform: _parsePlatform(message.platform),
            appVersion: message.version,
            protocolVersion: message.protocolVersion,
            sessionPort: message.port,
            capabilities: message.capabilities,
            networkAddresses: dev.networkAddresses,
            connectionState: DeviceConnectionState.discovered,
            trustState: dev.trustState,
          ),
          lastSeen: now,
        );
        changed = true;
      } else if (dev.name != message.name ||
          dev.manufacturer != message.manufacturer ||
          dev.model != message.model ||
          dev.sessionPort != message.port) {
        _records[message.deviceId] = _DeviceRecord(
          device: Device(
            deviceId: dev.deviceId,
            name: message.name,
            manufacturer: message.manufacturer,
            model: message.model,
            platform: dev.platform,
            appVersion: message.version,
            protocolVersion: message.protocolVersion,
            sessionPort: message.port,
            capabilities: message.capabilities,
            networkAddresses: dev.networkAddresses,
            connectionState: dev.connectionState,
            trustState: dev.trustState,
          ),
          lastSeen: now,
        );
        changed = true;
      }
    } else {
      final newDevice = Device(
        deviceId: message.deviceId,
        name: message.name,
        manufacturer: message.manufacturer,
        model: message.model,
        platform: _parsePlatform(message.platform),
        appVersion: message.version,
        protocolVersion: message.protocolVersion,
        sessionPort: message.port,
        capabilities: message.capabilities,
        networkAddresses: [remoteAddress],
        connectionState: DeviceConnectionState.discovered,
        trustState: DeviceTrustState.untrusted,
      );
      _records[message.deviceId] = _DeviceRecord(
        device: newDevice,
        lastSeen: now,
      );
      changed = true;
    }

    // A device ID can change after reinstalling or restoring the app. Reconcile
    // every announcement, not only the narrow case where the old record was
    // already offline, so a stale row cannot remain beside the live row.
    changed = _reconcileDuplicateRecords(message.deviceId) || changed;

    if (changed) {
      _notify();
    }
  }

  void pruneOffline(Duration timeout) {
    final cutoff = DateTime.now().subtract(timeout);
    bool changed = false;

    for (final entry in _records.entries) {
      final record = entry.value;
      // A live TCP session is authoritative. UDP discovery is only presence
      // information and may be lost by an access point for a few intervals.
      if (record.device.connectionState == DeviceConnectionState.connected) {
        continue;
      }
      if (record.lastSeen.isBefore(cutoff) &&
          record.device.connectionState != DeviceConnectionState.disconnected) {
        _records[entry.key] = _DeviceRecord(
          device: Device(
            deviceId: record.device.deviceId,
            name: record.device.name,
            manufacturer: record.device.manufacturer,
            model: record.device.model,
            sessionPort: record.device.sessionPort,
            platform: record.device.platform,
            appVersion: record.device.appVersion,
            protocolVersion: record.device.protocolVersion,
            capabilities: record.device.capabilities,
            networkAddresses: record.device.networkAddresses,
            connectionState: DeviceConnectionState.disconnected,
            trustState: record.device.trustState,
          ),
          lastSeen: record.lastSeen,
        );
        changed = true;
      }
    }

    if (changed) {
      _notify();
    }
  }

  void markSessionDisconnected(String deviceId) {
    final record = _records[deviceId];
    if (record == null ||
        record.device.connectionState != DeviceConnectionState.connected) {
      return;
    }
    final stillPresent =
        DateTime.now().difference(record.lastSeen) <=
        const Duration(seconds: 30);
    _records[deviceId] = _DeviceRecord(
      device: record.device.copyWith(
        connectionState: stillPresent
            ? DeviceConnectionState.discovered
            : DeviceConnectionState.disconnected,
      ),
      lastSeen: record.lastSeen,
    );
    _notify();
  }

  bool _reconcileDuplicateRecords(String preferredDeviceId) {
    final preferred = _records[preferredDeviceId];
    if (preferred == null) return false;

    final group = _records.entries
        .where(
          (entry) =>
              entry.key == preferredDeviceId ||
              _canMergeDuplicateDevices(preferred.device, entry.value.device),
        )
        .toList();
    if (group.length <= 1) return false;

    group.sort((left, right) {
      final leftConnected =
          left.value.device.connectionState == DeviceConnectionState.connected;
      final rightConnected =
          right.value.device.connectionState == DeviceConnectionState.connected;
      if (leftConnected != rightConnected) return rightConnected ? 1 : -1;

      final leftPreferred = left.key == preferredDeviceId;
      final rightPreferred = right.key == preferredDeviceId;
      if (leftPreferred != rightPreferred) return rightPreferred ? 1 : -1;

      return right.value.lastSeen.compareTo(left.value.lastSeen);
    });

    final survivor = group.first;
    bool changed = false;
    for (final candidate in group.skip(1)) {
      _records[survivor.key] = _mergeRecords(survivor.value, candidate.value);
      _records.remove(candidate.key);
      changed = true;
    }
    return changed;
  }

  static bool _canMergeDuplicateDevices(Device left, Device right) {
    if (left.deviceId == right.deviceId ||
        left.platform != right.platform ||
        left.name.trim().toLowerCase() != right.name.trim().toLowerCase() ||
        !_sameOptionalIdentity(left.manufacturer, right.manufacturer) ||
        !_sameOptionalIdentity(left.model, right.model)) {
      return false;
    }

    // A shared active LAN address is the strong signal. Two phones can have
    // the same model and user-facing name, but cannot own the same address at
    // the same time.
    return left.networkAddresses.any(
      (address) => right.networkAddresses.any(
        (other) => address.toLowerCase() == other.toLowerCase(),
      ),
    );
  }

  static bool _sameOptionalIdentity(String left, String right) {
    if (left.trim().isEmpty || right.trim().isEmpty) return true;
    return left.trim().toLowerCase() == right.trim().toLowerCase();
  }

  static _DeviceRecord _mergeRecords(
    _DeviceRecord target,
    _DeviceRecord source,
  ) {
    final sourceIsNewer = !source.lastSeen.isBefore(target.lastSeen);
    final addresses = <String>[...target.device.networkAddresses];
    for (final address in source.device.networkAddresses) {
      if (!addresses.any(
        (existing) => existing.toLowerCase() == address.toLowerCase(),
      )) {
        addresses.add(address);
      }
    }

    final trustState =
        source.device.trustState == DeviceTrustState.trusted ||
            source.device.trustState == DeviceTrustState.blocked
        ? source.device.trustState
        : target.device.trustState;
    final mergedDevice = target.device.copyWith(
      name: sourceIsNewer ? source.device.name : target.device.name,
      manufacturer: sourceIsNewer
          ? source.device.manufacturer
          : target.device.manufacturer,
      model: sourceIsNewer ? source.device.model : target.device.model,
      appVersion: sourceIsNewer
          ? source.device.appVersion
          : target.device.appVersion,
      protocolVersion: sourceIsNewer
          ? source.device.protocolVersion
          : target.device.protocolVersion,
      sessionPort: sourceIsNewer
          ? source.device.sessionPort
          : target.device.sessionPort,
      capabilities: sourceIsNewer
          ? source.device.capabilities
          : target.device.capabilities,
      networkAddresses: addresses,
      trustState: trustState,
    );
    return _DeviceRecord(
      device: mergedDevice,
      lastSeen: sourceIsNewer ? source.lastSeen : target.lastSeen,
    );
  }

  static DevicePlatform _parsePlatform(String p) {
    switch (p.toLowerCase()) {
      case 'android':
        return DevicePlatform.android;
      case 'windows':
        return DevicePlatform.windows;
      case 'linux':
        return DevicePlatform.linux;
      case 'macos':
        return DevicePlatform.macos;
      case 'ios':
        return DevicePlatform.ios;
      default:
        return DevicePlatform.unknown;
    }
  }

  void _notify() {
    if (!_controller.isClosed) {
      _controller.add(devices);
    }
  }

  void dispose() {
    _controller.close();
  }
}
