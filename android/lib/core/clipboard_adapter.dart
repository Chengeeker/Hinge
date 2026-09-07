import 'dart:async';

import 'package:flutter/services.dart';

/// Platform abstraction for reading, writing, and monitoring the clipboard.
abstract class ClipboardAdapter {
  Future<String?> getText();
  Future<void> setText(String text);
  Stream<String> get textStream;
  void startMonitoring();
  void stopMonitoring();
  void dispose();
}

/// Standard Flutter implementation using package:flutter/services.dart.
class FlutterClipboardAdapter implements ClipboardAdapter {
  final StreamController<String> _controller =
      StreamController<String>.broadcast();
  Timer? _timer;
  String? _lastKnownText;
  bool _isMonitoring = false;

  @override
  Stream<String> get textStream => _controller.stream;

  @override
  Future<String?> getText() async {
    try {
      final data = await Clipboard.getData(Clipboard.kTextPlain);
      return data?.text;
    } catch (_) {
      // Graceful degradation when running in background on Android 10+
      return null;
    }
  }

  @override
  Future<void> setText(String text) async {
    _lastKnownText = text;
    try {
      await Clipboard.setData(ClipboardData(text: text));
    } catch (_) {
      // Graceful degradation
    }
  }

  @override
  void startMonitoring() {
    if (_isMonitoring) return;
    _isMonitoring = true;

    // Check every 500ms when app is active
    _timer = Timer.periodic(
      const Duration(milliseconds: 500),
      (_) => _checkClipboard(),
    );
  }

  @override
  void stopMonitoring() {
    _isMonitoring = false;
    _timer?.cancel();
    _timer = null;
  }

  Future<void> _checkClipboard() async {
    if (!_isMonitoring) return;
    final text = await getText();
    if (text == null || text.isEmpty) return;

    if (text != _lastKnownText) {
      _lastKnownText = text;
      _controller.add(text);
    }
  }

  @override
  void dispose() {
    stopMonitoring();
    _controller.close();
  }
}

/// In-memory mock adapter for headless tests and unit verification.
class MockClipboardAdapter implements ClipboardAdapter {
  final StreamController<String> _controller =
      StreamController<String>.broadcast();
  String? _currentText;
  bool _isMonitoring = false;
  bool get isMonitoring => _isMonitoring;

  @override
  Stream<String> get textStream => _controller.stream;

  @override
  Future<String?> getText() async => _currentText;

  @override
  Future<void> setText(String text) async {
    _currentText = text;
  }

  void triggerLocalChange(String text) {
    _currentText = text;
    _controller.add(text);
  }

  @override
  void startMonitoring() {
    _isMonitoring = true;
  }

  @override
  void stopMonitoring() {
    _isMonitoring = false;
  }

  @override
  void dispose() {
    stopMonitoring();
    _controller.close();
  }
}
