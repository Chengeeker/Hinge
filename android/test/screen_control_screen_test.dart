import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/app/screen_control_screen.dart';
import 'package:hinge/core/workspace_data_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('hinge/platform');

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  testWidgets('Opens native settings and refreshes after returning', (
    tester,
  ) async {
    var opened = 0;
    var developerEnabled = false;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          if (call.method == 'screenControlStatus') {
            return {
              'supported': true,
              'developerOptionsEnabled': developerEnabled,
            };
          }
          if (call.method == 'openWirelessDebuggingSettings') {
            opened++;
            return true;
          }
          return null;
        });
    await tester.pumpWidget(
      MaterialApp(
        home: ScreenControlScreen(dataService: WorkspaceDataService()),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('请先开启开发者选项'), findsOneWidget);
    await tester.tap(find.text('打开系统调试设置'));
    await tester.pumpAndSettle();
    expect(opened, 1);
    developerEnabled = true;
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();
    expect(find.textContaining('开发者选项已开启'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Unsupported Android disables wireless debugging action', (
    tester,
  ) async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          channel,
          (call) async => {'supported': false},
        );
    await tester.pumpWidget(
      MaterialApp(
        home: ScreenControlScreen(dataService: WorkspaceDataService()),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    expect(find.textContaining('需要 Android 11'), findsOneWidget);
  });

  testWidgets('Unified settings opens computer controls and refreshes status', (
    tester,
  ) async {
    var status = '已关闭';
    var opened = 0;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (_) async => {'supported': true});
    await tester.pumpWidget(
      MaterialApp(
        home: ScreenControlScreen(
          dataService: WorkspaceDataService(),
          computerStatus: () => status,
          onOpenComputer: () async {
            opened++;
            status = '已开启';
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('电脑控制手机'), findsOneWidget);
    expect(find.text('已关闭'), findsOneWidget);
    await tester.tap(find.text('手机控制电脑'));
    await tester.pumpAndSettle();
    expect(opened, 1);
    expect(find.text('已开启'), findsOneWidget);
  });

  testWidgets(
    'Failed system settings launch has visible fallback instructions',
    (tester) async {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(
            channel,
            (call) async => call.method == 'screenControlStatus'
                ? {'supported': true}
                : false,
          );
      await tester.pumpWidget(
        MaterialApp(
          home: ScreenControlScreen(dataService: WorkspaceDataService()),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('打开系统调试设置'));
      await tester.pumpAndSettle();
      expect(find.text('无法打开系统设置，请手动进入开发者选项中的无线调试。'), findsOneWidget);
    },
  );
}
