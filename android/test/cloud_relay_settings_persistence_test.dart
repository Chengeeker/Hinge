import 'dart:async';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hinge/core/cloud_relay.dart';
import 'package:hinge/core/workspace_state.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('hinge/platform');

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  test('Cloud Relay settings survive a WorkspaceState reload', () async {
    Map<String, dynamic> stored = {};
    final saved = Completer<void>();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          switch (call.method) {
            case 'loadSettings':
              return stored;
            case 'saveSettings':
              stored = Map<String, dynamic>.from(call.arguments as Map);
              if (!saved.isCompleted) saved.complete();
              return true;
            default:
              throw MissingPluginException();
          }
        });

    final state = WorkspaceState();
    await state.load();
    state.setCloudRelaySettings(
      const CloudRelaySettings(
        enabled: true,
        endpoint: 'https://relay.example.test',
        deviceToken: 'device-token-for-test',
        relayEncryptionKey: 'encryption-key-for-test',
      ),
    );
    await saved.future;
    state.dispose();

    final restored = WorkspaceState();
    await restored.load();
    expect(restored.cloudRelaySettings.enabled, isTrue);
    expect(restored.cloudRelaySettings.endpoint, 'https://relay.example.test');
    expect(restored.cloudRelaySettings.deviceToken, 'device-token-for-test');
    expect(
      restored.cloudRelaySettings.relayEncryptionKey,
      'encryption-key-for-test',
    );
    restored.dispose();
  });
}
