import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

enum AppThemePreference { system, light, dark }

enum AppNavigationStyle { adaptive, sidebar, bottom }

class WorkspaceState extends ChangeNotifier {
  static const MethodChannel _settingsChannel = MethodChannel(
    // Settings are implemented by the Android host's platform channel. Keep
    // this name in one place so preferences survive a Flutter engine restart
    // and do not silently fall back to a second, incompatible file store.
    'hinge/platform',
  );

  AppThemePreference _themePreference = AppThemePreference.system;
  static const int defaultSeedColor = 0xFF6750A4;
  int _currentTabIndex = 0;
  bool _clipboardSyncEnabled = true;
  bool _pureBlackDarkMode = false;
  // Material You / Monet is the default on Android. An explicit user choice
  // is still persisted and can turn it off in personalization settings.
  bool _dynamicColorEnabled = true;
  int _fontWeightLevel = 0;
  int _seedColor = defaultSeedColor;
  AppNavigationStyle _navigationStyle = AppNavigationStyle.adaptive;
  bool _floatingCapsuleNavigation = false;
  String _imageStoragePath = '';
  String _videoStoragePath = '';
  String _fileStoragePath = '';
  bool _loaded = false;
  bool _disposed = false;

  WorkspaceState() {
    load();
  }

  AppThemePreference get themePreference => _themePreference;
  int get currentTabIndex => _currentTabIndex;
  bool get clipboardSyncEnabled => _clipboardSyncEnabled;
  bool get pureBlackDarkMode => _pureBlackDarkMode;
  bool get dynamicColorEnabled => _dynamicColorEnabled;
  int get fontWeightLevel => _fontWeightLevel;
  int get seedColor => _seedColor;
  AppNavigationStyle get navigationStyle => _navigationStyle;
  bool get floatingCapsuleNavigation => _floatingCapsuleNavigation;
  String get imageStoragePath => _imageStoragePath;
  String get videoStoragePath => _videoStoragePath;
  String get fileStoragePath => _fileStoragePath;
  bool get loaded => _loaded;

  int get fontWeightDelta =>
      const [0, -100, 50, 100, 200][_fontWeightLevel.clamp(0, 4)];

  ThemeMode get themeMode {
    switch (_themePreference) {
      case AppThemePreference.light:
        return ThemeMode.light;
      case AppThemePreference.dark:
        return ThemeMode.dark;
      case AppThemePreference.system:
        return ThemeMode.system;
    }
  }

  Future<void> load() async {
    Map<String, dynamic>? values;
    try {
      final raw = await _settingsChannel.invokeMethod<dynamic>('loadSettings');
      if (raw is Map) values = Map<String, dynamic>.from(raw);
    } on MissingPluginException {
      values = await _readFileSettings();
    } catch (_) {
      values = await _readFileSettings();
    }

    if (values != null) _apply(values);
    if (_disposed) return;
    _loaded = true;
    notifyListeners();
  }

  void setThemePreference(AppThemePreference pref) {
    if (_themePreference != pref) {
      _themePreference = pref;
      _changed();
    }
  }

  void setTabIndex(int index) {
    if (_currentTabIndex != index) {
      _currentTabIndex = index;
      notifyListeners();
    }
  }

  void setClipboardSync(bool enabled) {
    if (_clipboardSyncEnabled != enabled) {
      _clipboardSyncEnabled = enabled;
      _changed();
    }
  }

  void setPureBlackDarkMode(bool enabled) {
    if (_pureBlackDarkMode != enabled) {
      _pureBlackDarkMode = enabled;
      _changed();
    }
  }

  void setDynamicColorEnabled(bool enabled) {
    if (_dynamicColorEnabled != enabled) {
      _dynamicColorEnabled = enabled;
      _changed();
    }
  }

  void setFontWeightLevel(int level) {
    final value = level.clamp(0, 4);
    if (_fontWeightLevel != value) {
      _fontWeightLevel = value;
      _changed();
    }
  }

  void setSeedColor(int color) {
    final value = color.toUnsigned(32);
    if (_seedColor != value) {
      _seedColor = value;
      _changed();
    }
  }

  void setNavigationStyle(AppNavigationStyle style) {
    if (_navigationStyle != style) {
      _navigationStyle = style;
      _changed();
    }
  }

  void setFloatingCapsuleNavigation(bool enabled) {
    if (_floatingCapsuleNavigation != enabled) {
      _floatingCapsuleNavigation = enabled;
      _changed();
    }
  }

