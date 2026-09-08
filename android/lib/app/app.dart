import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter/services.dart';
import 'package:dynamic_color/dynamic_color.dart';
import 'package:material_symbols_icons/symbols.dart';
import 'package:flutter_svg/flutter_svg.dart';

import 'personalization_screen.dart';
import 'keep_alive_settings_screen.dart';
import 'default_apps_screen.dart';

import '../core/clipboard_adapter.dart';
import '../core/clipboard_manager.dart';
import '../core/constants.dart';
import '../core/device_identity_manager.dart';
import '../core/device_model.dart';
import '../core/device_registry.dart';
import '../core/discovery_service.dart';
import '../core/pairing_manager.dart';
import '../core/session_manager.dart';
import '../core/transfer_manager.dart';
import '../core/transfer_model.dart';
import '../core/trust_store.dart';
import '../core/workspace_data_service.dart';
import '../core/workspace_state.dart';

class HingeApp extends StatefulWidget {
  final DiscoveryService? discoveryService;
  final DeviceIdentity? identity;
  final String? initialDeviceName;
  final String? initialDeviceManufacturer;
  final String? initialDeviceModel;
  final TrustStore? trustStore;
  final TransferManager? transferManager;
  final ClipboardManager? clipboardManager;
  final WorkspaceState? workspaceState;
  final SessionManager? sessionManager;

  const HingeApp({
    super.key,
    this.discoveryService,
    this.identity,
    this.initialDeviceName,
    this.initialDeviceManufacturer,
    this.initialDeviceModel,
    this.trustStore,
    this.transferManager,
    this.clipboardManager,
    this.workspaceState,
    this.sessionManager,
  });

  @override
  State<HingeApp> createState() => _HingeAppState();
}

class _HingeAppState extends State<HingeApp> with WidgetsBindingObserver {
  late final DeviceIdentity _identity;
  late final DeviceRegistry _registry;
  late final DiscoveryService _discoveryService;
  late final TrustStore _trustStore;
  late final PairingManager _pairingManager;
  late final TransferManager _transferManager;
  late final ClipboardManager _clipboardManager;
  late final WorkspaceState _workspaceState;
  late final SessionManager _sessionManager;
  late final WorkspaceDataService _dataService;
  late final WorkspaceCommandRouter _commandRouter;
  bool _ownsDiscovery = false;
  bool _ownsTransfer = false;
  bool _ownsClipboard = false;
  bool _ownsWorkspace = false;
  bool _ownsSession = false;
  bool _hapticFeedbackEnabled = true;
  Map<String, int> _nativeDynamicColors = const <String, int>{};
  final Map<int, Offset> _globalTouchStarts = <int, Offset>{};
  final Set<int> _globalMovedPointers = <int>{};

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _workspaceState = widget.workspaceState ?? WorkspaceState();
    _ownsWorkspace = widget.workspaceState == null;
    _workspaceState.addListener(_handleWorkspaceStateChanged);
    _trustStore =
        widget.trustStore ??
        TrustStore(
          Platform.environment.containsKey('FLUTTER_TEST')
              ? '${Directory.systemTemp.path}${Platform.pathSeparator}hinge_test_trust_${DateTime.now().microsecondsSinceEpoch}.json'
              : null,
        );
    _transferManager = widget.transferManager ?? TransferManager();
    _ownsTransfer = widget.transferManager == null;

    if (widget.discoveryService != null && widget.identity != null) {
      _identity = widget.identity!;
      _discoveryService = widget.discoveryService!;
      _registry = _discoveryService.registry;
    } else {
      _ownsDiscovery = true;
      final identityManager = DeviceIdentityManager();
      _identity = identityManager.getOrCreateIdentity(
        widget.initialDeviceName ??
            (Platform.isWindows ? 'Windows Desktop' : 'Android Device'),
        widget.initialDeviceManufacturer,
        widget.initialDeviceModel,
      );
      _registry = DeviceRegistry();
      _discoveryService = DiscoveryService(
        localIdentity: _identity,
        registry: _registry,
        sessionPortProvider: () => _sessionManager.listeningPort,
      );
    }

    _pairingManager = PairingManager(
      localIdentity: _identity,
      trustStore: _trustStore,
    );
    _sessionManager =
        widget.sessionManager ??
        SessionManager(localIdentity: _identity, trustStore: _trustStore);
    _ownsSession = widget.sessionManager == null;
    _dataService = WorkspaceDataService();
    WidgetsBinding.instance.pointerRouter.addGlobalRoute(_handleGlobalPointer);
    _loadGlobalHapticFeedbackSetting();
    _loadStoragePaths();
    _commandRouter = WorkspaceCommandRouter(
      sessionManager: _sessionManager,
      dataService: _dataService,
      pairingManager: _pairingManager,
      transferManager: _transferManager,
    );
    _loadNativeDynamicColors();

    _clipboardManager =
        widget.clipboardManager ??
        ClipboardManager(
          localIdentity: _identity,
          adapter: Platform.environment.containsKey('FLUTTER_TEST')
              ? MockClipboardAdapter()
              : FlutterClipboardAdapter(),
        );
    _ownsClipboard = widget.clipboardManager == null;
    _clipboardManager.adapter.startMonitoring();

    if (!Platform.environment.containsKey('FLUTTER_TEST')) {
      unawaited(_startNetworkServices());
    }
  }

  Future<void> _startNetworkServices() async {
    // Bind the TCP listener before the first UDP announcement. This guarantees
    // that discovery never advertises the fallback/default port while the
    // Android socket is still being created.
    await _sessionManager.startListener();
    if (!mounted) return;
    _commandRouter.start();
    if (_ownsDiscovery) await _discoveryService.start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _workspaceState.removeListener(_handleWorkspaceStateChanged);
    WidgetsBinding.instance.pointerRouter.removeGlobalRoute(
      _handleGlobalPointer,
    );
    _commandRouter.dispose();
    if (_ownsSession) _sessionManager.dispose();
    if (_ownsDiscovery) _discoveryService.dispose();
    if (_ownsTransfer) _transferManager.dispose();
    if (_ownsClipboard) _clipboardManager.dispose();
    if (_ownsWorkspace) _workspaceState.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed &&
        Platform.isAndroid &&
        _workspaceState.dynamicColorEnabled) {
      // Re-read Android's system_* Monet roles after returning from wallpaper
      // or system theme settings. The native resource values survive app
      // restarts and are not replaced by a hard-coded seed color.
      unawaited(_loadNativeDynamicColors());
    }
  }

  @override
  Widget build(BuildContext context) {
    return DynamicColorBuilder(
      builder: (lightDynamic, darkDynamic) => AnimatedBuilder(
        animation: _workspaceState,
        builder: (context, child) => MaterialApp(
          title: AppConstants.appName,
          debugShowCheckedModeBanner: false,
          locale: const Locale('zh', 'CN'),
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          supportedLocales: const [Locale('zh', 'CN'), Locale('en', 'US')],
          theme: _hingeTheme(
            _workspaceState,
            brightness: Brightness.light,
            dynamicScheme: _workspaceState.dynamicColorEnabled
                ? _nativeDynamicScheme(
                    _nativeDynamicColors,
                    Brightness.light,
                    lightDynamic,
                  )
                : null,
          ),
          darkTheme: _hingeTheme(
            _workspaceState,
            brightness: Brightness.dark,
            dynamicScheme: _workspaceState.dynamicColorEnabled
                ? _nativeDynamicScheme(
                    _nativeDynamicColors,
                    Brightness.dark,
                    darkDynamic,
                  )
                : null,
          ),
          themeMode: _workspaceState.themeMode,
          home: child,
        ),
        child: DevicesScreen(
          localIdentity: _identity,
          discoveryService: _discoveryService,
          trustStore: _trustStore,
          pairingManager: _pairingManager,
          transferManager: _transferManager,
          clipboardManager: _clipboardManager,
          workspaceState: _workspaceState,
          sessionManager: _sessionManager,
          dataService: _dataService,
          onHapticFeedbackChanged: (enabled) {
            if (mounted) setState(() => _hapticFeedbackEnabled = enabled);
          },
        ),
      ),
    );
  }

  Future<void> _loadGlobalHapticFeedbackSetting() async {
    if (Platform.environment.containsKey('FLUTTER_TEST')) return;
    final enabled = await _dataService.hapticFeedbackEnabled();
    if (mounted) setState(() => _hapticFeedbackEnabled = enabled);
  }

  Future<void> _loadNativeDynamicColors() async {
    if (!Platform.isAndroid) return;
    final colors = await _dataService.loadSystemDynamicColors();
    if (!mounted || colors.isEmpty) return;
    setState(() => _nativeDynamicColors = colors);
  }

  void _handleGlobalPointer(PointerEvent event) {
    if (Platform.isWindows ||
        Platform.isLinux ||
        Platform.isMacOS ||
        Platform.environment.containsKey('FLUTTER_TEST')) {
      return;
    }
    if (event.kind != PointerDeviceKind.touch &&
        event.kind != PointerDeviceKind.stylus) {
      return;
    }
    if (event is PointerDownEvent) {
      _globalTouchStarts[event.pointer] = event.position;
      _globalMovedPointers.remove(event.pointer);
      return;
    }
    final start = _globalTouchStarts[event.pointer];
    if (event is PointerMoveEvent &&
        start != null &&
        (event.position - start).distance > 12) {
      _globalMovedPointers.add(event.pointer);
      return;
    }
    if (event is PointerUpEvent || event is PointerCancelEvent) {
      final wasTap =
          event is PointerUpEvent &&
          start != null &&
          !_globalMovedPointers.contains(event.pointer);
      _globalTouchStarts.remove(event.pointer);
      _globalMovedPointers.remove(event.pointer);
      if (wasTap && _hapticFeedbackEnabled) {
        HapticFeedback.selectionClick();
      }
    }
  }

  void _handleWorkspaceStateChanged() {
    // MaterialApp is rebuilt by AnimatedBuilder. DynamicColorBuilder supplies
    // the complete wallpaper-derived CorePalette when Android updates it.
  }

  Future<void> _loadStoragePaths() async {
    final paths = await _dataService.loadStoragePaths();
    if (!mounted) return;
    final image = paths['imagePath'] ?? '';
    final video = paths['videoPath'] ?? '';
    final file = paths['filePath'] ?? '';
    if (image.isEmpty && video.isEmpty && file.isEmpty) return;
    _workspaceState.setStoragePaths(
      image: _workspaceState.imageStoragePath.isEmpty
          ? image
          : _workspaceState.imageStoragePath,
      video: _workspaceState.videoStoragePath.isEmpty
          ? video
          : _workspaceState.videoStoragePath,
      file: _workspaceState.fileStoragePath.isEmpty
          ? file
          : _workspaceState.fileStoragePath,
    );
  }
}

ColorScheme? _nativeDynamicScheme(
  Map<String, int> colors,
  Brightness brightness,
  ColorScheme? fallback,
) {
  if (colors.isEmpty) return fallback;
  final prefix = brightness == Brightness.dark ? 'dark.' : 'light.';
  final base =
      fallback ??
      ColorScheme.fromSeed(
        seedColor: const Color(0xFF6750A4),
        brightness: brightness,
      );
  Color? role(String name) {
    final value = colors['$prefix$name'];
    return value == null ? null : Color(value);
  }

  // These roles are read from Android's public system_* resources. They are
  // already the wallpaper-derived Monet/HCT roles, so no green seed or second
  // Flutter palette is introduced here.
  return base.copyWith(
    primary: role('primary'),
    onPrimary: role('onPrimary'),
    primaryContainer: role('primaryContainer'),
    onPrimaryContainer: role('onPrimaryContainer'),
    secondary: role('secondary'),
    onSecondary: role('onSecondary'),
    secondaryContainer: role('secondaryContainer'),
    onSecondaryContainer: role('onSecondaryContainer'),
    tertiary: role('tertiary'),
    onTertiary: role('onTertiary'),
    tertiaryContainer: role('tertiaryContainer'),
    onTertiaryContainer: role('onTertiaryContainer'),
    surface: role('surface'),
    onSurface: role('onSurface'),
    onSurfaceVariant: role('onSurfaceVariant'),
    outline: role('outline'),
    outlineVariant: role('outlineVariant'),
    surfaceContainerLowest: role('surfaceContainerLowest'),
    surfaceContainerLow: role('surfaceContainerLow'),
    surfaceContainer: role('surfaceContainer'),
    surfaceContainerHigh: role('surfaceContainerHigh'),
    surfaceContainerHighest: role('surfaceContainerHighest'),
    surfaceBright: role('surfaceBright'),
    surfaceDim: role('surfaceDim'),
    error: role('error'),
    onError: role('onError'),
    errorContainer: role('errorContainer'),
    onErrorContainer: role('onErrorContainer'),
    inversePrimary: role('inversePrimary'),
    inverseSurface: role('inverseSurface'),
    onInverseSurface: role('onInverseSurface'),
  );
}

ThemeData _hingeTheme(
  WorkspaceState state, {
  required Brightness brightness,
  ColorScheme? dynamicScheme,
}) {
  var scheme =
      dynamicScheme ??
      ColorScheme.fromSeed(
        seedColor: Color(state.seedColor),
        brightness: brightness,
        dynamicSchemeVariant: DynamicSchemeVariant.expressive,
      );
  if (brightness == Brightness.light &&
      dynamicScheme == null &&
      state.seedColor == WorkspaceState.defaultSeedColor) {
    scheme = scheme.copyWith(
      primary: const Color(0xFF6750A4),
      onPrimary: Colors.white,
      primaryContainer: const Color(0xFFEADDFF),
      onPrimaryContainer: const Color(0xFF21005D),
      secondaryContainer: const Color(0xFFE8DEF8),
      onSecondaryContainer: const Color(0xFF1D192B),
      tertiaryContainer: const Color(0xFFFFD8E4),
      onTertiaryContainer: const Color(0xFF31111D),
      surface: const Color(0xFFFEF7FF),
      surfaceContainerLow: const Color(0xFFF7F2FA),
      surfaceContainer: const Color(0xFFF3EDF7),
      surfaceContainerHigh: const Color(0xFFECE6F0),
      surfaceContainerHighest: const Color(0xFFE6E0E9),
      onSurface: const Color(0xFF1D1B20),
      onSurfaceVariant: const Color(0xFF49454F),
      outline: const Color(0xFF79747E),
      outlineVariant: const Color(0xFFCAC4D0),
      inverseSurface: const Color(0xFF322F35),
      onInverseSurface: const Color(0xFFF5EFF7),
      inversePrimary: const Color(0xFFD0BCFF),
      error: const Color(0xFFB3261E),
      onError: Colors.white,
      errorContainer: const Color(0xFFF9DEDC),
      onErrorContainer: const Color(0xFF410E0B),
    );
  }
  if (brightness == Brightness.dark && state.pureBlackDarkMode) {
    scheme = scheme.copyWith(
      surface: Colors.black,
      surfaceDim: Colors.black,
      surfaceBright: const Color(0xFF1A1A1A),
      surfaceContainerLowest: Colors.black,
      surfaceContainerLow: const Color(0xFF080808),
      surfaceContainer: const Color(0xFF101010),
      surfaceContainerHigh: const Color(0xFF181818),
      surfaceContainerHighest: const Color(0xFF222222),
    );
  }
  if (dynamicScheme == null) {
    // Preset themes deliberately use one coherent accent family. A native
    // CorePalette keeps Android's primary/secondary/tertiary roles intact.
    scheme = scheme.copyWith(
      secondary: scheme.primary,
      onSecondary: scheme.onPrimary,
      secondaryContainer: scheme.primaryContainer,
      onSecondaryContainer: scheme.onPrimaryContainer,
      tertiary: scheme.primary,
      onTertiary: scheme.onPrimary,
      tertiaryContainer: scheme.primaryContainer,
      onTertiaryContainer: scheme.onPrimaryContainer,
    );
  }
  final textTheme = _withFontWeight(
    ThemeData(useMaterial3: true, colorScheme: scheme).textTheme,
    state.fontWeightDelta,
  );

  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    textTheme: textTheme,
    splashFactory: InkRipple.splashFactory,
    // Windows does not ship Roboto as a system font.  Use the native Chinese
    // UI font there; Flutter's null family keeps Android on its system font.
    fontFamily: Platform.isWindows ? 'Microsoft YaHei' : null,
    fontFamilyFallback: Platform.isWindows
        ? const ['Microsoft YaHei UI', 'Segoe UI']
        : null,
    scaffoldBackgroundColor: scheme.surface,
    appBarTheme: AppBarTheme(
      backgroundColor: scheme.surface,
      surfaceTintColor: Colors.transparent,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(
        color: scheme.onSurface,
        fontSize: 22,
        fontWeight: FontWeight.w600,
      ),
    ),
    cardTheme: CardThemeData(
      elevation: 0,
      margin: EdgeInsets.zero,
      // 部分 Android 12/13 厂商的动态方案会让 surfaceContainerLow 与
      // surface 几乎相同，卡片因此看起来像消失。仍使用 Monet 的中性角色，
      // 但改用对比更清楚的 surfaceContainer。
      color: scheme.surfaceContainer,
      surfaceTintColor: Colors.transparent,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.all(Radius.circular(20)),
      ),
    ),
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: scheme.surfaceContainer,
      indicatorColor: scheme.secondaryContainer,
      selectedIconTheme: IconThemeData(color: scheme.onSecondaryContainer),
      selectedLabelTextStyle: TextStyle(
        color: scheme.onSurface,
        fontWeight: FontWeight.w600,
      ),
    ),
    navigationDrawerTheme: NavigationDrawerThemeData(
      backgroundColor: scheme.surfaceContainer,
      indicatorColor: scheme.secondaryContainer,
      tileHeight: 64,
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: scheme.surface,
      indicatorColor: scheme.secondaryContainer,
      surfaceTintColor: Colors.transparent,
      height: 80,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surfaceContainerHighest,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide(color: scheme.primary, width: 2),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size(0, 52),
        shape: const StadiumBorder(),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size(0, 52),
        shape: const StadiumBorder(),
      ),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(
      backgroundColor: scheme.primaryContainer,
      foregroundColor: scheme.onPrimaryContainer,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.all(Radius.circular(16)),
      ),
    ),
  );
}

