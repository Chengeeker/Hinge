import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:liquid_glass_widgets/liquid_glass_widgets.dart';

import 'app/app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await LiquidGlassWidgets.initialize(enablePerformanceMonitor: false);
  String? initialDeviceName;
  String? initialDeviceManufacturer;
  String? initialDeviceModel;
  String? persistentDataDirectory;
  if (Platform.isAndroid) {
    try {
      final info = await const MethodChannel('hinge/platform')
          .invokeMethod<Map<dynamic, dynamic>>('deviceInfo');
      initialDeviceName = info?['name'] as String?;
      initialDeviceManufacturer = info?['manufacturer'] as String?;
      initialDeviceModel = info?['model'] as String?;
      persistentDataDirectory = info?['dataDirectory'] as String?;
    } on MissingPluginException {
      // Flutter tests and older installations use the Dart fallback name.
    } on PlatformException {
      // Native device information is optional; identity creation remains available.
    }
  }
  runApp(
    LiquidGlassWidgets.wrap(
      brightnessResolver: Theme.maybeBrightnessOf,
      child: HingeApp(
        initialDeviceName: initialDeviceName,
        initialDeviceManufacturer: initialDeviceManufacturer,
        initialDeviceModel: initialDeviceModel,
        persistentDataDirectory: persistentDataDirectory,
      ),
    ),
  );
}
