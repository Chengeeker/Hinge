import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';
import 'package:hinge/core/discovery_service.dart';

void main() {
  group('Workspace UI tests', () {
    testWidgets('HingeApp renders the new home workspace', (
      tester,
    ) async {
      const identity = DeviceIdentity(
        deviceId: 'test-device-id',
        name: 'Mock Phone',
      );
      final service = DiscoveryService(
        localIdentity: identity,
        registry: DeviceRegistry(),
      );

      await tester.pumpWidget(
        HingeApp(identity: identity, discoveryService: service),
      );

      expect(find.text('Hinge 办公套件'), findsOneWidget);
      expect(find.text('没有已连接的机型'), findsOneWidget);
      expect(find.text('工作区'), findsOneWidget);
    });

    test('Device model serialization and deserialization', () {
      const device = Device(
        deviceId: 'dev-12345',
        name: 'Windows Desktop',
        platform: DevicePlatform.windows,
        appVersion: '1.0.0',
        protocolVersion: '0.1',
        capabilities: ['file_transfer', 'clipboard'],
        networkAddresses: ['192.168.1.100'],
        connectionState: DeviceConnectionState.connected,
        trustState: DeviceTrustState.trusted,
      );

      final json = device.toJson();
      final deserialized = Device.fromJson(json);

      expect(deserialized.deviceId, equals('dev-12345'));
      expect(deserialized.platform, equals(DevicePlatform.windows));
      expect(deserialized.capabilities, contains('file_transfer'));
      expect(deserialized.trustState, equals(DeviceTrustState.trusted));
    });

    testWidgets('Discovered devices are actionable from the connection page', (
      tester,
    ) async {
      const identity = DeviceIdentity(
        deviceId: 'test-device-id',
        name: 'Mock Phone',
      );
      final registry = DeviceRegistry();
      final service = DiscoveryService(
        localIdentity: identity,
        registry: registry,
      );

      await tester.pumpWidget(
        HingeApp(identity: identity, discoveryService: service),
      );
      await tester.tap(find.text('连接设备'));
      await tester.pumpAndSettle();

      registry.upsertDevice(
        const DiscoveryMessage(
          deviceId: 'win-laptop-99',
          name: 'Alice PC',
          platform: 'windows',
          timestamp: 123456,
        ),
        '192.168.1.88',
      );
      await tester.pumpAndSettle();

      expect(find.text('Alice PC'), findsOneWidget);
      expect(find.textContaining('在线'), findsOneWidget);
      expect(find.text('连接'), findsOneWidget);
      expect(find.text('配对'), findsNothing);
    });

    testWidgets('Connection page does not expose a separate pairing action', (
      tester,
    ) async {
      const identity = DeviceIdentity(
        deviceId: 'test-device-id',
        name: 'Mock Phone',
      );
      final registry = DeviceRegistry();
      final service = DiscoveryService(
        localIdentity: identity,
        registry: registry,
      );
      await tester.pumpWidget(
        HingeApp(identity: identity, discoveryService: service),
      );
      await tester.tap(find.text('连接设备'));
      await tester.pumpAndSettle();
      registry.upsertDevice(
        const DiscoveryMessage(
          deviceId: 'win-laptop-99',
          name: 'Alice PC',
          platform: 'windows',
          timestamp: 123456,
        ),
        '192.168.1.88',
      );
      await tester.pumpAndSettle();

      expect(find.text('连接'), findsOneWidget);
      expect(find.text('配对'), findsNothing);
    });
  });
}
