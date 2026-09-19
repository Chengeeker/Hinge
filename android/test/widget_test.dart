import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/workspace_state.dart';

void main() {
  group('Workspace UI tests', () {
    testWidgets('HingeApp renders the new home workspace', (tester) async {
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

      expect(find.text('Hinge Work'), findsOneWidget);
      expect(find.text('没有已连接的机型'), findsOneWidget);
      expect(find.text('工作区'), findsOneWidget);
    });

    testWidgets('HingeApp remains readable in dark theme', (tester) async {
      const identity = DeviceIdentity(
        deviceId: 'dark-theme-device',
        name: 'Dark Theme Phone',
      );
      final service = DiscoveryService(
        localIdentity: identity,
        registry: DeviceRegistry(),
      );
      final workspaceState = WorkspaceState()
        ..cycleThemePreference()
        ..cycleThemePreference();

      await tester.pumpWidget(
        HingeApp(
          identity: identity,
          discoveryService: service,
          workspaceState: workspaceState,
        ),
      );

      expect(find.text('Hinge Work'), findsOneWidget);
      expect(find.text('没有已连接的机型'), findsOneWidget);
    });

    testWidgets('HingeApp keeps navigation usable with larger text', (
      tester,
    ) async {
      const identity = DeviceIdentity(
        deviceId: 'large-text-device',
        name: 'Large Text Phone',
      );
      final service = DiscoveryService(
        localIdentity: identity,
        registry: DeviceRegistry(),
      );

      await tester.pumpWidget(
        MediaQuery(
          data: const MediaQueryData(textScaler: TextScaler.linear(1.3)),
          child: HingeApp(identity: identity, discoveryService: service),
        ),
      );

      expect(find.text('Hinge Work'), findsOneWidget);
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

    testWidgets(
      'Floating capsule navigation bar renders with centered capsule, full-column indicator, and switches in settings',
      (tester) async {
        debugDefaultTargetPlatformOverride = TargetPlatform.android;
        try {
          const identity = DeviceIdentity(
            deviceId: 'test-capsule-device',
            name: 'Capsule Phone',
          );
          final service = DiscoveryService(
            localIdentity: identity,
            registry: DeviceRegistry(),
          );
          final state = WorkspaceState()
            ..setNavigationStyle(AppNavigationStyle.bottom)
            ..setFloatingCapsuleNavigation(true);

          await tester.pumpWidget(
            HingeApp(
              identity: identity,
              discoveryService: service,
              workspaceState: state,
            ),
          );
          await tester.pumpAndSettle();

          final capsuleFinder = find.byWidgetPredicate(
            (widget) =>
                widget is Container &&
                widget.constraints ==
                    const BoxConstraints.tightFor(width: 280, height: 64),
          );
          expect(capsuleFinder, findsOneWidget);

          final indicatorFinder = find.byWidgetPredicate(
            (widget) =>
                widget is Container &&
                widget.decoration is BoxDecoration &&
                (widget.decoration as BoxDecoration).borderRadius ==
                    BorderRadius.circular(28),
          );
          expect(indicatorFinder, findsOneWidget);

          await tester.tap(
            find.descendant(of: capsuleFinder, matching: find.text('笔记')),
          );
          await tester.pump();
          await tester.pump(const Duration(milliseconds: 300));
          expect(state.currentTabIndex, equals(3));

          state.setFloatingCapsuleNavigation(false);
          await tester.pump();
          await tester.pump(const Duration(milliseconds: 300));

          expect(find.byType(NavigationBar), findsOneWidget);
          expect(capsuleFinder, findsNothing);
        } finally {
          debugDefaultTargetPlatformOverride = null;
        }
      },
    );
  });
}
