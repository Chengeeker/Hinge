using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Foundation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    private const uint ElectronNotifyIconMessage = 0x8001;
    private const int WmLeftButtonDown = 0x0201;
    private const uint ElectronFirstNotifyIconId = 3;
    private const uint MaxNotifyIconIdProbe = 32;
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupRegistryValueName = "Hinge";
    private const string UserSettingsRegistryPath = @"Software\Hinge";
    private const string ReceiveDirectorySettingName = "ReceiveDirectory";
    private const string StartupArgument = "--startup";

    private readonly DeviceIdentity _localIdentity;
    private readonly TrustStore _trustStore;
    private readonly TransferManager _transferManager;
    private readonly Win32ClipboardAdapter _clipboardAdapter;
    private readonly ClipboardManager _clipboardManager;
    private readonly PairingManager _pairingManager;
    private readonly Win32InputInjector _inputInjector;
    private readonly RemoteInputManager _remoteInputManager;
    private readonly Win32NotificationPresenter _notificationPresenter;
    private readonly NotificationManager _notificationManager;
    private readonly SessionManager _sessionManager;
    private readonly DeviceRegistry _registry;
    private readonly DiscoveryService _discoveryService;
    private readonly Win32TrayManager _trayManager;
    private readonly WorkspaceRemoteClient _workspaceRemoteClient;
    private readonly PreviewCache _previewCache;
    private const int InitialFileBatchSize = 200;
    private const int AdditionalFileBatchSize = 200;
    private const int ThumbnailBudgetPerBatch = 80;
    private readonly HashSet<SessionConnection> _observedConnections = new();
    private readonly Dictionary<string, DateTime> _automaticConnectAttempts = new(StringComparer.OrdinalIgnoreCase);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _historicalReconnectTimer;
    private Device? _activeDevice;
    private SessionConnection? _activeConnection;
    private string? _connectingDeviceId;
    private string _fileCategory = "recent";
    private string _filePath = string.Empty;
    private IReadOnlyList<RemoteFileEntry> _visibleFileEntries = Array.Empty<RemoteFileEntry>();
    private int _recognizedFileCount;
    private int _loadedFileCount;
    private int _remoteFileOffset;
    private int _remoteFileTotal;
    private int _fileLoadGeneration;
    private bool _fileBatchLoading;
    private CancellationTokenSource? _fileLoadingCancellation;
    private HomePage? _homePage;
    private FileManagementPage? _filePage;
    private SettingsPage? _settingsPage;
    private NotesPage? _notesPage;
    private TodoPage? _todoPage;
    private CalendarPage? _calendarPage;
    private PhotosPage? _photosPage;
    private NotificationHistoryPage? _notificationHistoryPage;
    private PersonalizationPage? _personalizationPage;
    private readonly HashSet<Page> _configuredPages = new();
    private bool _resizingPane;
    private uint _resizePointerId;
    private AppWindow? _appWindow;
    private bool _allowClose;
    private bool _minimizeToTray;
    private string _receiveDirectory;
    private readonly SemaphoreSlim _remoteMediaReceiveGate = new(1, 1);
    private TaskCompletionSource<string>? _pendingRemoteMedia;
    private string? _pendingRemoteMediaName;
    private bool _fileSaveInProgress;
    private bool _photoSaveInProgress;
    private IntPtr _nativeDropWindowHandle;
    private IntPtr _originalWindowProc;
    private WindowProcDelegate? _windowProcDelegate;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _nativeDragPoller;
    private bool _nativeWindowMoveActive;
    private bool _localPointerGestureActive;
    private bool _nativeInternalRemoteDragActive;
    private bool _nativeExternalDragActive;
    private DateTime _nativeExternalDragCandidateSince;
    private CancellationTokenSource? _computerDropCancellation;

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
    private TextBlock ClipboardStatusText => Home.ClipboardStatus;
    private ListView FileCategoryList => _filePage?.Categories ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private ListView FileListView => _filePage?.Files ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock FilePathText => _filePage?.PathText ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock FileManagementStatusText => _filePage?.StatusText ?? throw new InvalidOperationException("文件管理页面尚未加载");
    private TextBlock SettingsStatusText => _settingsPage?.Status ?? throw new InvalidOperationException("设置页面尚未加载");

    public MainWindow()
    {
        InitializeComponent();
        PageRoot.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PageRoot_PointerPressed),
            true);
        PageRoot.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(PageRoot_PointerReleased),
            true);
        _minimizeToTray = LoadMinimizeToTray();
        _receiveDirectory = LoadReceiveDirectory();
        AppNavigation.OpenPaneLength = 200;
        ApplyWindowTheme(LoadWindowTheme());
        ApplyWindowMaterial(LoadWindowMaterial());
        ApplyBackgroundImage(LoadWindowBackgroundPath());
        ConfigureAppWindow();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ContentFrame.Navigated += ContentFrame_Navigated;
        ContentFrame.Navigate(typeof(HomePage));
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];

        var identityManager = new DeviceIdentityManager();
        _localIdentity = identityManager.GetOrCreateIdentity();
        _trustStore = new TrustStore();
        _pairingManager = new PairingManager(_localIdentity, _trustStore);
        _transferManager = new TransferManager(_receiveDirectory);
        _clipboardAdapter = new Win32ClipboardAdapter();
        _clipboardManager = new ClipboardManager(_localIdentity, _clipboardAdapter);
        _inputInjector = new Win32InputInjector();
        _remoteInputManager = new RemoteInputManager(_inputInjector, _trustStore);
        _notificationPresenter = new Win32NotificationPresenter(
            text => _ = _clipboardAdapter.SetTextAsync(text));
        _notificationManager = new NotificationManager(_notificationPresenter, _trustStore);
        _sessionManager = new SessionManager(_localIdentity, _trustStore);
        _registry = new DeviceRegistry();
        _discoveryService = new DiscoveryService(
            _localIdentity,
            _registry,
            sessionPortProvider: () => _sessionManager.ListeningPort);
        _trayManager = new Win32TrayManager();
        _workspaceRemoteClient = new WorkspaceRemoteClient();
        _previewCache = new PreviewCache();

        SetHeroDevice(null);
        LocalDeviceInfo.Text = $"本机：{_localIdentity.Name}\nID：{ShortId(_localIdentity.DeviceId)}";

        _registry.DevicesChanged += OnDevicesChanged;
        _discoveryService.ConnectionRequested += OnReverseConnectionRequested;
        _sessionManager.ClientConnected += OnClientConnected;
        _sessionManager.MessageReceived += OnMessageReceived;
        _transferManager.TransferProgressChanged += OnTransferProgress;
        _transferManager.FileReceived += OnFileReceived;
        _transferManager.TransferFailed += OnTransferFailed;
        _clipboardManager.ClipboardReceived += OnClipboardReceived;
        _clipboardManager.UrlHandoffReceived += OnUrlHandoffReceived;
        _notificationManager.NotificationReceived += OnNotificationReceived;
        Closed += OnClosed;

        try
        {
            _trayManager.Initialize("Hinge Work", "Hinge — 局域网智能协同平台已就绪");
            _trayManager.OpenRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                _appWindow?.Show();
                Activate();
            });
            _trayManager.ExitRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                _allowClose = true;
                _appWindow?.Show();
                Close();
            });
        }
        catch
        {
            // The tray is optional; the main WinUI window remains usable without it.
        }

        _clipboardAdapter.StartMonitoring();
        _sessionManager.StartListener();
        _discoveryService.Start();
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
        RefreshDeviceList(_registry.GetAllDevices());
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
                break;
            case FileManagementPage files:
                _filePage = files;
                ConfigureFileManagementPage(files);
                break;
            case SettingsPage settings:
                _settingsPage = settings;
                ConfigureSettingsPage(settings);
                break;
            case PersonalizationPage personalization:
                _personalizationPage = personalization;
                ConfigurePersonalizationPage(personalization);
                break;
            case NotesPage notes:
                _notesPage = notes;
                ConfigureNotesPage(notes);
                _ = notes.LoadAsync(GetConnectedConnection());
                break;
            case TodoPage todo:
                _todoPage = todo;
                ConfigureTodoPage(todo);
                _ = todo.LoadAsync(GetConnectedConnection());
                break;
            case CalendarPage calendar:
                _calendarPage = calendar;
                ConfigureCalendarPage(calendar);
                _ = calendar.LoadAsync(GetConnectedConnection());
                break;
            case PhotosPage photos:
                _photosPage = photos;
                ConfigurePhotosPage(photos);
                _ = photos.LoadAsync(GetConnectedConnection());
                break;
            case NotificationHistoryPage notificationHistory:
                _notificationHistoryPage = notificationHistory;
                ConfigureNotificationHistoryPage(notificationHistory);
                _ = notificationHistory.LoadAsync(GetConnectedConnection());
                break;
            case FeaturePage feature:
                ConfigureFeaturePage(feature);
                break;
        }
    }

    private void PageRoot_DragOver(object sender, DragEventArgs e)
    {
        RouteExternalDragOver(e);
    }

    private void PageRoot_DragLeave(object sender, DragEventArgs e)
    {
        RouteExternalDragLeave(e);
    }

    private void PageRoot_Drop(object sender, DragEventArgs e)
    {
        RouteExternalDrop(e);
    }

    private void ContentFrame_DragOver(object sender, DragEventArgs e)
    {
        RouteExternalDragOver(e);
    }

    private void RouteExternalDragOver(DragEventArgs e)
    {
        if (!_nativeInternalRemoteDragActive && IsNavigationPanePoint(e.GetPosition(PageRoot).X))
        {
            HideNativeDragFeedback();
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        // While a Hinge file is being dragged out, the only valid in-app drop
        // target is the visible cancel zone. Do not let page-level handlers
        // reinterpret that drag as a new computer-to-phone upload.
        if (_nativeInternalRemoteDragActive)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        switch (ContentFrame.Content)
        {
            case FileManagementPage files:
                files.HandleExternalDragOver(e);
                break;
            case PhotosPage photos:
                photos.HandleExternalDragOver(e);
                break;
            default:
                e.AcceptedOperation = DataPackageOperation.None;
                break;
        }
    }

    private void ContentFrame_DragLeave(object sender, DragEventArgs e)
    {
        RouteExternalDragLeave(e);
    }

    private void RouteExternalDragLeave(DragEventArgs e)
    {
        switch (ContentFrame.Content)
        {
            case FileManagementPage files:
                files.HandleExternalDragLeave(e);
                break;
            case PhotosPage photos:
                photos.HandleExternalDragLeave(e);
                break;
        }
    }

    private void ContentFrame_Drop(object sender, DragEventArgs e)
    {
        RouteExternalDrop(e);
    }

    private void RouteExternalDrop(DragEventArgs e)
    {
        if (!_nativeInternalRemoteDragActive && IsNavigationPanePoint(e.GetPosition(PageRoot).X))
        {
            HideNativeDragFeedback();
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        if (_nativeInternalRemoteDragActive)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        switch (ContentFrame.Content)
        {
            case FileManagementPage files:
                files.HandleExternalDrop(e);
                break;
            case PhotosPage photos:
                photos.HandleExternalDrop(e);
                break;
            default:
                e.AcceptedOperation = DataPackageOperation.None;
                break;
        }
    }

    private void ShowInternalRemoteDragCancelZone()
    {
        _nativeInternalRemoteDragActive = true;
        DragCancelZone.Visibility = Visibility.Visible;
        DragCancelZone.IsHitTestVisible = true;
    }

    private void HideInternalRemoteDragCancelZone()
    {
        _nativeInternalRemoteDragActive = false;
        DragCancelZone.IsHitTestVisible = false;
        DragCancelZone.Visibility = Visibility.Collapsed;
    }

    private void PageRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PageRoot).Properties.IsLeftButtonPressed) return;

        // This press originated inside our own window. Keep it distinct from
        // an Explorer drag that enters the window with the button already held.
        _localPointerGestureActive = true;
        _nativeExternalDragCandidateSince = default;
        if (_nativeExternalDragActive)
        {
            _nativeExternalDragActive = false;
            HideNativeDragFeedback();
        }
    }

    private void PageRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _localPointerGestureActive = false;
    }

    private void DragCancelZone_DragOver(object sender, DragEventArgs e)
    {
        // The zone is made visible only by DragStarting for a Hinge remote
        // file. Use that local state as the authority instead of relying on a
        // custom DataPackage property surviving the WinUI/OLE hand-off.
        if (!_nativeInternalRemoteDragActive)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "松开以取消发送";
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private void DragCancelZone_DragLeave(object sender, DragEventArgs e)
    {
        // Keep the zone visible while the current drag is outside the app so
        // that the user can bring the file back and cancel it deliberately.
    }

    private void DragCancelZone_Drop(object sender, DragEventArgs e)
    {
        if (!_nativeInternalRemoteDragActive)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        HideInternalRemoteDragCancelZone();
        _computerDropCancellation?.Cancel();
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        StatusText.Text = "已取消发送";
        if (_filePage != null) _filePage.StatusText.Text = "已取消发送";
    }

    private void ConfigureHomePage(HomePage page)
    {
        if (!_configuredPages.Add(page)) return;
        page.DeviceList.ItemClick += DeviceListView_ItemClick;
        page.Refresh.Click += BtnRefresh_Click;
        page.SendFile.Click += BtnFiles_Click;
        page.SendText.Click += BtnQuickTransfer_Click;
        page.ClipboardToggle.Click += BtnClipboard_Click;
    }

    private void ConfigureFileManagementPage(FileManagementPage page)
    {
        if (_configuredPages.Add(page))
        {
            page.NearEndReached += FilePage_NearEndReached;
            page.Categories.SelectionChanged += FileCategory_SelectionChanged;
            page.Files.ItemClick += FileListView_ItemClick;
            page.GridFiles.ItemClick += FileListView_ItemClick;
            page.ViewMode.SelectionChanged += FileViewMode_SelectionChanged;
            page.ApplyViewMode();
            page.SelectionStateChanged += FileSelectionStateChanged;
            page.SelectFiles.Click += SelectFilesButton_Click;
            page.SaveSelectedFiles.Click += SaveSelectedFilesButton_Click;
            page.DeleteSelectedFiles.Click += DeleteSelectedFilesButton_Click;
            page.CancelSelection.Click += CancelSelectionButton_Click;
            page.ImportFile.Click += BtnFiles_Click;
            page.RefreshFiles.Click += BtnRefreshFiles_Click;
            page.NavigateBack.Click += BtnBackRemoteFolder_Click;
            page.TypeFilter.SelectionChanged += FileFilter_SelectionChanged;
            page.DocumentFilter.SelectionChanged += FileFilter_SelectionChanged;
            page.SortOptions.SelectionChanged += FileFilter_SelectionChanged;
            page.FilesDropped += ComputerFilesDropped;
        }

        // Navigated is the one lifecycle callback guaranteed to run for both
        // a new page and the cached page. Never leave the XAML placeholder on
        // screen while waiting for an unrelated selection/Loaded event.
        page.StatusText.Text = "正在检查设备连接…";
        QueueFileManagementRefresh();
    }

    private void QueueFileManagementRefresh(bool forceRefresh = false)
    {
        if (_filePage == null) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh);
            }
            catch (Exception exception)
            {
                if (_filePage == null) return;
                _filePage.StatusText.Text = "文件管理初始化失败";
                ShowFileErrorState(exception.Message);
            }
        });
    }

    private void FilePage_NearEndReached(object? sender, EventArgs e)
    {
        _ = AppendNextFileBatchAsync();
    }

    private void FileSelectionStateChanged(object? sender, EventArgs e)
    {
        if (_filePage == null) return;
        var hasSelection = _filePage.GetSelectedEntries().Count > 0;
        _filePage.SaveSelectedFiles.IsEnabled = !_fileSaveInProgress && hasSelection;
        _filePage.DeleteSelectedFiles.IsEnabled = hasSelection;
    }

    private void SelectFilesButton_Click(object sender, RoutedEventArgs e)
    {
        _filePage?.EnterSelectionMode();
    }

    private void CancelSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        _filePage?.ExitSelectionMode();
    }

    private async void SaveSelectedFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_filePage == null || _fileSaveInProgress) return;
        var entries = _filePage.GetSelectedEntries();
        if (entries.Count == 0) return;

        _fileSaveInProgress = true;
        _filePage.SaveSelectedFiles.IsEnabled = false;
        try
        {
            await SaveSelectedFilesAsync(entries);
        }
        finally
        {
            _fileSaveInProgress = false;
            FileSelectionStateChanged(this, EventArgs.Empty);
        }
    }

    private async void DeleteSelectedFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_filePage == null) return;
        var entries = _filePage.GetSelectedEntries();
        if (entries.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = "删除选中文件？",
            Content = $"将从手机中删除 {entries.Count} 个文件。此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var connection = GetConnectedConnection();
        if (connection == null)
        {
            await ShowDialogAsync("无法删除", "设备会话已断开，请重新连接手机。", false);
            return;
        }

        try
        {
            FileManagementStatusText.Text = $"正在删除 {entries.Count} 个文件…";
            var deleted = await _workspaceRemoteClient.DeleteFilesAsync(
                connection,
                entries.Select(entry => entry.Uri).ToArray());
            _filePage.ExitSelectionMode();
            await RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh: true);
            FileManagementStatusText.Text = $"已删除 {deleted} 个文件";
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("删除失败", exception.Message, false);
        }
    }

    private void ConfigureSettingsPage(SettingsPage page)
    {
        if (!_configuredPages.Add(page)) return;
        page.Personalization.Click += BtnPersonalization_Click;
        page.Storage.Click += BtnStorage_Click;
        page.StoragePath.Text = _receiveDirectory;
        page.NotificationStatus.Text = _notificationPresenter.SystemNotificationStatus;
        page.OpenNotificationSettings.Click += OpenNotificationSettings_Click;
        page.About.Click += BtnAbout_Click;
        page.MinimizeToTray.IsOn = _minimizeToTray;
        page.MinimizeToTray.Toggled += MinimizeToTray_Toggled;
        page.StartWithWindows.IsOn = LoadStartWithWindows();
        page.SilentStartup.IsOn = LoadSilentStartup();
        page.SilentStartup.IsEnabled = page.StartWithWindows.IsOn;
        page.StartWithWindows.Toggled += StartWithWindows_Toggled;
        page.SilentStartup.Toggled += SilentStartup_Toggled;
    }

    private void ConfigurePersonalizationPage(PersonalizationPage page)
    {
        if (!_configuredPages.Add(page)) return;

        page.ThemeOptionsControl.SelectedIndex = WindowThemeIndex(LoadWindowTheme());
        page.MaterialOptionsControl.SelectedValue = LoadWindowMaterial();
        page.SetPreview(LoadWindowBackgroundPath());
        page.ThemeOptionsControl.SelectionChanged += PersonalizationTheme_SelectionChanged;
        page.MaterialOptionsControl.SelectionChanged += PersonalizationMaterial_SelectionChanged;
        page.ImportBackground.Click += ImportBackground_Click;
        page.ClearBackground.Click += ClearBackground_Click;
        page.BackToSettings.Click += HeaderBackButton_Click;
    }

    private void HeaderBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is PersonalizationPage)
        {
            NavigateTo("设置", 0);
        }
    }

    private void ConfigureNotesPage(NotesPage page) =>
        page.Configure(_workspaceRemoteClient, GetConnectedConnection);

    private void ConfigureTodoPage(TodoPage page) =>
        page.Configure(_workspaceRemoteClient, GetConnectedConnection);

    private void ConfigureCalendarPage(CalendarPage page) =>
        page.Configure(_workspaceRemoteClient, GetConnectedConnection);

    private void ConfigurePhotosPage(PhotosPage page)
    {
        if (_configuredPages.Add(page))
        {
            page.FilesDropped += ComputerFilesDropped;
            page.InternalRemoteDragStarted += (_, _) => ShowInternalRemoteDragCancelZone();
            page.PhotoSelectionStateChanged += PhotoSelectionStateChanged;
            page.SelectPhotos.Click += SelectPhotosButton_Click;
            page.SaveSelectedPhotos.Click += SaveSelectedPhotosButton_Click;
            page.CancelPhotoSelection.Click += CancelPhotoSelectionButton_Click;
        }

        page.Configure(
            _workspaceRemoteClient,
            GetConnectedConnection,
            OpenRemotePhotoWithDefaultAppAsync);
    }

    private void ConfigureNotificationHistoryPage(NotificationHistoryPage page)
    {
        if (!_configuredPages.Add(page)) return;
        page.Configure(
            _workspaceRemoteClient,
            GetConnectedConnection,
            OpenRemoteNotificationAsync,
            DeleteRemoteNotificationAsync,
            ClearRemoteNotificationHistoryAsync);
    }

    private async Task<bool> OpenRemoteNotificationAsync(RemoteNotificationHistoryItem item)
    {
        // QQ/Weixin notifications intentionally have a very small action:
        // wake the desktop client and leave navigation to that client. Do not
        // send an Android PendingIntent or a deep link after the client is
        // found; both can make a multi-process client create another window or
        // enter an unauthenticated login surface.
        if (IsDesktopChatPackage(item.PackageName))
        {
            return TryWakeOrStartDesktopChatApp(item.PackageName);
        }

        var connection = GetConnectedConnection();
        if (connection != null)
        {
            try
            {
                if (await _workspaceRemoteClient.OpenNotificationHistoryItemAsync(connection, item))
                {
                    return true;
                }
            }
            catch
            {
                // The Android notification listener may have stopped or the
                // original PendingIntent may have expired. Continue to the
                // local launcher below rather than surfacing a transport error.
            }
        }

        return false;
    }

    private async Task<bool> DeleteRemoteNotificationAsync(RemoteNotificationHistoryItem item)
    {
        var connection = GetConnectedConnection();
        if (connection == null) return false;
        return await _workspaceRemoteClient.DeleteNotificationHistoryItemAsync(connection, item.Id);
    }

    private async Task<bool> ClearRemoteNotificationHistoryAsync()
    {
        var connection = GetConnectedConnection();
        if (connection == null) return false;
        return await _workspaceRemoteClient.ClearNotificationHistoryAsync(connection);
    }

    private static bool IsDesktopChatPackage(string packageName) =>
        packageName.Equals("com.tencent.mm", StringComparison.OrdinalIgnoreCase) ||
        packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase);

    private enum DesktopChatWakeResult
    {
        NotRunning,
        Activated,
        AlreadyRunning,
    }

    private static bool TryWakeOrStartDesktopChatApp(string packageName)
    {
        // QQ and Weixin are single-instance applications. If a matching
        // process is resident, never start its executable: doing so can open
        // a second login surface when the client's main window is hidden.
        var wakeResult = TryWakeExistingDesktopChatProcess(packageName);
        if (wakeResult == DesktopChatWakeResult.Activated)
        {
            return true;
        }

        if (wakeResult == DesktopChatWakeResult.AlreadyRunning)
        {
            return false;
        }

        // Discovery and process startup are separate operations. Re-check
        // immediately before launching so a client that appeared during the
        // first scan cannot be mistaken for a missing client and started a
        // second time.
        var recheckResult = TryWakeExistingDesktopChatProcess(packageName);
        if (recheckResult != DesktopChatWakeResult.NotRunning)
        {
            return recheckResult == DesktopChatWakeResult.Activated;
        }

        var candidates = DesktopChatExecutableCandidates(packageName).ToArray();
        foreach (var executable in candidates)
        {
            if (!File.Exists(executable)) continue;
            try
            {
                using var process = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = executable,
                        WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
                        UseShellExecute = true,
                    });
                if (process != null) return true;
            }
            catch
            {
                // Try the next installation location.
            }
        }
        return false;
    }

    private static DesktopChatWakeResult TryWakeExistingDesktopChatProcess(
        string packageName)
    {
        var processNames = DesktopChatProcessNames(packageName);
        if (processNames.Length == 0) return DesktopChatWakeResult.NotRunning;

        var foundProcess = false;

        foreach (var processName in processNames)
        {
            System.Diagnostics.Process[] processes;
            try
            {
                processes = System.Diagnostics.Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                foundProcess = true;
                using (process)
                {
                    try
                    {
                        process.Refresh();
                        var processId = unchecked((uint)process.Id);
                        var preferredWindow = process.MainWindowHandle;
                        foreach (var windowHandle in FindDesktopChatWindows(
                            packageName,
                            processId,
                            preferredWindow))
                        {
                            if (TryActivateDesktopChatWindow(windowHandle))
                            {
                                return DesktopChatWakeResult.Activated;
                            }
                        }

                        // Tray-hidden Electron clients keep internal Chromium
                        // windows that are not taskbar windows. Never show
                        // those HWNDs directly. Deliver the same callback as a
                        // real click on the client's notification-area icon so
                        // the client creates/restores its own UI correctly.
                        if (TryInvokeElectronTrayIcon(processId))
                        {
                            return DesktopChatWakeResult.Activated;
                        }
                    }
                    catch
                    {
                        // A process can exit between enumeration and window
                        // inspection. Continue with the other processes; if
                        // it remains resident, the caller will keep the
                        // no-second-instance guard in place.
                    }
                }
            }
        }
        return foundProcess
            ? DesktopChatWakeResult.AlreadyRunning
            : DesktopChatWakeResult.NotRunning;
    }

    private static string[] DesktopChatProcessNames(string packageName) =>
        packageName.Equals("com.tencent.mm", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Weixin", "WeChat", "WeixinAppEx", "WeChatAppEx" }
            : packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase)
                ? new[] { "QQ", "QQNT", "QQEX" }
                : Array.Empty<string>();

    private static IReadOnlyList<IntPtr> FindDesktopChatWindows(
        string packageName,
        uint processId,
        IntPtr preferredWindow)
    {
        var windows = new List<IntPtr>();

        void AddWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero ||
                !IsWindow(windowHandle) ||
                windows.Contains(windowHandle))
            {
                return;
            }

            windows.Add(windowHandle);
        }

        // Process.MainWindowHandle is the best signal when the client is
        // minimized normally. Keep it first, then inspect only top-level
        // windows owned by that process for tray-hidden/multi-process clients.
        AddWindowIfUsable(preferredWindow);
        EnumWindows((windowHandle, _) =>
        {
            if (GetWindowThreadProcessId(windowHandle, out var ownerProcessId) == 0 ||
                ownerProcessId != processId ||
                GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            // EnumWindows never returns child controls. Only accept a
            // user-facing top-level window here. Chromium clients also create
            // titleless Chrome_WidgetWin_0 surfaces for rendering and login
            // plumbing; showing those surfaces produces a blank/black window.
            AddWindowIfUsable(windowHandle);
            return true;
        }, IntPtr.Zero);

        return OrderDesktopChatWindows(packageName, windows, preferredWindow).ToArray();

        void AddWindowIfUsable(IntPtr windowHandle)
        {
            if (!IsUsableDesktopChatWindow(packageName, windowHandle)) return;
            AddWindow(windowHandle);
        }
    }

    private static IEnumerable<IntPtr> OrderDesktopChatWindows(
        string packageName,
        IEnumerable<IntPtr> windows,
        IntPtr preferredWindow = default)
    {
        return windows
            .OrderByDescending(windowHandle => windowHandle == preferredWindow)
            .ThenByDescending(IsWindowVisible)
            .ThenByDescending(windowHandle => HasExpectedDesktopChatTitle(
                packageName,
                windowHandle))
            .ThenByDescending(HasSubstantialDesktopChatWindow)
            .ThenByDescending(GetDesktopChatWindowArea)
            .ThenByDescending(GetWindowTextLength);
    }

    private static bool IsUsableDesktopChatWindow(
        string packageName,
        IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle) ||
            GetWindow(windowHandle, GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        var className = GetWindowString(windowHandle, getClassName: true);
        var title = GetWindowString(windowHandle, getClassName: false);
        if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        // Never surface Chromium's titleless renderer/host surface. The
        // visible QQ/Weixin application window is a captioned top-level window.
        if (className.Equals("Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Electron_NotifyIconHostWindow", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("Base_PowerMessageWindow", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("IME", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        if ((style & WsCaption) == 0) return false;

        if (packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase) &&
            (!className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
             !title.Equals("QQ", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (packageName.Equals("com.tencent.mm", StringComparison.OrdinalIgnoreCase) &&
            !className.Equals("WeChatMainWndForPC", StringComparison.OrdinalIgnoreCase) &&
            (!className.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
             (!title.Contains("微信", StringComparison.OrdinalIgnoreCase) &&
              !title.Contains("WeChat", StringComparison.OrdinalIgnoreCase) &&
              !title.Contains("Weixin", StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        // Hidden Electron BrowserWindows are implementation details rather
        // than user-facing taskbar windows. They must be restored by the
        // client's own tray handler instead of ShowWindow/ShowWindowAsync.
        return IsWindowVisible(windowHandle);
    }

    private static bool TryInvokeElectronTrayIcon(uint processId)
    {
        var trayWindows = new List<IntPtr>();
        EnumWindows((windowHandle, _) =>
        {
            if (GetWindowThreadProcessId(windowHandle, out var ownerProcessId) != 0 &&
                ownerProcessId == processId &&
                GetWindowString(windowHandle, getClassName: true).Equals(
                    "Electron_NotifyIconHostWindow",
                    StringComparison.OrdinalIgnoreCase))
            {
                trayWindows.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var trayWindow in trayWindows)
        {
            var iconId = FindNotifyIconId(trayWindow);
            if (iconId == 0)
            {
                // Electron's first tray icon is ID 3. Keep this fallback for
                // clients using a GUID icon that Shell_NotifyIconGetRect
                // cannot identify by numeric ID.
                iconId = ElectronFirstNotifyIconId;
            }

            if (PostMessage(
                trayWindow,
                ElectronNotifyIconMessage,
                new IntPtr(iconId),
                new IntPtr(WmLeftButtonDown)))
            {
                return true;
            }
        }

        return false;
    }

    private static uint FindNotifyIconId(IntPtr trayWindow)
    {
        for (uint iconId = 1; iconId <= MaxNotifyIconIdProbe; iconId++)
        {
            var identifier = new NotifyIconIdentifier
            {
                Size = unchecked((uint)Marshal.SizeOf<NotifyIconIdentifier>()),
                WindowHandle = trayWindow,
                IconId = iconId,
                GuidItem = Guid.Empty,
            };

            if (Shell_NotifyIconGetRect(ref identifier, out _) == 0)
            {
                return iconId;
            }
        }

        return 0;
    }

    private static string GetWindowString(IntPtr windowHandle, bool getClassName)
    {
        var buffer = new StringBuilder(256);
        var length = getClassName
            ? GetClassName(windowHandle, buffer, buffer.Capacity)
            : GetWindowText(windowHandle, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    private static bool HasExpectedDesktopChatTitle(
        string packageName,
        IntPtr windowHandle)
    {
        var title = GetWindowString(windowHandle, getClassName: false);
        if (packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase))
        {
            return title.Equals("QQ", StringComparison.OrdinalIgnoreCase);
        }

        return title.Contains("微信", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("WeChat", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Weixin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSubstantialDesktopChatWindow(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return width >= 320 && height >= 240;
    }

    private static long GetDesktopChatWindowArea(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var rect)) return 0;
        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        return (long)width * height;
    }

    private static bool TryActivateDesktopChatWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle) ||
            !IsWindowVisible(windowHandle))
        {
            return false;
        }

        // The client has already made this window visible, so foregrounding
        // it is safe. Hidden windows are handled exclusively through the
        // client's own tray callback above.
        _ = SetForegroundWindow(windowHandle);
        return true;
    }

    private static IEnumerable<string> DesktopChatExecutableCandidates(string packageName)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? path)
        {
            var normalized = NormalizeExecutablePath(path);
            if (!string.IsNullOrWhiteSpace(normalized)) candidates.Add(normalized);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var executableNames = Array.Empty<string>();
        var registryTerms = Array.Empty<string>();

        if (packageName.Equals("com.tencent.mm", StringComparison.OrdinalIgnoreCase))
        {
            executableNames = new[] { "Weixin.exe", "WeChat.exe" };
            registryTerms = new[] { "微信", "微信桌面版", "Weixin", "WeChat" };
            AddCandidate(Path.Combine(local, "Tencent", "Weixin", "Weixin.exe"));
            AddCandidate(Path.Combine(local, "Tencent", "WeChat", "WeChat.exe"));
            AddCandidate(Path.Combine(local, "Programs", "Tencent", "Weixin", "Weixin.exe"));
            AddCandidate(Path.Combine(local, "Programs", "Tencent", "WeChat", "WeChat.exe"));
            AddCandidate(Path.Combine(roaming, "Tencent", "Weixin", "Weixin.exe"));
            AddCandidate(Path.Combine(roaming, "Tencent", "WeChat", "WeChat.exe"));
            AddCandidate(Path.Combine(programFiles, "Tencent", "Weixin", "Weixin.exe"));
            AddCandidate(Path.Combine(programFiles, "Tencent", "WeChat", "WeChat.exe"));
            AddCandidate(Path.Combine(programFilesX86, "Tencent", "Weixin", "Weixin.exe"));
            AddCandidate(Path.Combine(programFilesX86, "Tencent", "WeChat", "WeChat.exe"));
        }
        else if (packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase))
        {
            executableNames = new[] { "QQ.exe", "QQNT.exe" };
            registryTerms = new[] { "QQ", "QQNT", "腾讯QQ", "Tencent QQ" };
            AddCandidate(Path.Combine(local, "Tencent", "QQNT", "QQ.exe"));
            AddCandidate(Path.Combine(local, "Tencent", "QQ", "Bin", "QQ.exe"));
            AddCandidate(Path.Combine(local, "Programs", "Tencent", "QQNT", "QQ.exe"));
            AddCandidate(Path.Combine(local, "Programs", "Tencent", "QQ", "Bin", "QQ.exe"));
            AddCandidate(Path.Combine(roaming, "Tencent", "QQNT", "QQ.exe"));
            AddCandidate(Path.Combine(roaming, "Tencent", "QQ", "Bin", "QQ.exe"));
            AddCandidate(Path.Combine(programFiles, "Tencent", "QQNT", "QQ.exe"));
            AddCandidate(Path.Combine(programFiles, "Tencent", "QQ", "Bin", "QQ.exe"));
            AddCandidate(Path.Combine(programFilesX86, "Tencent", "QQNT", "QQ.exe"));
            AddCandidate(Path.Combine(programFilesX86, "Tencent", "QQ", "Bin", "QQ.exe"));
        }

        if (executableNames.Length == 0) return candidates;

        AddCandidatesFromAppPaths(executableNames, AddCandidate);
        AddCandidatesFromUninstallEntries(executableNames, registryTerms, AddCandidate);
        AddCandidatesFromRunningProcesses(
            DesktopChatProcessNames(packageName),
            AddCandidate);
        return candidates;
    }

    private static string? NormalizeExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        var value = rawPath.Trim();
        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            value = closingQuote > 1 ? value[1..closingQuote] : value.Trim('"');
        }
        else
        {
            var comma = value.IndexOf(',');
            if (comma > 0) value = value[..comma];
        }

        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private static void AddCandidatesFromAppPaths(
        IReadOnlyList<string> executableNames,
        Action<string?> addCandidate)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appPaths = baseKey.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\App Paths",
                        writable: false);
                    if (appPaths == null) continue;

                    foreach (var executableName in executableNames)
                    {
                        using var executableKey = appPaths.OpenSubKey(executableName, writable: false);
                        addCandidate(executableKey?.GetValue(string.Empty) as string);
                    }
                }
                catch
                {
                    // Registry access is only an optional discovery source.
                }
            }
        }
    }

    private static void AddCandidatesFromUninstallEntries(
        IReadOnlyList<string> executableNames,
        IReadOnlyList<string> displayNameTerms,
        Action<string?> addCandidate)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                        writable: false);
                    if (uninstall == null) continue;

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        using var appKey = uninstall.OpenSubKey(subKeyName, writable: false);
                        if (appKey == null) continue;
                        var displayName = appKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            !displayNameTerms.Any(term => string.Equals(
                                displayName.Trim(),
                                term,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        addCandidate(appKey.GetValue("DisplayIcon") as string);
                        var installLocation = NormalizeExecutablePath(
                            appKey.GetValue("InstallLocation") as string);
                        if (installLocation != null)
                        {
                            addCandidate(installLocation);
                            continue;
                        }

                        var rawLocation = appKey.GetValue("InstallLocation") as string;
                        if (string.IsNullOrWhiteSpace(rawLocation)) continue;
                        rawLocation = Environment.ExpandEnvironmentVariables(
                            rawLocation.Trim().Trim('"'));
                        foreach (var executableName in executableNames)
                        {
                            addCandidate(Path.Combine(rawLocation, executableName));
                        }
                    }
                }
                catch
                {
                    // Registry access is only an optional discovery source.
                }
            }
        }
    }

    private static void AddCandidatesFromRunningProcesses(
        IReadOnlyList<string> executableNames,
        Action<string?> addCandidate)
    {
        foreach (var processName in executableNames.Select(Path.GetFileNameWithoutExtension).Distinct(
            StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        addCandidate(process.MainModule?.FileName);
                    }
                    catch
                    {
                        // Some processes deny MainModule access; continue with
                        // registry and conventional installation locations.
                    }
                }
            }
        }
    }

    private void PhotoSelectionStateChanged(object? sender, EventArgs e)
    {
        if (_photosPage == null) return;
        _photosPage.SaveSelectedPhotos.IsEnabled =
            !_photoSaveInProgress && _photosPage.GetSelectedPhotos().Count > 0;
    }

    private void SelectPhotosButton_Click(object sender, RoutedEventArgs e)
    {
        _photosPage?.EnterPhotoSelectionMode();
    }

    private void CancelPhotoSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        _photosPage?.ExitPhotoSelectionMode();
    }

    private async void SaveSelectedPhotosButton_Click(object sender, RoutedEventArgs e)
    {
        if (_photosPage == null || _photoSaveInProgress) return;

        var photos = _photosPage.GetSelectedPhotos();
        if (photos.Count == 0) return;

        _photoSaveInProgress = true;
        _photosPage.SaveSelectedPhotos.IsEnabled = false;
        try
        {
            await SaveSelectedPhotosAsync(_photosPage, photos);
        }
        finally
        {
            _photoSaveInProgress = false;
            PhotoSelectionStateChanged(this, EventArgs.Empty);
        }
    }

    private async Task SaveSelectedPhotosAsync(
        PhotosPage page,
        IReadOnlyList<RemotePhotoItem> photos)
    {
        if (GetConnectedConnection() == null)
        {
            await ShowDialogAsync("无法保存", "设备会话已断开，请重新连接手机。", false);
            return;
        }

        var completed = 0;
        var failures = new List<string>();
        for (var index = 0; index < photos.Count; index++)
        {
            var photo = photos[index];
            try
            {
                StatusText.Text = $"正在保存图片 {index + 1}/{photos.Count}…";
                var entry = ToRemotePhotoFileEntry(photo);
                await ReceiveRemoteFileAsync(entry, StatusText.Text);
                completed++;
            }
            catch (Exception exception)
            {
                failures.Add($"{photo.Name}：{exception.Message}");
            }
        }

        page.ExitPhotoSelectionMode();
        var destination = _receiveDirectory;
        StatusText.Text = failures.Count == 0
            ? $"已保存 {completed} 张图片到 {destination}"
            : $"已保存 {completed} 张图片，{failures.Count} 张失败";

        if (failures.Count > 0)
        {
            await ShowDialogAsync(
                "部分图片保存失败",
                string.Join("\n", failures.Take(6)),
                false);
        }
    }

    private static RemoteFileEntry ToRemotePhotoFileEntry(RemotePhotoItem photo)
    {
        var extension = Path.GetExtension(photo.Name).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" or ".heif" => "image/heic",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/jpeg"
        };
        return new RemoteFileEntry
        {
            Id = photo.Id,
            Name = string.IsNullOrWhiteSpace(photo.Name) ? "手机图片.jpg" : photo.Name,
            Uri = photo.Uri,
            SizeBytes = photo.SizeBytes,
            ModifiedAt = photo.TakenAt,
            MimeType = mimeType,
            Category = "images"
        };
    }

    private async Task OpenRemotePhotoWithDefaultAppAsync(RemotePhotoItem photo)
    {
        var extension = Path.GetExtension(photo.Name).TrimStart('.');
        var entry = new RemoteFileEntry
        {
            Id = photo.Id,
            Name = photo.Name,
            Uri = photo.Uri,
            SizeBytes = photo.SizeBytes,
            ModifiedAt = photo.TakenAt,
            MimeType = string.IsNullOrWhiteSpace(extension)
                ? "image/jpeg"
                : $"image/{extension}"
        };
        var receivedPath = await ReceiveRemotePreviewFileAsync(entry, "正在读取图片预览…");
        await LaunchMediaFileAsync(entry, receivedPath, "图片");
    }

    private void ConfigureFeaturePage(FeaturePage page)
    {
        if (!_configuredPages.Add(page)) return;
        if (page.PrimaryActionButton?.Content?.ToString() == "选择文件并发送")
        {
            page.PrimaryActionButton.Click += BtnFiles_Click;
        }
        if (page.SecondaryActionButton?.Content?.ToString() == "发送文字或链接")
        {
            page.SecondaryActionButton.Click += BtnQuickTransfer_Click;
        }
    }

    private void AppNavigation_Loaded(object sender, RoutedEventArgs e) => UpdatePaneResizeHandle();

    private void AppNavigation_PaneStateChanged(NavigationView sender, object args) => UpdatePaneResizeHandle();

    private void PaneResizeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePaneResizeHandle();

    private void UpdatePaneResizeHandle()
    {
        PaneResizeHandle.Height = PaneResizeCanvas.ActualHeight;
        PaneResizeHandle.Visibility = AppNavigation.IsPaneOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        Canvas.SetLeft(PaneResizeHandle, Math.Max(0, AppNavigation.OpenPaneLength - 4));
        Canvas.SetTop(PaneResizeHandle, 0);
    }

    private void PaneResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PaneResizeCanvas);
        _resizingPane = true;
        _resizePointerId = point.PointerId;
        PaneResizeHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PaneResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(PaneResizeCanvas);
        if (!_resizingPane || point.PointerId != _resizePointerId) return;

        SetPaneWidth(point.Position.X);
        UpdatePaneResizeHandle();
        e.Handled = true;
    }

    private void PaneResizeHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizingPane && e.GetCurrentPoint(PaneResizeCanvas).PointerId == _resizePointerId)
        {
            _resizingPane = false;
            PaneResizeHandle.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void PaneResizeHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _resizingPane = false;
    }

    private void SetPaneWidth(double width)
    {
        AppNavigation.OpenPaneLength = Math.Clamp(width, 200, 420);
    }

    private static double LoadPaneWidth()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values["PaneWidth"] is double value)
            {
                return Math.Clamp(value, 200, 420);
            }
            if (ApplicationData.Current.LocalSettings.Values["PaneWidth"] is int integer)
            {
                return Math.Clamp(integer, 200, 420);
            }
        }
        catch
        {
            // Use the native default when local settings are unavailable.
        }
        return 248;
    }

    private void ConfigureAppWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeNativeFileDrop(hwnd);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            ApplyDefaultWindowSize(_appWindow, windowId);
            _appWindow.Closing += AppWindow_Closing;
        }
        catch
        {
            // Unpackaged test hosts may not expose an AppWindow until activation.
        }
    }

    private void InitializeNativeFileDrop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _nativeDropWindowHandle != IntPtr.Zero) return;

        _nativeDropWindowHandle = hwnd;
        DragAcceptFiles(hwnd, true);
        _windowProcDelegate = NativeWindowProc;
        var replacement = Marshal.GetFunctionPointerForDelegate(_windowProcDelegate);
        _originalWindowProc = SetWindowLongPtr(hwnd, GwlpWndProc, replacement);
        if (_originalWindowProc == IntPtr.Zero)
        {
            DragAcceptFiles(hwnd, false);
            _nativeDropWindowHandle = IntPtr.Zero;
            _windowProcDelegate = null;
            return;
        }

        _nativeDragPoller = DispatcherQueue.CreateTimer();
        _nativeDragPoller.Interval = TimeSpan.FromMilliseconds(32);
        _nativeDragPoller.Tick += NativeDragPoller_Tick;
        _nativeDragPoller.Start();
    }

    private IntPtr NativeWindowProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WmNcLButtonDown)
        {
            _nativeWindowMoveActive = true;
            _nativeExternalDragCandidateSince = default;
            DispatcherQueue.TryEnqueue(HideNativeDragFeedback);
        }
        else if (message == WmNcLButtonUp)
        {
            _nativeWindowMoveActive = false;
            _nativeExternalDragCandidateSince = default;
            HideInternalRemoteDragCancelZone();
            DispatcherQueue.TryEnqueue(HideNativeDragFeedback);
        }

        if (message == WmDropFiles)
        {
            var pointAvailable = DragQueryPoint(wParam, out var point);
            var paths = ReadNativeDropFiles(wParam);
            DragFinish(wParam);
            if (paths.Count > 0)
            {
                DispatcherQueue.TryEnqueue(() =>
                    HandleNativeFileDrop(paths, pointAvailable ? point : new NativePoint()));
            }
            return IntPtr.Zero;
        }

        return _originalWindowProc == IntPtr.Zero
            ? IntPtr.Zero
            : CallWindowProc(_originalWindowProc, hWnd, message, wParam, lParam);
    }

    private void NativeDragPoller_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var leftButtonDown = (GetAsyncKeyState(VkLButton) & 0x8000) != 0;
        if (!leftButtonDown)
        {
            // PointerReleased/WM_NCLBUTTONUP can occur outside our HWND. The
            // physical button state is the final authority for ending every
            // local drag gesture.
            _nativeWindowMoveActive = false;
            _localPointerGestureActive = false;
            _nativeExternalDragCandidateSince = default;
            HideInternalRemoteDragCancelZone();
            if (_nativeExternalDragActive)
            {
                _nativeExternalDragActive = false;
                HideNativeDragFeedback();
            }
            return;
        }

        if (_nativeWindowMoveActive ||
            _localPointerGestureActive ||
            _nativeInternalRemoteDragActive ||
            IsExternalWindowMoveOrResizeActive())
        {
            _nativeExternalDragCandidateSince = default;
            if (_nativeExternalDragActive)
            {
                _nativeExternalDragActive = false;
                HideNativeDragFeedback();
            }
            return;
        }

        if (!GetCursorPos(out var screenPoint) ||
            !GetWindowRect(_nativeDropWindowHandle, out var windowRect))
        {
            return;
        }

        var insideWindow = screenPoint.X >= windowRect.Left &&
            screenPoint.X < windowRect.Right &&
            screenPoint.Y >= windowRect.Top &&
            screenPoint.Y < windowRect.Bottom;
        if (!insideWindow)
        {
            _nativeExternalDragCandidateSince = default;
            if (_nativeExternalDragActive)
            {
                _nativeExternalDragActive = false;
                HideNativeDragFeedback();
            }
            return;
        }

        // Explorer drags do not send a button-down message to the target
        // window. A short hold filters ordinary cross-window clicks while
        // still giving the user a live destination preview before release.
        _nativeExternalDragCandidateSince = _nativeExternalDragCandidateSince == default
            ? DateTime.UtcNow
            : _nativeExternalDragCandidateSince;
        if (DateTime.UtcNow - _nativeExternalDragCandidateSince < TimeSpan.FromMilliseconds(120))
        {
            return;
        }

        if (!ScreenToClient(_nativeDropWindowHandle, ref screenPoint)) return;
        _nativeExternalDragActive = true;
        HandleNativeDragOver(screenPoint);
    }

    private static bool IsExternalWindowMoveOrResizeActive()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero) return false;

        var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
        if (foregroundThread == 0) return false;

        var guiThreadInfo = new NativeGuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<NativeGuiThreadInfo>(),
        };
        return GetGUIThreadInfo(foregroundThread, ref guiThreadInfo) &&
            (guiThreadInfo.Flags & GuiInMoveSize) != 0;
    }

    private void HandleNativeDragOver(NativePoint point)
    {
        if ((GetAsyncKeyState(VkLButton) & 0x8000) == 0) return;

        if (!_nativeInternalRemoteDragActive && IsNavigationPanePoint(point))
        {
            HideNativeDragFeedback();
            return;
        }

        if (ContentFrame.Content is PhotosPage photos)
        {
            var scale = photos.XamlRoot?.RasterizationScale ?? 1;
            var windowPoint = new Point(point.X / scale, point.Y / scale);
            try
            {
                var pagePoint = PageRoot.TransformToVisual(photos).TransformPoint(windowPoint);
                if (!photos.UpdateExternalDragPreview(pagePoint))
                {
                    photos.ClearExternalDragPreview();
                }
            }
            catch
            {
                photos.ClearExternalDragPreview();
            }
        }
        else if (ContentFrame.Content is FileManagementPage files)
        {
            files.ShowExternalDragPreview();
        }
    }

    private void HideNativeDragFeedback()
    {
        if (ContentFrame.Content is PhotosPage photos)
        {
            photos.ClearExternalDragPreview();
        }
        else if (ContentFrame.Content is FileManagementPage files)
        {
            files.ClearExternalDragPreview();
        }
    }

    private static IReadOnlyList<string> ReadNativeDropFiles(IntPtr hDrop)
    {
        const uint allFiles = 0xFFFFFFFF;
        var count = DragQueryFile(hDrop, allFiles, null, 0);
        if (count == 0) return Array.Empty<string>();

        var paths = new List<string>((int)count);
        for (uint index = 0; index < count; index++)
        {
            var length = DragQueryFile(hDrop, index, null, 0);
            if (length == 0) continue;
            var buffer = new StringBuilder((int)length + 1);
            DragQueryFile(hDrop, index, buffer, (uint)buffer.Capacity);
            var path = buffer.ToString();
            if (File.Exists(path)) paths.Add(path);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void HandleNativeFileDrop(
        IReadOnlyList<string> paths,
        NativePoint point)
    {
        if (paths.Count == 0) return;

        // The navigation pane is deliberately not a file drop target. This
        // guard covers the WM_DROPFILES fallback used when Explorer cannot
        // participate in the WinUI drag event, so sidebar drops never start a
        // transfer or report a misleading success message.
        if (!_nativeInternalRemoteDragActive && IsNavigationPanePoint(point))
        {
            HideNativeDragFeedback();
            return;
        }

        var destination = "Download";
        if (ContentFrame.Content is PhotosPage photos)
        {
            var scale = photos.XamlRoot?.RasterizationScale ?? 1;
            var windowPoint = new Point(point.X / scale, point.Y / scale);
            try
            {
                var pagePoint = PageRoot.TransformToVisual(photos).TransformPoint(windowPoint);
                destination = photos.ResolveExternalDropDestination(pagePoint);
            }
            catch
            {
                // Navigation can replace the page between WM_DROPFILES and
                // the dispatcher callback. Keep the generic Pictures target
                // instead of losing the drop completely.
                destination = "Pictures";
            }
            photos.ShowExternalDropFeedback(destination);
        }
        else if (ContentFrame.Content is FileManagementPage files)
        {
            destination = "Download/Hinge";
            files.ShowExternalDropFeedback();
        }

        // This is the Win32 shell-drop fallback for desktop/Explorer drags.
        // It uses the same transfer pipeline as the XAML Drop event.
        ComputerFilesDropped(
            this,
            new ComputerFilesDroppedEventArgs(paths, destination));
    }

    private bool IsNavigationPanePoint(NativePoint point)
    {
        var scale = PageRoot.XamlRoot?.RasterizationScale ?? 1;
        return IsNavigationPanePoint(point.X / scale);
    }

    private bool IsNavigationPanePoint(double xDip)
    {
        if (xDip < 0) return false;
        var paneWidth = AppNavigation.IsPaneOpen
            ? AppNavigation.OpenPaneLength
            : AppNavigation.CompactPaneLength;
        return xDip < paneWidth;
    }

    private void UninitializeNativeFileDrop()
    {
        if (_nativeDropWindowHandle == IntPtr.Zero) return;

        if (_nativeDragPoller != null)
        {
            _nativeDragPoller.Stop();
            _nativeDragPoller.Tick -= NativeDragPoller_Tick;
            _nativeDragPoller = null;
        }

        if (_originalWindowProc != IntPtr.Zero)
        {
            SetWindowLongPtr(
                _nativeDropWindowHandle,
                GwlpWndProc,
                _originalWindowProc);
        }
        DragAcceptFiles(_nativeDropWindowHandle, false);
        _nativeDropWindowHandle = IntPtr.Zero;
        _originalWindowProc = IntPtr.Zero;
        _windowProcDelegate = null;
    }

    private static void ApplyDefaultWindowSize(AppWindow appWindow, WindowId windowId)
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            var workArea = displayArea.WorkArea;
            double scale = Math.Min(
                1,
                Math.Min(
                    (double)workArea.Width / DefaultWindowWidth,
                    (double)workArea.Height / DefaultWindowHeight));
            int width = Math.Max(1, (int)Math.Round(DefaultWindowWidth * scale));
            int height = Math.Max(1, (int)Math.Round(DefaultWindowHeight * scale));

            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
        }
        catch
        {
            // Keep the platform-provided size when the window is not attached to
            // a display yet (for example, in an unpackaged test host).
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !_minimizeToTray) return;

        args.Cancel = true;
        sender.Hide();
        _trayManager.ShowNotification("Hinge", "应用仍在后台运行，可从系统托盘恢复或退出。");
    }

    private static string DefaultReceiveDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "Hinge");

    private static string LoadReceiveDirectory()
    {
        if (TryNormalizeReceiveDirectory(
                ReadUserSetting(ReceiveDirectorySettingName) as string,
                out var registryDirectory))
        {
            return registryDirectory;
        }

        try
        {
            if (ApplicationData.Current.LocalSettings.Values[ReceiveDirectorySettingName] is string localDirectory &&
                TryNormalizeReceiveDirectory(localDirectory, out var normalizedDirectory))
            {
                SaveReceiveDirectory(normalizedDirectory);
                return normalizedDirectory;
            }
        }
        catch
        {
            // Fall back to the stable default when LocalSettings is unavailable.
        }

        return DefaultReceiveDirectory();
    }

    private static bool TryNormalizeReceiveDirectory(string? value, out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            var fullPath = Path.GetFullPath(value.Trim());
            if (!Path.IsPathRooted(fullPath)) return false;

            var root = Path.GetPathRoot(fullPath);
            directory = string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? root ?? fullPath
                : fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static void SaveReceiveDirectory(string directory)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[ReceiveDirectorySettingName] = directory;
        }
        catch
        {
            // The registry copy below is the durable fallback for unpackaged
            // EXE updates and for environments without ApplicationData.
        }
        WriteUserSetting(ReceiveDirectorySettingName, directory);
    }

    private static string LoadWindowTheme()
    {
        try
        {
            if (ReadUserSetting("WindowTheme") is string registryTheme &&
                registryTheme is "system" or "light" or "dark")
            {
                return registryTheme;
            }
            if (ApplicationData.Current.LocalSettings.Values["WindowTheme"] is string theme &&
                theme is "system" or "light" or "dark")
            {
                WriteUserSetting("WindowTheme", theme);
                return theme;
            }
        }
        catch { }
        return "system";
    }

    private static void SaveWindowTheme(string theme)
    {
        try { ApplicationData.Current.LocalSettings.Values["WindowTheme"] = theme; }
        catch { }
        WriteUserSetting("WindowTheme", theme);
    }

    private void ApplyWindowTheme(string theme)
    {
        PageRoot.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        AppNavigation.RequestedTheme = PageRoot.RequestedTheme;
        ContentFrame.RequestedTheme = PageRoot.RequestedTheme;
    }

    private static int WindowThemeIndex(string theme) => theme switch
    {
        "light" => 1,
        "dark" => 2,
        _ => 0
    };

    private static string WindowThemeName(string theme) => theme switch
    {
        "light" => "浅色模式",
        "dark" => "深色模式",
        _ => "跟随系统"
    };

    private static string LoadWindowMaterial()
    {
        try
        {
            if (ReadUserSetting("WindowMaterial") is string registryMaterial &&
                registryMaterial is "mica" or "micaAlt" or "acrylic" or "acrylicThin")
            {
                return registryMaterial;
            }
            if (ApplicationData.Current.LocalSettings.Values["WindowMaterial"] is string material &&
                material is "mica" or "micaAlt" or "acrylic" or "acrylicThin")
            {
                WriteUserSetting("WindowMaterial", material);
                return material;
            }
        }
        catch { }
        return "mica";
    }

    private static void SaveWindowMaterial(string material)
    {
        try { ApplicationData.Current.LocalSettings.Values["WindowMaterial"] = material; }
        catch { }
        WriteUserSetting("WindowMaterial", material);
    }

    private static string WindowMaterialName(string material) => material switch
    {
        "micaAlt" => "MICA Alt · 层次云母",
        "acrylic" => "亚克力 · 磨砂玻璃",
        "acrylicThin" => "细亚克力 · 更透亮的磨砂",
        _ => "MICA · 柔和云母"
    };

    private void ApplyWindowMaterial(string material)
    {
        try
        {
            SystemBackdrop = material switch
            {
                "micaAlt" => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "acrylic" => new DesktopAcrylicBackdrop(),
                "acrylicThin" => new DesktopAcrylicBackdrop(),
                _ => new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base }
            };
        }
        catch
        {
            SystemBackdrop = new MicaBackdrop();
        }
    }

    private static string? LoadWindowBackgroundPath()
    {
        try
        {
            if (ReadUserSetting("WindowBackgroundPath") is string registryPath &&
                !string.IsNullOrWhiteSpace(registryPath))
            {
                return registryPath;
            }
            if (ApplicationData.Current.LocalSettings.Values["WindowBackgroundPath"] is string localPath &&
                !string.IsNullOrWhiteSpace(localPath))
            {
                WriteUserSetting("WindowBackgroundPath", localPath);
                return localPath;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveWindowBackgroundPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ApplicationData.Current.LocalSettings.Values.Remove("WindowBackgroundPath");
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WindowBackgroundPath"] = path;
            }
        }
        catch { }
        WriteUserSetting("WindowBackgroundPath", path);
    }

    private void ApplyBackgroundImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            WindowBackgroundImage.Source = null;
            WindowBackgroundImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            WindowBackgroundImage.Source = new BitmapImage(new Uri(path));
            WindowBackgroundImage.Visibility = Visibility.Visible;
        }
        catch
        {
            WindowBackgroundImage.Source = null;
            WindowBackgroundImage.Visibility = Visibility.Collapsed;
        }
    }

    private static bool LoadMinimizeToTray()
    {
        try
        {
            if (ReadUserSetting("MinimizeToTray") is int registryValue)
            {
                return registryValue != 0;
            }
            if (ApplicationData.Current.LocalSettings.Values["MinimizeToTray"] is bool value)
            {
                WriteUserSetting("MinimizeToTray", value ? 1 : 0);
                return value;
            }
            return false;
        }
        catch
        {
            return ReadUserSetting("MinimizeToTray") is int registryValue && registryValue != 0;
        }
    }

    private static void SaveMinimizeToTray(bool value)
    {
        try { ApplicationData.Current.LocalSettings.Values["MinimizeToTray"] = value; }
        catch { }
        WriteUserSetting("MinimizeToTray", value ? 1 : 0);
    }

    public static bool ShouldStartSilently(string? arguments)
    {
        return !string.IsNullOrWhiteSpace(arguments) &&
            arguments.Contains(StartupArgument, StringComparison.OrdinalIgnoreCase) &&
            LoadSilentStartup();
    }

    private static bool LoadStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: false);
            return key?.GetValue(StartupRegistryValueName) is string value &&
                !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySaveStartWithWindows(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true);
            if (key == null) return false;

            if (!value)
            {
                key.DeleteValue(StartupRegistryValueName, throwOnMissingValue: false);
                return true;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            key.SetValue(
                StartupRegistryValueName,
                $"\"{executablePath}\" {StartupArgument}",
                RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LoadSilentStartup()
    {
        try
        {
            if (ReadUserSetting("SilentStartup") is int registryValue)
            {
                return registryValue != 0;
            }
            if (ApplicationData.Current.LocalSettings.Values["SilentStartup"] is bool value)
            {
                WriteUserSetting("SilentStartup", value ? 1 : 0);
                return value;
            }
            return false;
        }
        catch
        {
            return ReadUserSetting("SilentStartup") is int registryValue && registryValue != 0;
        }
    }

    private static void SaveSilentStartup(bool value)
    {
        try { ApplicationData.Current.LocalSettings.Values["SilentStartup"] = value; }
        catch { }
        WriteUserSetting("SilentStartup", value ? 1 : 0);
    }

    private static object? ReadUserSetting(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserSettingsRegistryPath, writable: false);
            return key?.GetValue(valueName);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteUserSetting(string valueName, object? value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UserSettingsRegistryPath, writable: true);
            if (key == null) return;
            if (value == null)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(valueName, value);
            }
        }
        catch
        {
            // Registry mirroring is a compatibility layer; LocalSettings is
            // still the primary store when the registry is unavailable.
        }
    }

    private void OnDevicesChanged(object? sender, IReadOnlyList<Device> devices)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshDeviceList(devices);
            _ = TryAutoConnectHistoricalDeviceAsync(devices);
        });
    }

    private void OnReverseConnectionRequested(
        object? sender,
        DiscoveryConnectionRequestEventArgs args)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            // A reverse request marked as automatic is only valid for a peer
            // already present in this installation's trust store. Requests
            // without the marker remain manual-connect compatibility paths.
            if (args.Message.AutomaticReconnect &&
                !_trustStore.IsTrusted(args.Message.DeviceId))
            {
                return;
            }

            if (_sessionManager.ConnectionForDevice(args.Message.DeviceId) != null)
            {
                return;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            while (_connectingDeviceId != null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(120);
                if (_sessionManager.ConnectionForDevice(args.Message.DeviceId) != null)
                {
                    return;
                }
            }
            if (_connectingDeviceId != null) return;

            if (_registry.TryGetDevice(args.Message.DeviceId, out var device) && device != null)
            {
                await ConnectDeviceAsync(
                    device,
                    automatic: args.Message.AutomaticReconnect);
            }
        });
    }

    private async Task TryAutoConnectHistoricalDeviceAsync(IReadOnlyList<Device> devices)
    {
        if (_activeConnection?.State == SessionState.Connected ||
            _sessionManager.ActiveConnections.Any(connection =>
                connection.State == SessionState.Connected) ||
            _connectingDeviceId != null)
        {
            return;
        }

        Device? candidate = devices.FirstOrDefault(device =>
            device.ConnectionState != ConnectionState.Disconnected &&
            device.NetworkAddresses.Count > 0 &&
            _trustStore.IsTrusted(device.DeviceId));
        if (candidate == null) return;

        DateTime now = DateTime.UtcNow;
        if (_automaticConnectAttempts.TryGetValue(candidate.DeviceId, out var lastAttempt) &&
            now - lastAttempt < TimeSpan.FromSeconds(5))
        {
            return;
        }
        _automaticConnectAttempts[candidate.DeviceId] = now;

        StatusText.Text = $"正在自动连接历史设备：{candidate.Name}";
        HeaderStatusText.Text = "自动连接中...";
        await ConnectDeviceAsync(candidate, automatic: true);

        if (_sessionManager.ConnectionForDevice(candidate.DeviceId) != null)
        {
            _automaticConnectAttempts.Remove(candidate.DeviceId);
        }
        else
        {
            ScheduleHistoricalReconnect(candidate.DeviceId);
        }
    }

    private void ScheduleHistoricalReconnect(string? deviceId = null)
    {
        if (deviceId != null && !_trustStore.IsTrusted(deviceId)) return;
        if (_historicalReconnectTimer != null) return;

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_historicalReconnectTimer, timer))
            {
                _historicalReconnectTimer = null;
            }

            _ = TryAutoConnectHistoricalDeviceAsync(_registry.GetAllDevices());
        };
        _historicalReconnectTimer = timer;
        timer.Start();
    }

    private void CancelHistoricalReconnect()
    {
        _historicalReconnectTimer?.Stop();
        _historicalReconnectTimer = null;
    }

    private void OnClientConnected(object? sender, SessionConnection connection)
    {
        AttachConnection(connection);
        DispatcherQueue.TryEnqueue(() =>
        {
            HeaderStatusText.Text = "正在确认设备身份";
            StatusText.Text = "已建立网络连接，正在完成双向握手";
        });
    }

    private async void OnMessageReceived(object? sender, SessionMessageEventArgs args)
    {
        try
        {
            await _transferManager.HandleIncomingFrameAsync(args.Connection, args.Frame);
            await _clipboardManager.HandleIncomingFrameAsync(args.Connection, args.Frame);
            _remoteInputManager.HandleIncomingFrame(args.Connection, args.Frame);
            await _notificationManager.HandleIncomingFrameAsync(args.Connection, args.Frame);
        }
        catch (Exception exception)
        {
            // Network frames are raised from the socket read loop. Never allow a
            // malformed or failed frame to escape this async-void event handler
            // and terminate the WinUI process.
            DispatcherQueue.TryEnqueue(() =>
                StatusText.Text = $"网络数据处理失败：{exception.Message}");
        }
    }

    private void OnTransferProgress(object? sender, TransferProgress progress)
    {
        DispatcherQueue.TryEnqueue(() => StatusText.Text = $"正在传输：{progress.FileName} · {progress.Percentage:F0}%");
    }

    private void OnFileReceived(object? sender, string path)
    {
        if (_pendingRemoteMedia != null &&
            string.Equals(
                Path.GetFileName(path),
                Path.GetFileName(_pendingRemoteMediaName),
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingRemoteMedia.TrySetResult(path);
        }
        DispatcherQueue.TryEnqueue(() => StatusText.Text = $"已接收文件：{Path.GetFileName(path)}");
    }

    private void OnTransferFailed(object? sender, TransferFailure failure)
    {
        if (_pendingRemoteMedia != null &&
            string.Equals(
                Path.GetFileName(failure.FileName),
                Path.GetFileName(_pendingRemoteMediaName),
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingRemoteMedia.TrySetException(
                new IOException($"接收 {failure.FileName} 失败：{failure.Error}"));
        }

        DispatcherQueue.TryEnqueue(() =>
            StatusText.Text = $"文件接收失败：{failure.FileName} · {failure.Error}");
    }

    private void OnClipboardReceived(object? sender, ClipboardEventMessage message)
    {
        DispatcherQueue.TryEnqueue(() => ClipboardStatusText.Text = $"收到跨端内容：{Preview(message.Content)}");
    }

    private void OnUrlHandoffReceived(object? sender, string url)
    {
        DispatcherQueue.TryEnqueue(() => ClipboardStatusText.Text = $"收到网页链接：{Preview(url)}");
    }

    private void OnNotificationReceived(object? sender, NotificationEventMessage notification)
    {
        var isSmsNotification = string.Equals(notification.Source, "sms", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.Source, "mms", StringComparison.OrdinalIgnoreCase);
        var useTrayNotification = isSmsNotification ||
            !_notificationPresenter.IsSystemNotificationAvailable;

        // SMS/MMS always uses the tray balloon so it is useful while Hinge is
        // minimized. Only a recognized SMS/MMS verification code gets a
        // copy-on-click action; all other notifications keep the normal open
        // behavior.
        if (useTrayNotification)
        {
            var hasVerificationCode = isSmsNotification &&
                notification.IsVerificationCode &&
                !string.IsNullOrWhiteSpace(notification.VerificationCode);
            var fallbackText = hasVerificationCode
                ? $"{notification.Title}\n{notification.Content}\n点击此通知复制验证码"
                : $"{notification.Title}\n{notification.Content}";

            if (hasVerificationCode)
            {
                _trayManager.ShowNotification(
                    string.IsNullOrWhiteSpace(notification.AppName) ? "Hinge" : notification.AppName,
                    fallbackText,
                    () =>
                    {
                        if (!_notificationPresenter.TryCopyVerificationCode(notification.NotificationId))
                        {
                            _ = _clipboardAdapter.SetTextAsync(notification.VerificationCode!);
                        }
                    });
            }
            else
            {
                _trayManager.ShowNotification(
                    string.IsNullOrWhiteSpace(notification.AppName) ? "Hinge" : notification.AppName,
                    fallbackText);
            }
        }
        DispatcherQueue.TryEnqueue(() =>
            StatusText.Text = $"已收到远程通知：{notification.Title}");
    }

    private async void OpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));
        }
        catch
        {
            SettingsStatusText.Text = "无法打开 Windows 通知设置，请手动搜索“通知”。";
        }
    }

    private void RefreshDeviceList(IReadOnlyList<Device> devices)
    {
        DeviceCountText.Text = devices.Count == 0 ? "正在搜索附近设备" : $"发现 {devices.Count} 台设备";
        DiscoveryInfoBar.Title = devices.Count == 0 ? "正在搜索附近设备" : $"已发现 {devices.Count} 台设备";
        DiscoveryInfoBar.Message = devices.Count == 0
            ? "请确认两台设备在同一局域网，并允许 Hinge 通过 Windows 防火墙。"
            : "选择设备后直接连接；主动建立的局域网会话会自动保存为可信设备。";
        DeviceListView.Items.Clear();

        if (devices.Count == 0)
        {
            DeviceListView.Items.Add(new ListViewItem
            {
                IsHitTestVisible = false,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Padding = new Thickness(12, 18, 12, 18),
                    Children =
                    {
                        new ProgressRing { IsActive = true, Width = 24, Height = 24 },
                        new TextBlock
                        {
                            Text = "暂未收到其他设备的发现消息。可点击“手动连接”输入对方 IP。",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                }
            });
            return;
        }

        foreach (var device in devices)
        {
            bool online = device.ConnectionState != ConnectionState.Disconnected;
            bool connected = IsDeviceSessionConnected(device);
            var row = new Grid { MinHeight = 58 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new SymbolIcon
            {
                Symbol = device.Platform == DevicePlatform.Android ? Symbol.Phone : Symbol.AllApps,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                Width = 28,
                Height = 28
            };
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var details = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock { Text = device.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            details.Children.Add(new TextBlock
            {
                Text = connected
                    ? $"已连接 · {string.Join(", ", device.NetworkAddresses)}"
                    : online
                        ? $"在线 · {string.Join(", ", device.NetworkAddresses)}"
                    : "离线",
                FontSize = 14,
                Foreground = online
                    ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            Grid.SetColumn(details, 1);
            row.Children.Add(details);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            var connectButton = new Button
            {
                Content = connected ? "已连接" : online ? "连接" : "重试",
                Tag = device,
                IsEnabled = !connected
            };
            connectButton.Click += DeviceConnect_Click;
            actions.Children.Add(connectButton);

            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);

            DeviceListView.Items.Add(new ListViewItem
            {
                Content = row,
                Tag = device
            });
        }
    }

    private bool IsDeviceSessionConnected(Device device)
    {
        if (_activeConnection?.State == SessionState.Connected &&
            string.Equals(
                _activeConnection.RemoteDeviceId,
                device.DeviceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _sessionManager.ActiveConnections.Any(connection =>
            connection.State == SessionState.Connected &&
            string.Equals(
                connection.RemoteDeviceId,
                device.DeviceId,
                StringComparison.OrdinalIgnoreCase));
    }

    private async void DeviceConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Device device })
        {
            await ConnectDeviceAsync(device, automatic: false);
        }
    }

    private async Task ConnectDeviceAsync(Device device, bool automatic)
    {
        if (device.NetworkAddresses.Count == 0)
        {
            if (!automatic) ShowStatus("无法连接", $"未找到 {device.Name} 的局域网地址。");
            return;
        }

        if (_connectingDeviceId != null)
        {
            if (!automatic) ShowStatus("正在连接", "正在尝试连接另一台设备，请稍候。");
            return;
        }

        _connectingDeviceId = device.DeviceId;
        SessionConnection? connection = null;
        try
        {
            StatusText.Text = automatic
                ? $"正在自动连接：{device.Name}"
                : $"正在连接：{device.Name}";
            HeaderStatusText.Text = automatic ? "自动连接中..." : "连接中...";
            Exception? lastError = null;
            var addresses = OrderAddresses(device.NetworkAddresses).ToArray();

            // Request the reverse path immediately instead of waiting for a
            // blocked inbound TCP attempt to time out. SessionManager removes
            // duplicate sockets deterministically after identity exchange.
            foreach (var address in addresses)
            {
                try
                {
                    await _discoveryService.RequestReverseConnectionAsync(
                        address,
                        device.DiscoveryPort,
                        automaticReconnect: automatic);
                }
                catch { }
            }

            foreach (var address in addresses)
            {
                foreach (var port in new[] { device.SessionPort, Constants.SessionTcpPort }.Distinct())
                {
                    try
                    {
                        connection = await _sessionManager.ConnectToPeerAsync(address, port);
                        break;
                    }
                    catch (Exception exception)
                    {
                        lastError = exception;
                    }
                }
                if (connection != null) break;
            }

            if (connection == null)
            {
                connection = await WaitForReverseConnectionAsync(device.DeviceId);
            }
            if (connection == null)
            {
                throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
            }

            HeaderStatusText.Text = "正在确认设备身份";
            SessionPeerInfo peer = await WaitForPeerInfoAsync(connection);
            if (!string.Equals(peer.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                connection.Dispose();
                throw new InvalidOperationException("目标设备身份不匹配，已拒绝连接。");
            }
            await Task.Delay(20);
            connection = _sessionManager.ConnectionForDevice(device.DeviceId) ?? connection;

            // Only register and persist the peer after the hello identity has
            // matched the device discovered on the network. This prevents an
            // unexpected endpoint from becoming a historical trusted device.
            AttachConnection(connection);
            StatusText.Text = $"已连接到 {peer.Name}，历史设备已自动恢复";
        }
        catch (Exception exception)
        {
            // A connection that did not complete identity validation must not
            // remain alive in the background.
            if (connection is { State: not SessionState.Disconnected })
            {
                connection.Dispose();
            }
            HeaderStatusText.Text = "局域网就绪";
            if (automatic)
            {
                StatusText.Text = $"历史设备自动连接失败：{device.Name}";
            }
            else
            {
                ShowStatus("连接失败", $"无法连接到 {device.Name}：{exception.Message}");
            }
        }
        finally
        {
            if (string.Equals(_connectingDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _connectingDeviceId = null;
            }
        }
    }

    private async Task<SessionConnection?> WaitForReverseConnectionAsync(
        string deviceId,
        TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            var connection = _sessionManager.ConnectionForDevice(deviceId);
            if (connection != null) return connection;
            await Task.Delay(100);
        }
        return _sessionManager.ConnectionForDevice(deviceId);
    }

    private static async Task<SessionPeerInfo> WaitForPeerInfoAsync(
        SessionConnection connection,
        TimeSpan? timeout = null)
    {
        if (connection.PeerInfo is { } identified) return identified;

        var completion = new TaskCompletionSource<SessionPeerInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPeerIdentified(object? sender, SessionPeerInfo peer) => completion.TrySetResult(peer);
        connection.PeerIdentified += OnPeerIdentified;
        try
        {
            if (connection.PeerInfo is { } raced) return raced;
            return await completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(4));
        }
        finally
        {
            connection.PeerIdentified -= OnPeerIdentified;
        }
    }

    private void DeviceListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ListViewItem { Tag: Device device })
        {
            return;
        }

        var connection = GetConnectedConnection();
        bool isActiveDevice = _activeDevice?.DeviceId == device.DeviceId ||
            string.Equals(connection?.RemoteDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase);
        if (isActiveDevice && connection != null)
        {
            _activeConnection = connection;
            ShowFileManagement();
        }
        else
        {
            ShowStatus("设备尚未连接", $"请先点击“连接”建立 {device.Name} 的会话。");
        }
    }

    private void NavHome_Click(object sender, RoutedEventArgs e) => NavigateTo("首页", 0);
    private void NavTransfer_Click(object sender, RoutedEventArgs e) => NavigateTo("设备操作", 0);
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo("设置", 0);

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.QueryText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            RefreshDeviceList(_registry.GetAllDevices());
            return;
        }

        var destinations = new (string Keyword, string Title)[]
        {
            ("连接", "首页"),
            ("文件", "文件管理"),
            ("笔记", "笔记"),
            ("待办", "待办"),
            ("日历", "日历"),
            ("相册", "相册"),
            ("设置", "设置")
        };
        var destination = destinations.FirstOrDefault(item =>
            query.Contains(item.Keyword, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(destination.Keyword))
        {
            NavigateTo(destination.Title, 0);
            return;
        }

        var matches = _registry.GetAllDevices().Where(device =>
            device.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            device.NetworkAddresses.Any(address => address.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        RefreshDeviceList(matches);
        StatusText.Text = matches.Count == 0 ? $"没有找到“{query}”" : $"找到 {matches.Count} 台匹配设备";
    }

    private void AppNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavSettings_Click(sender, new RoutedEventArgs());
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case "home":
                NavigateTo("首页", 0);
                break;
            case "files":
                NavigateTo("文件管理", 0);
                break;
            case "notes":
                NavigateTo("笔记", 0);
                break;
            case "todo":
                NavigateTo("待办", 0);
                break;
            case "calendar":
                NavigateTo("日历", 0);
                break;
            case "photos":
                NavigateTo("相册", 0);
                break;
            case "notificationHistory":
                NavigateTo("手机历史通知", 0);
                break;
            case "transfers":
                NavigateTo("设备操作", 0);
                break;
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _discoveryService.BroadcastOnceAsync();
            ShowStatus("设备发现", "已发送局域网发现广播。");
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("设备发现失败", exception.Message, false);
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e) => _ = ShowManualConnectAsync();
    private void BtnFiles_Click(object sender, RoutedEventArgs e) => _ = PickAndSendFileAsync();
    private void BtnQuickTransfer_Click(object sender, RoutedEventArgs e) => _ = ShowQuickTransferAsync();
    private void BtnClipboard_Click(object sender, RoutedEventArgs e)
    {
        _clipboardManager.AutoSync = !_clipboardManager.AutoSync;
        ClipboardStatusText.Text = _clipboardManager.AutoSync ? "自动同步已开启" : "自动同步已暂停";
        StatusText.Text = ClipboardStatusText.Text;
    }

    private async Task ShowManualConnectAsync()
    {
        var input = new TextBox
        {
            PlaceholderText = "例如 192.168.1.105",
            InputScope = new Microsoft.UI.Xaml.Input.InputScope
            {
                Names = { new Microsoft.UI.Xaml.Input.InputScopeName { NameValue = Microsoft.UI.Xaml.Input.InputScopeNameValue.Number } }
            }
        };
        var dialog = new ContentDialog
        {
            Title = "手动连接设备",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "输入安卓设备的局域网 IPv4 地址：" },
                    input,
                    new TextBlock
                    {
                        Text = "设备仍需在 UDP 52830 发现端口和 TCP 52831 会话端口可达。",
                        FontSize = 14,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            PrimaryButtonText = "连接",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!IPAddress.TryParse(input.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            await ShowDialogAsync("地址无效", "请输入有效的 IPv4 地址，例如 192.168.1.105。", false);
            return;
        }

        try
        {
            HeaderStatusText.Text = "连接中...";
            var connection = await _sessionManager.ConnectToPeerAsync(address);
            AttachConnection(connection);
            var device = FindDeviceForConnection(connection);
            HeaderStatusText.Text = "正在确认设备身份";
            ActivityInfoBar.Title = "正在完成连接";
            ActivityInfoBar.Message = device == null
                ? $"已连接 {address}，正在等待设备身份响应。"
                : $"已连接到 {device.Name}，正在完成双向握手。";
            ActivityInfoBar.Severity = InfoBarSeverity.Informational;
        }
        catch (Exception exception)
        {
            HeaderStatusText.Text = "局域网就绪";
            await ShowDialogAsync("连接失败", $"无法连接到 {address}：{exception.Message}", false);
        }
    }

    private void NavigateTo(string title, double unusedOffset)
    {
        // Keep only the product name in the native title bar. Page names belong
        // to the navigation/content area and must not replace the app title.
        switch (title)
        {
            case "首页":
                ContentFrame.Navigate(typeof(HomePage));
                break;
            case "文件管理":
                ShowFileManagement();
                return;
            case "设置":
                ContentFrame.Navigate(typeof(SettingsPage));
                break;
            case "个性化":
                ContentFrame.Navigate(typeof(PersonalizationPage));
                break;
            case "笔记":
                ContentFrame.Navigate(typeof(NotesPage));
                break;
            case "待办":
                ContentFrame.Navigate(typeof(TodoPage));
                break;
            case "日历":
                ContentFrame.Navigate(typeof(CalendarPage));
                break;
            case "相册":
                ContentFrame.Navigate(typeof(PhotosPage));
                break;
            case "手机历史通知":
                ContentFrame.Navigate(typeof(NotificationHistoryPage));
                break;
            case "设备操作":
                NavigateToFeature("设备操作", "把常用的跨设备操作集中在这里，避免把剪贴板入口堆到标题栏。", "请选择一项操作", "\uE72D", "选择文件并发送", "发送文字或链接");
                return;
        }

        PageTitle.Text = title;
    }

    private void NavigateToFeature(string title, string description, string status, string glyph, string? primary = null, string? secondary = null)
    {
        ContentFrame.Navigate(typeof(FeaturePage), new FeaturePageOptions(title, description, status, glyph, primary, secondary));
        PageTitle.Text = title;
    }

    private void AttachConnection(SessionConnection connection)
    {
        _clipboardManager.RegisterConnection(connection);
        _notificationManager.RegisterConnection(connection);
        if (_observedConnections.Add(connection))
        {
            connection.StateChanged += OnConnectionStateChanged;
            connection.PeerIdentified += OnPeerIdentified;
        }
        _activeConnection = connection;
        if (connection.PeerInfo is { } peer)
        {
            OnPeerIdentified(connection, peer);
        }
    }

    private void OnPeerIdentified(object? sender, SessionPeerInfo peer)
    {
        if (sender is not SessionConnection connection || connection.State != SessionState.Connected) return;

        // 用户主动建立的局域网连接即视为授权。保留 TrustStore 作为底层
        // 兼容层，使通知、剪贴板等敏感通道继续沿用现有信任检查。
        _pairingManager.SaveTrustedPeer(peer.DeviceId, peer.Name);

        string remoteAddress = connection.RemoteAddress?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(remoteAddress))
        {
            _registry.UpsertDevice(new DiscoveryMessage
            {
                DeviceId = peer.DeviceId,
                Name = peer.Name,
                Manufacturer = peer.Manufacturer,
                Model = peer.Model,
                Platform = peer.Platform,
                Version = Constants.AppVersion,
                ProtocolVersion = Constants.ProtocolVersion,
                Port = Constants.SessionTcpPort
            }, remoteAddress);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (connection.State != SessionState.Connected) return;
            Device? device = FindDeviceForConnection(connection);
            if (device == null)
            {
                device = new Device
                {
                    DeviceId = peer.DeviceId,
                    Name = string.IsNullOrWhiteSpace(peer.Name) ? "Android 设备" : peer.Name,
                    Manufacturer = peer.Manufacturer,
                    Model = peer.Model,
                    Platform = Enum.TryParse<DevicePlatform>(peer.Platform, true, out var platform)
                        ? platform
                        : DevicePlatform.Android,
                    NetworkAddresses = string.IsNullOrWhiteSpace(remoteAddress)
                        ? new List<string>()
                        : new List<string> { remoteAddress },
                    ConnectionState = ConnectionState.Connected,
                    TrustState = TrustState.Trusted
                };
            }
            device.TrustState = TrustState.Trusted;

            _activeConnection = connection;
            SetActiveDevice(device);
            SetHeroDevice(device);
            HeaderStatusText.Text = $"已连接 {device.Name}";
            StatusText.Text = $"已建立局域网会话 · {DateTime.Now:HH:mm:ss}";
            ClipboardStatusText.Text = "已连接设备，可同步剪贴板";
            ActivityInfoBar.Title = "设备连接正常";
            ActivityInfoBar.Message = $"已连接到 {device.Name}，双向身份握手已完成。";
            ActivityInfoBar.Severity = InfoBarSeverity.Success;
            RefreshCurrentWorkspacePage(connection);
        });

        // 握手名称可能来自 Android 上一次启动时保存的身份文件。连接真正建立后，
        // 再读取一次 Android 原生设备信息，确保首页显示的是当前手机的市场名称。
        _ = RefreshRemoteDeviceInfoAsync(connection);
    }

    private async Task RefreshRemoteDeviceInfoAsync(SessionConnection connection)
    {
        try
        {
            var info = await _workspaceRemoteClient.LoadDeviceInfoAsync(connection);
            var deviceId = connection.RemoteDeviceId;
            var remoteAddress = connection.RemoteAddress?.ToString();
            if (info == null || string.IsNullOrWhiteSpace(deviceId) ||
                string.IsNullOrWhiteSpace(remoteAddress) ||
                string.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }

            _registry.UpsertDevice(new DiscoveryMessage
            {
                DeviceId = deviceId,
                Name = info.Name,
                Manufacturer = info.Manufacturer,
                Model = info.Model,
                Platform = "android",
                Version = Constants.AppVersion,
                ProtocolVersion = Constants.ProtocolVersion,
                Port = Constants.SessionTcpPort
            }, remoteAddress);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (connection.State != SessionState.Connected ||
                    _activeConnection != connection)
                {
                    return;
                }

                var device = FindDeviceForConnection(connection);
                if (device == null) return;
                SetActiveDevice(device);
                SetHeroDevice(device);
                HeaderStatusText.Text = $"已连接 {device.Name}";
                StatusText.Text = $"已建立局域网会话 · {DateTime.Now:HH:mm:ss}";
                ActivityInfoBar.Message = $"已连接到 {device.Name}，已刷新设备名称。";
            });
        }
        catch
        {
            // 旧版本 Android 不支持该工作区命令时，继续使用握手中的名称。
        }
    }

    private void OnConnectionStateChanged(object? sender, SessionState state)
    {
        if (sender is not SessionConnection connection ||
            state != SessionState.Disconnected)
        {
            return;
        }

        _clipboardManager.UnregisterConnection(connection);
        if (ReferenceEquals(_activeConnection, connection))
        {
            SessionConnection? replacement = connection.RemoteDeviceId is { } remoteId
                ? _sessionManager.ConnectionForDevice(remoteId)
                : null;
            if (replacement != null && !ReferenceEquals(replacement, connection))
            {
                AttachConnection(replacement);
                return;
            }

            var disconnectedDeviceId =
                connection.RemoteDeviceId ?? _activeDevice?.DeviceId ?? string.Empty;
            _registry.MarkSessionDisconnected(disconnectedDeviceId);
            DispatcherQueue.TryEnqueue(() =>
                ScheduleHistoricalReconnect(disconnectedDeviceId));

            DispatcherQueue.TryEnqueue(() =>
            {
                _activeConnection = null;
                _activeDevice = null;
                SetHeroDevice(null);
                HeaderStatusText.Text = "局域网就绪";
                StatusText.Text = "设备连接已断开";
                ActivityInfoBar.Title = "等待设备连接";
                ActivityInfoBar.Message = "设备会继续在局域网内被发现。";
                ActivityInfoBar.Severity = InfoBarSeverity.Informational;
                RefreshCurrentWorkspacePage(null);
            });
        }
    }

    private void RefreshCurrentWorkspacePage(SessionConnection? connection)
    {
        switch (ContentFrame.Content)
        {
            case FileManagementPage:
                _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
                break;
            case NotesPage notes:
                _ = notes.LoadAsync(connection);
                break;
            case TodoPage todo:
                _ = todo.LoadAsync(connection);
                break;
            case CalendarPage calendar:
                _ = calendar.LoadAsync(connection);
                break;
            case PhotosPage photos:
                _ = photos.LoadAsync(connection);
                break;
        }
    }

    private Device? FindDeviceForConnection(SessionConnection connection)
    {
        if (!string.IsNullOrWhiteSpace(connection.RemoteDeviceId) &&
            _registry.TryGetDevice(connection.RemoteDeviceId, out var identifiedDevice))
        {
            return identifiedDevice;
        }

        string? remoteAddress = connection.RemoteAddress?.ToString();
        if (string.IsNullOrWhiteSpace(remoteAddress))
        {
            return null;
        }

        return _registry.GetAllDevices().FirstOrDefault(device =>
            device.NetworkAddresses.Contains(remoteAddress, StringComparer.OrdinalIgnoreCase));
    }

    private void SetActiveDevice(Device device)
    {
        _activeDevice = device;
        device.ConnectionState = ConnectionState.Connected;
        if (_filePage != null)
        {
            _filePage.PathText.Text = "最近文件";
            _filePage.StatusText.Text = $"已连接 · {device.Name}";
        }
        RefreshDeviceList(_registry.GetAllDevices());
    }

    private void SetHeroDevice(Device? device)
    {
        if (device == null)
        {
            HeroDeviceName.Text = _localIdentity.Name;
            HeroDeviceDetail.Text = $"本机设备 ID：{ShortId(_localIdentity.DeviceId)} · 局域网服务已启动";
            HeroDeviceLogo.Content = new SymbolIcon { Symbol = Symbol.Phone, Width = 24, Height = 24 };
            return;
        }

        HeroDeviceName.Text = device.Name;
        HeroDeviceDetail.Text = $"手机设备 · 已连接 · {string.Join(", ", device.NetworkAddresses)}";
        HeroDeviceLogo.Content = BuildOfficialBrandLogo(device.Manufacturer, device.Model);
    }

    private static FrameworkElement BuildOfficialBrandLogo(string manufacturer, string model)
    {
        string? assetName = ResolveBrandAsset(manufacturer, model);
        if (assetName != null)
        {
            try
            {
                return new Image
                {
                    Width = 40,
                    Height = 30,
                    Stretch = Stretch.Uniform,
                    Source = new SvgImageSource(new Uri($"ms-appx:///Assets/Brands/{assetName}"))
                };
            }
            catch
            {
                // The package can still run if an optional brand asset is absent.
            }
        }

        return new SymbolIcon { Symbol = Symbol.Phone, Width = 24, Height = 24 };
    }

    private static string? ResolveBrandAsset(string manufacturer, string model)
    {
        var name = $"{manufacturer} {model}".Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Contains("honor", StringComparison.OrdinalIgnoreCase)) return "honor.svg";
        if (name.Contains("vivo", StringComparison.OrdinalIgnoreCase)) return "vivo.svg";
        if (name.Contains("xiaomi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("redmi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("poco", StringComparison.OrdinalIgnoreCase)) return "xiaomi.svg";
        if (name.Contains("samsung", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("galaxy", StringComparison.OrdinalIgnoreCase)) return "samsung.svg";
        if (name.Contains("huawei", StringComparison.OrdinalIgnoreCase)) return "huawei.svg";
        if (name.Contains("oppo", StringComparison.OrdinalIgnoreCase)) return "oppo.svg";
        return null;
    }

    private void ShowFileManagement()
    {
        // 首页的设备卡片和会话管理器都可能先于窗口字段完成更新。
        // 进入文件管理前把当前可用会话重新绑定，避免出现“首页已连接、
        // 文件管理却认为未连接”的分裂状态。
        _activeConnection = GetConnectedConnection();

        ContentFrame.Navigate(typeof(FileManagementPage));
        PageTitle.Text = "文件管理";
        ConfigureFileFilters(_fileCategory);
        if (FileCategoryList.SelectedIndex < 0)
        {
            FileCategoryList.SelectedIndex = 0;
        }
        else
        {
            _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
        }
    }

    private void FileCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileCategoryList.SelectedItem is not ListViewItem { Tag: string category })
        {
            return;
        }

        _fileCategory = category;
        _filePath = string.Empty;
        ConfigureFileFilters(category);
        _ = RefreshRemoteFilesAsync(category, _filePath);
    }

    private void BtnRefreshFiles_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh: true);
    }

    private void BtnBackRemoteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_fileCategory != "storage" || string.IsNullOrWhiteSpace(_filePath)) return;

        var normalized = _filePath.Trim('/');
        var separator = normalized.LastIndexOf('/');
        _filePath = separator < 0 ? string.Empty : normalized[..separator];
        _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
    }

    private void FileFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filePage == null || string.IsNullOrWhiteSpace(_fileCategory)) return;
        _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
    }

    private void ConfigureFileFilters(string category)
    {
        if (_filePage == null) return;
        bool documents = string.Equals(category, "documents", StringComparison.OrdinalIgnoreCase);
        _filePage.TypeFilter.Visibility = documents ? Visibility.Collapsed : Visibility.Visible;
        _filePage.DocumentFilter.Visibility = documents ? Visibility.Visible : Visibility.Collapsed;
        if (documents)
        {
            _filePage.DocumentFilter.SelectedIndex = 0;
        }
        else
        {
            _filePage.TypeFilter.SelectedIndex = 0;
        }
    }

    private void FileViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _filePage?.ApplyViewMode();
        _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
    }

    private async Task RefreshRemoteFilesAsync(string category, string path, bool forceRefresh = false)
    {
        var filePage = _filePage;
        if (filePage == null) return;

        // Update the visible status before any control-state mutation. This
        // also makes lifecycle failures observable instead of silently leaving
        // the original "等待连接" placeholder forever.
        filePage.StatusText.Text = "正在检查设备连接…";
        filePage.ExitSelectionMode();
        var generation = Interlocked.Increment(ref _fileLoadGeneration);
        _fileLoadingCancellation?.Cancel();
        _fileLoadingCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _fileLoadingCancellation = cancellation;
        _visibleFileEntries = Array.Empty<RemoteFileEntry>();
        _recognizedFileCount = 0;
        _loadedFileCount = 0;
        _remoteFileOffset = 0;
        _remoteFileTotal = 0;
        _fileBatchLoading = false;

        var connection = GetConnectedConnection();
        if (connection == null)
        {
            FileManagementStatusText.Text = "请先连接手机";
            return;
        }

        _activeConnection = connection;

        FileManagementStatusText.Text = "正在读取手机文件…";
        FilePathText.Text = category == "storage"
            ? string.IsNullOrWhiteSpace(path)
                ? "手机存储 · /storage/emulated/0/"
                : "手机存储 · /storage/emulated/0/" + path + "/"
            : FileCategoryName(category);
        if (_filePage != null)
        {
            _filePage.NavigateBack.Visibility = category == "storage" &&
                !string.IsNullOrWhiteSpace(path)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        ShowFileLoadingState();

        try
        {
            var page = await _workspaceRemoteClient.BrowseFilesPageAsync(
                connection,
                category,
                path,
                offset: 0,
                limit: InitialFileBatchSize,
                forceRefresh);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _fileLoadGeneration) return;

            _remoteFileOffset = page.Entries.Count;
            _remoteFileTotal = page.Total;
            var visibleEntries = ApplyFileFilters(page.Entries, category);
            _visibleFileEntries = visibleEntries;
            _recognizedFileCount = page.Total;
            _loadedFileCount = 0;
            ClearFileItemViews();

            if (page.Total == 0)
            {
                string message = "这个分类暂时没有可显示的内容。";
                ShowEmptyFileState(message);
                FileManagementStatusText.Text = "已识别 0 项";
                return;
            }

            await AppendNextFileBatchAsync(generation, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer category/filter request owns the UI now.
        }
        catch (Exception exception)
        {
            if (generation != _fileLoadGeneration) return;
            ShowFileErrorState(exception.Message);
            FileManagementStatusText.Text = "读取手机文件失败";
        }
    }

    private void ShowFileLoadingState()
    {
        ClearFileItemViews();
        FileListView.Items.Add(new ListViewItem
        {
            IsHitTestVisible = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Padding = new Thickness(12, 18, 12, 18),
                Children =
                {
                    new ProgressRing { IsActive = true, Width = 22, Height = 22 },
                    new TextBlock
                    {
                        Text = "正在从手机读取文件清单…",
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        });
        _filePage?.GridFiles.Items.Add(new GridViewItem
        {
            IsHitTestVisible = false,
            Content = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(20),
                Children =
                {
                    new ProgressRing { IsActive = true, Width = 22, Height = 22 },
                    new TextBlock { Text = "正在读取…" }
                }
            }
        });
    }

    private void ClearFileItemViews()
    {
        FileListView.Items.Clear();
        _filePage?.GridFiles.Items.Clear();
    }

    private void ShowEmptyFileState(string message)
    {
        FileListView.Items.Add(new ListViewItem
        {
            IsHitTestVisible = false,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(12, 18, 12, 18),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        });
        _filePage?.GridFiles.Items.Add(new GridViewItem
        {
            IsHitTestVisible = false,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(20),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        });
    }

    private void ShowFileErrorState(string message)
    {
        ClearFileItemViews();
        FileListView.Items.Add(new ListViewItem
        {
            IsHitTestVisible = false,
            Content = new TextBlock
            {
                Text = $"读取失败：{message}",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(12, 18, 12, 18),
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            }
        });
        _filePage?.GridFiles.Items.Add(new GridViewItem
        {
            IsHitTestVisible = false,
            Content = new TextBlock
            {
                Text = $"读取失败：{message}",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(20),
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            }
        });
    }

    private async Task AppendNextFileBatchAsync(
        int? expectedGeneration = null,
        CancellationToken? expectedCancellation = null)
    {
        var generation = expectedGeneration ?? _fileLoadGeneration;
        var cancellation = expectedCancellation ?? _fileLoadingCancellation?.Token ?? default;
        if (_fileBatchLoading ||
            generation != _fileLoadGeneration ||
            cancellation.IsCancellationRequested ||
            (_loadedFileCount >= _visibleFileEntries.Count &&
                _remoteFileOffset >= _remoteFileTotal))
        {
            return;
        }

        _fileBatchLoading = true;
        try
        {
            while (_loadedFileCount >= _visibleFileEntries.Count &&
                _remoteFileOffset < _remoteFileTotal)
            {
                var connection = GetConnectedConnection();
                if (connection == null) return;

                var nextPage = await _workspaceRemoteClient.BrowseFilesPageAsync(
                    connection,
                    _fileCategory,
                    _filePath,
                    _remoteFileOffset,
                    AdditionalFileBatchSize);
                cancellation.ThrowIfCancellationRequested();
                if (generation != _fileLoadGeneration) return;

                if (nextPage.Entries.Count == 0)
                {
                    _remoteFileOffset = _remoteFileTotal;
                    break;
                }

                _remoteFileOffset += nextPage.Entries.Count;
                _remoteFileTotal = Math.Max(_remoteFileTotal, nextPage.Total);
                _visibleFileEntries = _visibleFileEntries
                    .Concat(ApplyFileFilters(nextPage.Entries, _fileCategory))
                    .ToArray();
            }

            var start = _loadedFileCount;
            var batchSize = start == 0 ? InitialFileBatchSize : AdditionalFileBatchSize;
            var batch = _visibleFileEntries
                .Skip(start)
                .Take(batchSize)
                .ToList();
            var thumbnailItems = new List<(Image Image, RemoteFileEntry Entry)>();
            var gridMode = _filePage?.IsGridMode == true;
            var appended = 0;

            foreach (var entry in batch)
            {
                cancellation.ThrowIfCancellationRequested();
                if (generation != _fileLoadGeneration) return;

                if (!gridMode)
                {
                    var listItem = new ListViewItem
                    {
                        Tag = entry,
                        Content = BuildRemoteFileRow(entry)
                    };
                    ConfigureRemoteFileItem(listItem, entry);
                    FileListView.Items.Add(listItem);
                }
                else
                {
                    var tile = BuildRemoteFileTile(entry);
                    var gridItem = new GridViewItem
                    {
                        Tag = entry,
                        Content = tile.Tile
                    };
                    ConfigureRemoteFileItem(gridItem, entry);
                    _filePage?.GridFiles.Items.Add(gridItem);
                    if (tile.Thumbnail != null &&
                        thumbnailItems.Count < ThumbnailBudgetPerBatch)
                    {
                        thumbnailItems.Add((tile.Thumbnail, entry));
                    }
                }

                appended++;
                if (appended % 40 == 0)
                {
                    // Allow WinUI to measure the newly appended virtualized
                    // containers before the next chunk is added.
                    await Task.Yield();
                }
            }

            _loadedFileCount += batch.Count;
            var loadingSuffix = _remoteFileOffset < _remoteFileTotal
                ? $" · 已扫描 {_remoteFileOffset}/{_remoteFileTotal} 项，继续滚动加载"
                : " · 已加载全部";
            FileManagementStatusText.Text =
                $"已识别 {_recognizedFileCount} 项{loadingSuffix}";
            _filePage?.ResetNearEndTrigger();

            if (thumbnailItems.Count > 0)
            {
                _ = LoadRemoteFileThumbnailsAsync(
                    thumbnailItems,
                    generation,
                    cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The current listing was replaced by another category/filter.
        }
        finally
        {
            _fileBatchLoading = false;
        }
    }

    private static Grid BuildRemoteFileRow(RemoteFileEntry entry)
    {
        var row = new Grid
        {
            MinHeight = 58,
            ColumnSpacing = 12,
            Padding = new Thickness(4, 6, 4, 6)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = FileGlyph(entry),
            FontSize = 24,
            Foreground = entry.IsDirectory
                ? FolderBrush()
                : (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        details.Children.Add(new TextBlock
        {
            Text = entry.IsDirectory
                ? "文件夹"
                : $"{entry.MimeType} · {FormatBytes(entry.SizeBytes)}",
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        var date = new TextBlock
        {
            Text = FormatUnixMilliseconds(entry.ModifiedAt),
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(date, 2);
        row.Children.Add(date);
        return row;
    }

    private static (Grid Tile, Image? Thumbnail) BuildRemoteFileTile(RemoteFileEntry entry)
    {
        Image? thumbnail = null;
        FrameworkElement visual;
        if (!entry.IsDirectory && IsPreviewableMediaEntry(entry))
        {
            thumbnail = new Image
            {
                Width = 132,
                Height = 92,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            visual = new Border
            {
                Width = 140,
                Height = 100,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                Child = thumbnail
            };
        }
        else
        {
            var icon = new FontIcon
            {
                Glyph = FileGlyph(entry),
                FontSize = entry.IsDirectory ? 46 : 30,
                Foreground = entry.IsDirectory
                    ? FolderBrush()
                    : (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            };
            visual = entry.IsDirectory
                ? icon
                : new Border
                {
                    Width = 64,
                    Height = 64,
                    CornerRadius = new CornerRadius(12),
                    // 文件图标使用 Windows 的中性图标底，不把音频文件渲染成
                    // 一块突兀的蓝色卡片；文件类型仍由 glyph 区分。
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = icon
                };
        }
        var panel = new StackPanel
        {
            Width = 170,
            Spacing = 8,
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(visual);
        panel.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = entry.IsDirectory ? "文件夹" : FormatBytes(entry.SizeBytes),
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextAlignment = TextAlignment.Center
        });
        return (new Grid { MinHeight = 150, Children = { panel } }, thumbnail);
    }

    private static bool IsImageEntry(RemoteFileEntry entry) =>
        entry.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".heif" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static bool IsVideoEntry(RemoteFileEntry entry) =>
        entry.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".3gp", ".m4v" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static bool IsPreviewableMediaEntry(RemoteFileEntry entry) =>
        IsImageEntry(entry) || IsVideoEntry(entry);

    private static bool IsMp3Entry(RemoteFileEntry entry) =>
        entry.MimeType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(entry.Name), ".mp3", StringComparison.OrdinalIgnoreCase);

    private static string FileGlyph(RemoteFileEntry entry) =>
        entry.IsDirectory
            ? "\uE8B7"
            : IsMp3Entry(entry)
                ? "\uE8D6"
                : "\uE7C3";

    private void ConfigureRemoteFileItem(FrameworkElement item, RemoteFileEntry entry)
    {
        item.DoubleTapped += RemoteFileItem_DoubleTapped;
        if (!entry.IsDirectory && IsImageEntry(entry))
        {
            // The item provides a real StorageFile to Explorer instead of a
            // custom text payload, so it can be dropped on the desktop or any
            // other Windows folder.
            item.CanDrag = true;
            item.DragStarting += RemoteFileItem_DragStarting;
        }
    }

    private async void RemoteFileItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RemoteFileEntry entry }) return;
        e.Handled = true;
        if (_filePage?.IsSelectionMode == true) return;
        if (entry.IsDirectory && _fileCategory == "storage")
        {
            _filePath = entry.RelativePath;
            await RefreshRemoteFilesAsync(_fileCategory, _filePath);
            return;
        }

        if (IsImageEntry(entry))
        {
            await ShowRemoteImagePreviewAsync(entry);
            return;
        }

        if (IsVideoEntry(entry))
        {
            await ShowRemoteVideoPreviewAsync(entry);
            return;
        }

        if (IsAudioEntry(entry))
        {
            await ShowRemoteAudioPreviewAsync(entry);
        }
    }

    private void RemoteFileItem_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: RemoteFileEntry entry } ||
            entry.IsDirectory ||
            !IsImageEntry(entry))
        {
            args.Cancel = true;
            return;
        }

        var connection = GetConnectedConnection();
        if (connection == null || string.IsNullOrWhiteSpace(entry.Uri))
        {
            args.Cancel = true;
            return;
        }

        try
        {
            ShowInternalRemoteDragCancelZone();
            args.Data.Properties[HingeDragMetadata.InternalRemoteFile] = true;
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.Data.Properties.Title = entry.Name;
            args.Data.SetDataProvider(
                StandardDataFormats.StorageItems,
                request => ProvideRemoteStorageItemsAsync(
                    request,
                    connection,
                    entry.Uri,
                    entry.Name));
        }
        catch
        {
            HideInternalRemoteDragCancelZone();
            args.Cancel = true;
        }
    }

    private async void ProvideRemoteStorageItemsAsync(
        DataProviderRequest request,
        SessionConnection connection,
        string uri,
        string displayName)
    {
        var deferral = request.GetDeferral();
        try
        {
            var bytes = await _workspaceRemoteClient.LoadPhotoBytesAsync(connection, uri);
            if (bytes is not { Length: > 0 })
            {
                request.SetData(Array.Empty<StorageFile>());
                return;
            }

            var cacheDirectory = Path.Combine(Path.GetTempPath(), "Hinge", "DragCache");
            Directory.CreateDirectory(cacheDirectory);
            var safeName = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "手机图片.jpg";
            var tempPath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}_{safeName}");
            await File.WriteAllBytesAsync(tempPath, bytes);
            var file = await StorageFile.GetFileFromPathAsync(tempPath);
            request.SetData(new[] { file });
        }
        catch (Exception exception)
        {
            request.SetData(Array.Empty<StorageFile>());
            DispatcherQueue.TryEnqueue(() => StatusText.Text = $"文件拖出失败：{exception.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task ShowRemoteImagePreviewAsync(RemoteFileEntry entry)
    {
        try
        {
            var receivedPath = await ReceiveRemotePreviewFileAsync(entry, "正在读取图片预览…");
            await LaunchMediaFileAsync(entry, receivedPath, "图片");
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("图片打开失败", exception.Message, false);
        }
    }

    private async Task<string> ReceiveRemoteFileAsync(
        RemoteFileEntry entry,
        string progressText,
        string? destinationDirectory = null)
    {
        await _remoteMediaReceiveGate.WaitAsync();
        try
        {
            var connection = GetConnectedConnection();
            if (connection == null)
            {
                throw new InvalidOperationException("设备会话已断开，请重新连接手机。");
            }
            if (string.IsNullOrWhiteSpace(entry.Uri))
            {
                throw new InvalidOperationException("手机没有返回这个文件的有效地址。");
            }
            if (_pendingRemoteMedia != null)
            {
                throw new InvalidOperationException("另一个文件正在接收，请稍后再试。");
            }

            if (_filePage != null)
            {
                _filePage.StatusText.Text = progressText;
            }
            var completion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRemoteMedia = completion;
            _pendingRemoteMediaName = entry.Name;
            var targetDirectory = destinationDirectory ?? _receiveDirectory;
            using var incomingDirectory = _transferManager.RegisterIncomingDirectory(
                entry.Name,
                targetDirectory);
            try
            {
                await _workspaceRemoteClient.SendMediaToComputerAsync(
                    connection,
                    entry.Uri,
                    entry.Name,
                    entry.MimeType);
                return await completion.Task.WaitAsync(TimeSpan.FromMinutes(2));
            }
            finally
            {
                if (ReferenceEquals(_pendingRemoteMedia, completion))
                {
                    _pendingRemoteMedia = null;
                    _pendingRemoteMediaName = null;
                }
            }
        }
        finally
        {
            _remoteMediaReceiveGate.Release();
        }
    }

    private async Task<string> ReceiveRemotePreviewFileAsync(
        RemoteFileEntry entry,
        string progressText)
    {
        var location = _previewCache.GetLocation(entry);
        if (_previewCache.IsUsable(location, entry.SizeBytes))
        {
            return location.FilePath;
        }

        _previewCache.Prepare(location);
        try
        {
            var receivedPath = await ReceiveRemoteFileAsync(
                entry,
                progressText,
                location.Directory);
            _previewCache.Prune(location.Directory);
            return receivedPath;
        }
        catch
        {
            _previewCache.Remove(location);
            throw;
        }
    }

    private async Task SaveSelectedFilesAsync(IReadOnlyList<RemoteFileEntry> entries)
    {
        var connection = GetConnectedConnection();
        if (connection == null)
        {
            await ShowDialogAsync("无法保存", "设备会话已断开，请重新连接手机。", false);
            return;
        }

        var completed = 0;
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                await ReceiveRemoteFileAsync(
                    entry,
                    $"正在保存 {completed + 1}/{entries.Count} 个文件…",
                    _receiveDirectory);
                completed++;
            }
            catch (Exception exception)
            {
                failures.Add($"{entry.Name}：{exception.Message}");
            }
        }

        _filePage?.ExitSelectionMode();
        var destination = _receiveDirectory;
        FileManagementStatusText.Text = failures.Count == 0
            ? $"已保存 {completed} 个文件到 {destination}"
            : $"已保存 {completed} 个文件，{failures.Count} 个失败";
        if (failures.Count > 0)
        {
            await ShowDialogAsync(
                "部分文件保存失败",
                string.Join("\n", failures.Take(6)),
                false);
        }
    }

    private async Task ShowRemoteVideoPreviewAsync(RemoteFileEntry entry)
    {
        try
        {
            var receivedPath = await ReceiveRemotePreviewFileAsync(entry, "正在读取视频预览…");
            await ShowVideoFilePreviewAsync(entry, receivedPath);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("视频读取失败", exception.Message, false);
        }
    }

    private async Task ShowVideoFilePreviewAsync(RemoteFileEntry entry, string filePath)
    {
        await LaunchMediaFileAsync(entry, filePath, "视频");
    }

    private async Task ShowRemoteAudioPreviewAsync(RemoteFileEntry entry)
    {
        try
        {
            var receivedPath = await ReceiveRemotePreviewFileAsync(entry, "正在读取音频预览…");
            await ShowAudioFilePreviewAsync(entry, receivedPath);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("音频播放失败", exception.Message, false);
        }
    }

    private async Task ShowAudioFilePreviewAsync(RemoteFileEntry entry, string filePath)
    {
        await LaunchMediaFileAsync(entry, filePath, "音频");
    }

    private async Task LaunchMediaFileAsync(
        RemoteFileEntry entry,
        string filePath,
        string typeName)
    {
        try
        {
            var localFile = await StorageFile.GetFileFromPathAsync(filePath);
            // Metadata is requested in parallel with opening the cached file.
            // A slow or older Android peer must never block the user's default
            // Windows media app from launching.
            var metadataTask = TryLoadRemoteMediaMetadataAsync(entry);
            // Let Windows choose the registered native media handler. This
            // avoids hosting a second MediaPlayerElement window in Hinge
            // Suite, so closing playback cannot race the main window's
            // dispatcher or leave an audio/video graph running in-process.
            var launched = await Launcher.LaunchFileAsync(localFile);
            if (!launched)
            {
                using var process = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                launched = process != null;
            }

            if (launched)
            {
                var metadata = await metadataTask;
                var description = FormatRemoteMediaMetadata(metadata);
                var status = string.IsNullOrWhiteSpace(description)
                    ? $"已交给 Windows 默认应用打开：{entry.Name}"
                    : $"已交给 Windows 默认应用打开：{entry.Name} · {description}";
                if (_filePage != null)
                {
                    _filePage.StatusText.Text = status;
                }
                return;
            }

            await ShowDialogAsync(
                $"无法打开{typeName}",
                $"Windows 没有找到可以打开“{entry.Name}”的默认媒体播放器。",
                false);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync($"无法打开{typeName}", exception.Message, false);
        }
    }

    private async Task<RemoteMediaMetadata?> TryLoadRemoteMediaMetadataAsync(
        RemoteFileEntry entry)
    {
        var connection = GetConnectedConnection();
        if (connection == null || string.IsNullOrWhiteSpace(entry.Uri)) return null;
        try
        {
            return await _workspaceRemoteClient.LoadMediaMetadataAsync(
                connection,
                entry.Uri,
                entry.Name,
                entry.MimeType);
        }
        catch
        {
            // Metadata is enhancement-only. Opening the user's default app
            // must continue to work with older APKs or restricted providers.
            return null;
        }
    }

    private static string FormatRemoteMediaMetadata(RemoteMediaMetadata? metadata)
    {
        if (metadata == null) return string.Empty;
        var parts = new List<string>();
        if (metadata.Width > 0 && metadata.Height > 0)
        {
            parts.Add($"{metadata.Width}×{metadata.Height}");
        }
        if (metadata.DurationMs > 0)
        {
            var duration = TimeSpan.FromMilliseconds(metadata.DurationMs);
            parts.Add(duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss"));
        }
        if (metadata.Bitrate > 0)
        {
            parts.Add($"{metadata.Bitrate / 1000} kbps");
        }
        var camera = string.Join(" ", new[] { metadata.CameraMake, metadata.CameraModel }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(camera)) parts.Add(camera);
        if (!string.IsNullOrWhiteSpace(metadata.Artist)) parts.Add(metadata.Artist);
        return string.Join(" · ", parts);
    }

    private async Task SaveRemoteImageAsync(RemoteFileEntry entry, SessionConnection connection)
        => await SaveRemoteMediaAsync(entry, connection, "图片");

    private async Task SaveRemoteMediaAsync(RemoteFileEntry entry, SessionConnection connection, string typeName)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(entry.Name)
        };
        var extension = Path.GetExtension(entry.Name);
        if (string.IsNullOrWhiteSpace(extension)) extension = typeName == "视频" ? ".mp4" : ".jpg";
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle((App.Current as App)?.MainWindow ?? this));
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            var bytes = await _workspaceRemoteClient.LoadPhotoBytesAsync(connection, entry.Uri);
            if (bytes is not { Length: > 0 }) throw new InvalidOperationException("手机没有返回文件数据。");
            await FileIO.WriteBytesAsync(file, bytes);
            FileManagementStatusText.Text = $"{typeName}已保存：{file.Path}";
        }
        catch (Exception exception)
        {
            await ShowDialogAsync($"保存{typeName}失败", exception.Message, false);
        }
    }

    private static Brush FolderBrush() =>
        new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0xC1, 0x07));

    private static int GetFileThumbnailConcurrency()
    {
        var processorCount = Math.Max(1, Environment.ProcessorCount);
        var memory = GC.GetGCMemoryInfo();
        var memoryPressure = memory.TotalAvailableMemoryBytes > 0 &&
            memory.MemoryLoadBytes > memory.TotalAvailableMemoryBytes * 3 / 4;
        if (memoryPressure) return Math.Min(3, processorCount);
        return processorCount switch
        {
            >= 16 => 10,
            >= 8 => 8,
            >= 4 => 6,
            _ => 4
        };
    }

    private async Task LoadRemoteFileThumbnailsAsync(
        IReadOnlyList<(Image Image, RemoteFileEntry Entry)> items,
        int generation,
        CancellationToken cancellation)
    {
        using var gate = new SemaphoreSlim(GetFileThumbnailConcurrency());
        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                var acquired = false;
                try
                {
                    await gate.WaitAsync(cancellation);
                    acquired = true;
                    await LoadRemoteFileThumbnailAsync(
                        item.Image,
                        item.Entry,
                        generation,
                        cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // A newer listing owns the page now.
                }
                finally
                {
                    if (acquired) gate.Release();
                }
            }));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when changing category or filter.
        }
    }

    private async Task LoadRemoteFileThumbnailAsync(
        Image image,
        RemoteFileEntry entry,
        int generation,
        CancellationToken cancellation)
    {
        var connection = GetConnectedConnection();
        if (connection == null ||
            string.IsNullOrWhiteSpace(entry.Uri) ||
            generation != _fileLoadGeneration ||
            cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var bytes = await _workspaceRemoteClient.LoadPhotoThumbnailBytesAsync(
                connection,
                entry.Uri);
            if (bytes is not { Length: > 0 } ||
                generation != _fileLoadGeneration ||
                cancellation.IsCancellationRequested)
            {
                return;
            }
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (generation != _fileLoadGeneration || cancellation.IsCancellationRequested)
            {
                return;
            }
            image.Source = bitmap;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The request is stale after a navigation/filter change.
        }
        catch
        {
            // A missing/unsupported thumbnail keeps the file tile usable.
        }
    }

    private void FileListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        RemoteFileEntry? entry = e.ClickedItem switch
        {
            ListViewItem { Tag: RemoteFileEntry listEntry } => listEntry,
            GridViewItem { Tag: RemoteFileEntry gridEntry } => gridEntry,
            _ => null
        };
        if (entry == null)
        {
            return;
        }

        if (_filePage?.IsSelectionMode == true)
        {
            // In selection mode a single click only changes the selection;
            // opening folders or files remains a double-click action in the
            // normal browsing mode.
            return;
        }

        if (entry.IsDirectory && _fileCategory == "storage")
        {
            _filePath = entry.RelativePath;
            _ = RefreshRemoteFilesAsync(_fileCategory, _filePath);
            return;
        }

        // Images are opened by an explicit double-click so the first click
        // does not interrupt the preview with a metadata dialog.
        if (IsImageEntry(entry)) return;

        _ = ShowDialogAsync(
            entry.Name,
            $"类型：{entry.MimeType}\n大小：{FormatBytes(entry.SizeBytes)}\n位置：{entry.RelativePath}",
            false);
    }

    private IReadOnlyList<RemoteFileEntry> ApplyFileFilters(
        IReadOnlyList<RemoteFileEntry> entries,
        string category)
    {
        string typeFilter = SelectedTag(_filePage?.TypeFilter);
        string documentFilter = SelectedTag(_filePage?.DocumentFilter);
        string sort = SelectedTag(_filePage?.SortOptions, "timeDesc");

        IEnumerable<RemoteFileEntry> filtered = entries;
        if (category.Equals("recent", StringComparison.OrdinalIgnoreCase))
        {
            // A MediaStore provider can accidentally return a directory as a
            // zero-byte record. Recent files must never surface directories;
            // directory navigation remains available in phone storage.
            filtered = filtered.Where(entry => !entry.IsDirectory &&
                !IsRecentCacheNoise(entry));
        }
        if (category.Equals("documents", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(entry =>
                entry.IsDirectory || MatchesDocumentFilter(entry, documentFilter));
        }
        else if (!string.Equals(typeFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(entry =>
                entry.IsDirectory || MatchesTypeFilter(entry, typeFilter));
        }

        filtered = sort switch
        {
            "name" => filtered.OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            "type" => filtered.OrderBy(entry => entry.IsDirectory ? string.Empty : FileKind(entry))
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            "sizeAsc" => filtered.OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenBy(entry => entry.SizeBytes),
            "sizeDesc" => filtered.OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenByDescending(entry => entry.SizeBytes),
            "timeAsc" => filtered.OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenBy(entry => entry.ModifiedAt),
            _ => filtered.OrderBy(entry => entry.IsDirectory ? 0 : 1)
                .ThenByDescending(entry => entry.ModifiedAt),
        };
        return filtered.ToList();
    }

    private static bool IsRecentCacheNoise(RemoteFileEntry entry)
    {
        if (entry.IsDirectory) return false;

        var name = entry.Name.Trim();
        var path = $"{entry.RelativePath}/{name}"
            .Replace('\\', '/')
            .Trim('/')
            .ToLowerInvariant();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = Path.GetFileName(name).ToLowerInvariant();

        // Classify by source path/name, never by size. Small logs and other
        // meaningful user files in normal folders remain visible.
        if (segments.Any(segment => segment is "cache" or ".cache" or "code_cache" or
                "app_webview" or ".thumbnails" or "thumbnails" or "tmp" or "temp" or
                "logs" or "log" or "databases" or "shared_prefs" or "no_backup" ||
                segment.StartsWith("cache_") || segment.StartsWith("thumb")))
        {
            return true;
        }

        if (path.Contains("/android/data/") || path.Contains("/android/obb/"))
        {
            return true;
        }

        if (fileName.StartsWith(".")) return true;

        if (fileName is "cache" or ".nomedia" || fileName.StartsWith(".thumbdata"))
        {
            return true;
        }

        // MediaProvider and chat applications often persist web thumbnails
        // with the URL percent-encoded into the filename. They are useful
        // inside their owning app, but are not meaningful entries in a
        // user-facing "recent files" view. The rule is name/path based so a
        // legitimate small document is not removed merely because of size.
        if (fileName.Contains("%3a%2f%2f") ||
            fileName.Contains("%3a%252f%252f") ||
            fileName.Contains("%2f%2f"))
        {
            return true;
        }

        if (fileName.StartsWith("cache_") || fileName.StartsWith("thumb_") ||
            fileName.StartsWith("thumbnail_") || fileName.StartsWith("temp_") ||
            fileName.StartsWith("tmp_"))
        {
            return true;
        }

        // The recent view is for user-facing content, not diagnostic or
        // database sidecar files. Keep these hidden here only; they remain
        // available in their original folder views.
        if (fileName.EndsWith(".log") || fileName.EndsWith(".trace") ||
            fileName.EndsWith(".db-shm") || fileName.EndsWith(".db-wal") ||
            fileName.EndsWith(".lock") || fileName.EndsWith(".lck"))
        {
            return true;
        }

        var appPrivatePath = path.Contains("/android/data/") ||
            path.Contains("/android/obb/") || path.Contains("/android/media/");
        if (appPrivatePath && (fileName.EndsWith(".log") ||
            fileName.EndsWith(".json") || fileName.StartsWith("log_")))
        {
            return true;
        }

        // A few OEM providers expose zero-byte bookkeeping placeholders with
        // generic names. Keep arbitrary small user files, but hide only the
        // well-known placeholder names in the recent view.
        if (entry.SizeBytes == 0 && fileName is "file" or "文件" or "thumb" or "thumbnail")
        {
            return true;
        }

        return fileName.EndsWith(".tmp") ||
            fileName.EndsWith(".temp") ||
            fileName.EndsWith(".part") ||
            fileName.EndsWith(".crdownload") ||
            fileName.EndsWith(".download");
    }

    private static string SelectedTag(ComboBox? comboBox, string fallback = "all") =>
        (comboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static bool MatchesTypeFilter(RemoteFileEntry entry, string filter) =>
        filter switch
        {
            "images" => IsImageEntry(entry),
            "videos" => IsVideoEntry(entry),
            "audio" => IsAudioEntry(entry),
            "documents" => IsDocumentEntry(entry),
            "archives" => IsArchiveEntry(entry),
            "packages" => IsPackageEntry(entry),
            "other" => !IsImageEntry(entry) && !IsVideoEntry(entry) &&
                !IsAudioEntry(entry) && !IsDocumentEntry(entry) &&
                !IsArchiveEntry(entry) && !IsPackageEntry(entry),
            _ => true,
        };

    private static bool MatchesDocumentFilter(RemoteFileEntry entry, string filter)
    {
        string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        return filter switch
        {
            "pdf" => extension == ".pdf",
            "doc" => extension is ".doc" or ".docx" or ".odt",
            "xls" => extension is ".xls" or ".xlsx" or ".ods",
            "ppt" => extension is ".ppt" or ".pptx" or ".odp",
            "txt" => extension is ".txt" or ".md" or ".csv" or ".rtf",
            "other" => !MatchesDocumentFilter(entry, "pdf") &&
                !MatchesDocumentFilter(entry, "doc") &&
                !MatchesDocumentFilter(entry, "xls") &&
                !MatchesDocumentFilter(entry, "ppt") &&
                !MatchesDocumentFilter(entry, "txt"),
            _ => true,
        };
    }

    private static bool IsAudioEntry(RemoteFileEntry entry) =>
        entry.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".mp3", ".m4a", ".wav", ".flac", ".aac", ".ogg", ".opus" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static bool IsDocumentEntry(RemoteFileEntry entry) =>
        new[] { ".pdf", ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp", ".txt", ".md", ".csv", ".rtf" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase) ||
        entry.MimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
        entry.MimeType.Contains("text/", StringComparison.OrdinalIgnoreCase) ||
        entry.MimeType.Contains("word", StringComparison.OrdinalIgnoreCase) ||
        entry.MimeType.Contains("sheet", StringComparison.OrdinalIgnoreCase) ||
        entry.MimeType.Contains("presentation", StringComparison.OrdinalIgnoreCase);

    private static bool IsArchiveEntry(RemoteFileEntry entry) =>
        new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static bool IsPackageEntry(RemoteFileEntry entry) =>
        new[] { ".apk", ".xapk", ".exe", ".msi", ".msix", ".deb" }
            .Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase);

    private static string FileKind(RemoteFileEntry entry) =>
        IsImageEntry(entry) ? "图片" : IsVideoEntry(entry) ? "视频" :
        IsAudioEntry(entry) ? "音频" : IsDocumentEntry(entry) ? "文档" :
        IsArchiveEntry(entry) ? "压缩包" : IsPackageEntry(entry) ? "安装包" : "其他";

    private static string FileCategoryName(string category) => category switch
    {
        "recent" => "最近文件",
        "images" => "图片",
        "videos" => "视频",
        "audio" => "音频",
        "documents" => "文档",
        "wechat" => "微信相册",
        "qq" => "QQ 相册",
        _ => "手机文件"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):F1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):F1} GB";
    }

    private static string FormatUnixMilliseconds(long milliseconds)
    {
        if (milliseconds <= 0) return string.Empty;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                .ToLocalTime()
                .ToString("yyyy/M/d HH:mm");
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private void BtnPersonalization_Click(object sender, RoutedEventArgs e) => NavigateTo("个性化", 0);

    private void PersonalizationTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_personalizationPage?.ThemeOptionsControl.SelectedIndex is not (0 or 1 or 2)) return;
        string theme = _personalizationPage.ThemeOptionsControl.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system"
        };
        SaveWindowTheme(theme);
        ApplyWindowTheme(theme);
        StatusText.Text = $"窗口主题已切换：{WindowThemeName(theme)}";
    }

    private void PersonalizationMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_personalizationPage?.MaterialOptionsControl.SelectedValue is not string material) return;
        SaveWindowMaterial(material);
        ApplyWindowMaterial(material);
        StatusText.Text = $"窗口材质已切换：{WindowMaterialName(material)}";
    }

    private async void ImportBackground_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" })
        {
            picker.FileTypeFilter.Add(extension);
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        SaveWindowBackgroundPath(file.Path);
        ApplyBackgroundImage(file.Path);
        _personalizationPage?.SetPreview(file.Path);
        StatusText.Text = "主界面背景图片已应用";
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        SaveWindowBackgroundPath(null);
        ApplyBackgroundImage(null);
        _personalizationPage?.SetPreview(null);
        StatusText.Text = "主界面背景图片已清除";
    }

    private void MinimizeToTray_Toggled(object sender, RoutedEventArgs e)
    {
        _minimizeToTray = sender is ToggleSwitch { IsOn: true };
        SaveMinimizeToTray(_minimizeToTray);
        SettingsStatusText.Text = _minimizeToTray
            ? "关闭窗口时将隐藏到系统托盘，可从托盘恢复或退出。"
            : "关闭窗口时直接退出应用。";
    }

    private void StartWithWindows_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsPage == null || sender is not ToggleSwitch toggle) return;

        if (!TrySaveStartWithWindows(toggle.IsOn))
        {
            toggle.IsOn = !toggle.IsOn;
            SettingsStatusText.Text = "无法修改 Windows 开机启动设置，请检查当前账户权限。";
            return;
        }

        _settingsPage.SilentStartup.IsEnabled = toggle.IsOn;
        SettingsStatusText.Text = toggle.IsOn
            ? "已启用开机启动；下次登录 Windows 时会自动运行。"
            : "已关闭开机启动。";
    }

    private void SilentStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        SaveSilentStartup(toggle.IsOn);
        SettingsStatusText.Text = toggle.IsOn
            ? "已启用静默启动；开机启动时应用会停留在系统托盘。"
            : "已关闭静默启动；开机启动时会显示主窗口。";
    }

    public void StartSilentlyToTray()
    {
        try
        {
            _appWindow?.Hide();
        }
        catch
        {
            // If AppWindow is not available yet, activation will show the window.
        }
    }

    private async void BtnStorage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFolder? folder;
        try
        {
            folder = await picker.PickSingleFolderAsync();
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("无法选择目录", exception.Message, false);
            return;
        }

        if (folder == null) return;

        if (!TryNormalizeReceiveDirectory(folder.Path, out var directory))
        {
            await ShowDialogAsync("目录无效", "请选择一个有效的 Windows 文件夹。", false);
            return;
        }

        try
        {
            _transferManager.SetDownloadDirectory(directory);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("无法使用该目录", exception.Message, false);
            return;
        }

        _receiveDirectory = directory;
        SaveReceiveDirectory(_receiveDirectory);
        if (_settingsPage != null)
        {
            _settingsPage.StoragePath.Text = _receiveDirectory;
        }
        SettingsStatusText.Text = $"默认接收目录：{_receiveDirectory}";
        StatusText.Text = "存储设置已保存";
    }

    private async void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Hinge\n局域网优先的跨设备办公套件\n\nWindows 端：WinUI 3 + Windows App SDK\nAndroid 端：Flutter + Material 3 Expressive\n\n数据默认只在局域网设备之间传输。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = "访问 GitHub 项目主页",
            NavigateUri = new Uri("https://github.com/Chengeeker/Hinge"),
            HorizontalAlignment = HorizontalAlignment.Left
        });

        var dialog = new ContentDialog
        {
            Title = "关于 Hinge",
            Content = content,
            CloseButtonText = "关闭",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task ShowQuickTransferAsync()
    {
        var connection = GetConnectedConnection();
        if (connection == null)
        {
            await ShowDialogAsync("无法发送", "请先在“附近设备”中连接一台设备。", false);
            return;
        }

        var input = new TextBox
        {
            PlaceholderText = "输入要发送的文字或链接",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84
        };
        var dialog = new ContentDialog
        {
            Title = "发送文字或链接",
            Content = input,
            PrimaryButtonText = "发送",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            try
            {
                await _transferManager.SendTextAsync(connection, input.Text.Trim());
                StatusText.Text = "文字或链接已发送";
            }
            catch (Exception exception)
            {
                await ShowDialogAsync("发送失败", exception.Message, false);
            }
        }
    }

    private async Task PickAndSendFileAsync()
    {
        var connection = GetConnectedConnection();
        if (connection == null)
        {
            await ShowDialogAsync("无法发送文件", "请先在“附近设备”中连接一台设备。", false);
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            await _transferManager.SendFileAsync(connection, file.Path);
            StatusText.Text = $"文件已发送：{file.Name}";
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("文件发送失败", exception.Message, false);
        }
    }

    private async void ComputerFilesDropped(
        object? sender,
        ComputerFilesDroppedEventArgs args)
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _computerDropCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            await SendComputerFilesAsync(args, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The user deliberately dropped the item on the red cancel zone.
            // Keep the cancellation status instead of reporting a successful
            // send after the transfer task has stopped.
            StatusText.Text = "已取消发送";
            if (_filePage != null) _filePage.StatusText.Text = "已取消发送";
        }
        finally
        {
            if (ReferenceEquals(_computerDropCancellation, cancellation))
            {
                _computerDropCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task SendComputerFilesAsync(
        ComputerFilesDroppedEventArgs args,
        CancellationToken cancellationToken)
    {
        var connection = GetConnectedConnection();
        if (connection == null)
        {
            await ShowDialogAsync("无法发送文件", "请先在首页连接一台 Android 设备。", false);
            return;
        }

        var destination = args.DestinationPath.Trim('/');
        var completed = 0;
        var failures = new List<string>();
        foreach (var path in args.FilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _transferManager.SendFileAsync(
                    connection,
                    path,
                    cancellationToken: cancellationToken,
                    destinationPath: destination);
                completed++;
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(path)}：{exception.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var destinationLabel = string.IsNullOrWhiteSpace(destination)
            ? "手机默认目录"
            : $"手机 /{destination}";
        if (failures.Count == 0)
        {
            StatusText.Text = $"已发送 {completed} 个文件到 {destinationLabel}";
            if (_filePage != null) _filePage.StatusText.Text = StatusText.Text;
            return;
        }

        var summary = $"已发送 {completed} 个文件到 {destinationLabel}。\n失败 {failures.Count} 个：\n" +
            string.Join("\n", failures.Take(5));
        StatusText.Text = $"发送完成：成功 {completed}，失败 {failures.Count}";
        if (_filePage != null) _filePage.StatusText.Text = StatusText.Text;
        await ShowDialogAsync("部分文件发送失败", summary, false);
    }

    private SessionConnection? GetConnectedConnection()
    {
        if (_activeConnection?.State == SessionState.Connected &&
            !string.IsNullOrWhiteSpace(_activeConnection.RemoteDeviceId))
        {
            return _activeConnection;
        }

        var connection = _sessionManager.ActiveConnections.FirstOrDefault(
            candidate => candidate.State == SessionState.Connected &&
                !string.IsNullOrWhiteSpace(candidate.RemoteDeviceId));
        // The identity handshake is the source of truth for a usable session.
        // A fast reconnect can briefly publish the peer before the UI receives
        // the corresponding StateChanged event; do not make every workspace
        // page wait forever on that transient ordering window.
        connection ??= _sessionManager.ActiveConnections.FirstOrDefault(
            candidate => candidate.PeerInfo != null &&
                !string.IsNullOrWhiteSpace(candidate.RemoteDeviceId));
        if (connection != null)
        {
            _activeConnection = connection;
        }
        return connection;
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
        await dialog.ShowAsync();
    }

    private async void ShowStatus(string title, string message)
    {
        StatusText.Text = message;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        CancelHistoricalReconnect();
        UninitializeNativeFileDrop();
        if (_appWindow != null)
        {
            _appWindow.Closing -= AppWindow_Closing;
        }
        _discoveryService.Dispose();
        _sessionManager.Dispose();
        _transferManager.Dispose();
        _clipboardManager.Dispose();
        _notificationManager.Dispose();
        _remoteInputManager.Dispose();
        _trayManager.Dispose();
    }

    public void RestoreFromTray()
    {
        _appWindow?.Show();
        Activate();
    }

    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8] + "...";
    private static string Preview(string value) => value.Length <= 32 ? value : value[..32] + "...";

    private static IReadOnlyList<IPAddress> OrderAddresses(IEnumerable<string> addresses)
    {
        var localAddresses = new List<(IPAddress Address, IPAddress Mask)>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
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
