Warning: truncated output (original token count: 95217)
Total output lines: 9289

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Foundation;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Hinge.Core;
using Hinge.Platform;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

namespace Hinge.App;

public sealed partial class MainWindow : Window
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint GuiInMoveSize = 0x0002;
    private const int VkLButton = 0x01;
    private const int GwlpWndProc = -4;
    private const int DefaultWindowWidth = 1555;
    private const int DefaultWindowHeight = 1000;
    private const uint GwOwner = 4;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const uint WmSetIcon = 0x0080;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;
    private const nint IconSmall = 0;
    private const nint IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const uint ElectronNotifyIconMessage = 0x8001;
    private const int WmLeftButtonDown = 0x0201;
    private const uint ElectronFirstNotifyIconId = 3;
    private const uint MaxNotifyIconIdProbe = 32;
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryValueName = "Hinge";
    private const string UserSettingsRegistryPath = @"Software\Hinge";
    private const string ReceiveDirectorySettingName = "ReceiveDirectory";
    private const string PairingCodeSettingName = "PairingCode";
    private const string RemoteInputEnabledSettingName = "RemoteInputEnabled";
    private const string RemoteInputEncryptionRequiredSettingName = "RemoteInputEncryptionRequired";
    private const string WirelessAdbHostSettingName = "WirelessAdbHost";
    private const string WirelessAdbConnectPortSettingName = "WirelessAdbConnectPort";
    private const string WirelessAdbClipboardSyncSettingName = "WirelessAdbClipboardSync";
    private const string CloudRelayEnabledSettingName = "CloudRelayEnabled";
    private const string CloudRelayEndpointSettingName = "CloudRelayEndpoint";
    private const string CloudRelayDeviceTokenSettingName = "CloudRelayDeviceToken";
    private const string CloudRelayEncryptionKeySettingName = "CloudRelayEncryptionKey";
    private const string ShowTrayBackgroundNoticeSettingName = "ShowTrayBackgroundNotice";
    private const string StartupArgument = "--startup";

    private readonly DeviceIdentity _localIdentity;
    private readonly TrustStore _trustStore;
    private readonly TransferManager _transferManager;
    private readonly Win32ClipboardAdapter _clipboardAdapter;
    private readonly PairingManager _pairingManager;
    private readonly Win32InputInjector _inputInjector;
    private readonly RemoteInputManager _remoteInputManager;
    private readonly Win32NotificationPresenter _notificationPresenter;
    private readonly NotificationManager _notificationManager;
    private readonly SessionManager _sessionManager;
    private readonly DeviceRegistry _registry;
    private readonly DiscoveryService _discoveryService;
    private readonly Win32TrayManager _trayManager;
    private readonly BluetoothWakeAdvertiser _wakeAdvertiser;
    private (string DeviceId, CancellationTokenSource Cancellation)? _bleRecovery;
    private long? _lastBleRecoveryAt;
    private readonly WorkspaceRemoteClient _workspaceRemoteClient;
    private readonly PreviewCache _previewCache;
    private readonly CloudRelayClient _cloudRelayClient;
    private readonly CloudRelayTransferService _cloudRelayTransferService;
    private const int InitialFileBatchSize = 200;
    private const int AdditionalFileBatchSize = 200;
    private readonly SemaphoreSlim _fileThumbnailGate = new(GetFileThumbnailConcurrency());
    private readonly SemaphoreSlim _wirelessAdbClipboardSyncGate = new(1, 1);
    private WirelessAdbConnectionManager? _wirelessAdbConnectionManager;
    // Screen sessions are per phone; the current screen page only displays
    // the session belonging to the selected Hinge DeviceId.
    private CancellationTokenSource? _screenCancellation =>
        _deviceWorkspace.SelectedDeviceId is { } id &&
        _screenSessions.TryGetValue(id, out var session) ? session : null;
    private bool _wirelessAdbBusy;
    private string _screenStatus = "投屏尚未启动。";
    private string _screenFrameRate = "实时渲染帧率：未运行";
    private readonly HashSet<SessionConnection> _observedConnections = new();
    private readonly Dictionary<string, DateTime> _automaticConnectAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _manualDisconnectSuppressedDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _historicalReconnectTimer;
    private CancellationTokenSource? _wirelessAdbClipboardSyncCancellation;
    private CancellationTokenSource? _wirelessAdbRecoveryCancellation;
    private string _wirelessAdbRecoveryStatus = "局域网会话掉线后，通过已连接的无线 ADB 尝试唤醒手机。";
    private CancellationTokenSource? _adbConsoleCancellation;
    private CancellationTokenSource? _wirelessAdbQrCancellation;
    private int _adbConsoleRevision;
    private Task? _wirelessAdbClipboardSyncTask;
    private WirelessAdbClipboardBridge? _wirelessAdbClipboardBridge;
    private string? _lastAndroidClipboardText;
    private string _wirelessAdbConnectionStatus = "尚未配对无线 ADB 设备。";
    private string _wirelessAdbClipboardStatus = "剪贴板同步已关闭。";
    private int _wirelessAdbClipboardGeneration;
    private int _desktopClipboardChangeGeneration;
    private readonly DeviceConnectionAttemptTracker _connectionAttempts = new();
    // Preserve a distinct UI row for each device across asynchronous discovery refreshes.
    private readonly Dictionary<string, ListViewItem> _nearbyDeviceRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _nearbyDeviceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private int _deviceRefreshQueued;
    private Device? _activeDevice;
    private SessionConnection? _activeConnection;
    private string _fileCategory = "recent";
    private string _filePath = string.Empty;
    private sealed record PhoneBrowserState(string Category, string Path, int View, int Type, int Document, int Sort);
    private sealed class FileThumbnailRequestState
    {
        public FileThumbnailRequestState(
            RemoteFileEntry entry,
            int generation,
            CancellationToken cancellation)
        {
            Entry = entry;
            Generation = generation;
            Cancellation = cancellation;
        }

        public RemoteFileEntry Entry { get; }
        public int Generation { get; }
        public CancellationToken Cancellation { get; }
        public bool Started { get; set; }
    }

    private readonly Dictionary<string, PhoneBrowserState> _phoneBrowserStates = new(StringComparer.OrdinalIgnoreCase);
    private string? _filePhoneDeviceId;
    private bool _restoringFilePhone;
    private IReadOnlyList<RemoteFileEntry> _visibleFileEntries = Array.Empty<RemoteFileEntry>();
    private int _loadedFileCount;
    private int _remoteFileOffset;
    private int _remoteFileTotal;
    private int _fileLoadGeneration;
    private bool _fileBatchLoading;
    private bool _fileThumbnailResumeQueued;
    private CancellationTokenSource? _fileLoadingCancellation;
    private HomePage? _homePage;
    private FileManagementPage? _filePage;
    private WindowsSettingsPage? _windowsSettingsPage;
    private TransferSettingsPage? _transferSettingsPage;
    private bool _blePairingRunning;
    private string? _blePairingStatus;
    private RemoteSettingsPage? _remoteSettingsPage;
    private ScreenControlPage? _screenControlPage;
    private CloudRelaySettingsPage? _cloudRelaySettingsPage;
    private NotesPage? _notesPage;
    private TodoPage? _todoPage;
    private CalendarPage? _calendarPage;
    private PhotosPage? _photosPage;
    private NotificationHistoryPage? _notificationHistoryPage;
    private PersonalizationPage? _personalizationPage;
    private SettingsPage? _settingsPage;
    private RemoteInputSettingsPage? _remoteInputSettingsPage;
    private readonly HashSet<Page> _configuredPages = new();
    private bool _resizingPane;
    private uint _resizePointerId;
    private AppWindow? _appWindow;
    private InputNonClientPointerSource? _titleBarInput;
    private XamlRoot? _windowXamlRoot;
    private long _paneOpenChangedToken;
    private bool _allowClose;
    private bool _minimizeToTray;
    private bool _showTrayBackgroundNotice;
    private string _receiveDirectory;
    private string _localPairingCode;
    private CloudRelaySettings _cloudRelaySettings;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _cloudRelayPollTimer;
    private bool _cloudRelayPollInFlight;
    private readonly Dictionary<string, string> _remotePairingCodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _contentDialogGate = new(1, 1);
    private readonly SemaphoreSlim _remoteMediaReceiveGate = new(1, 1);
    private TaskCompletionSource<string>? _pendingRemoteMedia;
    private string? _pendingRemoteMediaName;
    private string? _pendingRemoteMediaDeviceId;
    private bool _fileSaveInProgress;
    private bool _photoSaveInProgress;
    private IntPtr _nativeDropWindowHandle;
    private IntPtr _originalWindowProc;
    private WindowProcDelegate? _windowProcDelegate;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _nativeDragPoller;
    private bool _nativeWindowMoveActive;
    private bool _localPointerGestureActive;
    private bool _nativeInternalRemoteDragActive;
    private bool _nativeWebDavRemoteDragActive;
    private bool _nativeExternalDragActive;
    private DateTime _nativeExternalDragCandidateSince;
    private CancellationTokenSource? _computerDropCancellation;
    private readonly SemaphoreSlim _shellSendGate = new(1, 1);
    private readonly PendingFileSendStore _pendingFileSendStore = new();
    private readonly TransferHistoryStore _transferHistoryStore = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingSendCancellations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TransferProgressUpdateInterval = TimeSpan.FromMilliseconds(100);
    private readonly ShellSendPipeServer _shellSendPipeServer;
    private IntPtr _windowIconLarge;
    private IntPtr _windowIconSmall;
    private IntPtr _windowIconClassWindowHandle;
    private IntPtr _previousClassIconLarge;
    private IntPtr _previousClassIconSmall;
    private bool _classIconLargeApplied;
    private bool _classIconSmallApplied;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcDelegate(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(
        IntPtr hWnd,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool accept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint DragQueryFile(
        IntPtr hDrop,
        uint fileIndex,
        [Out] StringBuilder? fileName,
        uint characterCount);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool DragQueryPoint(IntPtr hDrop, out NativePoint point);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int index,
        IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", SetLastError = true)]
    private static extern IntPtr SetClassLongPtr(
        IntPtr hWnd,
        int index,
        IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousProc,
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc callback,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr hWnd,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(
        IntPtr hWnd,
        uint command);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint message,
        nint wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInstance,
        string name,
        uint imageType,
        int width,
        int height,
        uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(
        ref NotifyIconIdentifier identifier,
        out NativeRect iconLocation);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref NativeGuiThreadInfo guiThreadInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint IconId;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public NativeRect CaretRect;
    }

    private HomePage Home => _homePage ?? throw new InvalidOperationException("首页尚未加载");
    private ListView DeviceListView => Home.DeviceList;
    private TextBlock DeviceCountText => Home.DeviceCountTextBlock;
    private TextBlock HeroDeviceName => Home.HeroDeviceNameText;
    private TextBlock HeroDeviceDetail => Home.HeroDeviceDetailText;
    private ContentControl HeroDeviceLogo => Home.HeroDeviceLogoControl;
    private TextBlock LocalDeviceInfo => Home.LocalDeviceInfoText;
    private InfoBar ActivityInfoBar => Home.DiscoveryStatus;
    private ListView FileCategoryList => _filePage?.Categories ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private ListView FileListView => _filePage?.Files ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock FilePathText => _filePage?.PathText ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock FileManagementStatusText => _filePage?.StatusText ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock SettingsStatusText => _windowsSettingsPage?.Status ?? throw new InvalidOperationException("Windows 设置页面尚未加载");
    private TextBlock TransferSettingsStatusText => _transferSettingsPage?.Status ?? throw new InvalidOperationException("传输与连接设置页面尚未加载");

    public MainWindow()
    {
        App.LogLifecycle("main-window-constructor-enter");
        InitializeComponent();
        App.LogLifecycle("main-window-initialize-component-complete");
        ApplyWindowFont(LoadWindowFont());
        PageRoot.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PageRoot_PointerPressed),
            true);
        PageRoot.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(PageRoot_PointerReleased),
            true);
        _minimizeToTray = LoadMinimizeToTray();
        _showTrayBackgroundNotice = LoadShowTrayBackgroundNotice();
        _receiveDirectory = LoadReceiveDirectory();
        _localPairingCode = LoadPairingCode();
        _cloudRelaySettings = LoadCloudRelaySettings();
        AppNavigation.OpenPaneLength = 200;
        _paneOpenChangedToken = AppNavigation.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty, (_, _) => UpdatePaneResizeHandle());
        ThemeBrushes.ApplyPalette(LoadWindowPalette());
        var initialWindowTheme = LoadWindowTheme();
        ApplyWindowTheme(initialWindowTheme);
        ApplyWindowMaterial(LoadWindowMaterial());
        ApplyBackgroundImage(LoadWindowBackgroundPath());
        App.LogLifecycle("configure-app-window-begin");
        ConfigureAppWindow();
        App.LogLifecycle("configure-app-window-complete");
        ApplyAppWindowTitleBarTheme(initialWindowTheme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        if (_appWindow is { } window)
        {
            window.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            window.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            _titleBarInput = InputNonClientPointerSource.GetForWindowId(window.Id);
            window.Changed += IntegratedTitleBar_WindowChanged;
        }
        ThemeBrushes.PaletteChanged += IntegratedTitleBar_PaletteChanged;
        ContentFrame.Navigated += ContentFrame_Navigated;
        ContentFrame.Navigate(typeof(HomePage));
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        App.LogLifecycle("main-window-navigation-complete");

        var identityManager = new DeviceIdentityManager();
        _localIdentity = identityManager.GetOrCreateIdentity();
        _trustStore = new TrustStore();
        _pairingManager = new PairingManager(_localIdentity, _trustStore);
        _transferManager = new TransferManager(_receiveDirectory);
        _clipboardAdapter = new Win32ClipboardAdapter();
        _inputInjector = new Win32InputInjector();
        _remoteInputManager = new RemoteInputManager(_inputInjector, _trustStore, LoadRemoteInputEnabled(), LoadRemoteInputEncryptionRequired());
        _remoteInputManager.StatusChanged += RemoteInputManager_StatusChanged;
        _notificationPresenter = new Win32NotificationPresenter(
            text => _ = _clipboardAdapter.SetTextAsync(text));
        _notificationManager = new NotificationManager(_notificationPresenter, _trustStore);
        _sessionManager = new SessionManager(
            _localIdentity,
            _trustStore,
            localPairingCode: _localPairingCode);
        _registry = new DeviceRegistry();
        _discoveryService = new DiscoveryService(
            _localIdentity,
            _registry,
            sessionPortProvider: () => _sessionManager.ListeningPort)
        {
            PairingRequired = _localPairingCode.Length > 0
        };
        _trayManager = new Win32TrayManager();
        _wakeAdvertiser = new BluetoothWakeAdvertiser();
        _workspaceRemoteClient = new WorkspaceRemoteClient();
        _previewCache = new PreviewCache();
        _cloudRelayClient = new CloudRelayClient();
        _cloudRelayTransferService = new CloudRelayTransferService(_cloudRelayClient);
        RestoreDeviceWorkspace();
        App.LogLifecycle("main-window-services-created");

        SetHeroDevice(null);
        LocalDeviceInfo.Text = $"本机：{_localIdentity.Name}\nID：{ShortId(_localIdentity.DeviceId)}";

        _registry.DevicesChanged += OnDevicesChanged;
        _discoveryService.ConnectionRequested += OnReverseConnectionRequested;
        _sessionManager.ClientConnected += OnClientConnected;
        _sessionManager.MessageReceived += OnMessageReceived;
        _transferManager.TransferProgressChanged += OnTransferProgress;
        _transferManager.FileReceivedFromDevice += OnFileReceived;
        _transferManager.TransferFailed += OnTransferFailed;
        _notificationManager.NotificationReceived += OnNotificationReceived;
        Closed += OnClosed;

        var savedAdbHost = ReadUserSetting(WirelessAdbHostSettingName) as string ?? string.Empty;
        var savedAdbPort = ReadUserSetting(WirelessAdbConnectPortSettingName) as string ?? string.Empty;
        if (WirelessAdbClient.IsPaired(savedAdbHost, savedAdbPort))
        {
            StartWirelessAdbConnectionManager();
            if (_deviceWorkspace.SelectedDeviceId is { } id &&
                IsDeviceClipboardEnabled(id))
            {
                var endpoint = GetDeviceAdbEndpoint(id);
                _ = ConfigureWirelessAdbClipboardSyncAsync(true, endpoint.Host,
                    endpoint.Port, id);
            }
        }

        try
        {
            _trayManager.Initialize("Hinge Work", "Hinge — 局域网智能协同平台已就绪");
            _trayManager.OpenRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                _appWindow?.Show();
                Activate();
            });
            _trayManager.TransferRequested += (_, _) => DispatcherQueue.TryEnqueue(async () =>
            {
                _appWindow?.Show();
                Activate();
                try { await PickAndSendFileAsync(); }
                catch (Exception exception)
                {
                    await ShowDialogAsync("无法打开文件选择器", exception.Message, false);
                }
            });
            _trayManager.ExitRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                App.LogLifecycle("tray-exit-requested");
                _allowClose = true;
                _appWindow?.Show();
                Close();
            });
        }
        catch
        {
            // The tray is optional; the main WinUI window remains usable without it.
        }

        _sessionManager.StartListener();
        App.LogLifecycle($"session-listener-started listening={_sessionManager.IsListening}");
        _discoveryService.Start();
        App.LogLifecycle($"discovery-started listening={_discoveryService.IsListening}");
        if (!_sessionManager.IsListening)
        {
            DiscoveryInfoBar.Severity = InfoBarSeverity.Error;
            DiscoveryInfoBar.Title = "设备会话未监听";
            DiscoveryInfoBar.Message = _sessionManager.LastError ??
                $"TCP {Constants.SessionTcpPort} 被占用；请关闭旧版 Hinge 后重试。";
            HeaderStatusText.Text = "会话服务异常";
        }
        else if (!_discoveryService.IsListening)
        {
            DiscoveryInfoBar.Severity = InfoBarSeverity.Error;
            DiscoveryInfoBar.Title = "设备发现服务未监听标准端口";
            DiscoveryInfoBar.Message = _discoveryService.LastError ?? "请检查 UDP 52830 是否被其他程序占用。";
            HeaderStatusText.Text = "发现服务异常";
        }
        Home.ApplyThemePalette();
        RefreshDeviceList(_registry.GetAllDevices());
        RefreshExplorerSendMenu();
        App.LogLifecycle("explorer-menu-refreshed");
        _shellSendPipeServer = new ShellSendPipeServer(arguments =>
        {
            HandleActivationArguments(arguments);
        });
        StartCloudRelayPolling();
        App.LogLifecycle("main-window-constructor-complete");
        if (_desktopSharingEnabled)
            DispatcherQueue.TryEnqueue(() => _ = RestoreDesktopSharingPreferenceAsync());
    }

    public bool HandleActivationArguments(string? arguments)
    {
        if (!ShellSendRequest.IsMarked(arguments)) return false;

        if (!ShellSendRequest.TryParse(arguments, out var request) || request == null)
        {
            _trayManager.ShowNotification("Hinge", "无法读取资源管理器选中的文件。请重试。");
            return true;
        }

        DispatcherQueue.TryEnqueue(() => _ = SendShellFilesAsync(request));
        return true;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // 页面标题和设备发现提示不再作为全局内容层显示；各页面只保留自己的
        // 内容布局，避免每次切换都重复占用顶部空间。
        DiscoveryInfoBar.Visibility = Visibility.Collapsed;
        HeaderBackButton.Visibility = Visibility.Collapsed;
        switch (e.Content)
        {
            case HomePage home:
                _homePage = home;
                ConfigureHomePage(home);
                RefreshTransferHistory();
                break;
            case FileManagementPage files:
                _filePage = files;
                ConfigureFileManagementPage(files);
                break;
            case SettingsPage settings:
                _settingsPage = settings;
                ConfigureSettingsPage(settings);
                break;
            case WindowsSettingsPage windowsSettings:
                _windowsSettingsPage = windowsSettings;
                ConfigureWindowsSettingsPage(windowsSettings);
                break;
            case TransferSettingsPage transferSettings:
                _transferSettingsPage = transferSettings;
                ConfigureTransferSettingsPage(transferSettings);
                break;
            case RemoteSettingsPage remoteSettings:
                _remoteSettingsPage = remoteSettings;
                remoteSettings.RemoteStatus.Text = GetRemoteInputSettingsSummary();
                remoteSettings.PhoneStatus.Text = _screenCancellation != null ? "正在投屏" : "未运行";
                if (_configuredPages.Add(remoteSettings))
                {
                    remoteSettings.Back.Click += (_, _) => ContentFrame.Navigate(typeof(SettingsPage));
                    remoteSettings.PhoneScreen.Click += (_, _) => ContentFrame.Navigate(typeof(ScreenControlPage));
                    remoteSettings.RemoteInput.Click += (_, _) => ContentFrame.Navigate(typeof(RemoteInputSettingsPage));
                }
                break;
            case ScreenControlPage screenControl:
                if (!ReferenceEquals(_screenControlPage, screenControl))
                {
                    _screenControlPage = screenControl;
                    ConfigureScreenControlPage(screenControl);
                }
                UpdateScreenStatus(_screenStatus);
                break;
            case RemoteInputSettingsPage remoteInputSettings:
                _remoteInputSettingsPage = remoteInputSettings;
                ConfigureRemoteInputSettingsPage(remoteInputSettings);
                break;
            case GameLayoutSettingsPage gameLayoutSettings:
                ConfigureGameLayoutSettingsPage(gameLayoutSettings);
                …80217 tokens truncated…ceId)) return null;
        foreach (var trusted in _trustStore.GetAllTrustedDevices())
        {
            try
            {
                if (string.Equals(
                        CloudRelayCrypto.ComputeRelayDeviceId(
                            _cloudRelaySettings.RelayEncryptionKey,
                            trusted.DeviceId),
                        relayDeviceId,
                        StringComparison.Ordinal))
                {
                    return trusted.DeviceId;
                }
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private async Task<TransferBatchResult?> SendFilesViaCloudAsync(
        string deviceId,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken,
        string? pendingId = null)
    {
        if (!_cloudRelaySettings.IsConfigured || !_trustStore.IsTrusted(deviceId)) return null;
        bool registered;
        try
        {
            registered = await _cloudRelayClient.IsPeerRegisteredAsync(
                _cloudRelaySettings,
                _localIdentity.DeviceId,
                deviceId,
                cancellationToken);
        }
        catch
        {
            return null;
        }
        if (!registered) return null;

        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) return null;

        var failures = new List<string>();
        var completedPaths = new List<string>();
        int completed = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(path);
            var trusted = _trustStore.GetDevice(deviceId);
            var history = FindTransferHistory(pendingId, path) ?? new TransferHistoryRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = deviceId,
                DeviceName = trusted?.Name ?? "Cloud Relay 设备",
                FileName = fileInfo.Name,
                FilePath = path,
                PendingId = pendingId ?? string.Empty,
                Direction = TransferDirection.Send,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            history = history with
            {
                State = TransferState.Transferring,
                BytesTransferred = 0,
                TotalBytes = fileInfo.Length,
                Error = string.Empty,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _transferHistoryStore.Upsert(history);
            RefreshTransferHistory();

#if HINGE_TRANSFER_DIAGNOSTICS
            var sendTimer = System.Diagnostics.Stopwatch.StartNew();
            App.LogLifecycle(
                $"file_send_started route=cloud pendingId={pendingId ?? "none"} " +
                $"sizeBytes={fileInfo.Length}");
#endif
            var progressReporter = new Progress<TransferProgress>(progress =>
            {
                _transferHistoryStore.Upsert(history with
                {
                    TransferId = progress.TransferId,
                    State = progress.State,
                    BytesTransferred = progress.BytesTransferred,
                    TotalBytes = progress.TotalBytes,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
            });
            try
            {
                string transferId = await _cloudRelayTransferService.SendFileAsync(
                    _cloudRelaySettings,
                    _localIdentity.DeviceId,
                    deviceId,
                    path,
                    progress: progressReporter,
                    cancellationToken: cancellationToken);
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=cloud transferId={transferId} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    "result=uploaded");
#endif
                _transferHistoryStore.Upsert(history with
                {
                    TransferId = transferId,
                    State = TransferState.AwaitingPickup,
                    BytesTransferred = fileInfo.Length,
                    TotalBytes = fileInfo.Length,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
                completed++;
                completedPaths.Add(path);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=cloud pendingId={pendingId ?? "none"} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    "result=cancelled");
#endif
                _transferHistoryStore.Upsert(history with
                {
                    State = TransferState.Cancelled,
                    Error = "用户取消或发送超时",
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
                throw;
            }
            catch (Exception exception)
            {
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=cloud pendingId={pendingId ?? "none"} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    $"result=failed errorType={exception.GetType().Name}");
#endif
                _transferHistoryStore.Upsert(history with
                {
                    State = TransferState.Failed,
                    Error = exception.Message,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
                failures.Add($"{fileInfo.Name}：{exception.Message}");
            }
        }

        return new TransferBatchResult(completed, failures, "Cloud Relay", completedPaths);
    }

    private async Task<TransferBatchResult?> SendFilesToConnectionAsync(
        SessionConnection connection,
        IEnumerable<string> filePaths,
        string destination,
        CancellationToken cancellationToken,
        bool showFailureDialog,
        string? pendingId = null)
    {
        var paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            if (showFailureDialog)
            {
                await ShowDialogAsync("无法发送文件", "没有找到可发送的本地文件。", false);
            }
            return null;
        }

        var completed = 0;
        var failures = new List<string>();
        var completedPaths = new List<string>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(path);
            var history = FindTransferHistory(pendingId, path) ?? new TransferHistoryRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = connection.RemoteDeviceId ?? string.Empty,
                DeviceName = GetPhoneTargets().FirstOrDefault(phone =>
                    string.Equals(phone.DeviceId, connection.RemoteDeviceId,
                        StringComparison.OrdinalIgnoreCase))?.Name ??
                    connection.PeerInfo?.Name ?? "Android 设备",
                FileName = fileInfo.Name,
                FilePath = path,
                PendingId = pendingId ?? string.Empty,
                Direction = TransferDirection.Send,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            history = history with
            {
                State = TransferState.Transferring,
                BytesTransferred = 0,
                TotalBytes = fileInfo.Length,
                Error = string.Empty,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _transferHistoryStore.Upsert(history);
            RefreshTransferHistory();

            // SendFileAsync already reports progress per chunk. Persist and
            // repaint it at a modest cadence so the history card moves
            // smoothly without turning every network frame into a disk write
            // and a full ListView rebuild.
            var progressGate = new object();
            var latestHistory = history;
            var lastProgressUpdate = DateTimeOffset.MinValue;
            var progressStopped = false;
            var progressReporter = new Progress<TransferProgress>(progress =>
            {
                if (progress.State != TransferState.Transferring) return;

                TransferHistoryRecord? snapshot = null;
                var now = DateTimeOffset.UtcNow;
                lock (progressGate)
                {
                    if (progressStopped) return;
                    if (lastProgressUpdate != DateTimeOffset.MinValue &&
                        now - lastProgressUpdate < TransferProgressUpdateInterval &&
                        progress.BytesTransferred < progress.TotalBytes)
                    {
                        return;
                    }

                    latestHistory = latestHistory with
                    {
                        TransferId = progress.TransferId,
                        State = TransferState.Transferring,
                        BytesTransferred = progress.BytesTransferred,
                        TotalBytes = progress.TotalBytes,
                        UpdatedAtUtc = now
                    };
                    lastProgressUpdate = now;
                    snapshot = latestHistory;
                }

                if (snapshot is null) return;
                _transferHistoryStore.Upsert(snapshot);
                RefreshTransferHistory();
            });

            void StopProgress()
            {
                lock (progressGate)
                {
                    progressStopped = true;
                }
            }

#if HINGE_TRANSFER_DIAGNOSTICS
            var sendTimer = System.Diagnostics.Stopwatch.StartNew();
            App.LogLifecycle(
                $"file_send_started route=lan pendingId={pendingId ?? "none"} " +
                $"sizeBytes={fileInfo.Length}");
#endif
            try
            {
                var transferId = await _transferManager.SendFileAsync(
                    connection,
                    path,
                    progress: progressReporter,
                    cancellationToken: cancellationToken,
                    destinationPath: destination);
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=lan transferId={transferId} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    "result=completed");
#endif

                TransferHistoryRecord completedHistory;
                lock (progressGate)
                {
                    progressStopped = true;
                    completedHistory = latestHistory with
                    {
                        TransferId = transferId,
                        State = TransferState.Completed,
                        BytesTransferred = fileInfo.Length,
                        TotalBytes = fileInfo.Length,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                    latestHistory = completedHistory;
                }
                _transferHistoryStore.Upsert(completedHistory);
                RefreshTransferHistory();
                completed++;
                completedPaths.Add(path);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=lan pendingId={pendingId ?? "none"} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    "result=cancelled");
#endif
                StopProgress();
                _transferHistoryStore.Upsert(latestHistory with
                {
                    State = TransferState.Cancelled,
                    Error = "用户取消或发送超时",
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
                throw;
            }
            catch (Exception exception)
            {
#if HINGE_TRANSFER_DIAGNOSTICS
                App.LogLifecycle(
                    $"file_send_timing route=lan pendingId={pendingId ?? "none"} " +
                    $"sizeBytes={fileInfo.Length} elapsedMs={sendTimer.ElapsedMilliseconds} " +
                    $"result=failed errorType={exception.GetType().Name}");
#endif
                StopProgress();
                _transferHistoryStore.Upsert(latestHistory with
                {
                    State = TransferState.Failed,
                    Error = exception.Message,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                RefreshTransferHistory();
                failures.Add($"{Path.GetFileName(path)}：{exception.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var destinationLabel = string.IsNullOrWhiteSpace(destination)
            ? "手机默认目录"
            : $"手机 /{destination}";

        var result = new TransferBatchResult(completed, failures, destinationLabel, completedPaths);
        if (failures.Count == 0)
        {
            StatusText.Text = $"已发送 {completed} 个文件到 {destinationLabel}";
            if (_filePage != null) _filePage.StatusText.Text = StatusText.Text;
            return result;
        }

        var summary = $"已发送 {completed} 个文件到 {destinationLabel}。\n失败 {failures.Count} 个：\n" +
            string.Join("\n", failures.Take(5));
        StatusText.Text = $"发送完成：成功 {completed}，失败 {failures.Count}";
        if (_filePage != null) _filePage.StatusText.Text = StatusText.Text;
        if (showFailureDialog)
        {
            await ShowDialogAsync("部分文件发送失败", summary, false);
        }
        return result;
    }

    private TransferHistoryRecord? FindTransferHistory(string? pendingId, string filePath)
    {
        return _transferHistoryStore.GetAll().FirstOrDefault(record =>
            string.Equals(record.PendingId, pendingId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
            record.State is not TransferState.Completed and not TransferState.Cancelled);
    }

    private void EnsurePendingTransferHistory(PendingFileSend pending)
    {
        var device = _registry.GetAllDevices().FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceId, pending.DeviceId, StringComparison.OrdinalIgnoreCase));
        var trusted = _trustStore.GetDevice(pending.DeviceId);
        var deviceName = device?.Name ?? trusted?.Name ?? "已配对设备";

        foreach (var path in pending.FilePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            var existing = _transferHistoryStore.GetAll().FirstOrDefault(record =>
                string.Equals(record.PendingId, pending.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null) continue;

            _transferHistoryStore.Upsert(new TransferHistoryRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = pending.DeviceId,
                DeviceName = deviceName,
                FileName = fileName,
                FilePath = path,
                PendingId = pending.Id,
                Direction = TransferDirection.Send,
                State = TransferState.WaitingAccept,
                TotalBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                CreatedAtUtc = pending.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        RefreshTransferHistory();
    }

    private void MarkPendingTransferHistoryWaiting(
        string pendingId,
        IEnumerable<string> paths,
        string? error = null)
    {
        var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in _transferHistoryStore.GetAll().Where(record =>
            string.Equals(record.PendingId, pendingId, StringComparison.OrdinalIgnoreCase) &&
            pathSet.Contains(record.FilePath)))
        {
            _transferHistoryStore.Upsert(record with
            {
                State = TransferState.WaitingAccept,
                Error = error ?? string.Empty,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        RefreshTransferHistory();
    }

    private void MarkPendingTransferHistoryCancelled(string pendingId)
    {
        foreach (var record in _transferHistoryStore.GetAll().Where(record =>
            string.Equals(record.PendingId, pendingId, StringComparison.OrdinalIgnoreCase) &&
            record.State is not TransferState.Completed and not TransferState.Cancelled))
        {
            _transferHistoryStore.Upsert(record with
            {
                State = TransferState.Cancelled,
                Error = "用户取消",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        RefreshTransferHistory();
    }

    private async Task CancelTransferHistoryAsync(TransferHistoryRecord record)
    {
        if (!record.CanCancel) return;

        if (!string.IsNullOrWhiteSpace(record.PendingId))
        {
            if (_pendingSendCancellations.TryGetValue(record.PendingId, out var cancellation))
            {
                cancellation.Cancel();
            }
            _pendingFileSendStore.Remove(record.PendingId);
            MarkPendingTransferHistoryCancelled(record.PendingId);
            StatusText.Text = $"已取消发送：{record.FileName}";
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.TransferId))
        {
            _transferManager.CancelTransfer(record.TransferId);
            _transferHistoryStore.Upsert(record with
            {
                State = TransferState.Cancelled,
                Error = "用户取消",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            RefreshTransferHistory();
            StatusText.Text = $"已取消发送：{record.FileName}";
        }

        await Task.CompletedTask;
    }

    private void RefreshExplorerSendMenu()
    {
        var lastDeviceId = _trustStore.GetAllTrustedDevices()
            .Where(device => device.TrustState == TrustState.Trusted)
            .OrderByDescending(device => device.LastSeen)
            .ThenByDescending(device => device.PairedAt)
            .Select(device => device.DeviceId).FirstOrDefault();
        ExplorerSendMenu.Refresh(Environment.ProcessPath, PhoneDeviceTarget.ForSendMenu(GetPhoneTargets(), lastDeviceId)
            .Select(device => new ExplorerSendDevice(device.DeviceId, device.DisplayName)));
    }

    private string? GetPreferredTrustedDeviceId()
    {
        if (ContentFrame.Content is FileManagementPage { IsPhoneSelected: true } files)
            return files.SelectedPhoneDeviceId;
        return _trustStore.GetAllTrustedDevices()
            .Where(device => device.TrustState == TrustState.Trusted &&
                !string.IsNullOrWhiteSpace(device.DeviceId))
            .OrderByDescending(device => device.LastSeen)
            .ThenByDescending(device => device.PairedAt)
            .Select(device => device.DeviceId)
            .FirstOrDefault();
    }

    private SessionConnection? GetConnectedConnection()
    {
        // Device selection is an explicit route, never 'whichever socket was
        // first'. An offline chosen phone must not cause a write to another.
        if (ContentFrame.Content is FileManagementPage { IsPhoneSelected: true } files)
            return files.SelectedPhoneDeviceId is { } id
                ? _sessionManager.ConnectionForDevice(id) : null;
        return SelectedWorkspaceConnection();
    }

    private SessionConnection? GetFileConnection(RemoteFileEntry entry) =>
        string.IsNullOrWhiteSpace(entry.SourceDeviceId) ? GetConnectedConnection() : _sessionManager.ConnectionForDevice(entry.SourceDeviceId);

    internal static async Task<ContentDialogResult> ShowM3ContentDialogAsync(ContentDialog dialog)
    {
        ApplyM3ContentDialogStyle(dialog);
        var primaryByKeyboard = false;
        if (dialog.DefaultButton == ContentDialogButton.Primary)
        {
            dialog.DefaultButton = ContentDialogButton.None;
            dialog.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Enter || args.Handled || !dialog.IsPrimaryButtonEnabled) return;
                args.Handled = true;
                primaryByKeyboard = true;
                dialog.Hide();
            };
        }
        var result = await ShowContentDialogAsync(dialog);
        return primaryByKeyboard ? ContentDialogResult.Primary : result;
    }

    internal static void ApplyM3ContentDialogStyle(ContentDialog dialog)
    {
        if (dialog.XamlRoot?.Content is FrameworkElement owner)
            dialog.RequestedTheme = owner.ActualTheme;
        ThemeBrushes.ApplyControlResources(dialog);
        if (dialog.Content is DependencyObject content) ThemeBrushes.ApplyLogicalControlResources(content);
        var resources = Application.Current.Resources;
        dialog.Background = ThemeBrushes.MutedSurface(dialog);
        dialog.Foreground = ThemeBrushes.Primary(dialog);
        dialog.BorderBrush = ThemeBrushes.Border(dialog);
        dialog.BorderThickness = new Thickness(1);
        dialog.CornerRadius = ThemeBrushes.CornerRadius;
        dialog.PrimaryButtonStyle ??= (Style)resources["HingeM3FilledButtonStyle"];
        dialog.CloseButtonStyle = (Style)resources["HingeM3TextButtonStyle"];
        dialog.TitleTemplate = (DataTemplate)resources["HingeM3DialogTitleTemplate"];
        if (dialog.Content is string)
            dialog.ContentTemplate = (DataTemplate)resources["HingeM3DialogMessageTemplate"];
    }

    internal static async Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
    {
        await _contentDialogGate.WaitAsync();
        try
        {
            ApplyM3ContentDialogStyle(dialog);
            return await dialog.ShowAsync();
        }
        finally
        {
            _contentDialogGate.Release();
        }
    }

    internal static async Task<bool> ConfirmFileActionAsync(
        XamlRoot xamlRoot, string title, string message, string confirmText, bool destructive = false)
    {
        var resources = Application.Current.Resources;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            // WinUI focuses the first command and keeps custom styles when no default is declared.
            PrimaryButtonText = "取消",
            PrimaryButtonStyle = (Style)resources["HingeM3TonalButtonStyle"],
            SecondaryButtonText = confirmText,
            SecondaryButtonStyle = (Style)resources[destructive ? "HingeM3DestructiveButtonStyle" : "HingeM3FilledButtonStyle"]
        };
        return await ShowContentDialogAsync(dialog) == ContentDialogResult.Secondary;
    }

    private async Task ShowDialogAsync(string title, string message, bool primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = primary ? "关闭" : "知道了",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        await ShowContentDialogAsync(dialog);
    }

    private Task ShowStatusAsync(string title, string message)
    {
        StatusText.Text = message;
        return ShowStatusDialogSafelyAsync(title, message);
    }

    private async Task ShowStatusDialogSafelyAsync(string title, string message)
    {
        try
        {
            await ShowDialogAsync(title, message, primary: false);
        }
        catch
        {
            // A status dialog is best effort. It must never turn a background
            // connection notification into an unobserved UI task exception.
        }
    }

    private void StopAdbWork()
    {
        if (_wirelessAdbRecoveryCancellation is { } recovery) _ = recovery.CancelAsync();
        foreach (var screen in _screenSessions.Values)
            screen.Cancel();
        if (_wirelessAdbClipboardSyncCancellation is { } clipboard) _ = clipboard.CancelAsync();
        if (_adbConsoleCancellation is { } console) _ = console.CancelAsync();
        if (_wirelessAdbQrCancellation is { } qr) _ = qr.CancelAsync();
        var manager = _wirelessAdbConnectionManager;
        _wirelessAdbConnectionManager = null;
        if (manager != null) _ = manager.DisposeAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AdbProcessGate.Shared.SessionEnding = true;
        _bleRecovery?.Cancellation.Cancel();
        StopAdbWork();
        StopWirelessAdbInventory();
        AdbProcessGate.TryStopPrivateServer();
        _desktopSharingClosed = true;
        _ = StopDesktopSharingAsync();
        ThemeBrushes.PaletteChanged -= IntegratedTitleBar_PaletteChanged;
        AppNavigation.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, _paneOpenChangedToken);
        if (_windowXamlRoot != null) _windowXamlRoot.Changed -= IntegratedTitleBar_RootChanged;
        CancelHistoricalReconnect();
        Clipboard.ContentChanged -= DesktopClipboard_ContentChanged;
        _wirelessAdbClipboardBridge?.Dispose();
        _wirelessAdbClipboardBridge = null;
        ExplorerSendMenu.Clear();
        ReleaseWindowIcons();
        UninitializeNativeFileDrop();
        if (_appWindow != null)
        {
            _appWindow.Closing -= AppWindow_Closing;
            _appWindow.Changed -= IntegratedTitleBar_WindowChanged;
        }
        _discoveryService.Dispose();
        _sessionManager.Dispose();
        _transferManager.Dispose();
        _cloudRelayPollTimer?.Stop();
        _cloudRelayClient.Dispose();
        _notificationManager.Dispose();
        _remoteInputManager.Dispose();
        _wakeAdvertiser.Dispose();
        _trayManager.Dispose();
        _shellSendPipeServer.Dispose();
        _shellSendGate.Dispose();
    }

    public void RestoreFromTray()
    {
        _appWindow?.Show();
        Activate();
        RefreshTaskbarIcon();
    }

    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8] + "...";
    private static string Preview(string value) => value.Length <= 32 ? value : value[..32] + "...";

    private static IReadOnlyList<IPAddress> OrderAddresses(IEnumerable<string> addresses)
    {
        var localAddresses = new List<(IPAddress Address, IPAddress Mask)>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                NetworkInterfaceHelper.IsVirtualOrVpnInterface(networkInterface))
            {
                continue;
            }

            try
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        unicast.IPv4Mask != null)
                    {
                        localAddresses.Add((unicast.Address, unicast.IPv4Mask));
                    }
                }
            }
            catch
            {
                // An adapter may disappear while Wi-Fi changes; ignore it.
            }
        }

        return addresses
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address != null)
            .Select(address => address!)
            .Distinct()
            .OrderByDescending(address => localAddresses.Any(local => IsSameSubnet(address, local.Address, local.Mask)))
            .ThenBy(address => address.Equals(IPAddress.Loopback) || address.GetAddressBytes()[0] == 169)
            .ToList();
    }

    private static bool IsSameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        byte[] leftBytes = left.GetAddressBytes();
        byte[] rightBytes = right.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        return leftBytes.Length == rightBytes.Length &&
               leftBytes.Length == maskBytes.Length &&
               leftBytes.Select((value, index) => (byte)(value & maskBytes[index]))
                   .SequenceEqual(rightBytes.Select((value, index) => (byte)(value & maskBytes[index])));
    }
}

