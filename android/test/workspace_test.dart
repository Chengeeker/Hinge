import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/workspace_state.dart';

void main() {
  group('Workspace Core & State Tests', () {
    test('WorkspaceState initial values and getters', () {
      final state = WorkspaceState();
      expect(state.themePreference, equals(AppThemePreference.system));
      expect(state.themeMode, equals(ThemeMode.system));
      expect(state.currentTabIndex, equals(0));
      expect(state.clipboardSyncEnabled, isTrue);
    });

    test('WorkspaceState cycles theme preference properly', () {
      final state = WorkspaceState();
      int notifyCount = 0;
      state.addListener(() => notifyCount++);

      state.cycleThemePreference();
      expect(state.themePreference, equals(AppThemePreference.light));
      expect(state.themeMode, equals(ThemeMode.light));
      expect(notifyCount, equals(1));

      state.cycleThemePreference();
      expect(state.themePreference, equals(AppThemePreference.dark));
      expect(state.themeMode, equals(ThemeMode.dark));
      expect(notifyCount, equals(2));

      state.cycleThemePreference();
      expect(state.themePreference, equals(AppThemePreference.system));
      expect(state.themeMode, equals(ThemeMode.system));
      expect(notifyCount, equals(3));
    });

    test('WorkspaceState updates tab index and clipboard sync', () {
      final state = WorkspaceState();
      state.setTabIndex(2);
      expect(state.currentTabIndex, equals(2));

      state.setClipboardSync(false);
      expect(state.clipboardSyncEnabled, isFalse);
    });
  });

  group('Workspace Dashboard UI Tests', () {
    testWidgets('Renders seven navigation destinations and switches pages', (
      tester,
    ) async {
      const identity = DeviceIdentity(deviceId: 'ws-dev-1', name: 'Pixel Test');
      final service = DiscoveryService(
        localIdentity: identity,
        registry: DeviceRegistry(),
      );
      final workspaceState = WorkspaceState();

      await tester.pumpWidget(
        HingeApp(
          identity: identity,
          discoveryService: service,
          workspaceState: workspaceState,
        ),
      );

      expect(find.text('首页'), findsOneWidget);
      expect(find.text('Hinge 办公套件'), findsOneWidget);

      await tester.tap(find.byTooltip('打开导航栏'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.text('已连接的机型'), findsOneWidget);
      expect(find.text('连接设备'), findsWidgets);
      expect(find.text('笔记代办'), findsOneWidget);
      expect(find.text('日历'), findsWidgets);
      expect(find.text('相册'), findsWidgets);

      await tester.tap(find.text('笔记代办'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 900));
      expect(workspaceState.currentTabIndex, equals(3));
      expect(find.text('笔记与待办'), findsOneWidget);

      await tester.tap(find.byTooltip('打开导航栏'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await tester.tap(find.text('日历'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 900));
      expect(workspaceState.currentTabIndex, equals(4));
      expect(find.text('日历'), findsWidgets);
    });
  });
}
