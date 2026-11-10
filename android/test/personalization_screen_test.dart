import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/app.dart';
import 'package:hinge/app/personalization_screen.dart';
import 'package:hinge/core/device_identity_manager.dart';
import 'package:hinge/core/device_registry.dart';
import 'package:hinge/core/discovery_service.dart';
import 'package:hinge/core/workspace_state.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('hinge/platform');
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          if (call.method == 'loadSettings') return <String, dynamic>{};
          return null;
        });
  });
  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
    debugDefaultTargetPlatformOverride = null;
  });

  for (final platform in [TargetPlatform.android, TargetPlatform.windows]) {
    testWidgets(
      'ordinary controls disable pressed ink on $platform without changing taps',
      (tester) async {
        debugDefaultTargetPlatformOverride = platform;
        try {
          const identity = DeviceIdentity(
            deviceId: 'appearance-test',
            name: 'Test',
          );
          final discovery = DiscoveryService(
            localIdentity: identity,
            registry: DeviceRegistry(),
          );
          final state = WorkspaceState();
          addTearDown(() async {
            await tester.pumpWidget(const SizedBox.shrink());
            discovery.dispose();
            state.dispose();
          });
          await tester.pumpWidget(
            HingeApp(
              identity: identity,
              discoveryService: discovery,
              workspaceState: state,
            ),
          );
          await tester.pump();
          final app = tester.widget<MaterialApp>(find.byType(MaterialApp));
          for (final theme in [app.theme!, app.darkTheme!]) {
            expect(theme.splashFactory, same(NoSplash.splashFactory));
            expect(theme.splashColor, Colors.transparent);
            expect(theme.highlightColor, Colors.transparent);
            final overlays = [
              theme.filledButtonTheme.style?.overlayColor,
              theme.outlinedButtonTheme.style?.overlayColor,
              theme.textButtonTheme.style?.overlayColor,
              theme.elevatedButtonTheme.style?.overlayColor,
              theme.iconButtonTheme.style?.overlayColor,
              theme.segmentedButtonTheme.style?.overlayColor,
              theme.checkboxTheme.overlayColor,
              theme.radioTheme.overlayColor,
              theme.switchTheme.overlayColor,
              theme.navigationBarTheme.overlayColor,
            ];
            for (final overlay in overlays) {
              expect(
                overlay?.resolve({WidgetState.pressed}),
                Colors.transparent,
              );
              expect(
                overlay?.resolve({WidgetState.focused}),
                isNull,
                reason: 'keyboard focus uses the component default',
              );
            }
            final slider = theme.sliderTheme.overlayColor;
            expect(
              WidgetStateProperty.resolveAs(slider, {WidgetState.dragged}),
              Colors.transparent,
            );
            expect(
              WidgetStateProperty.resolveAs(slider, {WidgetState.focused}),
              isNot(Colors.transparent),
            );
          }
          if (platform == TargetPlatform.android) {
            state.setTabIndex(6);
            await tester.pumpAndSettle();
            final tile = tester.widget<ListTile>(
              find.ancestor(
                of: find.text('个性化'),
                matching: find.byType(ListTile),
              ),
            );
            expect(
              tester.getTopLeft(find.text('常规')).dx,
              closeTo(
                tester
                    .getTopLeft(
                      find.descendant(
                        of: find.byWidget(tile.leading!),
                        matching: find.byType(Icon),
                      ),
                    )
                    .dx,
                .01,
              ),
              reason: 'section heading aligns with the actual icon, not its slot or the text',
            );
            final gesture = await tester.startGesture(
              tester.getCenter(find.text('个性化')),
            );
            await tester.pump(const Duration(milliseconds: 800));
            expect(find.byType(PersonalizationScreen), findsNothing);
            await gesture.up();
            await tester.pumpAndSettle();
            expect(find.byType(PersonalizationScreen), findsOneWidget);
          }
          expect(tester.takeException(), isNull);
        } finally {
          debugDefaultTargetPlatformOverride = null;
        }
      },
    );
  }

  for (final mode in ['light', 'dark', 'black']) {
    testWidgets('Review settings geometry and selections in $mode mode', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final state = WorkspaceState();
      addTearDown(state.dispose);
      var scheme = ColorScheme.fromSeed(
        seedColor: Colors.deepPurple,
        brightness: mode == 'light' ? Brightness.light : Brightness.dark,
      );
      if (mode == 'black') scheme = scheme.copyWith(surface: Colors.black);
      await tester.pumpWidget(
        MaterialApp(
          theme: ThemeData(useMaterial3: true, colorScheme: scheme),
          home: MediaQuery(
            data: const MediaQueryData(
              size: Size(390, 844),
              padding: EdgeInsets.only(top: 24),
              viewPadding: EdgeInsets.only(top: 24, bottom: 16),
            ),
            child: PersonalizationScreen(
              state: state,
              isDesktop: false,
              hapticFeedbackEnabled: false,
              onHapticFeedbackChanged: (_) async {},
            ),
          ),
        ),
      );
      await tester.pump();
      final list = tester.widget<ListView>(find.byType(ListView).first);
      expect(list.padding, const EdgeInsets.fromLTRB(16, 92, 16, 28));
      for (final card in tester.widgetList<Card>(find.byType(Card))) {
        expect(card.margin, EdgeInsets.zero);
        expect(card.elevation, 0);
        expect(
          card.color,
          mode == 'black'
              ? scheme.surfaceContainer
              : scheme.surfaceContainerLow,
        );
        expect(
          (card.shape as RoundedRectangleBorder).borderRadius,
          BorderRadius.circular(24),
        );
      }
      final header = tester.widget<Text>(find.text('明暗模式'));
      expect(header.style!.fontSize, 15);
      expect(header.style!.color, scheme.onSurfaceVariant);
      final tileTheme = ListTileTheme.of(tester.element(find.text('跟随系统')));
      expect(
        tileTheme.contentPadding,
        const EdgeInsets.symmetric(horizontal: 16, vertical: 3),
      );
      expect(tileTheme.titleTextStyle!.fontSize, 16);
      expect(find.byType(BackdropFilter), findsOneWidget);
      expect(find.text('底栏布局'), findsNothing);

      await tester.tap(find.text('浅色模式'));
      await tester.pump();
      expect(state.themePreference, AppThemePreference.light);
      final scrollable = find.byType(Scrollable).first;
      await tester.scrollUntilVisible(
        find.text('自定义应用字体粗细'),
        250,
        scrollable: scrollable,
      );
      await tester.tap(find.text('自定义应用字体粗细'));
      await tester.pumpAndSettle();
      expect(
        ListTileTheme.of(tester.element(find.text('中等')))
            .titleTextStyle!
            .fontSize,
        16,
      );
      await tester.tap(find.text('中等'));
      await tester.pumpAndSettle();
      expect(state.fontWeightLevel, 2);
      expect(find.byType(AlertDialog), findsNothing);
      expect(find.text('效果预览 (中等)'), findsOneWidget);

      await tester.scrollUntilVisible(
        find.text('使用悬浮底栏'),
        250,
        scrollable: scrollable,
      );
      await tester.tap(find.text('使用悬浮底栏'));
      await tester.pumpAndSettle();
      expect(state.floatingCapsuleNavigation, isFalse);
      await tester.scrollUntilVisible(
        find.text('使用液态玻璃效果'),
        200,
        scrollable: scrollable,
      );
      await tester.tap(find.text('使用液态玻璃效果'));
      await tester.pumpAndSettle();
      expect(state.useLiquidGlass, isTrue);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('preset chips fit a narrow page with large text', (tester) async {
    tester.view.physicalSize = const Size(320, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final state = WorkspaceState();
    addTearDown(state.dispose);
    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: const MediaQueryData(
            size: Size(320, 900),
            textScaler: TextScaler.linear(1.6),
          ),
          child: PersonalizationScreen(
            state: state,
            isDesktop: false,
            hapticFeedbackEnabled: false,
            onHapticFeedbackChanged: (_) async {},
          ),
        ),
      ),
    );
    await tester.pump();
    state.setDynamicColorEnabled(false);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('极光蓝'), 200);
    await tester.tap(find.text('极光蓝'));
    await tester.pumpAndSettle();
    expect(state.seedColor, 0xFF1976D2);
    expect(tester.takeException(), isNull);
  });
}
