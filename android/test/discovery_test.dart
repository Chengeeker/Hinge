import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';

void main() {
  group('Device Core Tests', () {
    test('DeviceIdentityManager generates and persists identity', () {
      final tempFile = File('/test_id_.json');
      try {
        final manager = DeviceIdentityManager(tempFile.path);
        final id1 = manager.getOrCreateIdentity('TestPhone');

        expect(id1.deviceId, isNotEmpty);
        expect(id1.name, equals('TestPhone'));

        // Load again from same path
        final manager2 = DeviceIdentityManager(tempFile.path);
        final id2 = manager2.getOrCreateIdentity();

        expect(id2.deviceId, equals(id1.deviceId));
        expect(id2.name, equals('TestPhone'));
      } finally {
        if (tempFile.existsSync()) tempFile.deleteSync();
      }
    });

    test('DiscoveryMessage round-trip serialization', () {
      final msg = DiscoveryMessage(
        deviceId: 'uuid-1234-5678',
        name: 'My Laptop',
        platform: 'windows',
        port: 52831,
        capabilities: ['file_transfer', 'clipboard'],
        timestamp: 1756992000,
      );

      final json = msg.toJson();
      final parsed = DiscoveryMessage.fromJson(json);

      expect(parsed.deviceId, equals('uuid-1234-5678'));
      expect(parsed.platform, equals('windows'));
      expect(parsed.capabilities, contains('clipboard'));
      expect(parsed.timestamp, equals(1756992000));
    });

    test(
      'DeviceRegistry upsert and pruneOffline marks device disconnected',
      () {
        final registry = DeviceRegistry();
        final msg = DiscoveryMessage(
          deviceId: 'remote-win-01',
          name: 'Windows Workstation',
          platform: 'windows',
          timestamp: 1756992000,
        );

        registry.upsertDevice(msg, '192.168.1.120');

        expect(registry.devices.length, equals(1));
        expect(registry.devices.first.name, equals('Windows Workstation'));
        expect(
          registry.devices.first.connectionState,
          equals(DeviceConnectionState.discovered),
        );
        expect(
          registry.devices.first.networkAddresses,
          contains('192.168.1.120'),
        );

        // Prune offline with zero duration marks it disconnected
        registry.pruneOffline(Duration.zero);

        expect(
          registry.devices.first.connectionState,
          equals(DeviceConnectionState.disconnected),
        );
        registry.dispose();
      },
    );

    test('DeviceRegistry prefers the most recently seen address', () {
      final registry = DeviceRegistry();
      final msg = DiscoveryMessage(
        deviceId: 'moving-phone',
        name: 'Phone',
        platform: 'android',
        timestamp: 1,
      );

      registry.upsertDevice(msg, '192.168.1.20');
      registry.upsertDevice(msg, '192.168.3.34');
      registry.upsertDevice(msg, '192.168.1.20');

      expect(registry.devices.single.networkAddresses, [
        '192.168.1.20',
        '192.168.3.34',
      ]);
      registry.dispose();
    });
  });
}
