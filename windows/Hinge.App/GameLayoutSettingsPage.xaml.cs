using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Hinge.App;

public sealed partial class GameLayoutSettingsPage : Page
{
    private const double CanvasWidth = 1280;
    private const double CanvasHeight = 576;
    private const double SnapTolerance = 12;
    private SessionManager? _sessionManager;
    private TrustStore? _trustStore;
    private SessionConnection? _connection;
    private GameLayoutDocument? _document;
    private GameLayoutPreset? _preset;
    private GameLayoutControl? _control;
    private readonly Dictionary<GameLayoutControl, Border> _rendered = new();
    private readonly HashSet<SessionConnection> _pendingIdentityConnections = [];
    private bool _updating;
    private bool _sessionEventsAttached;
    private uint? _dragPointer;
    private GameLayoutControl? _gestureControl;
    private Border? _gestureBorder;
    private PointerGesture _pointerGesture;
    private ResizeCorner _resizeCorner;
    private Point _dragOffset;
    private Rect _resizeStartRect;
    private readonly List<Border> _resizeHandles = [];
    private readonly List<Border> _alignmentGuides = [];
    private CancellationTokenSource? _readTimeout;

    private enum PointerGesture { None, Move, Resize }
    private enum ResizeCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    public Button BackToSettings => BackButton;