  void setStoragePaths({
    required String image,
    required String video,
    required String file,
  }) {
    final nextImage = image.trim();
    final nextVideo = video.trim();
    final nextFile = file.trim();
    if (_imageStoragePath == nextImage &&
        _videoStoragePath == nextVideo &&
        _fileStoragePath == nextFile) {
      return;
    }
    _imageStoragePath = nextImage;
    _videoStoragePath = nextVideo;
    _fileStoragePath = nextFile;
    _changed();
  }

  void cycleThemePreference() {
    switch (_themePreference) {
      case AppThemePreference.system:
        setThemePreference(AppThemePreference.light);
        break;
      case AppThemePreference.light:
        setThemePreference(AppThemePreference.dark);
        break;
      case AppThemePreference.dark:
        setThemePreference(AppThemePreference.system);
        break;
    }
  }

  void _changed() {
    notifyListeners();
    _persist();
  }

  Map<String, dynamic> _toJson() => {
    'themePreference': _themePreference.name,
    'clipboardSyncEnabled': _clipboardSyncEnabled,
    'pureBlackDarkMode': _pureBlackDarkMode,
    'dynamicColorEnabled': _dynamicColorEnabled,
    'fontWeightLevel': _fontWeightLevel,
    'seedColor': _seedColor,
    'navigationStyle': _navigationStyle.name,
    'floatingCapsuleNavigation': _floatingCapsuleNavigation,
    'imageStoragePath': _imageStoragePath,
    'videoStoragePath': _videoStoragePath,
    'fileStoragePath': _fileStoragePath,
  };

  void _apply(Map<String, dynamic> values) {
    final theme = '${values['themePreference'] ?? ''}';
    _themePreference = AppThemePreference.values.firstWhere(
      (item) => item.name == theme,
      orElse: () => AppThemePreference.system,
    );
    _clipboardSyncEnabled = values['clipboardSyncEnabled'] != false;
    _pureBlackDarkMode = values['pureBlackDarkMode'] == true;
    // Missing legacy keys should follow the Android-native default instead of
    // silently disabling Monet after a fresh install or an update.
    _dynamicColorEnabled = values['dynamicColorEnabled'] != false;
    _fontWeightLevel = ((values['fontWeightLevel'] as num?)?.toInt() ?? 0)
        .clamp(0, 4);
    _seedColor = ((values['seedColor'] as num?)?.toInt() ?? defaultSeedColor)
        .toUnsigned(32);
    final navigation = '${values['navigationStyle'] ?? ''}';
    _navigationStyle = AppNavigationStyle.values.firstWhere(
      (item) => item.name == navigation,
      orElse: () => AppNavigationStyle.adaptive,
    );
    _floatingCapsuleNavigation = values['floatingCapsuleNavigation'] == true;
    _imageStoragePath = '${values['imageStoragePath'] ?? ''}'.trim();
    _videoStoragePath = '${values['videoStoragePath'] ?? ''}'.trim();
    _fileStoragePath = '${values['fileStoragePath'] ?? ''}'.trim();
  }

  Future<void> _persist() async {
    if (_disposed) return;
    try {
      await _settingsChannel.invokeMethod<void>('saveSettings', _toJson());
      return;
    } on MissingPluginException {
      // Flutter Windows has no native settings channel; use the local file.
    } catch (_) {
      // A file fallback keeps preferences usable on test and future hosts.
    }
    try {
      final file = File('${_localRoot()}/settings.json');
      await file.parent.create(recursive: true);
      await file.writeAsString(jsonEncode(_toJson()));
    } catch (_) {}
  }

  Future<Map<String, dynamic>?> _readFileSettings() async {
    try {
      final file = File('${_localRoot()}/settings.json');
      if (!file.existsSync()) return null;
      final raw = jsonDecode(await file.readAsString());
      return raw is Map ? Map<String, dynamic>.from(raw) : null;
    } catch (_) {
      return null;
    }
  }

  static String _localRoot() {
    final home = Platform.isWindows
        ? (Platform.environment['LOCALAPPDATA'] ??
              Platform.environment['APPDATA'] ??
              Directory.systemTemp.path)
        : (Platform.environment['HOME'] ?? Directory.systemTemp.path);
    return Platform.isWindows ? '$home/Hinge' : '$home/.hinge';
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
