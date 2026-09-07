import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'app/app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  String? initialDeviceName;
  String? initialDeviceManufacturer;
  String? initialDeviceModel;
  if (Platform.isAndroid) {
    try {
      final info = await const MethodChannel('hinge/platform')
          .invokeMethod<Map<dynamic, dynamic>>('deviceInfo');
      initialDeviceName = info?['name'] as String?;
      initialDeviceManufacturer = info?['manufacturer'] as String?;
      initialDeviceModel = info?['model'] as String?;
    } on MissingPluginException {
      // Flutter tests and older installations use the Dart fallback name.
    } on PlatformException {
      // Native device information is optional; identity creation remains available.
    }
  }
  runApp(
    HingeApp(
      initialDeviceName: initialDeviceName,
      initialDeviceManufacturer: initialDeviceManufacturer,
      initialDeviceModel: initialDeviceModel,
    ),
  );
}
