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
          dev.model != message.model) {
        _records[message.deviceId] = _DeviceRecord(
          device: Device(
            deviceId: dev.deviceId,
            name: message.name,
            manufacturer: message.manufacturer,
            model: message.model,
            platform: dev.platform,
            appVersion: message.version,
            protocolVersion: message.protocolVersion,
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
    final stillPresent = DateTime.now().difference(record.lastSeen) <=
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
