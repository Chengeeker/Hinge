import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/workspace_state.dart';
import 'package:liquid_glass_widgets/liquid_glass_widgets.dart';

void main() {
  testWidgets('liquid glass navigation adapts to light and dark themes', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    const channel = MethodChannel('hinge/platform');
    final messenger =
        TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
    messenger.setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'loadSettings') return <String, dynamic>{};
      return null;
    });
    addTearDown(() => messenger.setMockMethodCallHandler(channel, null));

    const identity = DeviceIdentity(
      deviceId: 'glass-test-device',
      name: 'Test',
    );
    final discovery = DiscoveryService(
      localIdentity: identity,
      registry: DeviceRegistry(),
    );
    final workspaceState = WorkspaceState();
    addTearDown(() async {
      await tester.pumpWidget(const SizedBox.shrink());
      discovery.dispose();
      workspaceState.dispose();
    });

    await tester.pumpWidget(
      HingeApp(
        identity: identity,
        discoveryService: discovery,
        workspaceState: workspaceState,
      ),
    );
    await tester.pump();
    workspaceState.setNavigationStyle(AppNavigationStyle.bottom);
    workspaceState.setFloatingCapsuleNavigation(true);
    workspaceState.setUseLiquidGlass(true);
    workspaceState.setThemePreference(AppThemePreference.light);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));

    var bar = tester.widget<GlassTabBar>(find.byType(GlassTabBar));
    expect(bar.settings?.glassColor, const Color(0xF0FFFFFF));
    expect(bar.settings?.rimLight, 1);
    expect(bar.settings?.shadowElevation, 0.55);
    expect(bar.indicatorSettings?.glassColor, const Color(0x18000000));
    expect(bar.indicatorSettings?.bodyMode, GlassBodyMode.clear);
    expect(bar.indicatorSettings?.rimShade, 0.12);
    expect(bar.indicatorSettings?.rimLight, 0.35);
    expect(bar.indicatorColor, const Color(0x26000000));
    expect(
      bar.selectedIconColor,
      Theme.of(tester.element(find.byType(GlassTabBar))).colorScheme.primary,
    );
    expect(
      bar.unselectedIconColor,
      Theme.of(tester.element(find.byType(GlassTabBar)))
          .colorScheme
          .onSurfaceVariant,
    );
    expect(
      find.byKey(const ValueKey('liquid-glass-light-rim')),
      findsOneWidget,
    );

    workspaceState.setThemePreference(AppThemePreference.dark);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));

    bar = tester.widget<GlassTabBar>(find.byType(GlassTabBar));
    expect(bar.settings?.glassColor, const Color(0x66000000));
    expect(bar.settings?.rimLight, 0);
    expect(bar.settings?.shadowElevation, 0);
    expect(bar.indicatorSettings?.glassColor, Colors.transparent);
    expect(bar.indicatorSettings?.bodyMode, GlassBodyMode.adaptive);
    expect(bar.indicatorSettings?.rimShade, 0);
    expect(bar.indicatorSettings?.rimLight, 0);
    expect(bar.indicatorColor, const Color(0x66000000));
    expect(bar.selectedIconColor, Colors.white);
    expect(bar.unselectedIconColor, Colors.white);
    expect(find.byKey(const ValueKey('liquid-glass-light-rim')), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
