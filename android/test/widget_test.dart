import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/app/app_message_snackbar.dart';
import 'package:hinge/app/page_components.dart';
import 'package:hinge/app/personalization_screen.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_model.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_message.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/session_manager.dart';
import 'package:hinge/core/trust_store.dart';
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

    testWidgets('HingeApp restores UI state for an already connected session', (
      tester,
    ) async {
      final tempDirectory = await tester.runAsync(
        () => Directory.systemTemp.createTemp('hinge-restored-session-test-'),
      );
      expect(tempDirectory, isNotNull);
      final testDirectory = tempDirectory!;
      const localIdentity = DeviceIdentity(
        deviceId: 'restored-local-device',
        name: 'Local Device',
      );
      const remoteIdentity = DeviceIdentity(
        deviceId: 'restored-remote-device',
        name: 'Remote Windows',
      );
      final trustStore = TrustStore(
        '${testDirectory.path}${Platform.pathSeparator}trust.json',
      );
      final registry = DeviceRegistry();
      final discovery = DiscoveryService(
        localIdentity: localIdentity,
        registry: registry,
      );
      registry.upsertDevice(
        const DiscoveryMessage(
          deviceId: 'restored-remote-device',
          name: 'Remote Windows',
          platform: 'windows',
          timestamp: 123456,
        ),
        '127.0.0.1',
      );

      final server = SessionManager(
        localIdentity: localIdentity,
        trustStore: trustStore,
        listenPort: 0,
      );
      final client = SessionManager(
        localIdentity: remoteIdentity,
        trustStore: trustStore,
        listenPort: 0,
      );
      addTearDown(() async {
        await tester.pumpWidget(const SizedBox.shrink());
        client.dispose();
        server.dispose();
        discovery.dispose();
        await tester.runAsync(() async {
          if (await testDirectory.exists()) {
            await testDirectory.delete(recursive: true);
          }
        });
      });
      await tester.runAsync(() async {
        await server.startListener();
        await client
            .connectToPeer(InternetAddress.loopbackIPv4, server.listeningPort)
            .timeout(const Duration(seconds: 8));
        await Future<void>.delayed(const Duration(milliseconds: 50));
      });
      expect(server.connectionForDevice(remoteIdentity.deviceId), isNotNull);

      await tester.pumpWidget(
        HingeApp(
          identity: localIdentity,
          discoveryService: discovery,
          trustStore: trustStore,
          sessionManager: server,
          persistentDataDirectory: testDirectory.path,
        ),
      );
      // A connected device starts an asynchronous storage read; keep the
      // indeterminate progress indicator from making pumpAndSettle wait for
      // an animation that intentionally remains active in widget tests.
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.textContaining('已建立会话'), findsOneWidget);
      expect(find.text('已发现 · 可建立会话'), findsNothing);
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

    testWidgets('Settings remain readable on a compact large-text surface', (
      tester,
    ) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(360, 740);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      const identity = DeviceIdentity(
        deviceId: 'settings-large-text-device',
        name: 'Settings Test Phone',
      );
      final service = DiscoveryService(
        localIdentity: identity,
        registry: DeviceRegistry(),
      );
      final workspaceState = WorkspaceState()
        ..cycleThemePreference()
        ..cycleThemePreference()
        ..setTabIndex(6);

      await tester.pumpWidget(
        MediaQuery(
          data: const MediaQueryData(textScaler: TextScaler.linear(1.3)),
          child: HingeApp(
            identity: identity,
            discoveryService: service,
            workspaceState: workspaceState,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('常规'), findsOneWidget);
      expect(find.text('个性化'), findsOneWidget);
      expect(find.text('关于应用'), findsOneWidget);
      expect(tester.takeException(), isNull);

      await tester.tap(find.text('个性化'));
      await tester.pumpAndSettle();
      expect(find.text('纯黑深色模式'), findsOneWidget);
      expect(find.byType(Divider), findsNothing);
      expect(find.text('自动匹配系统深色 / 浅色设置'), findsNothing);
      expect(find.text('深色模式下使用纯黑背景，适合 OLED 屏幕'), findsNothing);
      expect(find.text('正在使用壁纸色彩'), findsOneWidget);
      expect(find.text('关闭动态取色后可选择下方预置主题'), findsNothing);
      expect(find.text('轻盈精炼视觉，适合大字号阅读'), findsNothing);
      expect(find.text('点击按钮、切换页面时提供轻微触感反馈'), findsNothing);
    });

    testWidgets('Android page body uses the 16dp M3 side inset', (
      tester,
    ) async {
      await tester.pumpWidget(
        const MaterialApp(home: HingePageBody(child: SizedBox.shrink())),
      );

      final scrollView = tester.widget<SingleChildScrollView>(
        find.byType(SingleChildScrollView),
      );
      expect(scrollView.padding, const EdgeInsets.fromLTRB(16, 8, 16, 32));
    });

    test('Device model serialization and deserialization', () {
      const device = Device(
        deviceId: 'dev-12345',
        name: 'Windows Desktop',
        platform: DevicePlatform.windows,
        appVersion: '1.0.0',
        protocolVersion: '0.1',
        capabilities: ['file_transfer'],
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

          final messenger = tester.state<ScaffoldMessengerState>(
            find.byType(ScaffoldMessenger),
          );
          messenger.showSnackBar(
            buildHingeMessageSnackBar(
              '连接失败：测试提示',
              floatingCapsuleVisible: true,
              capsuleBottomMargin: 16,
            ),
          );
          await tester.pump();
          await tester.pump(const Duration(milliseconds: 300));
          final snackBarTextFinder = find.text('连接失败：测试提示');
          expect(snackBarTextFinder, findsOneWidget);
          expect(
            tester.getRect(snackBarTextFinder).bottom,
            lessThan(tester.getRect(capsuleFinder).top),
          );

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

  testWidgets('Personalization hides captions repeated by feature titles', (
    tester,
  ) async {
    final state = WorkspaceState();
    addTearDown(state.dispose);

    await tester.pumpWidget(
      MaterialApp(
        home: PersonalizationScreen(
          state: state,
          isDesktop: false,
          hapticFeedbackEnabled: false,
          onHapticFeedbackChanged: (_) async {},
        ),
      ),
    );

    expect(find.text('自动匹配系统深色 / 浅色设置'), findsNothing);
    expect(find.text('深色模式下使用纯黑背景，适合 OLED 屏幕'), findsNothing);
    expect(find.text('开启后从 Android 壁纸提取系统色；关闭后使用下方预置配色'), findsNothing);
    expect(find.text('关闭动态取色后可选择下方预置主题'), findsNothing);
    expect(find.byType(Divider), findsNothing);

    final scrollable = find.byType(Scrollable).first;
    await tester.scrollUntilVisible(
      find.text('震动反馈'),
      250,
      scrollable: scrollable,
    );
    expect(find.text('点击按钮、切换页面时提供轻微触感反馈'), findsNothing);

    await tester.drag(scrollable, const Offset(0, 1500));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('自定义应用字体粗细'),
      250,
      scrollable: scrollable,
    );
    await tester.ensureVisible(find.text('自定义应用字体粗细'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('自定义应用字体粗细'));
    await tester.pumpAndSettle();
    expect(find.text('轻盈精炼视觉，适合大字号阅读'), findsNothing);
  });
}