    public GameLayoutSettingsPage()
    {
        InitializeComponent();
        SettingsPageLayout.MatchViewportWidth(LayoutScrollViewer, LayoutPageRoot);
        PhoneSelector.SelectionChanged += PhoneSelector_SelectionChanged;
        ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;
        ControlList.SelectionChanged += ControlList_SelectionChanged;
        RefreshPhonesButton.Click += (_, _) => RefreshPhones();
        ReadFromPhoneButton.Click += async (_, _) => await RequestFromPhoneAsync();
        SaveAndSyncButton.Click += async (_, _) => await SaveAndSyncAsync();
        AddProfileButton.Click += (_, _) => AddProfile();
        DeleteProfileButton.Click += (_, _) => DeleteProfile();
        AddControlButton.Click += (_, _) => AddControl();
        DeleteControlButton.Click += (_, _) => DeleteControl();
        ProfileNameBox.TextChanged += ProfileNameBox_TextChanged;
        ControlLabelBox.TextChanged += ControlLabelBox_TextChanged;
        ControlBindingCombo.SelectionChanged += ControlBindingCombo_SelectionChanged;
        ShapeCombo.SelectionChanged += ShapeCombo_SelectionChanged;
        DirectionKeysBox.TextChanged += DirectionKeysBox_TextChanged;
        PositionXSlider.ValueChanged += (_, e) => UpdateControl(c => c.X = e.NewValue);
        PositionYSlider.ValueChanged += (_, e) => UpdateControl(c => c.Y = e.NewValue);
        SizeSlider.ValueChanged += (_, e) => UpdateControl(c => c.Size = e.NewValue);
        AspectSlider.ValueChanged += (_, e) => UpdateControl(c => c.Aspect = e.NewValue);
        CornerRadiusSlider.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            CornerRadiusLabel.Text = $"圆角比例（{Math.Round(e.NewValue * 100):0}%）";
            UpdateControl(c => c.CornerRadius = e.NewValue);
        };
        PopulateBindingOptions();
        Loaded += (_, _) =>
        {
            ThemeBrushes.PaletteChanged += DrawControls;
            AttachSessionEvents();
            RefreshPhones();
        };
        Unloaded += (_, _) =>
        {
            ThemeBrushes.PaletteChanged -= DrawControls;
            DetachSessionEvents();
        };
    }

    public void Configure(SessionManager sessionManager, TrustStore trustStore)
    {
        _sessionManager = sessionManager;
        _trustStore = trustStore;
        AttachSessionEvents();
        RefreshPhones();
    }

    private void AttachSessionEvents()
    {
        if (_sessionEventsAttached || _sessionManager == null) return;
        _sessionManager.ClientConnected += SessionManager_ClientConnected;
        _sessionEventsAttached = true;
    }

    private void DetachSessionEvents()
    {
        if (_sessionEventsAttached && _sessionManager != null)
            _sessionManager.ClientConnected -= SessionManager_ClientConnected;
        _sessionEventsAttached = false;
        foreach (var connection in _pendingIdentityConnections)
            connection.PeerIdentified -= PendingConnection_PeerIdentified;
        _pendingIdentityConnections.Clear();
    }

    private void SessionManager_ClientConnected(object? sender, SessionConnection connection)
    {
        if (connection.PeerInfo != null)
        {
            DispatcherQueue.TryEnqueue(RefreshPhones);
            return;
        }
        if (_pendingIdentityConnections.Add(connection))
            connection.PeerIdentified += PendingConnection_PeerIdentified;
    }

    private void PendingConnection_PeerIdentified(object? sender, SessionPeerInfo peer)
    {
        if (sender is not SessionConnection connection) return;
        connection.PeerIdentified -= PendingConnection_PeerIdentified;
        _pendingIdentityConnections.Remove(connection);
        if (peer.Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded) RefreshPhones();
            });
    }

    private void RefreshPhones()
    {
        string? selectedId = _connection?.RemoteDeviceId;
        _updating = true;
        PhoneSelector.Items.Clear();
        foreach (var connection in _sessionManager?.ActiveConnections.Where(IsTrustedAndroid) ?? Enumerable.Empty<SessionConnection>())
        {
            PhoneSelector.Items.Add(new ComboBoxItem
            {
                Content = connection.PeerInfo?.Name ?? "Android 手机",
                Tag = connection
            });
        }
        var selected = PhoneSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => (item.Tag as SessionConnection)?.RemoteDeviceId == selectedId)
            ?? PhoneSelector.Items.OfType<ComboBoxItem>().FirstOrDefault();
        PhoneSelector.SelectedItem = selected;
        _updating = false;
        SelectPhone(selected?.Tag as SessionConnection);
        if (selected == null)
            SyncStatusText.Text = "没有已连接且受信任的 Android 手机；先在 Hinge 首页连接手机。";
    }

    private bool IsTrustedAndroid(SessionConnection connection) =>
        connection.State == SessionState.Connected &&
        connection.PeerInfo?.Platform.Equals("android", StringComparison.OrdinalIgnoreCase) == true &&
        connection.RemoteDeviceId is { Length: > 0 } id && _trustStore?.IsTrusted(id) == true;

    private void PhoneSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        SelectPhone((PhoneSelector.SelectedItem as ComboBoxItem)?.Tag as SessionConnection);
    }

    private void SelectPhone(SessionConnection? connection)
    {
        if (ReferenceEquals(_connection, connection))
        {
            if (connection != null) _ = RequestFromPhoneAsync();
            return;
        }
        if (_connection != null)
        {
            _connection.FrameReceived -= Connection_FrameReceived;
            _connection.StateChanged -= Connection_StateChanged;
        }
        CancelReadTimeout();
        _connection = connection;
        if (_connection == null) return;
        _connection.FrameReceived += Connection_FrameReceived;
        _connection.StateChanged += Connection_StateChanged;
        _ = RequestFromPhoneAsync();
    }

    private void Connection_StateChanged(object? sender, SessionState state)
    {
        if (state == SessionState.Connected)
        {
            DispatcherQueue.TryEnqueue(RefreshPhones);
            return;
        }
        CancelReadTimeout();
        DispatcherQueue.TryEnqueue(() => SyncStatusText.Text = "手机连接已断开；未同步的编辑仍保留在本页。重新连接后可再次保存。");
    }

    private void CancelReadTimeout()
    {
        var timeout = _readTimeout;
        _readTimeout = null;
        timeout?.Cancel();
        timeout?.Dispose();
    }

    private async Task ShowReadTimeoutAsync(SessionConnection connection, CancellationTokenSource timeout)
    {
        var token = timeout.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(_connection, connection) && ReferenceEquals(_readTimeout, timeout))
                {
                    _readTimeout = null;
                    timeout.Dispose();
                    SyncStatusText.Text = "手机没有返回布局。请确认手机端 Hinge 已更新并保持连接，然后点“重新读取”。";
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task RequestFromPhoneAsync()
    {
        if (_connection is not { State: SessionState.Connected } connection || !IsTrustedAndroid(connection))
        {
            SyncStatusText.Text = "请先连接一台已信任的 Android 手机。";
            return;
        }
        if (_readTimeout != null) return;
        try
        {
            CancelReadTimeout();
            var timeout = new CancellationTokenSource();
            _readTimeout = timeout;
            _ = ShowReadTimeoutAsync(connection, timeout);
            SyncStatusText.Text = "正在读取手机上的游戏布局…";
            await connection.SendFrameAsync(MessageType.GameProfileSync, GameLayoutProtocol.RequestPayload());
        }
        catch (Exception exception)
        {
            CancelReadTimeout();
            SyncStatusText.Text = $"读取失败：{exception.Message}";
        }
    }

    private async Task SaveAndSyncAsync()
    {
        if (_document == null || _connection is not { State: SessionState.Connected } connection || !IsTrustedAndroid(connection))
        {
            SyncStatusText.Text = "请先读取布局，并保持已信任的 Android 手机连接。";
            return;
        }
        try
        {
            var payload = GameLayoutProtocol.SetPayload(_document);
            SyncStatusText.Text = "正在保存并同步…";
            SaveAndSyncButton.IsEnabled = false;
            await connection.SendFrameAsync(MessageType.GameProfileSync, payload);
        }
        catch (Exception exception)
        {
            SyncStatusText.Text = $"同步失败：{exception.Message}";
        }
        finally
        {
            SaveAndSyncButton.IsEnabled = true;
        }
    }

    private void Connection_FrameReceived(object? sender, ProtocolFrame frame)
    {
        if (!ReferenceEquals(sender, _connection) || _connection == null || !IsTrustedAndroid(_connection)) return;
        if (frame.Type != MessageType.GameProfileSync) return;
        if (!GameLayoutProtocol.TryRead(frame.Payload, out var action, out var document))
        {
            CancelReadTimeout();
            DispatcherQueue.TryEnqueue(() => SyncStatusText.Text = "手机返回的数据格式不兼容；请更新 Android 与 Windows 端后重试。");
            return;
        }
        if (action is "data" or "error") CancelReadTimeout();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (action == "data" && document != null)
            {
                _document = document;
                SelectPreset(document.Presets.FirstOrDefault(preset => preset.Id == document.Selected) ?? document.Presets[0]);
                SyncStatusText.Text = $"已读取 {document.Presets.Count} 套预设。单击控件选中；再次拖动主体移动，拖动角柄调整大小和比例；移动到其他控件附近时会对齐吸附。";
            }
            else if (action == "ack")
            {
                SyncStatusText.Text = "已保存并同步到手机；游戏模式退出重进后即可使用。";
            }
            else if (action == "error")
            {
                SyncStatusText.Text = "手机拒绝了布局同步；检查连接和预设数据后重试。";
            }
        });
    }

    private void SelectPreset(GameLayoutPreset preset)
    {
        _preset = preset;
        _document!.Selected = preset.Id;
        _updating = true;
        ProfileSelector.Items.Clear();
        foreach (var item in _document.Presets)
            ProfileSelector.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item });
        ProfileSelector.SelectedItem = ProfileSelector.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, preset));
        ProfileNameBox.Text = preset.Name;
        ControlList.Items.Clear();
        foreach (var control in preset.Controls)
            ControlList.Items.Add(new ListViewItem { Content = control.Label, Tag = control });
        _control = preset.Controls.FirstOrDefault();
        ControlList.SelectedItem = ControlList.Items.OfType<ListViewItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, _control));
        _updating = false;
        RefreshSelectedControl();
        DrawControls();
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ProfileSelector.SelectedItem is not ComboBoxItem { Tag: GameLayoutPreset preset }) return;
        SelectPreset(preset);
    }

    private void ControlList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _control = (ControlList.SelectedItem as ListViewItem)?.Tag as GameLayoutControl;
        RefreshSelectedControl();
        DrawControls();
    }

    private void RefreshSelectedControl()
    {
        _updating = true;
        bool hasControl = _control != null;
        ControlLabelBox.IsEnabled = hasControl;
        ControlBindingCombo.IsEnabled = hasControl;
        PositionXSlider.IsEnabled = hasControl;
        PositionYSlider.IsEnabled = hasControl;
        SizeSlider.IsEnabled = hasControl;
        AspectSlider.IsEnabled = _control is { Joystick: false, Shape: not "circle" };
        ShapeCombo.IsEnabled = _control is { Joystick: false };
        CornerRadiusSlider.IsEnabled = _control is { Joystick: false, Shape: "rounded" };
        DirectionKeysBox.IsEnabled = _control?.Joystick == true;
        if (_control is { } control)
        {
            if (control.Shape == "circle") control.Aspect = 1;
            ControlLabelBox.Text = control.Label;
            ControlBindingCombo.SelectedItem = ControlBindingCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is int key && key == control.Binding);
            PositionXSlider.Value = control.X;
            PositionYSlider.Value = control.Y;
            SizeSlider.Value = control.Size;
            AspectSlider.Value = control.Aspect;
            ShapeCombo.SelectedItem = ShapeCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == (control.Joystick ? "circle" : control.Shape));
            CornerRadiusSlider.Value = control.CornerRadius;
            CornerRadiusLabel.Text = $"圆角比例（{Math.Round(control.CornerRadius * 100):0}%）";
            DirectionKeysBox.Text = string.Join(',', control.Directions);
        }
        else
        {
            ControlLabelBox.Text = string.Empty;
            ControlBindingCombo.SelectedItem = null;
            ShapeCombo.SelectedItem = null;
            CornerRadiusSlider.Value = .12;
            CornerRadiusLabel.Text = "圆角比例（12%）";
            DirectionKeysBox.Text = string.Empty;
        }
        _updating = false;
    }

    private void ProfileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _preset == null) return;
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0) return;
        _preset.Name = name[..Math.Min(40, name.Length)];
        if (ProfileSelector.SelectedItem is ComboBoxItem item) item.Content = _preset.Name;
    }

    private void ControlLabelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _control == null) return;
        _control.Label = ControlLabelBox.Text[..Math.Min(64, ControlLabelBox.Text.Length)];
        if (ControlList.SelectedItem is ListViewItem item) item.Content = _control.Label;
        DrawControls();
    }

    private void ControlBindingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _control == null ||
            ControlBindingCombo.SelectedItem is not ComboBoxItem { Tag: int key } || !ValidKey(key)) return;
        _control.Binding = key;
        DrawControls();
    }

    private void ShapeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _control == null || _control.Joystick ||
            ShapeCombo.SelectedItem is not ComboBoxItem { Tag: string shape } ||
            shape is not ("auto" or "circle" or "rounded" or "rect")) return;
        _control.Shape = shape;
        if (shape == "circle")
        {
            _control.Aspect = 1;
            _control.CornerRadius = .5;
        }
        RefreshSelectedControl();
        DrawControls();
    }

    private void PopulateBindingOptions()
    {
        void Add(int key, string label) => ControlBindingCombo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        Add(0, "不发送按键");
        Add(-1, "鼠标左键");
        Add(-2, "鼠标右键");
        Add(-3, "鼠标中键");
        for (int key = 65; key <= 90; key++) Add(key, ((char)key).ToString());
        for (int key = 48; key <= 57; key++) Add(key, ((char)key).ToString());
        for (int key = 112; key <= 135; key++) Add(key, $"F{key - 111}");
        foreach (var (key, label) in new (int, string)[]
        {
            (8, "退格"), (9, "Tab"), (13, "Enter"), (16, "Shift"),
            (17, "Ctrl"), (18, "Alt"), (27, "Esc"), (32, "Space"),
            (33, "Page Up"), (34, "Page Down"), (35, "End"), (36, "Home"),
            (37, "←"), (38, "↑"), (39, "→"), (40, "↓"), (45, "Insert"),
            (46, "Delete"), (91, "左 Win"), (92, "右 Win")
        }) Add(key, label);
    }

    private void DirectionKeysBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _control?.Joystick != true) return;
        var parts = DirectionKeysBox.Text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts.All(part => int.TryParse(part, out int key) && ValidKeyboardKey(key))) return;
        _control.Directions = parts.Select(int.Parse).ToArray();
        DrawControls();
    }

    private void UpdateControl(Action<GameLayoutControl> update)
    {
        if (_updating || _control == null) return;
        update(_control);
        DrawControls();
    }

    private static bool ValidKeyboardKey(int key) => key is >= 8 and <= 254;
    private static bool ValidKey(int key) => ValidKeyboardKey(key) || key == 0 || key is >= -3 and <= -1;

    private void AddProfile()
    {
        if (_document == null || _document.Presets.Count >= 32 || _preset == null) return;
        var name = $"{_preset.Name} 副本";
        var copy = new GameLayoutPreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name[..Math.Min(40, name.Length)],
            Controls = _preset.Controls.Select(control => new GameLayoutControl
            {
                Id = Guid.NewGuid().ToString("N"), Label = control.Label, Image = control.Image,
                X = control.X, Y = control.Y, Size = control.Size, Aspect = control.Aspect,
                Shape = control.Shape, CornerRadius = control.CornerRadius,
                Binding = control.Binding, Joystick = control.Joystick, Directions = control.Directions.ToArray()
            }).ToList()
        };
        _document.Presets.Add(copy);
        SelectPreset(copy);
    }

    private void DeleteProfile()
    {
        if (_document == null || _preset == null || _document.Presets.Count <= 1) return;
        _document.Presets.Remove(_preset);
        SelectPreset(_document.Presets[0]);
    }

    private void AddControl()
    {
        if (_preset == null || _preset.Controls.Count >= 64) return;
        var control = new GameLayoutControl { Id = Guid.NewGuid().ToString("N"), Label = "新按键", X = .5, Y = .5 };
        _preset.Controls.Add(control);
        SelectPreset(_preset);
        var item = ControlList.Items.OfType<ListViewItem>().FirstOrDefault(candidate => ReferenceEquals(candidate.Tag, control));
        ControlList.SelectedItem = item;
    }

    private void DeleteControl()
    {
        if (_preset == null || _control == null) return;
        _preset.Controls.Remove(_control);
        SelectPreset(_preset);
    }

    private void DrawControls()
    {
        CancelCanvasGesture();
        LayoutCanvas.Children.Clear();
        _rendered.Clear();
        _resizeHandles.Clear();
        _alignmentGuides.Clear();
        AddToolbarMarker("×", CanvasWidth / 2 - 26, 8);
        AddToolbarMarker("⚙", CanvasWidth - 60, 8);
        if (_preset == null) return;
        foreach (var control in _preset.Controls)
        {
            double height = CanvasHeight * control.Size;
            bool circle = control.Joystick || control.Shape == "circle";
            if (control.Shape == "circle")
            {
                control.Aspect = 1;
                control.CornerRadius = .5;
            }
            double width = Math.Min(CanvasWidth, height * (circle ? 1 : control.Aspect));
            height = Math.Min(CanvasHeight, height);
            var border = new Border
            {
                Width = width,
                Height = height,
                Tag = control,
                Background = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3SurfaceContainerHighestBrush")),
                BorderBrush = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, _control == control ? "HingeM3PrimaryBrush" : "HingeM3OutlineVariantBrush")),
                BorderThickness = new Thickness(_control == control ? 3 : 2),
                CornerRadius = new CornerRadius(CornerRadiusFor(control, width, height)),
                Child = new TextBlock
                {
                    Text = control.Label,
                    Foreground = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3OnSurfaceBrush")),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 28,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            border.PointerPressed += Control_PointerPressed;
            border.PointerMoved += Control_PointerMoved;
            border.PointerReleased += CanvasGesture_PointerReleased;
            border.PointerCanceled += CanvasGesture_PointerCanceled;
            LayoutCanvas.Children.Add(border);
            _rendered[control] = border;
            PlaceControl(control, border);
        }
        DrawResizeHandles();
    }

    private static double CornerRadiusFor(GameLayoutControl control, double width, double height) =>
        control.Joystick || control.Shape == "circle"
            ? Math.Min(width, height) / 2
            : control.Shape switch
            {
                "rect" => 0,
                "rounded" => Math.Min(width, height) * control.CornerRadius,
                _ => Math.Abs(control.Aspect - 1) < .02 ? height / 2 : 14
            };

    private void AddToolbarMarker(string label, double left, double top)
    {
        var marker = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(26),
            Background = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3SurfaceContainerHighBrush")),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 27,
                Foreground = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3OnSurfaceBrush")),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        LayoutCanvas.Children.Add(marker);
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
    }

    private void PlaceControl(GameLayoutControl control, FrameworkElement element)
    {
        double left = Math.Clamp(control.X * CanvasWidth - element.Width / 2, 0, CanvasWidth - element.Width);
        double top = Math.Clamp(control.Y * CanvasHeight - element.Height / 2, 0, CanvasHeight - element.Height);
        var rect = new Rect(left, top, element.Width, element.Height);
        if (Overlaps(rect, new Rect(CanvasWidth / 2 - 28, 0, 56, 56)) ||
            Overlaps(rect, new Rect(CanvasWidth - 64, 0, 64, 56))) top = 60;
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, Math.Min(top, CanvasHeight - element.Height));
    }

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;

    private void DrawResizeHandles()
    {
        if (_control == null || !_rendered.TryGetValue(_control, out var border)) return;
        double left = Canvas.GetLeft(border);
        double top = Canvas.GetTop(border);
        foreach (var (corner, x, y, symbol) in new[]
        {
            (ResizeCorner.TopLeft, left, top, "↖"),
            (ResizeCorner.TopRight, left + border.Width, top, "↗"),
            (ResizeCorner.BottomLeft, left, top + border.Height, "↙"),
            (ResizeCorner.BottomRight, left + border.Width, top + border.Height, "↘")
        })
        {
            var handle = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3PrimaryBrush")),
                BorderBrush = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3SurfaceBrush")),
                BorderThickness = new Thickness(2),
                Tag = corner,
                Child = new TextBlock
                {
                    Text = symbol,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3OnPrimaryBrush"))
                }
            };
            AutomationProperties.SetName(handle, $"调整 {symbol} 角的大小和宽高比");
            ToolTipService.SetToolTip(handle, $"拖动 {symbol} 角调整大小和宽高比");
            handle.PointerPressed += ResizeHandle_PointerPressed;
            handle.PointerMoved += ResizeHandle_PointerMoved;
            handle.PointerReleased += CanvasGesture_PointerReleased;
            handle.PointerCanceled += CanvasGesture_PointerCanceled;
            Canvas.SetLeft(handle, x - handle.Width / 2);
            Canvas.SetTop(handle, y - handle.Height / 2);
            LayoutCanvas.Children.Add(handle);
            _resizeHandles.Add(handle);
        }
    }

    private void UpdateResizeHandles()
    {
        if (_control == null || !_rendered.TryGetValue(_control, out var border) || _resizeHandles.Count != 4) return;
        double left = Canvas.GetLeft(border);
        double top = Canvas.GetTop(border);
        var points = new[]
        {
            new Point(left, top),
            new Point(left + border.Width, top),
            new Point(left, top + border.Height),
            new Point(left + border.Width, top + border.Height)
        };
        for (int i = 0; i < _resizeHandles.Count; i++)
        {
            Canvas.SetLeft(_resizeHandles[i], points[i].X - _resizeHandles[i].Width / 2);
            Canvas.SetTop(_resizeHandles[i], points[i].Y - _resizeHandles[i].Height / 2);
        }
    }

    private void ResizeHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_control == null || sender is not Border { Tag: ResizeCorner corner } handle ||
            !_rendered.TryGetValue(_control, out var border)) return;
        _pointerGesture = PointerGesture.Resize;
        _gestureControl = _control;
        _gestureBorder = border;
        _resizeCorner = corner;
        _resizeStartRect = new Rect(Canvas.GetLeft(border), Canvas.GetTop(border), border.Width, border.Height);
        PreserveShapeOnResize(_gestureControl, _resizeStartRect);
        RefreshSelectedControl();
        _dragPointer = e.Pointer.PointerId;
        handle.CapturePointer(e.Pointer);
        ClearAlignmentGuides();
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerGesture != PointerGesture.Resize || _dragPointer != e.Pointer.PointerId ||
            _gestureControl == null || _gestureBorder == null) return;
        Point point = e.GetCurrentPoint(LayoutCanvas).Position;
        ResizeControl(point);
        e.Handled = true;
    }

    private void ResizeControl(Point point)
    {
        if (_gestureControl == null || _gestureBorder == null) return;
        bool leftCorner = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
        bool topCorner = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight;
        double anchorX = leftCorner ? _resizeStartRect.Right : _resizeStartRect.Left;
        double anchorY = topCorner ? _resizeStartRect.Bottom : _resizeStartRect.Top;
        double width;
        double height;
        if (_gestureControl.Joystick || _gestureControl.Shape == "circle")
        {
            double diameter = Math.Clamp(
                (Math.Abs(point.X - anchorX) + Math.Abs(point.Y - anchorY)) / 2,
                CanvasHeight * 0.1,
                CanvasHeight * 0.48);
            width = diameter;
            height = diameter;
            _gestureControl.Aspect = 1;
        }
        else
        {
            width = Math.Clamp(Math.Abs(point.X - anchorX), 48, CanvasWidth);
            height = Math.Clamp(Math.Abs(point.Y - anchorY), CanvasHeight * 0.1, CanvasHeight * 0.48);
            width = height * Math.Clamp(width / height, 0.5, 3);
        }
        double left = Math.Clamp(anchorX - (leftCorner ? width : 0), 0, CanvasWidth - width);
        double top = Math.Clamp(anchorY - (topCorner ? height : 0), 0, CanvasHeight - height);
        _gestureBorder.Width = width;
        _gestureBorder.Height = height;
        _gestureBorder.CornerRadius = new CornerRadius(CornerRadiusFor(_gestureControl, width, height));
        Canvas.SetLeft(_gestureBorder, left);
        Canvas.SetTop(_gestureBorder, top);
        _gestureControl.X = (left + width / 2) / CanvasWidth;
        _gestureControl.Y = (top + height / 2) / CanvasHeight;
        _gestureControl.Size = height / CanvasHeight;
        if (!_gestureControl.Joystick) _gestureControl.Aspect = width / height;
        if (_gestureControl.Shape == "circle") _gestureControl.Aspect = 1;
        PlaceControl(_gestureControl, _gestureBorder);
        _gestureControl.X = (Canvas.GetLeft(_gestureBorder) + width / 2) / CanvasWidth;
        _gestureControl.Y = (Canvas.GetTop(_gestureBorder) + height / 2) / CanvasHeight;
        UpdatePositionSliders();
        UpdateResizeHandles();
    }

    private static void PreserveShapeOnResize(GameLayoutControl control, Rect rect)
    {
        if (control.Joystick || control.Shape != "auto") return;
        if (Math.Abs(control.Aspect - 1) < .02)
        {
            control.Shape = "circle";
            control.Aspect = 1;
            control.CornerRadius = .5;
        }
        else
        {
            control.Shape = "rounded";
            control.CornerRadius = Math.Clamp(14 / Math.Min(rect.Width, rect.Height), 0, .5);
        }
    }

    private void Control_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: GameLayoutControl control } border) return;
        if (_control != control)
        {
            _control = control;
            _updating = true;
            ControlList.SelectedItem = ControlList.Items.OfType<ListViewItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, control));
            _updating = false;
            RefreshSelectedControl();
            DrawControls();
            e.Handled = true;
            return;
        }
        _pointerGesture = PointerGesture.Move;
        _gestureControl = control;
        _gestureBorder = border;
        Point point = e.GetCurrentPoint(LayoutCanvas).Position;
        _dragOffset = new Point(point.X - Canvas.GetLeft(border), point.Y - Canvas.GetTop(border));
        _dragPointer = e.Pointer.PointerId;
        border.CapturePointer(e.Pointer);
        ClearAlignmentGuides();
        e.Handled = true;
    }

    private void Control_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerGesture != PointerGesture.Move || _dragPointer != e.Pointer.PointerId ||
            _gestureControl == null || _gestureBorder == null) return;
        Point point = e.GetCurrentPoint(LayoutCanvas).Position;
        double left = Math.Clamp(point.X - _dragOffset.X, 0, CanvasWidth - _gestureBorder.Width);
        double top = Math.Clamp(point.Y - _dragOffset.Y, 0, CanvasHeight - _gestureBorder.Height);
        var rect = new Rect(left, top, _gestureBorder.Width, _gestureBorder.Height);
        double? guideX = SnapAxis(ref left, rect.Width, true);
        double? guideY = SnapAxis(ref top, rect.Height, false);
        left = Math.Clamp(left, 0, CanvasWidth - rect.Width);
        top = Math.Clamp(top, 0, CanvasHeight - rect.Height);
        Canvas.SetLeft(_gestureBorder, left);
        Canvas.SetTop(_gestureBorder, top);
        _gestureControl.X = (left + rect.Width / 2) / CanvasWidth;
        _gestureControl.Y = (top + rect.Height / 2) / CanvasHeight;
        PlaceControl(_gestureControl, _gestureBorder);
        _gestureControl.X = (Canvas.GetLeft(_gestureBorder) + rect.Width / 2) / CanvasWidth;
        _gestureControl.Y = (Canvas.GetTop(_gestureBorder) + rect.Height / 2) / CanvasHeight;
        UpdatePositionSliders();
        UpdateResizeHandles();
        DrawAlignmentGuides(guideX, guideY);
        e.Handled = true;
    }

    private double? SnapAxis(ref double position, double size, bool horizontal)
    {
        double canvasSize = horizontal ? CanvasWidth : CanvasHeight;
        var targets = new List<double> { 0, canvasSize / 2, canvasSize };
        foreach (var (control, border) in _rendered)
        {
            if (ReferenceEquals(control, _gestureControl)) continue;
            double start = horizontal ? Canvas.GetLeft(border) : Canvas.GetTop(border);
            double extent = horizontal ? border.Width : border.Height;
            targets.AddRange([start, start + extent / 2, start + extent]);
        }
        double[] offsets = [0, size / 2, size];
        double bestDelta = 0;
        double bestTarget = 0;
        double bestDistance = SnapTolerance + 1;
        foreach (double offset in offsets)
        foreach (double target in targets)
        {
            double delta = target - (position + offset);
            double distance = Math.Abs(delta);
            if (distance < bestDistance)
            {
                bestDelta = delta;
                bestTarget = target;
                bestDistance = distance;
            }
        }
        if (bestDistance > SnapTolerance) return null;
        position += bestDelta;
        return bestTarget;
    }

    private void DrawAlignmentGuides(double? x, double? y)
    {
        ClearAlignmentGuides();
        if (x is double guideX) AddAlignmentGuide(guideX, vertical: true);
        if (y is double guideY) AddAlignmentGuide(guideY, vertical: false);
    }

    private void AddAlignmentGuide(double position, bool vertical)
    {
        var guide = new Border
        {
            Width = vertical ? 2 : CanvasWidth,
            Height = vertical ? CanvasHeight : 2,
            Background = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3PrimaryBrush")),
            Opacity = 0.8,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(guide, vertical ? position - 1 : 0);
        Canvas.SetTop(guide, vertical ? 0 : position - 1);
        LayoutCanvas.Children.Add(guide);
        _alignmentGuides.Add(guide);
    }

    private void ClearAlignmentGuides()
    {
        foreach (var guide in _alignmentGuides) LayoutCanvas.Children.Remove(guide);
        _alignmentGuides.Clear();
    }

    private void UpdatePositionSliders()
    {
        if (_gestureControl == null) return;
        _updating = true;
        PositionXSlider.Value = _gestureControl.X;
        PositionYSlider.Value = _gestureControl.Y;
        SizeSlider.Value = _gestureControl.Size;
        AspectSlider.Value = _gestureControl.Aspect;
        _updating = false;
    }

    private void CancelCanvasGesture()
    {
        _dragPointer = null;
        _pointerGesture = PointerGesture.None;
        _gestureControl = null;
        _gestureBorder = null;
    }

    private void CanvasGesture_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer != e.Pointer.PointerId) return;
        if (sender is UIElement element) element.ReleasePointerCapture(e.Pointer);
        CancelCanvasGesture();
        ClearAlignmentGuides();
    }

    private void CanvasGesture_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        CanvasGesture_PointerReleased(sender, e);

    private void EditorLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 1008;
        EditorLayout.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(1.5, GridUnitType.Star);
        EditorLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(EditorPropertiesCard, compact ? 1 : 0);
        Grid.SetColumn(EditorPropertiesCard, compact ? 0 : 1);
    }
}