TextTheme _withFontWeight(TextTheme theme, int delta) {
  if (delta == 0) return theme;
  TextStyle? adjust(TextStyle? style) {
    if (style == null) return null;
    final value = ((style.fontWeight?.value ?? 400) + delta).clamp(100, 900);
    final weight = switch (value) {
      100 => FontWeight.w100,
      200 => FontWeight.w200,
      300 => FontWeight.w300,
      500 => FontWeight.w500,
      600 => FontWeight.w600,
      700 => FontWeight.w700,
      800 => FontWeight.w800,
      900 => FontWeight.w900,
      _ => FontWeight.w400,
    };
    return style.copyWith(fontWeight: weight);
  }

  return theme.copyWith(
    displayLarge: adjust(theme.displayLarge),
    displayMedium: adjust(theme.displayMedium),
    displaySmall: adjust(theme.displaySmall),
    headlineLarge: adjust(theme.headlineLarge),
    headlineMedium: adjust(theme.headlineMedium),
    headlineSmall: adjust(theme.headlineSmall),
    titleLarge: adjust(theme.titleLarge),
    titleMedium: adjust(theme.titleMedium),
    titleSmall: adjust(theme.titleSmall),
    bodyLarge: adjust(theme.bodyLarge),
    bodyMedium: adjust(theme.bodyMedium),
    bodySmall: adjust(theme.bodySmall),
    labelLarge: adjust(theme.labelLarge),
    labelMedium: adjust(theme.labelMedium),
    labelSmall: adjust(theme.labelSmall),
  );
}

class _HingeLogo extends StatelessWidget {
  final double size;

  const _HingeLogo({required this.size});

  @override
  Widget build(BuildContext context) {
    return ClipRRect(
      borderRadius: BorderRadius.circular(size * .25),
      child: Image.asset(
        'assets/icon9.png',
        width: size,
        height: size,
        fit: BoxFit.cover,
      ),
    );
  }
}

class DevicesScreen extends StatefulWidget {
  final DeviceIdentity localIdentity;
  final DiscoveryService discoveryService;
  final TrustStore trustStore;
  final PairingManager pairingManager;
  final TransferManager transferManager;
  final ClipboardManager clipboardManager;
  final WorkspaceState? workspaceState;
  final SessionManager sessionManager;
  final WorkspaceDataService dataService;
  final ValueChanged<bool>? onHapticFeedbackChanged;

  const DevicesScreen({
    super.key,
    required this.localIdentity,
    required this.discoveryService,
    required this.trustStore,
    required this.pairingManager,
    required this.transferManager,
    required this.clipboardManager,
    required this.sessionManager,
    required this.dataService,
    this.workspaceState,
    this.onHapticFeedbackChanged,
  });

  @override
  State<DevicesScreen> createState() => _DevicesScreenState();
}

