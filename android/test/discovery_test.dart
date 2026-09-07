import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';
import 'package:hinge/core/discovery_service.dart';

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
        connectionRequested: true,
      );

      final json = msg.toJson();
      final parsed = DiscoveryMessage.fromJson(json);

      expect(parsed.deviceId, equals('uuid-1234-5678'));
      expect(parsed.platform, equals('windows'));
      expect(parsed.port, equals(52831));
      expect(parsed.capabilities, contains('clipboard'));
      expect(parsed.timestamp, equals(1756992000));
      expect(parsed.connectionRequested, isTrue);
    });

    test('DiscoveryService emits reverse connection requests', () async {
      final probe = await RawDatagramSocket.bind(
        InternetAddress.loopbackIPv4,
        0,
      );
      final port = probe.port;
      probe.close();

      final registry = DeviceRegistry();
      final service = DiscoveryService(
        localIdentity: const DeviceIdentity(
          deviceId: 'local-device',
          name: 'Local',
        ),
        registry: registry,
        listenPort: port,
      );
      final sender = await RawDatagramSocket.bind(
        InternetAddress.loopbackIPv4,
        0,
      );
      try {
        await service.start();
        final requestFuture = service.connectionRequests.first.timeout(
          const Duration(seconds: 2),
        );
        final message = DiscoveryMessage(
          deviceId: 'remote-device',
          name: 'Remote',
          platform: 'windows',
          timestamp: DateTime.now().millisecondsSinceEpoch ~/ 1000,
          connectionRequested: true,
        );
        sender.send(
          utf8.encode(jsonEncode(message.toJson())),
          InternetAddress.loopbackIPv4,
          port,
        );

        final request = await requestFuture;
        expect(request.message.deviceId, 'remote-device');
        expect(request.remoteAddress, InternetAddress.loopbackIPv4.address);
      } finally {
        sender.close();
        service.dispose();
      }
    });

    test(
      'DeviceRegistry upsert and pruneOffline marks device disconnected',
      () async {
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

        // Ensure the monotonic wall-clock has advanced before using a zero
        // timeout; Windows can otherwise return the same timestamp twice.
        await Future<void>.delayed(const Duration(milliseconds: 2));
        // Prune offline with zero duration marks it disconnected.
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

    test('DeviceRegistry reconciles duplicate identities on one LAN address', () {
      final registry = DeviceRegistry();
      final first = DiscoveryMessage(
        deviceId: 'phone-first-id',
        name: 'vivo X200 Pro mini',
        manufacturer: 'vivo',
        model: 'V2419A',
        platform: 'android',
        timestamp: 1,
      );
      final second = DiscoveryMessage(
        deviceId: 'phone-second-id',
        name: 'vivo X200 Pro mini',
        manufacturer: 'vivo',
        model: 'V2419A',
        platform: 'android',
        timestamp: 2,
      );

      // Neither record is offline yet. The registry should still collapse the
      // stale identity instead of waiting for a prune cycle.
      registry.upsertDevice(first, '192.168.3.27');
      registry.upsertDevice(second, '192.168.3.27');

      expect(registry.devices, hasLength(1));
      expect(registry.devices.single.deviceId, equals('phone-second-id'));
      registry.dispose();
    });
  });
}