class _DevicesScreenState extends State<DevicesScreen>
    with WidgetsBindingObserver {
  static const _pageTitles = ['首页', '已连接的机型', '连接设备', '笔记代办', '日历', '相册', '设置'];

  late final WorkspaceState _workspaceState;
  bool _ownsWorkspaceState = false;
  List<Device> _registryDevices = [];
  StreamSubscription<List<Device>>? _deviceSubscription;
  StreamSubscription<SessionState>? _connectionSubscription;
  StreamSubscription<SessionConnection>? _incomingConnectionSubscription;
  StreamSubscription<DiscoveryConnectionRequest>?
  _connectionRequestSubscription;
  StreamSubscription<String>? _fileReceivedSubscription;
  final List<StreamSubscription<dynamic>> _incomingPeerSubscriptions = [];
  final Set<SessionConnection> _watchedConnections = <SessionConnection>{};
  StreamSubscription? _clipboardSubscription;
  StreamSubscription? _urlSubscription;
  SessionConnection? _activeConnection;
  Device? _activeDevice;
  Device? _lastConnectedDevice;
  String? _connectingDeviceId;

  StorageInfo? _storage;
  bool _storageLoading = false;
  String? _storageError;
  List<CalendarEvent> _calendarEvents = [];
  bool? _calendarPermission;
  bool _calendarLoading = false;
  String? _calendarError;
  List<PhotoItem> _photos = [];
  List<PhotoAlbum> _photoAlbums = [];
  final Map<String, Future<Uint8List?>> _photoBytesCache = {};
  final Map<String, Future<Uint8List?>> _photoPreviewBytesCache = {};
  String? _selectedAlbumId;
  bool? _photosPermission;
  bool _photosLoading = false;
  String? _photosError;
  List<RemoteFileItem> _files = [];
  bool _filesLoading = false;
  String? _filesError;
  List<WorkspaceNote> _notes = [];
  List<WorkspaceTask> _tasks = [];
  bool _notesLoading = false;
  bool _tasksLoading = false;
  static const int _photoPageSize = 200;
  int _photosTotal = 0;
  int _photosOffset = 0;
  bool _photoPageRequestInFlight = false;
  final _AsyncLimiter _photoThumbnailLimiter = _AsyncLimiter(6);
  String? _notesError;
  String? _tasksError;
  bool _hapticFeedbackEnabled = true;
  Timer? _listenerStatusTimer;
  bool _showMobileWorkspaceOverview = false;
  String? _mobileWorkspaceFocus;
  bool _permissionPromptShown = false;
  bool _userDisconnected = false;
  bool _resumeReconnectScheduled = false;
  final Map<String, DateTime> _automaticConnectAttempts = <String, DateTime>{};
  int _connectionAttemptGeneration = 0;

  final TextEditingController _ipController = TextEditingController();

  bool get _isDesktop =>
      Platform.isWindows || Platform.isLinux || Platform.isMacOS;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _workspaceState = widget.workspaceState ?? WorkspaceState();
    _ownsWorkspaceState = widget.workspaceState == null;
    widget.transferManager.chooseReceiveDirectory =
        _chooseWindowsReceiveDirectory;
    _configureTransferStorage();
    widget.clipboardManager.autoSync = _workspaceState.clipboardSyncEnabled;
    _registryDevices = widget.discoveryService.registry.devices;
    _deviceSubscription = widget.discoveryService.registry.devicesStream.listen(
      (devices) {
        if (!mounted) return;
        setState(() => _registryDevices = devices);
        _refreshStorage();
        // Discovery is presence information only.  The trust store decides
        // whether this device may be connected to without another prompt.
        unawaited(_maybeAutoConnectHistoricalDevice(devices));
      },
    );
    _incomingConnectionSubscription = widget.sessionManager.onClientConnected
        .listen(_watchIncomingConnection);
    _connectionRequestSubscription = widget.discoveryService.connectionRequests
        .listen((request) {
          unawaited(_handleReverseConnectionRequest(request));
        });
    _workspaceState.addListener(_onWorkspaceChanged);
    _clipboardSubscription = widget.clipboardManager.clipboardStream.listen((
      message,
    ) {
      if (!mounted) return;
      _showMessage('剪贴板已同步：${_shorten(message.content, 36)}');
    });
    _urlSubscription = widget.clipboardManager.urlHandoffStream.listen((url) {
      if (!mounted) return;
      _showMessage('收到链接：$url');
    });
    _fileReceivedSubscription = widget.transferManager.fileReceivedStream
        .listen((path) {
          if (_isDesktop) return;
          unawaited(widget.dataService.showFileReceivedNotification(path));
          if (mounted) {
            final name = path.split(Platform.pathSeparator).last;
            _showMessage('已收到文件：$name');
          }
        });
    _refreshNotes();
    _refreshTasks();
    _loadHapticFeedbackSetting();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _refreshStorage();
      unawaited(_maybeRequestAndroidPermissions());
    });
    _listenerStatusTimer = Timer(const Duration(milliseconds: 800), () {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _workspaceState.removeListener(_onWorkspaceChanged);
    widget.transferManager.chooseReceiveDirectory = null;
    if (_ownsWorkspaceState) _workspaceState.dispose();
    _deviceSubscription?.cancel();
    _connectionSubscription?.cancel();
    _incomingConnectionSubscription?.cancel();
    _connectionRequestSubscription?.cancel();
    _fileReceivedSubscription?.cancel();
    _listenerStatusTimer?.cancel();
    _listenerStatusTimer = null;
    for (final subscription in _incomingPeerSubscriptions) {
      subscription.cancel();
    }
    _incomingPeerSubscriptions.clear();
    _clipboardSubscription?.cancel();
    _urlSubscription?.cancel();
    _activeConnection?.dispose();
    _ipController.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed || _isDesktop) return;
    // The foreground service keeps the listener alive while the app is
    // backgrounded. Refresh discovery and repair a stale Dart socket when the
    // activity becomes visible again, which also covers OEMs that reclaim the
    // Flutter process despite the service notification.
    unawaited(widget.discoveryService.broadcastOnce());
    unawaited(_reconnectAfterResume());
  }

  Future<void> _reconnectAfterResume() async {
    if (_resumeReconnectScheduled ||
        _userDisconnected ||
        _activeConnection?.state == SessionState.connected ||
        _connectingDeviceId != null ||
        _lastConnectedDevice == null) {
      return;
    }
    _resumeReconnectScheduled = true;
    try {
      await Future<void>.delayed(const Duration(milliseconds: 250));
      if (!mounted ||
          _userDisconnected ||
          _activeConnection?.state == SessionState.connected) {
        return;
      }
      final target = _allDevices.cast<Device?>().firstWhere(
        (device) => device?.deviceId == _lastConnectedDevice!.deviceId,
        orElse: () => _lastConnectedDevice,
      );
      if (target != null && target.networkAddresses.isNotEmpty) {
        await _connect(target);
      }
    } finally {
      _resumeReconnectScheduled = false;
    }
  }

  Future<void> _maybeAutoConnectHistoricalDevice(List<Device> devices) async {
    if (_userDisconnected ||
        _activeConnection?.state == SessionState.connected ||
        _connectingDeviceId != null) {
      return;
    }

    final candidate = devices.cast<Device?>().firstWhere(
      (device) =>
          device != null &&
          device.networkAddresses.isNotEmpty &&
          _isDiscovered(device) &&
          widget.trustStore.isTrusted(device.deviceId),
      orElse: () => null,
    );
    if (candidate == null) return;

    final now = DateTime.now();
    final lastAttempt = _automaticConnectAttempts[candidate.deviceId];
    if (lastAttempt != null &&
        now.difference(lastAttempt) < const Duration(seconds: 12)) {
      return;
    }
    _automaticConnectAttempts[candidate.deviceId] = now;

    _showMessage('正在自动连接历史设备：${candidate.name}…');
    await _connect(candidate, automatic: true);
  }

  Future<void> _maybeRequestAndroidPermissions() async {
    if (_isDesktop || _permissionPromptShown) return;
    _permissionPromptShown = true;
    try {
      final calendarGranted = await widget.dataService.hasCalendarPermission();
      final photosGranted = await widget.dataService.hasPhotosPermission();
      final mediaGranted = await widget.dataService.hasMediaPermission();
      final allFilesGranted = await widget.dataService.hasAllFilesAccess();
      final notificationGranted = await widget.dataService
          .hasNotificationPermission();
      if (mounted) {
        setState(() {
          _calendarPermission = calendarGranted;
          _photosPermission = photosGranted;
        });
      }
      if (calendarGranted &&
              photosGranted &&
              mediaGranted &&
              allFilesGranted &&
              notificationGranted ||
          !mounted) {
        return;
      }

      final shouldRequest = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('完善手机工作区'),
          content: const Text(
            '为了让 Windows 端完整查看手机日历、照片、视频、音频、PDF、DOC、XLS、PPT 和 TXT 文件，并在收到文件时及时提醒，Hinge 需要日历、媒体、通知以及系统“所有文件访问”权限。笔记和待办保存在本机，不需要额外权限。',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('稍后处理'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext, true),
              child: const Text('继续授权'),
            ),
          ],
        ),
      );
      if (shouldRequest != true || !mounted) return;

      if (!calendarGranted) {
        await widget.dataService.requestCalendarPermission();
      }
      if (!photosGranted) {
        await widget.dataService.requestPhotosPermission();
      }
      if (!mediaGranted) {
        await widget.dataService.requestMediaPermission();
      }
      if (!allFilesGranted) {
        await widget.dataService.openAllFilesAccessSettings();
      }
      if (!notificationGranted) {
        await widget.dataService.requestNotificationPermission();
      }
      await _refreshCalendar();
      await _refreshPhotoAlbums();
    } catch (error) {
      if (mounted) {
        _showMessage('权限请求未完成：$error');
      }
    }
  }

  void _onWorkspaceChanged() {
    if (!mounted) return;
    _configureTransferStorage();
    widget.clipboardManager.autoSync = _workspaceState.clipboardSyncEnabled;
    setState(() {});
    _refreshStorage();
    if (_workspaceState.currentTabIndex == 4) _refreshCalendar();
    if (_workspaceState.currentTabIndex == 5) _refreshPhotos();
  }

  List<Device> get _allDevices {
    final devices = <String, Device>{
      for (final device in _registryDevices) device.deviceId: device,
    };
    for (final trusted in widget.trustStore.getAllTrustedDevices()) {
      final current = devices[trusted.deviceId];
      devices[trusted.deviceId] =
          current?.copyWith(trustState: DeviceTrustState.trusted) ??
          Device(
            deviceId: trusted.deviceId,
            name: trusted.name,
            platform: DevicePlatform.android,
            appVersion: '已记录',
            protocolVersion: AppConstants.protocolVersion,
            capabilities: const [],
            networkAddresses: const [],
            connectionState: DeviceConnectionState.disconnected,
            trustState: DeviceTrustState.trusted,
          );
    }
    final list = devices.values.toList();
    list.sort((a, b) {
      final aTrusted = a.trustState == DeviceTrustState.trusted ? 0 : 1;
      final bTrusted = b.trustState == DeviceTrustState.trusted ? 0 : 1;
      return aTrusted != bTrusted
          ? aTrusted - bTrusted
          : a.name.compareTo(b.name);
    });
    return list;
  }

  Device? get _selectedDevice {
    final devices = _allDevices;
    if (_activeDevice != null) {
      for (final device in devices) {
        if (device.deviceId == _activeDevice!.deviceId) return device;
      }
    }
    for (final device in devices) {
      if (device.trustState == DeviceTrustState.trusted &&
          _isDiscovered(device)) {
        return device;
      }
    }
    for (final device in devices) {
      if (device.platform == DevicePlatform.android && _isDiscovered(device)) {
        return device;
      }
    }
    return devices.isEmpty ? null : devices.first;
  }

  bool _isDiscovered(Device device) {
    return device.networkAddresses.isNotEmpty &&
        device.connectionState != DeviceConnectionState.disconnected;
  }

  void _setPage(int index) {
    if (!_isDesktop) {
      setState(() {
        _showMobileWorkspaceOverview = false;
        _mobileWorkspaceFocus = null;
      });
    }
    _workspaceState.setTabIndex(index);
  }

  void _openMobileWorkspaceSection(String section) {
    if (_isDesktop) return;
    setState(() {
      _showMobileWorkspaceOverview = false;
      _mobileWorkspaceFocus = section;
    });
    _workspaceState.setTabIndex(3);
  }

  void _selectNavigationDestination(int index) {
    if (_isDesktop) {
      _setPage(index);
      return;
    }
    switch (index) {
      case 0:
        setState(() {
          _showMobileWorkspaceOverview = false;
          _mobileWorkspaceFocus = null;
        });
        _workspaceState.setTabIndex(0);
        break;
      case 1:
        setState(() {
          _showMobileWorkspaceOverview = true;
          _mobileWorkspaceFocus = null;
        });
        _workspaceState.setTabIndex(3);
        break;
      case 2:
        setState(() {
          _showMobileWorkspaceOverview = false;
          _mobileWorkspaceFocus = null;
        });
        _workspaceState.setTabIndex(6);
        break;
    }
  }

  int get _selectedNavigationIndex {
    if (_isDesktop) return _workspaceState.currentTabIndex;
    if (_workspaceState.currentTabIndex == 6) return 2;
    if (_showMobileWorkspaceOverview ||
        _workspaceState.currentTabIndex >= 3 &&
            _workspaceState.currentTabIndex <= 5) {
      return 1;
    }
    return 0;
  }

  void _handleMobileBack() {
    if (_isDesktop) return;
    if (_workspaceState.currentTabIndex == 5 && _selectedAlbumId != null) {
      setState(() {
        _selectedAlbumId = null;
        _photos = [];
        _photosOffset = 0;
        _photosTotal = 0;
        _photosError = null;
      });
      return;
    }
    if (_showMobileWorkspaceOverview) {
      setState(() => _showMobileWorkspaceOverview = false);
      _workspaceState.setTabIndex(0);
      return;
    }
    if (_mobileWorkspaceFocus != null) {
      setState(() {
        _mobileWorkspaceFocus = null;
        _showMobileWorkspaceOverview = true;
      });
      _workspaceState.setTabIndex(3);
      return;
    }
    final tab = _workspaceState.currentTabIndex;
    if (tab >= 3 && tab <= 5) {
      setState(() => _showMobileWorkspaceOverview = true);
      _workspaceState.setTabIndex(3);
      return;
    }
    if (tab != 0) {
      _workspaceState.setTabIndex(0);
      return;
    }
    // Keep the Flutter engine and the foreground connection service alive when
    // the user backs out of the root page. SystemNavigator.pop() destroys the
    // activity on several Android/OEM builds, which also tears down the Dart
    // TCP listener even though the foreground service is still running.
    unawaited(_moveAndroidTaskToBack());
  }

  Future<void> _moveAndroidTaskToBack() async {
    if (_isDesktop) return;
    try {
      await const MethodChannel('hinge/platform')
          .invokeMethod<bool>('moveTaskToBack');
    } on MissingPluginException {
      // Flutter tests and older builds have no Android host method.
      await SystemNavigator.pop();
    } on PlatformException {
      await SystemNavigator.pop();
    }
  }

  void _setClipboardSync(bool enabled) {
    widget.clipboardManager.autoSync = enabled;
    _workspaceState.setClipboardSync(enabled);
    setState(() {});
    _showMessage(enabled ? '剪贴板同步已开启' : '剪贴板同步已关闭');
  }

  void _configureTransferStorage() {
    widget.transferManager.configureStorage(
      imagePath: _workspaceState.imageStoragePath,
      videoPath: _workspaceState.videoStoragePath,
      filePath: _workspaceState.fileStoragePath,
    );
  }

  Future<void> _loadHapticFeedbackSetting() async {
    final enabled = await widget.dataService.hapticFeedbackEnabled();
    if (mounted) setState(() => _hapticFeedbackEnabled = enabled);
  }

  Future<void> _setHapticFeedbackEnabled(bool enabled) async {
    setState(() => _hapticFeedbackEnabled = enabled);
    await widget.dataService.setHapticFeedbackEnabled(enabled);
    if (enabled) HapticFeedback.selectionClick();
  }

  void _watchIncomingConnection(SessionConnection connection) {
    _watchConnectionData(connection);
    _incomingPeerSubscriptions.add(
      connection.peerStream.listen(
        (peer) => _registerIncomingPeer(connection, peer),
      ),
    );
    _incomingPeerSubscriptions.add(
      connection.stateStream.listen((state) {
        if (!mounted || state != SessionState.disconnected) return;
        widget.discoveryService.registry.markSessionDisconnected(
          connection.peerInfo?.deviceId ?? '',
        );
        if (_activeConnection == connection) {
          setState(() {
            _activeConnection = null;
            _activeDevice = null;
          });
        }
      }),
    );
    // The peer hello can arrive before the broadcast stream subscription is
    // attached. SessionConnection retains the latest hello so an incoming
    // connection is still shown in 已连接的机型 in that race window.
    final peer = connection.peerInfo;
    if (peer != null) _registerIncomingPeer(connection, peer);
  }

  void _watchConnectionData(SessionConnection connection) {
    if (!_watchedConnections.add(connection)) return;
    widget.clipboardManager.registerConnection(connection);
    _incomingPeerSubscriptions.add(
      connection.frames.listen((frame) {
        unawaited(
          widget.clipboardManager.handleIncomingFrame(connection, frame),
        );
        unawaited(
          widget.transferManager.handleIncomingFrame(connection, frame),
        );
      }),
    );
    _incomingPeerSubscriptions.add(
      connection.stateStream.listen((state) {
        if (state == SessionState.disconnected) {
          _watchedConnections.remove(connection);
          widget.clipboardManager.unregisterConnection(connection);
        }
      }),
    );
  }

  Future<void> _handleReverseConnectionRequest(
    DiscoveryConnectionRequest request,
  ) async {
    if (!mounted ||
        request.message.deviceId == widget.localIdentity.deviceId ||
        widget.sessionManager.connectionForDevice(request.message.deviceId) !=
            null) {
      return;
    }

    // If a simultaneous direct attempt is still finishing, give it a short
    // chance to release the connection gate before honoring the reverse ask.
    final deadline = DateTime.now().add(const Duration(seconds: 4));
    while (_connectingDeviceId != null && DateTime.now().isBefore(deadline)) {
      await Future<void>.delayed(const Duration(milliseconds: 120));
      if (!mounted ||
          widget.sessionManager.connectionForDevice(request.message.deviceId) !=
              null) {
        return;
      }
    }
    if (!mounted || _connectingDeviceId != null) return;

    final device = widget.discoveryService.registry.devices
        .cast<Device?>()
        .firstWhere(
          (candidate) => candidate?.deviceId == request.message.deviceId,
          orElse: () => null,
        );
    if (device != null) {
      await _connect(device, automatic: true);
    }
  }

  void _registerIncomingPeer(
    SessionConnection connection,
    SessionPeerInfo peer,
  ) {
    // 用户已经主动建立了这条局域网会话，连接本身就是授权动作。保留
    // TrustStore 作为底层兼容层，让通知、剪贴板等旧的信任检查继续工作，
    // 但不再要求用户额外完成一套“配对”流程。
    widget.pairingManager.saveTrustedPeer(peer.deviceId, peer.name);
    final existing = _allDevices.cast<Device?>().firstWhere(
      (device) => device?.deviceId == peer.deviceId,
      orElse: () => null,
    );
    final device =
        (existing ??
                Device(
                  deviceId: peer.deviceId,
                  name: peer.name,
                  platform: _parseDevicePlatform(peer.platform),
                  appVersion: '已连接',
                  protocolVersion: AppConstants.protocolVersion,
                  capabilities: const [],
                  networkAddresses: [connection.remoteAddress],
                  connectionState: DeviceConnectionState.connected,
                  trustState: DeviceTrustState.trusted,
                ))
            .copyWith(
              name: peer.name,
              platform: _parseDevicePlatform(peer.platform),
              networkAddresses: {
                ...(existing?.networkAddresses ?? const <String>[]),
                connection.remoteAddress,
              }.toList(),
              connectionState: DeviceConnectionState.connected,
              trustState: DeviceTrustState.trusted,
            );

    if (!mounted) return;
    _lastConnectedDevice = device;
    _userDisconnected = false;
    setState(() {
      final index = _registryDevices.indexWhere(
        (item) => item.deviceId == peer.deviceId,
      );
      if (index >= 0) {
        final updated = [..._registryDevices];
        updated[index] = device;
        _registryDevices = updated;
      } else {
        _registryDevices = [..._registryDevices, device];
      }
      _activeConnection = connection;
      _activeDevice = device;
    });
    _refreshStorage();
  }

  DevicePlatform _parseDevicePlatform(String platform) {
    switch (platform.toLowerCase()) {
      case 'android':
        return DevicePlatform.android;
      case 'windows':
        return DevicePlatform.windows;
      case 'linux':
        return DevicePlatform.linux;
      case 'macos':
        return DevicePlatform.macos;
      case 'ios':
        return DevicePlatform.ios;
      default:
        return DevicePlatform.unknown;
    }
  }

  void _showMessage(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  Future<void> _refreshDiscovery() async {
    await widget.discoveryService.broadcastOnce();
    if (mounted) {
      final error = widget.discoveryService.lastError;
      _showMessage(error == null ? '已发送局域网发现广播' : '发现广播异常：$error');
    }
  }

  Future<void> _refreshStorage() async {
    final selected = _selectedDevice;
    if (selected == null || (_isDesktop && selected.networkAddresses.isEmpty)) {
      if (mounted) {
        setState(() => _storage = null);
      }
      return;
    }
    if (_isDesktop &&
        selected.platform == DevicePlatform.android &&
        (_activeConnection == null ||
            _activeDevice?.deviceId != selected.deviceId)) {
      if (mounted) {
        setState(() {
          _storage = null;
          _storageLoading = false;
          _storageError = null;
        });
      }
      return;
    }
    final deviceId = selected.deviceId;
    if (mounted) {
      setState(() {
        _storageLoading = true;
        _storageError = null;
      });
    }
    try {
      final raw = _isDesktop && selected.platform == DevicePlatform.android
          ? await _invokeWorkspace(selected, 'getDeviceSummary')
          : await widget.dataService.readStorage();
      if (!mounted || _selectedDevice?.deviceId != deviceId) return;
      setState(() {
        _storage = raw is Map
            ? StorageInfo.fromJson(raw)
            : raw is StorageInfo
            ? raw
            : null;
        _storageLoading = false;
      });
    } catch (error) {
      if (!mounted || _selectedDevice?.deviceId != deviceId) return;
      setState(() {
        _storageLoading = false;
        _storageError = '$error';
      });
    }
  }

  Future<dynamic> _invokeWorkspace(
    Device device,
    String command, [
    Map<String, dynamic> payload = const {},
  ]) {
    final client = WorkspaceRemoteClient(widget.sessionManager);
    final active = _activeConnection;
    if (active != null &&
        _activeDevice?.deviceId == device.deviceId &&
        active.state == SessionState.connected) {
      return client.invokeOnConnection(active, command, payload);
    }
    return client.invoke(device, command, payload);
  }

  Future<void> _connect(Device device, {bool automatic = false}) async {
    if (device.networkAddresses.isEmpty) {
      _showMessage('还没有收到 ${device.name} 的局域网地址，请先刷新发现或检查同一 Wi-Fi');
      return;
    }
    if (_connectingDeviceId != null) {
      _showMessage('正在尝试连接另一台设备，请稍候');
      return;
    }
    final attemptGeneration = ++_connectionAttemptGeneration;
    _lastConnectedDevice = device;
    _userDisconnected = false;
    if (_activeDevice?.deviceId == device.deviceId &&
        _activeConnection != null) {
      _showMessage('${device.name} 已连接');
      return;
    }
    final existing = widget.sessionManager.connectionForDevice(device.deviceId);
    if (existing != null) {
      _watchConnectionData(existing);
      setState(() {
        _activeConnection = existing;
        _activeDevice = device;
      });
      await _refreshStorage();
      _showMessage('已恢复 ${device.name} 的设备会话');
      return;
    }
    setState(() => _connectingDeviceId = device.deviceId);
    _showMessage(
      automatic ? '正在自动连接历史设备 ${device.name}…' : '正在尝试连接 ${device.name}…',
    );
    Object? lastError;
    final previousConnection = _activeConnection;
    try {
      final addresses = await _orderedConnectionAddresses(
        device.networkAddresses,
      );
      SessionConnection? connection;
      var reverseConnection = false;
      // Start both directions at once. Duplicate sockets are collapsed by
      // SessionManager after the identity handshake, while the first usable
      // path wins without waiting for an inbound TCP timeout.
      for (final address in addresses) {
        widget.discoveryService.requestReverseConnection(address);
      }
      for (final address in addresses) {
        for (final port in {device.sessionPort, AppConstants.sessionTcpPort}) {
          try {
            connection = await widget.sessionManager.connectToPeer(
              InternetAddress(address),
              port,
            );
            break;
          } catch (error) {
            lastError = error;
          }
        }
        if (connection != null) break;
      }
      if (connection == null) {
        connection = await _waitForReverseConnection(device.deviceId);
        reverseConnection = connection != null;
      }
      if (connection == null) {
        throw StateError(lastError == null ? '没有可用的地址' : '$lastError');
      }
      if (attemptGeneration != _connectionAttemptGeneration ||
          (automatic && _userDisconnected)) {
        connection.dispose();
        return;
      }
      if (!reverseConnection) _watchConnectionData(connection);
      final peer =
          connection.peerInfo ??
          await connection.peerStream.first.timeout(
            const Duration(seconds: 3),
            onTimeout: () => throw TimeoutException('对方没有完成设备身份握手'),
          );
      if (peer.deviceId != device.deviceId) {
        connection.dispose();
        throw StateError('目标设备身份不匹配');
      }
      // Simultaneous direct and reverse attempts may briefly create two
      // sockets. Use the connection retained by SessionManager's deterministic
      // duplicate resolver before wiring feature streams to the UI.
      await Future<void>.delayed(const Duration(milliseconds: 20));
      connection =
          widget.sessionManager.connectionForDevice(device.deviceId) ??
          connection;
      if (attemptGeneration != _connectionAttemptGeneration ||
          (automatic && _userDisconnected)) {
        connection.dispose();
        return;
      }
      _connectionSubscription?.cancel();
      _connectionSubscription = connection.stateStream.listen((state) {
        if (!mounted || state != SessionState.disconnected) return;
        widget.discoveryService.registry.markSessionDisconnected(peer.deviceId);
        setState(() {
          if (identical(_activeConnection, connection)) {
            _activeConnection = null;
            _activeDevice = null;
          }
        });
        _showMessage('${device.name} 已断开');
      });
      if (previousConnection != null &&
          previousConnection.peerInfo?.deviceId != device.deviceId) {
        previousConnection.dispose();
      }
      _registerIncomingPeer(connection, peer);
      await _refreshStorage();
      _showMessage('已连接 ${device.name}');
    } catch (error) {
      _showMessage('连接 ${device.name} 失败：$error');
    } finally {
      if (mounted) setState(() => _connectingDeviceId = null);
    }
  }

  Future<SessionConnection?> _waitForReverseConnection(
    String deviceId, {
    Duration timeout = const Duration(seconds: 5),
  }) async {
    final deadline = DateTime.now().add(timeout);
    while (DateTime.now().isBefore(deadline)) {
      final connection = widget.sessionManager.connectionForDevice(deviceId);
      if (connection != null) return connection;
      await Future<void>.delayed(const Duration(milliseconds: 100));
    }
    return widget.sessionManager.connectionForDevice(deviceId);
  }

  Future<List<String>> _orderedConnectionAddresses(
    List<String> addresses,
  ) async {
    final unique = addresses.toSet().toList();
    try {
      final interfaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
        includeLinkLocal: false,
      );
      final localPrefixes = <String>{};
      for (final networkInterface in interfaces) {
        for (final address in networkInterface.addresses) {
          final parts = address.address.split('.');
          if (parts.length == 4) {
            localPrefixes.add('${parts[0]}.${parts[1]}.${parts[2]}.');
          }
        }
      }
      unique.sort((a, b) {
        int score(String value) {
          final parts = value.split('.');
          final prefix = parts.length == 4
              ? '${parts[0]}.${parts[1]}.${parts[2]}.'
              : '';
          if (localPrefixes.contains(prefix)) return 0;
          if (value.startsWith('127.') || value.startsWith('169.254.')) {
            return 3;
          }
          return 1;
        }

        return score(a).compareTo(score(b));
      });
    } catch (_) {}
    return unique;
  }

  void _disconnect() {
    final name = _activeDevice?.name ?? '设备';
    _connectionAttemptGeneration++;
    _connectionSubscription?.cancel();
    _connectionSubscription = null;
    _activeConnection?.dispose();
    _lastConnectedDevice = null;
    _userDisconnected = true;
    setState(() {
      _activeConnection = null;
      _activeDevice = null;
      _storage = null;
    });
    _showMessage('已断开 $name');
  }

  void _showManualIpDialog() {
    showDialog<void>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('通过 IP 地址连接'),
        content: TextField(
          controller: _ipController,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(
            labelText: '设备 IP 地址',
            hintText: '例如 192.168.1.105',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () {
              final ip = _ipController.text.trim();
              if (ip.isEmpty) return;
              widget.discoveryService.probeManualIp(ip);
              Navigator.pop(dialogContext);
              _showMessage('已向 $ip 发送定向探测');
            },
            child: const Text('探测'),
          ),
        ],
      ),
    );
  }

  Future<void> _refreshCalendar() async {
    if (mounted) {
      setState(() {
        _calendarLoading = true;
        _calendarError = null;
      });
    }
    try {
      final selected = _selectedDevice;
      if (_isDesktop) {
        if (selected == null) {
          _calendarEvents = [];
          _calendarPermission = true;
          _calendarError = '请先连接 Android 手机，再读取手机日历';
        } else if (selected.platform == DevicePlatform.android) {
          if (_activeConnection == null ||
              _activeDevice?.deviceId != selected.deviceId) {
            _calendarEvents = [];
            _calendarPermission = true;
            _calendarError = '请先建立设备会话，再读取手机日历';
          } else {
            final raw = await _invokeWorkspace(selected, 'calendarEvents');
            _calendarEvents = raw is List
                ? raw.whereType<Map>().map(CalendarEvent.fromJson).toList()
                : [];
            _calendarPermission = true;
          }
        } else {
          _calendarEvents = [];
          _calendarPermission = true;
          _calendarError = '当前选择的设备不是 Android 手机';
        }
      } else {
        _calendarPermission = await widget.dataService.hasCalendarPermission();
        if (_calendarPermission == true) {
          _calendarEvents = await widget.dataService.loadCalendar();
        }
      }
    } on PlatformException catch (error) {
      _calendarError = error.message ?? error.code;
    } catch (error) {
      _calendarError = '$error';
    }
    if (mounted) setState(() => _calendarLoading = false);
  }

  Future<void> _requestCalendarPermission() async {
    await widget.dataService.requestCalendarPermission();
    await _refreshCalendar();
  }

  Future<void> _refreshPhotos() async {
    if (_selectedAlbumId == null) {
      await _refreshPhotoAlbums();
    } else {
      await _refreshAlbumPhotos(_selectedAlbumId!);
    }
  }

  Future<void> _refreshPhotoAlbums() async {
    _photoBytesCache.clear();
    _photoPreviewBytesCache.clear();
    if (mounted) {
      setState(() {
        _photosLoading = true;
        _photosError = null;
        _selectedAlbumId = null;
        _photos = [];
        _photosOffset = 0;
        _photosTotal = 0;
      });
    }
    try {
      final selected = _selectedDevice;
      if (_isDesktop) {
        if (selected == null) {
          _photoAlbums = [];
          _photosPermission = true;
          _photosError = '请先连接 Android 手机，再读取手机相册集';
        } else if (selected.platform == DevicePlatform.android) {
          if (_activeConnection == null ||
              _activeDevice?.deviceId != selected.deviceId) {
            _photoAlbums = [];
            _photosPermission = true;
            _photosError = '请先建立设备会话，再读取手机相册集';
          } else {
            final raw = await _invokeWorkspace(selected, 'photoAlbums');
            _photoAlbums = raw is List
                ? raw.whereType<Map>().map(PhotoAlbum.fromJson).toList()
                : [];
            _photosPermission = true;
          }
        } else {
          _photoAlbums = [];
          _photosPermission = true;
          _photosError = '当前选择的设备不是 Android 手机';
        }
      } else {
        _photosPermission = await widget.dataService.hasPhotosPermission();
        if (_photosPermission == true) {
          _photoAlbums = await widget.dataService.loadPhotoAlbums();
        }
      }
    } on PlatformException catch (error) {
      _photosError = error.message ?? error.code;
    } catch (error) {
      _photosError = '$error';
    }
    if (mounted) setState(() => _photosLoading = false);
  }

  Future<void> _openPhotoAlbum(PhotoAlbum album) async {
    setState(() {
      _selectedAlbumId = album.id;
      _photos = [];
      _photosError = null;
    });
    await _refreshAlbumPhotos(album.id);
  }

  Future<void> _refreshAlbumPhotos(String albumId) async {
    _photoBytesCache.clear();
    _photoPreviewBytesCache.clear();
    await _loadMorePhotoPage(albumId, reset: true);
  }

  Future<void> _loadMorePhotoPage(String albumId, {required bool reset}) async {
    if (_photoPageRequestInFlight ||
        (!reset && _photosOffset >= _photosTotal)) {
      return;
    }
    if (reset) {
      _photosOffset = 0;
      _photosTotal = 0;
      _photos = [];
    }
    _photoPageRequestInFlight = true;
    if (mounted) {
      setState(() {
        _photosLoading = true;
        _photosError = null;
      });
    }

    try {
      final selected = _selectedDevice;
      PhotoPage page;
      if (_isDesktop) {
        if (selected == null) {
          _photosPermission = true;
          _photosError = '请先连接 Android 手机，再读取相册内容';
          return;
        } else if (selected.platform == DevicePlatform.android) {
          if (_activeConnection == null ||
              _activeDevice?.deviceId != selected.deviceId) {
            _photosPermission = true;
            _photosError = '请先建立设备会话，再读取相册内容';
            return;
          } else {
            try {
              final raw = await _invokeWorkspace(selected, 'photosPage', {
                'albumId': albumId,
                'offset': _photosOffset,
                'limit': _photoPageSize,
              });
              page = raw is Map
                  ? PhotoPage.fromJson(raw)
                  : const PhotoPage(items: [], total: 0);
            } on PlatformException {
              // Keep older Android peers usable while the paged command is
              // being rolled out: only the current window is retained.
              final raw = await _invokeWorkspace(selected, 'photos', {
                'albumId': albumId,
              });
              final all = raw is List
                  ? raw.whereType<Map>().map(PhotoItem.fromJson).toList()
                  : <PhotoItem>[];
              final start = _photosOffset.clamp(0, all.length).toInt();
              page = PhotoPage(
                items: all.skip(start).take(_photoPageSize).toList(),
                total: all.length,
              );
            }
            _photosPermission = true;
          }
        } else {
          _photosPermission = true;
          _photosError = '当前选择的设备不是 Android 手机';
          return;
        }
      } else {
        _photosPermission = await widget.dataService.hasPhotosPermission();
        if (_photosPermission == true) {
          page = await widget.dataService.loadPhotoPage(
            albumId: albumId,
            offset: _photosOffset,
            limit: _photoPageSize,
          );
        } else {
          return;
        }
      }

      if (!mounted || _selectedAlbumId != albumId) return;
      if (reset) _photos = [];
      _photos.addAll(page.items);
      _photosTotal = page.total;
      _photosOffset = (_photosOffset + page.items.length)
          .clamp(0, _photosTotal)
          .toInt();
      if (page.items.isEmpty && _photosOffset < _photosTotal) {
        // A provider can skip rows while still reporting the full count;
        // advance to the end rather than repeatedly requesting an empty page.
        _photosOffset = _photosTotal;
      }
    } on PlatformException catch (error) {
      _photosError = error.message ?? error.code;
    } catch (error) {
      _photosError = '$error';
    } finally {
      _photoPageRequestInFlight = false;
      if (mounted) setState(() => _photosLoading = false);
    }
  }

  Future<void> _requestPhotosPermission() async {
    await widget.dataService.requestPhotosPermission();
    await _refreshPhotos();
  }

  Future<void> _refreshFiles() async {
    if (mounted) {
      setState(() {
        _filesLoading = true;
        _filesError = null;
      });
    }
    try {
      final selected = _selectedDevice;
      final raw =
          _isDesktop &&
              selected != null &&
              selected.platform == DevicePlatform.android
          ? (_activeConnection != null &&
                    _activeDevice?.deviceId == selected.deviceId
                ? await _invokeWorkspace(selected, 'files')
                : null)
          : await widget.dataService.loadFiles();
      if (raw is List<RemoteFileItem>) {
        _files = raw;
      } else if (raw is List) {
        _files = raw.whereType<Map>().map(RemoteFileItem.fromJson).toList();
      } else {
        _files = [];
      }
    } on PlatformException catch (error) {
      _filesError = error.message ?? error.code;
    } catch (error) {
      _filesError = '$error';
    }
    if (mounted) setState(() => _filesLoading = false);
  }

  Future<void> _refreshNotes() async {
    if (mounted) setState(() => _notesLoading = true);
    try {
      final notes = await widget.dataService.loadNotes();
      notes.sort((a, b) {
        final pinned = (b.pinned ? 1 : 0).compareTo(a.pinned ? 1 : 0);
        return pinned != 0 ? pinned : b.updatedAt.compareTo(a.updatedAt);
      });
      _notes = notes;
      _notesError = null;
    } catch (error) {
      _notesError = '$error';
    } finally {
      if (mounted) setState(() => _notesLoading = false);
    }
  }

  Future<void> _refreshTasks() async {
    if (mounted) setState(() => _tasksLoading = true);
    try {
      _tasks = await widget.dataService.loadTasks();
      _tasksError = null;
    } catch (error) {
      _tasksError = '$error';
    } finally {
      if (mounted) setState(() => _tasksLoading = false);
    }
  }

  Future<void> _editNote([WorkspaceNote? note]) async {
    final titleController = TextEditingController(text: note?.title ?? '');
    final contentController = TextEditingController(text: note?.content ?? '');
    final save = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(note == null ? '新建笔记' : '编辑笔记'),
        content: SizedBox(
          width: 480,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: titleController,
                autofocus: true,
                decoration: const InputDecoration(labelText: '标题'),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: contentController,
                minLines: 4,
                maxLines: 8,
                decoration: const InputDecoration(labelText: '内容'),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('保存'),
          ),
        ],
      ),
    );
    if (save == true) {
      final title = titleController.text.trim();
      final content = contentController.text.trim();
      if (title.isEmpty && content.isEmpty) {
        _showMessage('标题和内容不能同时为空');
      } else {
        final updated =
            note?.copyWith(
              title: title.isEmpty ? '未命名笔记' : title,
              content: content,
              updatedAt: DateTime.now().millisecondsSinceEpoch,
            ) ??
            WorkspaceNote(
              id: WorkspaceDataService.newId(),
              title: title.isEmpty ? '未命名笔记' : title,
              content: content,
              updatedAt: DateTime.now().millisecondsSinceEpoch,
            );
        try {
          await widget.dataService.saveNote(updated);
          await _refreshNotes();
        } catch (error) {
          _showMessage('保存笔记失败：$error');
        }
      }
    }
    titleController.dispose();
    contentController.dispose();
  }

  Future<void> _deleteNote(WorkspaceNote note) async {
    final confirmed = await _confirm('删除笔记', '确定删除“${note.title}”吗？');
    if (!confirmed) return;
    try {
      await widget.dataService.deleteNote(note.id);
      await _refreshNotes();
    } catch (error) {
      _showMessage('删除笔记失败：$error');
    }
  }

  Future<void> _toggleNotePinned(WorkspaceNote note) async {
    try {
      await widget.dataService.saveNote(
        note.copyWith(
          pinned: !note.pinned,
          updatedAt: DateTime.now().millisecondsSinceEpoch,
        ),
      );
      await _refreshNotes();
    } catch (error) {
      _showMessage('更新笔记失败：$error');
    }
  }

  Future<void> _editTask([WorkspaceTask? task]) async {
    final controller = TextEditingController(text: task?.title ?? '');
    DateTime? dueDate = task?.dueAt == null
        ? null
        : DateTime.fromMillisecondsSinceEpoch(task!.dueAt!);
    bool clearDueDate = false;
    final save = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: Text(task == null ? '新建待办' : '编辑待办'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: controller,
                autofocus: true,
                decoration: const InputDecoration(labelText: '待办内容'),
                onSubmitted: (_) => Navigator.pop(dialogContext, true),
              ),
              const SizedBox(height: 8),
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: Icon(
                  Symbols.event_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
                title: Text(
                  dueDate == null
                      ? '无截止日期'
                      : '截止 ${dueDate!.year}/${dueDate!.month}/${dueDate!.day}',
                ),
                trailing: dueDate == null
                    ? const Icon(Symbols.chevron_right_rounded)
                    : IconButton(
                        tooltip: '清除截止日期',
                        onPressed: () => setDialogState(() {
                          dueDate = null;
                          clearDueDate = true;
                        }),
                        icon: const Icon(Symbols.close_rounded),
                      ),
                onTap: () async {
                  final initial = dueDate ?? DateTime.now();
                  final picked = await showDatePicker(
                    context: dialogContext,
                    locale: const Locale('zh', 'CN'),
                    firstDate: DateTime(2000),
                    lastDate: DateTime(2100),
                    initialDate: initial,
                  );
                  if (picked == null) return;
                  setDialogState(() {
                    dueDate = DateTime(picked.year, picked.month, picked.day);
                    clearDueDate = false;
                  });
                },
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext, true),
              child: const Text('保存'),
            ),
          ],
        ),
      ),
    );
    if (save == true) {
      final title = controller.text.trim();
      if (title.isEmpty) {
        _showMessage('待办内容不能为空');
      } else {
        final updatedAt = DateTime.now().millisecondsSinceEpoch;
        final updated = task == null
            ? WorkspaceTask(
                id: WorkspaceDataService.newId(),
                title: title,
                dueAt: dueDate?.millisecondsSinceEpoch,
                updatedAt: updatedAt,
              )
            : WorkspaceTask(
                id: task.id,
                title: title,
                completed: task.completed,
                dueAt: clearDueDate
                    ? null
                    : dueDate?.millisecondsSinceEpoch ?? task.dueAt,
                updatedAt: updatedAt,
              );
        try {
          await widget.dataService.saveTask(updated);
          await _refreshTasks();
        } catch (error) {
          _showMessage('保存待办失败：$error');
        }
      }
    }
    controller.dispose();
  }

  Future<void> _toggleTask(WorkspaceTask task, bool value) async {
    try {
      await widget.dataService.saveTask(
        task.copyWith(
          completed: value,
          updatedAt: DateTime.now().millisecondsSinceEpoch,
        ),
      );
      await _refreshTasks();
    } catch (error) {
      _showMessage('更新待办失败：$error');
    }
  }

  Future<void> _deleteTask(WorkspaceTask task) async {
    final confirmed = await _confirm('删除待办', '确定删除“${task.title}”吗？');
    if (!confirmed) return;
    try {
      await widget.dataService.deleteTask(task.id);
      await _refreshTasks();
    } catch (error) {
      _showMessage('删除待办失败：$error');
    }
  }

  Future<bool> _confirm(String title, String message) async {
    return await showDialog<bool>(
          context: context,
          builder: (dialogContext) => AlertDialog(
            title: Text(title),
            content: Text(message),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext, false),
                child: const Text('取消'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(dialogContext, true),
                child: const Text('确定'),
              ),
            ],
          ),
        ) ??
        false;
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final desktop = constraints.maxWidth >= 900;
        final bottomNavigation =
            !_isDesktop ||
            _workspaceState.navigationStyle == AppNavigationStyle.bottom ||
            _workspaceState.floatingCapsuleNavigation;
        final hasDrawer = _isDesktop && !desktop && !bottomNavigation;
        final floatingCapsule =
            bottomNavigation && _workspaceState.floatingCapsuleNavigation;
        return PopScope(
          canPop: _isDesktop,
          onPopInvokedWithResult: (didPop, result) {
            if (!didPop && !_isDesktop) _handleMobileBack();
          },
          child: Scaffold(
            drawer: hasDrawer ? _buildDrawer() : null,
            appBar: _buildAppBar(desktop, hasDrawer: hasDrawer),
            bottomNavigationBar:
                bottomNavigation && !_workspaceState.floatingCapsuleNavigation
                ? _buildBottomNavigationBar()
                : null,
            body: Stack(
              fit: StackFit.expand,
              children: [
                Row(
                  children: [
                    if (desktop && !bottomNavigation)
                      _buildNavigationRail(constraints.maxWidth),
                    Expanded(child: _buildAnimatedPage()),
                  ],
                ),
                if (floatingCapsule)
                  Positioned(
                    left: 0,
                    right: 0,
                    bottom: 0,
                    child: _buildFloatingNavigationBar(),
                  ),
              ],
            ),
          ),
        );
      },
    );
  }

  PreferredSizeWidget _buildAppBar(bool desktop, {bool hasDrawer = false}) {
    final showMobileBack =
        !_isDesktop &&
        (_mobileWorkspaceFocus != null ||
            _workspaceState.currentTabIndex == 1 ||
            _workspaceState.currentTabIndex == 2 ||
            _workspaceState.currentTabIndex == 4 ||
            _workspaceState.currentTabIndex == 5);
    return AppBar(
      automaticallyImplyLeading: hasDrawer,
      leading: showMobileBack
          ? IconButton(
              tooltip: '返回上一页',
              icon: const Icon(Symbols.arrow_back_rounded),
              onPressed: _handleMobileBack,
            )
          : !hasDrawer
          ? null
          : Builder(
              builder: (context) => IconButton(
                tooltip: '打开导航栏',
                icon: const Icon(Symbols.menu_rounded),
                onPressed: () => Scaffold.of(context).openDrawer(),
              ),
            ),
      title: Text(
        !_isDesktop && _showMobileWorkspaceOverview
            ? '工作区'
            : !_isDesktop && _mobileWorkspaceFocus == 'tasks'
            ? '待办'
            : _pageTitles[_workspaceState.currentTabIndex],
      ),
      actions: [
        if (_workspaceState.currentTabIndex == 3 &&
            _mobileWorkspaceFocus == 'notes')
          IconButton(
            tooltip: '新建笔记',
            icon: const Icon(Symbols.edit_rounded),
            onPressed: _editNote,
          )
        else if (_workspaceState.currentTabIndex == 3 &&
            _mobileWorkspaceFocus == 'tasks')
          IconButton(
            tooltip: '新建待办',
            icon: const Icon(Symbols.edit_rounded),
            onPressed: _editTask,
          )
        else if (_workspaceState.currentTabIndex == 3 && _isDesktop)
          PopupMenuButton<String>(
            tooltip: '新建工作项',
            icon: const Icon(Symbols.edit_rounded),
            onSelected: (value) {
              if (value == 'note') {
                _editNote();
              } else if (value == 'task') {
                _editTask();
              }
            },
            itemBuilder: (context) => const [
              PopupMenuItem<String>(value: 'note', child: Text('新建笔记')),
              PopupMenuItem<String>(value: 'task', child: Text('新建待办')),
            ],
          )
        else if (_workspaceState.currentTabIndex != 4 &&
            _workspaceState.currentTabIndex != 5)
          IconButton(
            tooltip: '刷新设备发现',
            icon: const Icon(Symbols.refresh_rounded),
            onPressed: _refreshDiscovery,
          ),
        const SizedBox(width: 8),
      ],
    );
  }

  Widget _buildBottomNavigationBar() {
    return NavigationBar(
      selectedIndex: _selectedNavigationIndex,
      onDestinationSelected: _selectNavigationDestination,
      destinations: _navigationBarDestinations,
    );
  }

  Widget _buildFloatingNavigationBar() {
    final scheme = Theme.of(context).colorScheme;
    final destinations = _navigationBarDestinations;
    return SafeArea(
      top: false,
      minimum: const EdgeInsets.only(bottom: 12),
      child: Align(
        alignment: Alignment.bottomCenter,
        child: FractionallySizedBox(
          widthFactor: 0.62,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 260),
            child: Material(
              color: scheme.surfaceContainer,
              elevation: 8,
              shadowColor: scheme.shadow.withValues(alpha: 0.28),
              shape: StadiumBorder(
                side: BorderSide(color: scheme.outlineVariant),
              ),
              clipBehavior: Clip.antiAlias,
              child: Padding(
                padding: const EdgeInsets.all(4),
                child: Row(
                  children: List.generate(destinations.length, (index) {
                    final destination = destinations[index];
                    final selected = index == _selectedNavigationIndex;
                    final foreground = selected
                        ? scheme.onSecondaryContainer
                        : scheme.onSurfaceVariant;
                    return Expanded(
                      child: Semantics(
                        button: true,
                        selected: selected,
                        label: destination.label,
                        child: Tooltip(
                          message: destination.label,
                          child: InkWell(
                            onTap: () => _selectNavigationDestination(index),
                            borderRadius: BorderRadius.circular(28),
                            child: AnimatedContainer(
                              duration: const Duration(milliseconds: 180),
                              curve: Curves.easeOutCubic,
                              padding: const EdgeInsets.symmetric(
                                horizontal: 3,
                                vertical: 5,
                              ),
                              decoration: BoxDecoration(
                                color: selected
                                    ? scheme.secondaryContainer
                                    : Colors.transparent,
                                borderRadius: BorderRadius.circular(28),
                              ),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  IconTheme(
                                    data: IconThemeData(
                                      color: foreground,
                                      size: 20,
                                    ),
                                    child: selected
                                        ? (destination.selectedIcon ??
                                              destination.icon)
                                        : destination.icon,
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    destination.label,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      color: foreground,
                                      fontSize: 11,
                                      fontWeight: selected
                                          ? FontWeight.w700
                                          : FontWeight.w500,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),
                    );
                  }),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildAnimatedPage() {
    final pageKey = ValueKey<String>(
      '${_workspaceState.currentTabIndex}-$_showMobileWorkspaceOverview-$_mobileWorkspaceFocus',
    );
    return AnimatedSwitcher(
      // Keep one consistent page-level motion for navigation destinations.
      // The previous 180ms/2.5% transition was too subtle on phones and made
      // a page switch feel like a hard cut when a page was also loading data.
      duration: const Duration(milliseconds: 280),
      reverseDuration: const Duration(milliseconds: 220),
      switchInCurve: Curves.easeOutCubic,
      switchOutCurve: Curves.easeInCubic,
      layoutBuilder: (currentChild, previousChildren) => Stack(
        fit: StackFit.expand,
        clipBehavior: Clip.hardEdge,
        children: <Widget>[
          ...previousChildren,
          ...?currentChild == null ? null : <Widget>[currentChild],
        ],
      ),
      transitionBuilder: (child, animation) {
        final curvedAnimation = CurvedAnimation(
          parent: animation,
          curve: Curves.easeOutCubic,
          reverseCurve: Curves.easeInCubic,
        );
        final slide = Tween<Offset>(
          begin: const Offset(0.04, 0),
          end: Offset.zero,
        ).animate(curvedAnimation);
        return FadeTransition(
          opacity: curvedAnimation,
          child: SlideTransition(position: slide, child: child),
        );
      },
      child: RepaintBoundary(key: pageKey, child: _buildPage()),
    );
  }

  Widget _buildNavigationRail(double width) {
    return NavigationRail(
      extended: width >= 1180,
      minExtendedWidth: 224,
      labelType: width >= 1180 ? null : NavigationRailLabelType.all,
      selectedIndex: _workspaceState.currentTabIndex,
      onDestinationSelected: _setPage,
      leading: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 28),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            const _HingeLogo(size: 40),
            if (width >= 1180) ...[
              const SizedBox(width: 12),
              Text(
                'Hinge',
                style: Theme.of(context).textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w700),
              ),
            ],
          ],
        ),
      ),
      destinations: _navigationRailDestinations,
    );
  }

  Widget _buildDrawer() {
    return NavigationDrawer(
      selectedIndex: _workspaceState.currentTabIndex,
      onDestinationSelected: (index) {
        Navigator.pop(context);
        _setPage(index);
      },
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 18),
          child: Row(
            children: [
              const _HingeLogo(size: 44),
              const SizedBox(width: 12),
              const Expanded(
                child: Text(
                  'Hinge',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
                ),
              ),
            ],
          ),
        ),
        ..._navigationDrawerDestinations,
      ],
    );
  }

  List<NavigationRailDestination> get _navigationRailDestinations => const [
    NavigationRailDestination(
      icon: Icon(Symbols.home_rounded),
      selectedIcon: Icon(Symbols.home_rounded, fill: 1),
      label: Text('首页'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.devices_rounded),
      selectedIcon: Icon(Symbols.devices_rounded, fill: 1),
      label: Text('已连接的机型'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.add_link_rounded),
      selectedIcon: Icon(Symbols.add_link_rounded, fill: 1),
      label: Text('连接设备'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.note_rounded),
      selectedIcon: Icon(Symbols.note_rounded, fill: 1),
      label: Text('笔记代办'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.calendar_month_rounded),
      selectedIcon: Icon(Symbols.calendar_month_rounded, fill: 1),
      label: Text('日历'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.photo_library_rounded),
      selectedIcon: Icon(Symbols.photo_library_rounded, fill: 1),
      label: Text('相册'),
    ),
    NavigationRailDestination(
      icon: Icon(Symbols.settings_rounded),
      selectedIcon: Icon(Symbols.settings_rounded, fill: 1),
      label: Text('设置'),
    ),
  ];

  List<Widget> get _navigationDrawerDestinations => const [
    NavigationDrawerDestination(
      icon: Icon(Symbols.home_rounded),
      selectedIcon: Icon(Symbols.home_rounded, fill: 1),
      label: Text('首页'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.devices_rounded),
      selectedIcon: Icon(Symbols.devices_rounded, fill: 1),
      label: Text('已连接的机型'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.add_link_rounded),
      selectedIcon: Icon(Symbols.add_link_rounded, fill: 1),
      label: Text('连接设备'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.note_rounded),
      selectedIcon: Icon(Symbols.note_rounded, fill: 1),
      label: Text('笔记代办'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.calendar_month_rounded),
      selectedIcon: Icon(Symbols.calendar_month_rounded, fill: 1),
      label: Text('日历'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.photo_library_rounded),
      selectedIcon: Icon(Symbols.photo_library_rounded, fill: 1),
      label: Text('相册'),
    ),
    NavigationDrawerDestination(
      icon: Icon(Symbols.settings_rounded),
      selectedIcon: Icon(Symbols.settings_rounded, fill: 1),
      label: Text('设置'),
    ),
  ];

  List<NavigationDestination> get _navigationBarDestinations => _isDesktop
      ? const [
          NavigationDestination(
            icon: Icon(Symbols.home_rounded),
            selectedIcon: Icon(Symbols.home_rounded, fill: 1),
            label: '首页',
          ),
          NavigationDestination(
            icon: Icon(Symbols.devices_rounded),
            selectedIcon: Icon(Symbols.devices_rounded, fill: 1),
            label: '机型',
          ),
          NavigationDestination(
            icon: Icon(Symbols.add_link_rounded),
            selectedIcon: Icon(Symbols.add_link_rounded, fill: 1),
            label: '连接',
          ),
          NavigationDestination(
            icon: Icon(Symbols.note_rounded),
            selectedIcon: Icon(Symbols.note_rounded, fill: 1),
            label: '笔记',
          ),
          NavigationDestination(
            icon: Icon(Symbols.calendar_month_rounded),
            selectedIcon: Icon(Symbols.calendar_month_rounded, fill: 1),
            label: '日历',
          ),
          NavigationDestination(
            icon: Icon(Symbols.photo_library_rounded),
            selectedIcon: Icon(Symbols.photo_library_rounded, fill: 1),
            label: '相册',
          ),
          NavigationDestination(
            icon: Icon(Symbols.settings_rounded),
            selectedIcon: Icon(Symbols.settings_rounded, fill: 1),
            label: '设置',
          ),
        ]
      : const [
          NavigationDestination(
            icon: Icon(Symbols.home_rounded),
            selectedIcon: Icon(Symbols.home_rounded, fill: 1),
            label: '首页',
          ),
          NavigationDestination(
            icon: Icon(Symbols.workspaces_rounded),
            selectedIcon: Icon(Symbols.workspaces_rounded, fill: 1),
            label: '工作区',
          ),
          NavigationDestination(
            icon: Icon(Symbols.settings_rounded),
            selectedIcon: Icon(Symbols.settings_rounded, fill: 1),
            label: '设置',
          ),
        ];

  Widget _buildPage() {
    if (!_isDesktop && _showMobileWorkspaceOverview) {
      return _buildMobileWorkspacePage();
    }
    if (!_isDesktop && _mobileWorkspaceFocus != null) {
      return _buildNotesPage(focus: _mobileWorkspaceFocus);
    }
    if (!_isDesktop && _workspaceState.currentTabIndex == 0) {
      return _buildMobileHomePage();
    }
    switch (_workspaceState.currentTabIndex) {
      case 1:
        return _buildConnectedModelsPage();
      case 2:
        return _buildConnectPage();
      case 3:
        return _buildNotesPage();
      case 4:
        return _buildCalendarPage();
      case 5:
        return _buildPhotosPage();
      case 6:
        return _buildSettingsPage();
      default:
        return _buildHomePage();
    }
  }

  Widget _pageBody(Widget child) {
    final bottomPadding =
        !_isDesktop && _workspaceState.floatingCapsuleNavigation
        ? 136 + MediaQuery.viewPaddingOf(context).bottom
        : 32.0;
    return SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(24, 8, 24, bottomPadding),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 1180),
          child: child,
        ),
      ),
    );
  }

  Widget _buildMobileHomePage() {
    final selected = _selectedDevice;
    final connectedCount = _allDevices
        .where((device) => _isDiscovered(device))
        .length;
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildWelcomeHeader(selected),
          const SizedBox(height: 16),
          _buildSectionTitle('设备', '管理当前会话，或在局域网中连接新设备'),
          const SizedBox(height: 10),
          Card(
            child: Column(
              children: [
                _mobileHomeEntry(
                  icon: Symbols.devices_rounded,
                  title: '已连接的机型',
                  subtitle: '$connectedCount 台设备已连接',
                  onTap: () => _setPage(1),
                ),
                const Divider(height: 1),
                _mobileHomeEntry(
                  icon: Symbols.add_link_rounded,
                  title: '连接设备',
                  subtitle: '发现同一局域网中的手机或电脑',
                  onTap: () => _setPage(2),
                ),
              ],
            ),
          ),
          if (selected != null) ...[
            const SizedBox(height: 16),
            _buildDeviceSummaryCard(selected),
            const SizedBox(height: 16),
            _buildSectionTitle('设备操作', '剪贴板和文件传输都从这里开始'),
            const SizedBox(height: 10),
            _buildOperationsCard(selected),
          ],
        ],
      ),
    );
  }

  Widget _mobileHomeEntry({
    required IconData icon,
    required String title,
    required String subtitle,
    required VoidCallback onTap,
  }) {
    final scheme = Theme.of(context).colorScheme;
    return ListTile(
      leading: CircleAvatar(
        backgroundColor: scheme.primaryContainer,
        foregroundColor: scheme.onPrimaryContainer,
        child: Icon(icon),
      ),
      title: Text(title),
      subtitle: Text(subtitle),
      trailing: const Icon(Symbols.chevron_right_rounded),
      onTap: onTap,
    );
  }

  Widget _buildMobileWorkspacePage() {
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '笔记、待办、日历和相册集中在这里',
            style: Theme.of(context).textTheme.bodyLarge,
          ),
          const SizedBox(height: 16),
          _mobileWorkspaceEntry(
            icon: Symbols.note_rounded,
            title: '笔记',
            subtitle: '${_notes.length} 篇笔记，支持新建、编辑和删除',
            onTap: () => _openMobileWorkspaceSection('notes'),
          ),
          _mobileWorkspaceEntry(
            icon: Symbols.checklist_rounded,
            title: '待办',
            subtitle:
                '${_tasks.where((task) => !task.completed).length} 项待完成任务',
            onTap: () => _openMobileWorkspaceSection('tasks'),
          ),
          _mobileWorkspaceEntry(
            icon: Symbols.calendar_month_rounded,
            title: '日历',
            subtitle: '${_calendarEvents.length} 项手机日程',
            onTap: () => _setPage(4),
          ),
          _mobileWorkspaceEntry(
            icon: Symbols.photo_library_rounded,
            title: '相册',
            subtitle: '${_photoAlbums.length} 个相册集，按最新照片显示封面',
            onTap: () => _setPage(5),
          ),
        ],
      ),
    );
  }

  Widget _mobileWorkspaceEntry({
    required IconData icon,
    required String title,
    required String subtitle,
    required VoidCallback onTap,
  }) {
    final scheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        child: ListTile(
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 18,
            vertical: 8,
          ),
          leading: CircleAvatar(
            backgroundColor: scheme.secondaryContainer,
            foregroundColor: scheme.onSecondaryContainer,
            child: Icon(icon),
          ),
          title: Text(title),
          subtitle: Text(subtitle),
          trailing: const Icon(Symbols.chevron_right_rounded),
          onTap: onTap,
        ),
      ),
    );
  }

  Widget _buildHomePage() {
    final selected = _selectedDevice;
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildWelcomeHeader(selected),
          const SizedBox(height: 20),
          _buildDeviceSummaryCard(selected),
          const SizedBox(height: 20),
          _buildSectionTitle('设备操作', '只显示当前版本真正可用的入口'),
          const SizedBox(height: 10),
          _buildOperationsCard(selected),
          const SizedBox(height: 20),
          _buildSectionTitle('工作区', '手机日程、笔记、待办和相册都从对应数据源读取'),
          const SizedBox(height: 10),
          _buildWorkspaceSummary(),
          const SizedBox(height: 20),
          _buildFilesCard(selected),
        ],
      ),
    );
  }

  Widget _buildWelcomeHeader(Device? selected) {
    final name = selected?.name ?? '尚未连接手机';
    final brandAsset = _isDesktop
        ? _brandAssetFor(selected)
        : _brandAssetForIdentity(widget.localIdentity);
    return Card(
      color: Theme.of(context).colorScheme.primaryContainer,
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Hinge Work',
                    style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      color: Theme.of(context).colorScheme.onPrimaryContainer,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    selected == null
                        ? '连接手机后，在一处查看文件、日程和工作内容。'
                        : '当前设备 · $name',
                    style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                      color: Theme.of(context).colorScheme.onPrimaryContainer,
                    ),
                  ),
                ],
              ),
            ),
            brandAsset == null
                ? Icon(
                    selected == null
                        ? Symbols.devices_other_rounded
                        : Symbols.phone_android_rounded,
                    size: 48,
                    color: Theme.of(context).colorScheme.onPrimaryContainer,
                  )
                : Container(
                    width: 56,
                    height: 48,
                    padding: const EdgeInsets.symmetric(horizontal: 6),
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: SvgPicture.asset(
                      'assets/brands/$brandAsset',
                      fit: BoxFit.contain,
                    ),
                  ),
          ],
        ),
      ),
    );
  }

  String? _brandAssetFor(Device? device) {
    if (device == null || device.platform != DevicePlatform.android) {
      return null;
    }
    return _brandAssetForValues(device.manufacturer, device.model);
  }

  String? _brandAssetForIdentity(DeviceIdentity identity) {
    return _brandAssetForValues(identity.manufacturer, identity.model);
  }

  String? _brandAssetForValues(String manufacturer, String model) {
    final name = '$manufacturer $model'.toLowerCase();
    if (name.contains('honor')) return 'honor.svg';
    if (name.contains('vivo')) return 'vivo.svg';
    if (name.contains('xiaomi') ||
        name.contains('redmi') ||
        name.contains('poco')) {
      return 'xiaomi.svg';
    }
    if (name.contains('samsung') || name.contains('galaxy')) {
      return 'samsung.svg';
    }
    if (name.contains('huawei')) return 'huawei.svg';
    if (name.contains('oppo')) return 'oppo.svg';
    return null;
  }

  Widget _buildDeviceSummaryCard(Device? selected) {
    if (selected == null) {
      return _emptyCard(
        icon: Symbols.devices_other_rounded,
        title: '没有已连接的机型',
        message: '从“连接设备”开始，设备出现后直接连接即可使用。',
        action: FilledButton.icon(
          onPressed: () => _setPage(2),
          icon: const Icon(Symbols.add_link_rounded),
          label: const Text('连接设备'),
        ),
      );
    }
    final online =
        _activeDevice?.deviceId == selected.deviceId &&
        _activeConnection != null;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                _deviceIcon(selected.platform, size: 40),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        selected.name,
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        online
                            ? '已建立会话 · ${selected.networkAddresses.join(', ')}'
                            : _isDiscovered(selected)
                            ? '已发现 · 可建立会话'
                            : '等待设备上线',
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                if (online)
                  OutlinedButton.icon(
                    onPressed: _disconnect,
                    icon: const Icon(Symbols.link_off_rounded),
                    label: const Text('断开连接'),
                  )
                else
                  FilledButton.icon(
                    onPressed:
                        _connectingDeviceId == null && _isDiscovered(selected)
                        ? () => _connect(selected)
                        : () => _setPage(2),
                    icon: Icon(
                      _isDiscovered(selected)
                          ? Symbols.link_rounded
                          : Symbols.search_rounded,
                    ),
                    label: Text(
                      _connectingDeviceId == selected.deviceId
                          ? '连接中…'
                          : _isDiscovered(selected)
                          ? '连接'
                          : '查找设备',
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 20),
            if (_storageLoading) const LinearProgressIndicator(),
            if (_storageError != null) ...[
              _inlineError(_storageError!),
              const SizedBox(height: 10),
            ],
            Text('手机存储', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 8),
            if (_storage != null) ...[
              Row(
                children: [
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(8),
                      child: LinearProgressIndicator(
                        minHeight: 10,
                        value: _storage!.usedRatio.clamp(0.0, 1.0),
                        backgroundColor: Theme.of(context)
                            .colorScheme
                            .surfaceContainerHighest,
                      ),
                    ),
                  ),
                  const SizedBox(width: 14),
                  Text(
                    '${_formatBytes(_storage!.usedBytes)} / ${_formatBytes(_storage!.totalBytes)}',
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                ],
              ),
              const SizedBox(height: 5),
              Text(
                '剩余 ${_formatBytes(_storage!.freeBytes)}',
                style: TextStyle(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ] else if (!_storageLoading) ...[
              Text(
                _isDesktop && selected.networkAddresses.isEmpty
                    ? '设备尚未上线，连接后读取存储情况'
                    : '当前设备没有返回存储信息',
                style: TextStyle(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildOperationsCard(Device? selected) {
    final canUse = selected != null;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            ActionChip(
              avatar: const Icon(Symbols.folder_rounded, size: 20),
              label: const Text('文件管理'),
              onPressed: canUse ? _refreshFiles : null,
            ),
            ActionChip(
              avatar: Icon(
                widget.clipboardManager.autoSync
                    ? Symbols.content_copy_rounded
                    : Symbols.content_paste_off_rounded,
                size: 20,
              ),
              label: Text(
                widget.clipboardManager.autoSync ? '剪贴板同步中' : '剪贴板同步已关闭',
              ),
              onPressed: () =>
                  _setClipboardSync(!widget.clipboardManager.autoSync),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildWorkspaceSummary() {
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth > 720
            ? (constraints.maxWidth - 24) / 4
            : 260.0;
        return Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            _summaryCard(
              width: width,
              icon: Symbols.calendar_month_rounded,
              title: '日历',
              value: '${_calendarEvents.length} 项日程',
              color: Theme.of(context).colorScheme.surfaceContainer,
              onTap: () => _setPage(4),
            ),
            _summaryCard(
              width: width,
              icon: Symbols.checklist_rounded,
              title: '待办',
              value: '${_tasks.where((task) => !task.completed).length} 项未完成',
              color: Theme.of(context).colorScheme.surfaceContainer,
              onTap: () => _setPage(3),
            ),
            _summaryCard(
              width: width,
              icon: Symbols.note_rounded,
              title: '笔记',
              value: '${_notes.length} 篇笔记',
              color: Theme.of(context).colorScheme.surfaceContainer,
              onTap: () => _setPage(3),
            ),
            _summaryCard(
              width: width,
              icon: Symbols.photo_library_rounded,
              title: '相册',
              value: '${_photoAlbums.length} 个相册',
              color: Theme.of(context).colorScheme.surfaceContainer,
              onTap: () => _setPage(5),
            ),
          ],
        );
      },
    );
  }

  Widget _summaryCard({
    required double width,
    required IconData icon,
    required String title,
    required String value,
    required Color color,
    required VoidCallback onTap,
  }) {
    return SizedBox(
      width: width,
      child: Card(
        color: color,
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Row(
              children: [
                Icon(
                  icon,
                  size: 28,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 12),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: Theme.of(context).textTheme.titleMedium),
                    const SizedBox(height: 4),
                    Text(value, style: Theme.of(context).textTheme.bodyMedium),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildFilesCard(Device? selected) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const Icon(Symbols.folder_rounded),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    '手机文件',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  tooltip: '刷新手机文件',
                  onPressed: selected == null ? null : _refreshFiles,
                  icon: _filesLoading
                      ? const SizedBox.square(
                          dimension: 20,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Symbols.refresh_rounded),
                ),
              ],
            ),
            if (_filesError != null) _inlineError(_filesError!),
            if (selected == null)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 18),
                child: Text('连接手机后读取最近的文件。'),
              )
            else if (_files.isEmpty && !_filesLoading)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 18),
                child: Text('暂无文件，或手机还没有授予媒体读取权限。'),
              )
            else
              ..._files.take(6).map(_fileTile),
          ],
        ),
      ),
    );
  }

  Widget _fileTile(RemoteFileItem file) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: const Icon(Symbols.insert_drive_file_rounded),
      title: Text(file.name, maxLines: 1, overflow: TextOverflow.ellipsis),
      subtitle: Text('${file.mimeType} · ${_formatBytes(file.sizeBytes)}'),
      trailing: const Icon(Symbols.chevron_right_rounded),
      onTap: () => showDialog<void>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: Text(file.name),
          content: Text(
            '类型：${file.mimeType}\n大小：${_formatBytes(file.sizeBytes)}\n来源：手机媒体库',
          ),
          actions: [
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('关闭'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildConnectedModelsPage() {
    final devices = _allDevices
        .where(
          (device) =>
              device.connectionState == DeviceConnectionState.connected ||
              (_activeDevice?.deviceId == device.deviceId &&
                  _activeConnection != null),
        )
        .toList();
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildSectionTitle('已连接的机型', '显示当前已经建立会话的设备'),
          const SizedBox(height: 12),
          if (devices.isEmpty)
            _emptyCard(
              icon: Symbols.devices_other_rounded,
              title: '还没有已连接设备',
              message: '在“连接设备”中找到手机后，直接点击“连接”即可开始使用。',
              action: FilledButton.icon(
                onPressed: () => _setPage(2),
                icon: const Icon(Symbols.add_link_rounded),
                label: const Text('连接设备'),
              ),
            )
          else
            ...devices.map(_connectedDeviceCard),
        ],
      ),
    );
  }

  Widget _connectedDeviceCard(Device device) {
    final online = _isDiscovered(device);
    final connected =
        _activeDevice?.deviceId == device.deviceId && _activeConnection != null;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        child: ListTile(
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 20,
            vertical: 10,
          ),
          leading: _deviceIcon(device.platform),
          title: Text(device.name),
          subtitle: Text(
            connected
                ? '已建立 TCP 会话'
                : online
                ? '在线 · ${device.networkAddresses.join(', ')}'
                : '等待设备上线',
          ),
          trailing: _connectingDeviceId == device.deviceId
              ? const SizedBox.square(
                  dimension: 24,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : connected
              ? OutlinedButton(onPressed: _disconnect, child: const Text('断开'))
              : FilledButton(
                  onPressed: _connectingDeviceId == null && online
                      ? () => _connect(device)
                      : () => _setPage(2),
                  child: Text(online ? '连接' : '查找'),
                ),
        ),
      ),
    );
  }

  Widget _buildConnectPage() {
    final devices = _allDevices;
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: _buildSectionTitle('连接设备', '仅在同一局域网内发现，不依赖云端账号')),
              IconButton(
                tooltip: '通过 IP 地址连接',
                onPressed: _showManualIpDialog,
                icon: const Icon(Symbols.add_link_rounded),
              ),
              IconButton(
                tooltip: '重新广播',
                onPressed: _refreshDiscovery,
                icon: const Icon(Symbols.refresh_rounded),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (widget.discoveryService.lastError != null)
            _errorCard(
              '发现服务异常',
              '${widget.discoveryService.lastError}\n请检查系统防火墙是否允许 UDP ${AppConstants.discoveryUdpPort}。',
            ),
          if (widget.sessionManager.lastError != null)
            _errorCard(
              '会话监听异常',
              '${widget.sessionManager.lastError}\n电脑无法被手机连接时，请确认 TCP ${AppConstants.sessionTcpPort} 已放行，并且没有同时运行旧版本客户端。',
            ),
          _statusCard(
            icon: widget.discoveryService.isListening
                ? Symbols.wifi_rounded
                : Symbols.wifi_off_rounded,
            title: widget.discoveryService.isListening
                ? '正在监听局域网发现'
                : '发现服务未监听标准端口',
            message: widget.discoveryService.isListening
                ? '每 3 秒发送广播，并每 10 秒补充探测当前 Wi-Fi 子网。'
                : '当前只能发送探测，无法接收设备回应；这通常是端口冲突或系统权限问题。',
          ),
          _statusCard(
            icon: widget.sessionManager.isListening
                ? Symbols.link_rounded
                : Symbols.link_off_rounded,
            title: widget.sessionManager.isListening ? '正在监听设备会话' : '设备会话未监听',
            message: widget.sessionManager.isListening
                ? 'TCP ${AppConstants.sessionTcpPort} 可接受手机主动连接。'
                : '手机发现电脑后仍连接超时，通常是端口被占用或防火墙未放行。',
          ),
          const SizedBox(height: 16),
          if (devices.isEmpty)
            _emptyCard(
              icon: Symbols.search_rounded,
              title: '正在搜索附近的设备',
              message: '请确保手机和电脑连在同一个局域网，并让两端应用保持打开。也可以使用右上角的 IP 定向探测。',
              action: FilledButton.icon(
                onPressed: _refreshDiscovery,
                icon: const Icon(Symbols.refresh_rounded),
                label: const Text('立即搜索'),
              ),
            )
          else
            ...devices.map(_discoveredDeviceCard),
        ],
      ),
    );
  }

  Widget _discoveredDeviceCard(Device device) {
    final online = _isDiscovered(device);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Row(
            children: [
              _deviceIcon(device.platform),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      device.name,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      '${online ? '在线' : '离线'} · ${device.networkAddresses.join(', ')}',
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              FilledButton(
                onPressed: _connectingDeviceId == null && online
                    ? () => _connect(device)
                    : null,
                child: Text(
                  _connectingDeviceId == device.deviceId ? '连接中…' : '连接',
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildNotesPage({String? focus}) {
    final showNotes = focus != 'tasks';
    final showTasks = focus != 'notes';
    final title = focus == 'tasks'
        ? '待办'
        : focus == 'notes'
        ? '笔记'
        : '笔记与待办';
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _isDesktop
              ? _buildSectionTitle(title, '本地持久化的笔记和任务工作区')
              : Text(
                  focus == 'tasks'
                      ? '待办事项支持新建、完成、编辑和删除'
                      : focus == 'notes'
                      ? '笔记支持新建、编辑、置顶和删除'
                      : '本地持久化的笔记和任务工作区',
                  style: Theme.of(context).textTheme.bodyLarge,
                ),
          const SizedBox(height: 16),
          LayoutBuilder(
            builder: (context, constraints) {
              if (showNotes && showTasks && constraints.maxWidth >= 760) {
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: _buildNotesSection()),
                    const SizedBox(width: 16),
                    Expanded(child: _buildTasksSection()),
                  ],
                );
              }
              final sections = <Widget>[];
              if (showNotes) sections.add(_buildNotesSection());
              if (showNotes && showTasks) {
                sections.add(const SizedBox(height: 16));
              }
              if (showTasks) sections.add(_buildTasksSection());
              return Column(children: sections);
            },
          ),
        ],
      ),
    );
  }

  Widget _buildNotesSection() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Symbols.note_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    '笔记',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            if (_notesLoading)
              const LinearProgressIndicator()
            else if (_notesError != null)
              _inlineError('笔记读取失败：$_notesError')
            else if (_notes.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 28),
                child: Text('还没有笔记，点击右上角开始记录。'),
              )
            else
              ..._notes.map(_noteTile),
          ],
        ),
      ),
    );
  }

  Widget _noteTile(WorkspaceNote note) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      title: Text(note.title, maxLines: 1, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        note.content.isEmpty ? '无正文' : note.content,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      onTap: () => _editNote(note),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            tooltip: note.pinned ? '取消置顶' : '置顶笔记',
            onPressed: () => _toggleNotePinned(note),
            icon: Icon(Symbols.push_pin_rounded),
          ),
          IconButton(
            tooltip: '删除笔记',
            onPressed: () => _deleteNote(note),
            icon: const Icon(Symbols.delete_rounded),
          ),
        ],
      ),
    );
  }

  Widget _buildTasksSection() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Symbols.checklist_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    '待办',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            if (_tasksLoading)
              const LinearProgressIndicator()
            else if (_tasksError != null)
              _inlineError('待办读取失败：$_tasksError')
            else if (_tasks.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 28),
                child: Text('还没有待办，点击右上角添加一项。'),
              )
            else
              ..._tasks.map(_taskTile),
          ],
        ),
      ),
    );
  }

  Widget _taskTile(WorkspaceTask task) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: Checkbox(
        value: task.completed,
        onChanged: (value) => _toggleTask(task, value ?? false),
      ),
      title: Text(
        task.title,
        style: TextStyle(
          decoration: task.completed ? TextDecoration.lineThrough : null,
          color: task.completed
              ? Theme.of(context).colorScheme.onSurfaceVariant
              : null,
        ),
      ),
      subtitle: task.dueAt == null
          ? null
          : Text('截止 ${_formatDate(task.dueAt!)}'),
      onTap: () => _editTask(task),
      trailing: IconButton(
        tooltip: '删除待办',
        onPressed: () => _deleteTask(task),
        icon: const Icon(Symbols.delete_rounded),
      ),
    );
  }

  Widget _buildSettingsPage() {
    final scheme = Theme.of(context).colorScheme;
    return Theme(
      // Settings cards are rounded surfaces. The default ink splash can be
      // painted by an un-clipped ancestor as a sharp rectangle on long press,
      // so settings use a quiet pressed state instead of that mismatched
      // expansion animation.
      data: Theme.of(context).copyWith(splashFactory: NoSplash.splashFactory),
      child: _pageBody(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '管理应用偏好、外观与数据存储',
              style: Theme.of(context).textTheme.bodyLarge,
            ),
            const SizedBox(height: 16),
            Card(
              child: ListTile(
                leading: Icon(Symbols.palette_rounded, color: scheme.primary),
                title: const Text('个性化'),
                subtitle: Text(
                  '${_themePreferenceLabel(_workspaceState.themePreference)} · ${_navigationStyleLabel(_isDesktop ? _workspaceState.navigationStyle : AppNavigationStyle.bottom)}',
                ),
                trailing: const Icon(Symbols.chevron_right_rounded),
                onTap: _openPersonalizationPage,
              ),
            ),
            const SizedBox(height: 16),
            Card(
              child: ListTile(
                leading: Icon(
                  Symbols.folder_special_rounded,
                  color: scheme.primary,
                ),
                title: const Text('存储设置'),
                subtitle: Text(
                  _workspaceState.fileStoragePath.isEmpty
                      ? '使用默认的 Hinge 文件夹'
                      : '图片、视频和其他文件的保存位置已自定义',
                ),
                trailing: const Icon(Symbols.chevron_right_rounded),
                onTap: _showStorageSettingsDialog,
              ),
            ),
            if (!_isDesktop) ...[
              const SizedBox(height: 16),
              Card(
                child: ListTile(
                  leading: Icon(Symbols.shield_rounded, color: scheme.primary),
                  title: const Text('保活设置'),
                  subtitle: const Text('通知、电池优化和厂商后台保护引导'),
                  trailing: const Icon(Symbols.chevron_right_rounded),
                  onTap: () => Navigator.of(context).push<void>(
                    MaterialPageRoute<void>(
                      builder: (_) => KeepAliveSettingsScreen(
                        dataService: widget.dataService,
                      ),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 16),
              Card(
                child: ListTile(
                  leading: Icon(
                    Symbols.open_in_new_rounded,
                    color: scheme.primary,
                  ),
                  title: const Text('默认应用'),
                  subtitle: const Text('选择通知中的图片、视频和文件打开方式'),
                  trailing: const Icon(Symbols.chevron_right_rounded),
                  onTap: () => Navigator.of(context).push<void>(
                    MaterialPageRoute<void>(
                      builder: (_) =>
                          DefaultAppsScreen(dataService: widget.dataService),
                    ),
                  ),
                ),
              ),
            ],
            const SizedBox(height: 16),
            Card(
              child: ListTile(
                leading: Icon(Symbols.info_rounded, color: scheme.primary),
                title: const Text('关于应用'),
                subtitle: Text(
                  '${AppConstants.appName} ${AppConstants.appVersion}',
                ),
                trailing: const Icon(Symbols.chevron_right_rounded),
                onTap: _showAboutDialog,
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _showAboutDialog() {
    showDialog<void>(
      context: context,
      builder: (dialogContext) {
        final dialogScheme = Theme.of(dialogContext).colorScheme;

        Widget feature(IconData icon, String text) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 7),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(icon, size: 22, color: dialogScheme.primary),
                const SizedBox(width: 14),
                Expanded(child: Text(text)),
              ],
            ),
          );
        }

        return Dialog(
          insetPadding: const EdgeInsets.symmetric(
            horizontal: 28,
            vertical: 24,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(28),
          ),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 430),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(24, 28, 24, 20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  DecoratedBox(
                    decoration: BoxDecoration(
                      color: dialogScheme.surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(24),
                    ),
                    child: const Padding(
                      padding: EdgeInsets.all(14),
                      child: _HingeLogo(size: 88),
                    ),
                  ),
                  const SizedBox(height: 18),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Flexible(
                        child: Text(
                          AppConstants.appName,
                          style: Theme.of(dialogContext).textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w700),
                          textAlign: TextAlign.center,
                        ),
                      ),
                      const SizedBox(width: 10),
                      DecoratedBox(
                        decoration: BoxDecoration(
                          color: dialogScheme.primaryContainer,
                          borderRadius: BorderRadius.circular(999),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 11,
                            vertical: 5,
                          ),
                          child: Text(
                            'v${AppConstants.appVersion}',
                            style: TextStyle(
                              color: dialogScheme.onPrimaryContainer,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    '局域网优先的跨设备办公工作台',
                    style: Theme.of(dialogContext).textTheme.bodyLarge,
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 20),
                  feature(Symbols.devices_rounded, '设备发现、连接与文件访问'),
                  feature(Symbols.workspaces_rounded, '笔记、待办、日历与相册工作区'),
                  feature(Symbols.sync_rounded, '数据默认保存在设备本地'),
                  const SizedBox(height: 12),
                  Material(
                    color: dialogScheme.surfaceContainerHighest,
                    borderRadius: BorderRadius.circular(18),
                    clipBehavior: Clip.antiAlias,
                    child: ListTile(
                      leading: Icon(
                        Symbols.code_rounded,
                        color: dialogScheme.primary,
                      ),
                      title: const Text('GitHub 开源地址'),
                      subtitle: const Text('github.com/Chengeeker/Hinge'),
                      trailing: const Icon(Symbols.open_in_new_rounded),
                      onTap: () async {
                        final opened = await widget.dataService
                            .openProjectUrl();
                        if (!opened && mounted) {
                          _showMessage('无法打开 GitHub 项目地址');
                        }
                      },
                    ),
                  ),
                  const SizedBox(height: 20),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton(
                      onPressed: () => Navigator.pop(dialogContext),
                      child: const Text('我知道了'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  Future<void> _openPersonalizationPage() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => PersonalizationScreen(
          state: _workspaceState,
          isDesktop: _isDesktop,
          hapticFeedbackEnabled: _hapticFeedbackEnabled,
          onHapticFeedbackChanged: _setHapticFeedbackEnabled,
        ),
      ),
    );
  }

  Future<void> _showStorageSettingsDialog() async {
    final imageController = TextEditingController(
      text: _workspaceState.imageStoragePath,
    );
    final videoController = TextEditingController(
      text: _workspaceState.videoStoragePath,
    );
    final fileController = TextEditingController(
      text: _workspaceState.fileStoragePath,
    );
    final save = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('存储设置'),
        content: SizedBox(
          width: 560,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  _isDesktop
                      ? '接收文件时会先询问保存位置；以下路径用于默认分类。'
                      : '安卓端默认写入 Download/Hinge，并按类型分类保存。',
                ),
                const SizedBox(height: 14),
                TextField(
                  controller: imageController,
                  decoration: const InputDecoration(
                    labelText: '图片存储路径',
                    prefixIcon: Icon(Symbols.image_rounded),
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: videoController,
                  decoration: const InputDecoration(
                    labelText: '视频存储路径',
                    prefixIcon: Icon(Symbols.video_library_rounded),
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: fileController,
                  decoration: const InputDecoration(
                    labelText: '其他文件存储路径',
                    prefixIcon: Icon(Symbols.folder_rounded),
                  ),
                ),
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('保存'),
          ),
        ],
      ),
    );
    if (save == true) {
      _workspaceState.setStoragePaths(
        image: imageController.text,
        video: videoController.text,
        file: fileController.text,
      );
      _showMessage('存储路径已更新');
    }
    imageController.dispose();
    videoController.dispose();
    fileController.dispose();
  }

  Future<String?> _chooseWindowsReceiveDirectory(FileOfferMessage offer) async {
    final controller = TextEditingController(
      text: _workspaceState.fileStoragePath.isEmpty
          ? _defaultWindowsStoragePath()
          : _workspaceState.fileStoragePath,
    );
    final directory = await showDialog<String>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => AlertDialog(
        title: const Text('选择保存位置'),
        content: SizedBox(
          width: 560,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('正在接收：${offer.fileName}'),
              const SizedBox(height: 12),
              TextField(
                controller: controller,
                autofocus: true,
                decoration: const InputDecoration(
                  labelText: '文件夹路径',
                  hintText: r'C:\Users\你的用户名\Downloads\Hinge',
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('取消接收'),
          ),
          FilledButton(
            onPressed: () {
              final path = controller.text.trim();
              if (path.isNotEmpty) Navigator.pop(dialogContext, path);
            },
            child: const Text('保存到此处'),
          ),
        ],
      ),
    );
    controller.dispose();
    return directory;
  }

  String _defaultWindowsStoragePath() {
    final profile = Platform.environment['USERPROFILE'];
    if (profile != null && profile.isNotEmpty) {
      return '$profile${Platform.pathSeparator}Downloads${Platform.pathSeparator}Hinge';
    }
    return widget.transferManager.downloadDirectory;
  }

  // Kept for compatibility with older state restoration paths; the settings
  // card now opens the dedicated personalization page below.
  // ignore: unused_element
  Future<void> _showPersonalizationDialog() async {
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) {
          final state = _workspaceState;
          return AlertDialog(
            title: const Text('个性化'),
            content: SizedBox(
              width: 520,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '明暗模式',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 4),
                    RadioGroup<AppThemePreference>(
                      groupValue: state.themePreference,
                      onChanged: (value) {
                        if (value == null) return;
                        state.setThemePreference(value);
                        setDialogState(() {});
                      },
                      child: const Column(
                        children: [
                          RadioListTile<AppThemePreference>(
                            value: AppThemePreference.system,
                            title: Text('跟随系统'),
                          ),
                          RadioListTile<AppThemePreference>(
                            value: AppThemePreference.light,
                            title: Text('浅色'),
                          ),
                          RadioListTile<AppThemePreference>(
                            value: AppThemePreference.dark,
                            title: Text('深色'),
                          ),
                        ],
                      ),
                    ),
                    const Divider(),
                    SwitchListTile.adaptive(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('纯黑深色模式'),
                      subtitle: const Text('深色模式下使用真正的黑色背景，适合 OLED 屏幕'),
                      value: state.pureBlackDarkMode,
                      onChanged: (value) {
                        state.setPureBlackDarkMode(value);
                        setDialogState(() {});
                      },
                    ),
                    SwitchListTile.adaptive(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Material U 动态取色'),
                      subtitle: Text(
                        _isDesktop
                            ? 'Windows 使用应用主题色；安卓端读取系统动态强调色'
                            : '读取系统强调色生成 Material 3 配色方案',
                      ),
                      value: state.dynamicColorEnabled,
                      onChanged: (value) {
                        state.setDynamicColorEnabled(value);
                        setDialogState(() {});
                      },
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '字体粗细',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 8),
                    SegmentedButton<int>(
                      segments: const [
                        ButtonSegment(value: 0, label: Text('标准')),
                        ButtonSegment(value: 1, label: Text('稍粗')),
                        ButtonSegment(value: 2, label: Text('偏粗')),
                      ],
                      selected: {state.fontWeightLevel},
                      onSelectionChanged: (values) {
                        state.setFontWeightLevel(values.first);
                        setDialogState(() {});
                      },
                    ),
                    const SizedBox(height: 18),
                    Text(
                      '导航布局风格',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    RadioGroup<AppNavigationStyle>(
                      groupValue: state.navigationStyle,
                      onChanged: (value) {
                        if (value == null) return;
                        state.setNavigationStyle(value);
                        setDialogState(() {});
                      },
                      child: const Column(
                        children: [
                          RadioListTile<AppNavigationStyle>(
                            value: AppNavigationStyle.adaptive,
                            title: Text('自适应'),
                            subtitle: Text('桌面侧边栏，手机抽屉导航'),
                          ),
                          RadioListTile<AppNavigationStyle>(
                            value: AppNavigationStyle.sidebar,
                            title: Text('侧边栏'),
                            subtitle: Text(
                              '使用 Material 3 NavigationRail / Drawer',
                            ),
                          ),
                          RadioListTile<AppNavigationStyle>(
                            value: AppNavigationStyle.bottom,
                            title: Text('底部导航'),
                            subtitle: Text('所有页面统一使用底部导航栏'),
                          ),
                        ],
                      ),
                    ),
                    SwitchListTile.adaptive(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('悬浮胶囊底栏'),
                      subtitle: const Text('使用悬浮的 Material 3 Expressive 胶囊导航'),
                      value: state.floatingCapsuleNavigation,
                      onChanged: (value) {
                        state.setFloatingCapsuleNavigation(value);
                        setDialogState(() {});
                      },
                    ),
                  ],
                ),
              ),
            ),
            actions: [
              FilledButton(
                onPressed: () => Navigator.pop(dialogContext),
                child: const Text('完成'),
              ),
            ],
          );
        },
      ),
    );
  }

  String _themePreferenceLabel(AppThemePreference preference) {
    switch (preference) {
      case AppThemePreference.system:
        return '跟随系统';
      case AppThemePreference.light:
        return '浅色';
      case AppThemePreference.dark:
        return '深色';
    }
  }

  String _navigationStyleLabel(AppNavigationStyle style) {
    switch (style) {
      case AppNavigationStyle.adaptive:
        return '自适应导航';
      case AppNavigationStyle.sidebar:
        return '侧边栏导航';
      case AppNavigationStyle.bottom:
        return '底部导航';
    }
  }

  Widget _buildCalendarPage() {
    final eventsByDay = <String, List<CalendarEvent>>{};
    for (final event in _calendarEvents) {
      eventsByDay
          .putIfAbsent(_calendarDayKey(event.start), () => [])
          .add(event);
    }
    final selectedEvents =
        eventsByDay[_calendarDayKey(_selectedCalendarDate)] ??
        const <CalendarEvent>[];
    return _pageBody(
      Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: _isDesktop
                    ? _buildSectionTitle('日历', '读取 Android 系统日历；桌面端通过已连接手机请求')
                    : Text(
                        '按日期查看手机日程',
                        style: Theme.of(context).textTheme.bodyLarge,
                      ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (_calendarPermission == false && !_isDesktop)
            _permissionCard(
              icon: Symbols.calendar_month_rounded,
              title: '需要日历读取权限',
              message: '只读系统日程，不会修改手机日历。',
              onPressed: _requestCalendarPermission,
              onOpenSettings: () => widget.dataService.openAppSettings(),
            ),
          if (_calendarError != null) _errorCard('读取日历失败', _calendarError!),
          if (_calendarPermission != false) ...[
            _buildCalendarGrid(eventsByDay),
            const SizedBox(height: 16),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(20),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${_selectedCalendarDate.month}月${_selectedCalendarDate.day}日的日程',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 10),
                    if (selectedEvents.isEmpty)
                      const Text('这一天没有日程')
                    else
                      ...selectedEvents.map(_calendarTile),
                  ],
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildCalendarGrid(Map<String, List<CalendarEvent>> eventsByDay) {
    final firstOfMonth = DateTime(_calendarMonth.year, _calendarMonth.month, 1);
    final firstCell = firstOfMonth.subtract(
      Duration(days: firstOfMonth.weekday - DateTime.monday),
    );
    final monthLabel = '${_calendarMonth.year}年${_calendarMonth.month}月';
    final weekdays = ['一', '二', '三', '四', '五', '六', '日'];
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
        child: Column(
          children: [
            Row(
              children: [
                Text(monthLabel, style: Theme.of(context).textTheme.titleLarge),
                const Spacer(),
                IconButton(
                  tooltip: '上个月',
                  onPressed: () => setState(() {
                    _calendarMonth = DateTime(
                      _calendarMonth.year,
                      _calendarMonth.month - 1,
                    );
                  }),
                  icon: const Icon(Symbols.chevron_left_rounded),
                ),
                IconButton(
                  tooltip: '回到今天',
                  onPressed: () {
                    final today = DateTime.now();
                    setState(() {
                      _calendarMonth = DateTime(today.year, today.month);
                      _selectedCalendarDate = DateTime(
                        today.year,
                        today.month,
                        today.day,
                      );
                    });
                  },
                  icon: const Icon(Symbols.today_rounded),
                ),
                IconButton(
                  tooltip: '下个月',
                  onPressed: () => setState(() {
                    _calendarMonth = DateTime(
                      _calendarMonth.year,
                      _calendarMonth.month + 1,
                    );
                  }),
                  icon: const Icon(Symbols.chevron_right_rounded),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              children: [
                for (final weekday in weekdays)
                  Expanded(
                    child: Center(
                      child: Text(
                        weekday,
                        style: Theme.of(context).textTheme.labelMedium,
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 6),
            SizedBox(
              height: 6 * 48,
              child: GridView.builder(
                physics: const NeverScrollableScrollPhysics(),
                itemCount: 42,
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: 7,
                  mainAxisExtent: 48,
                ),
                itemBuilder: (context, index) {
                  final date = firstCell.add(Duration(days: index));
                  final key = _calendarDayKey(date);
                  final hasEvents = eventsByDay.containsKey(key);
                  final selected =
                      _calendarDayKey(_selectedCalendarDate) == key;
                  final inMonth = date.month == _calendarMonth.month;
                  final today = _calendarDayKey(DateTime.now()) == key;
                  final scheme = Theme.of(context).colorScheme;
                  return Padding(
                    padding: const EdgeInsets.all(3),
                    child: InkWell(
                      borderRadius: BorderRadius.circular(16),
                      onTap: () => setState(() {
                        _selectedCalendarDate = date;
                        _calendarMonth = DateTime(date.year, date.month);
                      }),
                      child: AnimatedContainer(
                        duration: const Duration(milliseconds: 160),
                        decoration: BoxDecoration(
                          color: selected ? scheme.primary : null,
                          border: today && !selected
                              ? Border.all(color: scheme.primary, width: 1.5)
                              : null,
                          borderRadius: BorderRadius.circular(16),
                        ),
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text(
                              '${date.day}',
                              style: TextStyle(
                                color: selected
                                    ? scheme.onPrimary
                                    : inMonth
                                    ? scheme.onSurface
                                    : scheme.onSurfaceVariant.withValues(
                                        alpha: .45,
                                      ),
                                fontWeight: today || selected
                                    ? FontWeight.w700
                                    : null,
                              ),
                            ),
                            const SizedBox(height: 3),
                            Container(
                              width: 5,
                              height: 5,
                              decoration: BoxDecoration(
                                color: hasEvents
                                    ? selected
                                          ? scheme.onPrimary
                                          : scheme.primary
                                    : Colors.transparent,
                                shape: BoxShape.circle,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  );
                },
              ),
            ),
            if (_calendarLoading) const LinearProgressIndicator(),
          ],
        ),
      ),
    );
  }

  DateTime _selectedCalendarDate = DateTime.now();
  DateTime _calendarMonth = DateTime(DateTime.now().year, DateTime.now().month);

  String _calendarDayKey(DateTime date) =>
      '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

  Widget _calendarTile(CalendarEvent event) {
    final start = event.allDay ? '全天' : _formatDateTime(event.start);
    final end = event.end == null || event.allDay
        ? ''
        : ' - ${_formatDateTime(event.end!)}';
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Card(
        child: ListTile(
          leading: CircleAvatar(
            backgroundColor: Theme.of(context).colorScheme.primaryContainer,
            child: Icon(
              Symbols.event_rounded,
              color: Theme.of(context).colorScheme.primary,
            ),
          ),
          title: Text(event.title),
          subtitle: Text(
            '$start$end${event.location.isEmpty ? '' : '\n${event.location}'}',
          ),
        ),
      ),
    );
  }

  Widget _buildPhotosPage() {
    final inAlbum = _selectedAlbumId != null;
    final album = inAlbum
        ? _photoAlbums.cast<PhotoAlbum?>().firstWhere(
            (item) => item?.id == _selectedAlbumId,
            orElse: () => null,
          )
        : null;
    return NotificationListener<ScrollNotification>(
      onNotification: (notification) {
        if (inAlbum &&
            notification.metrics.axis == Axis.vertical &&
            notification.metrics.extentAfter < 900 &&
            _photosOffset < _photosTotal) {
          unawaited(_loadMorePhotoPage(_selectedAlbumId!, reset: false));
        }
        return false;
      },
      child: _pageBody(
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                if (inAlbum)
                  IconButton(
                    tooltip: '返回相册集',
                    onPressed: () {
                      setState(() {
                        _selectedAlbumId = null;
                        _photos = [];
                        _photosOffset = 0;
                        _photosTotal = 0;
                        _photosError = null;
                      });
                    },
                    icon: const Icon(Symbols.arrow_back_rounded),
                  ),
                Expanded(
                  child: _buildSectionTitle(
                    inAlbum ? (album?.name ?? '相册') : '相册集',
                    inAlbum
                        ? '共 ${album?.count ?? _photos.length} 张图片'
                        : '先选择相册，再查看其中的图片；不会默认铺开全部照片',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            if (_photosPermission == false && !_isDesktop)
              _permissionCard(
                icon: Symbols.photo_library_rounded,
                title: '需要照片读取权限',
                message: '只读取图片列表和缩略图，不会上传照片。',
                onPressed: _requestPhotosPermission,
                onOpenSettings: () => widget.dataService.openAppSettings(),
              ),
            if (_photosError != null) _errorCard('读取相册失败', _photosError!),
            if (_photosPermission != false) ...[
              if (!inAlbum && _photoAlbums.isEmpty && !_photosLoading)
                _emptyCard(
                  icon: Symbols.photo_library_rounded,
                  title: '暂无相册集',
                  message: '手机媒体库中没有可读取的图片或相册。',
                )
              else if (!inAlbum)
                GridView.builder(
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  itemCount: _photoAlbums.length,
                  gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                    maxCrossAxisExtent: 280,
                    mainAxisExtent: 250,
                    crossAxisSpacing: 12,
                    mainAxisSpacing: 12,
                  ),
                  itemBuilder: (context, index) =>
                      _photoAlbumTile(_photoAlbums[index]),
                )
              else if (_photos.isEmpty && !_photosLoading)
                _emptyCard(
                  icon: Symbols.photo_rounded,
                  title: '相册为空',
                  message: '这个相册中暂时没有可读取的图片。',
                )
              else
                _buildPhotoMasonry(),
            ],
          ],
        ),
      ),
    );
  }

  Widget _photoAlbumTile(PhotoAlbum album) {
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => _openPhotoAlbum(album),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: FutureBuilder<Uint8List?>(
                future: _loadPhotoBytesByUri(album.coverUri),
                builder: (context, snapshot) {
                  final bytes = snapshot.data;
                  return bytes == null
                      ? _photoPlaceholder(icon: Symbols.photo_library_rounded)
                      : Image.memory(
                          bytes,
                          width: double.infinity,
                          fit: BoxFit.cover,
                        );
                },
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 10, 14, 12),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      album.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text('${album.count} 张'),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  // ignore: unused_element
  Widget _photoTile(PhotoItem photo) {
    return _photoTileAtWidth(photo, null);
  }

  Widget _photoTileAtWidth(PhotoItem photo, double? width) {
    final imageRatio = photo.width > 0 && photo.height > 0
        ? (photo.width / photo.height).clamp(.72, 1.55).toDouble()
        : 1.15;
    final imageHeight = width == null ? 180.0 : width / imageRatio;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => _showPhotoPreview(photo),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              height: imageHeight,
              child: FutureBuilder<Uint8List?>(
                future: _loadPhotoBytes(photo),
                builder: (context, snapshot) {
                  final bytes = snapshot.data;
                  return bytes == null
                      ? _photoPlaceholder()
                      : Image.memory(
                          bytes,
                          width: double.infinity,
                          fit: BoxFit.cover,
                        );
                },
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 10),
              child: Text(
                photo.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildPhotoMasonry() {
    return LayoutBuilder(
      builder: (context, constraints) {
        final columnCount = constraints.maxWidth < 520
            ? 2
            : (constraints.maxWidth / 240).round().clamp(2, 5);
        final columns = List.generate(columnCount, (_) => <PhotoItem>[]);
        final heights = List.filled(columnCount, 0.0);
        final columnWidth =
            (constraints.maxWidth - (columnCount - 1) * 12) / columnCount;
        for (final photo in _photos) {
          var target = 0;
          for (var index = 1; index < columnCount; index++) {
            if (heights[index] < heights[target]) target = index;
          }
          columns[target].add(photo);
          final ratio = photo.width > 0 && photo.height > 0
              ? (photo.width / photo.height).clamp(.72, 1.55).toDouble()
              : 1.15;
          heights[target] += columnWidth / ratio + 58 + 12;
        }
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var index = 0; index < columns.length; index++) ...[
              Expanded(
                child: Column(
                  children: [
                    for (final photo in columns[index])
                      _photoTileAtWidth(photo, columnWidth),
                  ],
                ),
              ),
              if (index != columns.length - 1) const SizedBox(width: 12),
            ],
          ],
        );
      },
    );
  }

  Future<void> _showPhotoPreview(PhotoItem photo) async {
    await showDialog<void>(
      context: context,
      builder: (dialogContext) {
        final size = MediaQuery.sizeOf(dialogContext);
        return Dialog(
          child: ConstrainedBox(
            constraints: BoxConstraints(
              maxWidth: size.width > 900 ? 820 : size.width - 32,
              maxHeight: size.height - 48,
            ),
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          photo.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                      ),
                      IconButton(
                        tooltip: '关闭预览',
                        onPressed: () => Navigator.pop(dialogContext),
                        icon: const Icon(Symbols.close_rounded),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  Flexible(
                    child: FutureBuilder<Uint8List?>(
                      future: _loadPhotoPreviewBytes(photo),
                      builder: (context, snapshot) {
                        if (snapshot.connectionState != ConnectionState.done) {
                          return const Center(
                            child: CircularProgressIndicator(),
                          );
                        }
                        final bytes = snapshot.data;
                        if (bytes == null) {
                          return Center(
                            child: Text(
                              '图片预览读取失败',
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.error,
                              ),
                            ),
                          );
                        }
                        return InteractiveViewer(
                          minScale: 1,
                          maxScale: 5,
                          child: Image.memory(bytes, fit: BoxFit.contain),
                        );
                      },
                    ),
                  ),
                  const SizedBox(height: 14),
                  FutureBuilder<Map<String, dynamic>>(
                    future: _loadPhotoMetadata(photo),
                    initialData: const <String, dynamic>{},
                    builder: (context, snapshot) {
                      final metadata =
                          snapshot.data ?? const <String, dynamic>{};
                      final width =
                          (metadata['width'] as num?)?.toInt() ?? photo.width;
                      final height =
                          (metadata['height'] as num?)?.toInt() ?? photo.height;
                      final camera = [
                        '${metadata['cameraMake'] ?? ''}'.trim(),
                        '${metadata['cameraModel'] ?? ''}'.trim(),
                      ].where((value) => value.isNotEmpty).join(' ');
                      final originalDate =
                          '${metadata['dateTimeOriginal'] ?? ''}'.trim();
                      final details = [
                        '尺寸：${width > 0 ? '$width × $height' : '未知'}',
                        '拍摄时间：${_formatDate(photo.takenAt)}',
                        if (camera.isNotEmpty) '设备：$camera',
                        if (originalDate.isNotEmpty) '原始时间：$originalDate',
                        '媒体地址：${photo.uri}',
                      ].join('\n');
                      return Text(
                        details,
                        style: Theme.of(context).textTheme.bodySmall,
                      );
                    },
                  ),
                  const SizedBox(height: 8),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.end,
                    children: [
                      if (!_isDesktop) ...[
                        OutlinedButton.icon(
                          onPressed: () =>
                              _sendPhotoToComputer(photo, dialogContext),
                          icon: const Icon(Symbols.send_rounded),
                          label: const Text('发送'),
                        ),
                        const SizedBox(width: 10),
                      ],
                      FilledButton(
                        onPressed: () => Navigator.pop(dialogContext),
                        child: const Text('完成'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  Future<void> _sendPhotoToComputer(
    PhotoItem photo,
    BuildContext dialogContext,
  ) async {
    final connection = _activeConnection;
    if (_isDesktop ||
        connection == null ||
        _activeDevice?.platform != DevicePlatform.windows) {
      _showMessage('请先在手机端连接 Windows 电脑');
      return;
    }
    Navigator.pop(dialogContext);
    _showMessage('正在发送 ${photo.name}…');
    try {
      final bytes = await widget.dataService.loadPhotoBytes(photo.uri);
      if (bytes == null || bytes.isEmpty) {
        throw StateError('图片原图读取失败');
      }
      await widget.transferManager.sendBytes(
        connection,
        fileName: photo.name,
        bytes: bytes,
        mimeType: 'image/${_photoExtension(photo.name)}',
      );
      if (mounted) _showMessage('已发送 ${photo.name}');
    } catch (error) {
      if (mounted) _showMessage('发送失败：$error');
    }
  }

  Future<Map<String, dynamic>> _loadPhotoMetadata(PhotoItem photo) async {
    try {
      return await widget.dataService.loadMediaMetadata(
        uri: photo.uri,
        name: photo.name,
        mimeType: 'image/${_photoExtension(photo.name)}',
      );
    } catch (_) {
      // Metadata is supplementary; a provider without EXIF must not break
      // the normal high-resolution preview.
      return const <String, dynamic>{};
    }
  }

  String _photoExtension(String name) {
    final dot = name.lastIndexOf('.');
    if (dot < 0 || dot == name.length - 1) return 'jpeg';
    final extension = name.substring(dot + 1).toLowerCase();
    return const {
          'jpg',
          'jpeg',
          'png',
          'webp',
          'gif',
          'heic',
        }.contains(extension)
        ? extension
        : 'jpeg';
  }

  Future<Uint8List?> _loadPhotoPreviewBytes(PhotoItem photo) async {
    final cached = _photoPreviewBytesCache[photo.uri];
    if (cached != null) return cached;
    final future = _fetchPhotoPreviewBytesByUri(photo.uri);
    _photoPreviewBytesCache[photo.uri] = future;
    return future;
  }

  Future<Uint8List?> _fetchPhotoPreviewBytesByUri(String uri) async {
    final selected = _selectedDevice;
    if (_isDesktop &&
        selected != null &&
        selected.platform == DevicePlatform.android &&
        _activeConnection != null &&
        _activeDevice?.deviceId == selected.deviceId) {
      final raw = await _invokeWorkspace(selected, 'photoPreviewBytes', {
        'uri': uri,
      });
      return raw is String ? base64Decode(raw) : null;
    }
    return widget.dataService.loadPhotoPreviewBytes(uri);
  }

  Future<Uint8List?> _loadPhotoBytes(PhotoItem photo) async {
    return _loadPhotoBytesByUri(photo.uri);
  }

  Future<Uint8List?> _loadPhotoBytesByUri(String uri) async {
    if (uri.isEmpty) return null;
    final cached = _photoBytesCache[uri];
    if (cached != null) return cached;

    final future = _fetchPhotoThumbnailBytesByUri(uri);
    _photoBytesCache[uri] = future;
    return future;
  }

  Future<Uint8List?> _fetchPhotoThumbnailBytesByUri(String uri) async {
    return _photoThumbnailLimiter.run(() async {
      final selected = _selectedDevice;
      if (_isDesktop &&
          selected != null &&
          selected.platform == DevicePlatform.android) {
        final raw = await _invokeWorkspace(selected, 'photoThumbnailBytes', {
          'uri': uri,
        });
        return raw is String ? base64Decode(raw) : null;
      }
      return widget.dataService.loadPhotoThumbnailBytes(uri);
    });
  }

  Widget _photoPlaceholder({IconData icon = Symbols.photo_rounded}) {
    return ColoredBox(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Center(
        child: Icon(
          icon,
          size: 40,
          color: Theme.of(context).colorScheme.onSurfaceVariant,
        ),
      ),
    );
  }

  Widget _buildSectionTitle(String title, String subtitle) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: Theme.of(context).textTheme.headlineSmall),
        const SizedBox(height: 4),
        Text(
          subtitle,
          style: TextStyle(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }

  Widget _statusCard({
    required IconData icon,
    required String title,
    required String message,
  }) {
    return Card(
      color: Theme.of(context).colorScheme.surfaceContainer,
      child: ListTile(
        leading: Icon(icon, color: Theme.of(context).colorScheme.primary),
        title: Text(title),
        subtitle: Text(message),
      ),
    );
  }

  Widget _permissionCard({
    required IconData icon,
    required String title,
    required String message,
    required VoidCallback onPressed,
    VoidCallback? onOpenSettings,
  }) {
    return Card(
      color: Theme.of(context).colorScheme.surfaceContainerHigh,
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Row(
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [Text(title), Text(message)],
              ),
            ),
            Wrap(
              spacing: 8,
              runSpacing: 4,
              alignment: WrapAlignment.end,
              children: [
                if (onOpenSettings != null)
                  OutlinedButton(
                    onPressed: onOpenSettings,
                    child: const Text('系统设置'),
                  ),
                FilledButton(onPressed: onPressed, child: const Text('授予权限')),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _errorCard(String title, String message) {
    return Card(
      color: Theme.of(context).colorScheme.errorContainer,
      child: ListTile(
        leading: Icon(
          Symbols.warning_rounded,
          color: Theme.of(context).colorScheme.error,
        ),
        title: Text(title),
        subtitle: Text(message),
      ),
    );
  }

  Widget _inlineError(String message) {
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 8),
      child: Text(
        message,
        style: TextStyle(color: Theme.of(context).colorScheme.error),
      ),
    );
  }

  Widget _emptyCard({
    required IconData icon,
    required String title,
    required String message,
    Widget? action,
  }) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          children: [
            Icon(icon, size: 42, color: Theme.of(context).colorScheme.primary),
            const SizedBox(height: 12),
            Text(
              title,
              style: Theme.of(context).textTheme.titleLarge,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 6),
            Text(message, textAlign: TextAlign.center),
            if (action != null) ...[const SizedBox(height: 18), action],
          ],
        ),
      ),
    );
  }

  Widget _deviceIcon(DevicePlatform platform, {double size = 44}) {
    final icon = platform == DevicePlatform.android
        ? Symbols.phone_android_rounded
        : platform == DevicePlatform.windows
        ? Symbols.desktop_windows_rounded
        : Symbols.devices_other_rounded;
    return CircleAvatar(
      radius: size / 2,
      backgroundColor: Theme.of(context).colorScheme.primaryContainer,
      child: Icon(
        icon,
        size: size * .55,
        color: Theme.of(context).colorScheme.onPrimaryContainer,
      ),
    );
  }

  String _formatBytes(int bytes) {
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(1)} GB';
  }

  String _formatDateTime(DateTime date) {
    return '${date.month}月${date.day}日 ${date.hour.toString().padLeft(2, '0')}:${date.minute.toString().padLeft(2, '0')}';
  }

  String _formatDate(int millis) {
    if (millis <= 0) return '日期未知';
    final date = DateTime.fromMillisecondsSinceEpoch(millis);
    return '${date.year}/${date.month}/${date.day}';
  }

  String _shorten(String value, int maxLength) {
    final compact = value.replaceAll('\n', ' ');
    return compact.length <= maxLength
        ? compact
        : '${compact.substring(0, maxLength)}…';
  }
}

class _AsyncLimiter {
  final int _limit;
  int _active = 0;
  final List<Completer<void>> _waiters = [];

  _AsyncLimiter(this._limit);

  Future<T> run<T>(Future<T> Function() action) async {
    if (_active >= _limit) {
      final waiter = Completer<void>();
      _waiters.add(waiter);
      await waiter.future;
    }
    _active++;
    try {
      return await action();
    } finally {
      _active--;
      if (_waiters.isNotEmpty) {
        _waiters.removeAt(0).complete();
      }
    }
  }
}
