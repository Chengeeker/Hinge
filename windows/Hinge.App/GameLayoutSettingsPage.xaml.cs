using Hinge.Core;
using Microsoft.UI.Xaml;
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
    private CancellationTokenSource? _readTimeout;

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
        DirectionKeysBox.TextChanged += DirectionKeysBox_TextChanged;
        PositionXSlider.ValueChanged += (_, e) => UpdateControl(c => c.X = e.NewValue);
        PositionYSlider.ValueChanged += (_, e) => UpdateControl(c => c.Y = e.NewValue);
        SizeSlider.ValueChanged += (_, e) => UpdateControl(c => c.Size = e.NewValue);
        AspectSlider.ValueChanged += (_, e) => UpdateControl(c => c.Aspect = e.NewValue);
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
                SyncStatusText.Text = $"已从手机读取 {document.Presets.Count} 套预设；拖动左侧按键调整位置，保存后同步。";
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
        AspectSlider.IsEnabled = hasControl;
        DirectionKeysBox.IsEnabled = _control?.Joystick == true;
        if (_control is { } control)
        {
            ControlLabelBox.Text = control.Label;
            ControlBindingCombo.SelectedItem = ControlBindingCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is int key && key == control.Binding);
            PositionXSlider.Value = control.X;
            PositionYSlider.Value = control.Y;
            SizeSlider.Value = control.Size;
            AspectSlider.Value = control.Aspect;
            DirectionKeysBox.Text = string.Join(',', control.Directions);
        }
        else
        {
            ControlLabelBox.Text = string.Empty;
            ControlBindingCombo.SelectedItem = null;
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
        LayoutCanvas.Children.Clear();
        _rendered.Clear();
        AddToolbarMarker("×", CanvasWidth / 2 - 26, 8);
        AddToolbarMarker("⚙", CanvasWidth - 60, 8);
        if (_preset == null) return;
        foreach (var control in _preset.Controls)
        {
            double height = CanvasHeight * control.Size;
            double width = Math.Min(CanvasWidth, height * (control.Joystick ? 1 : control.Aspect));
            height = Math.Min(CanvasHeight, height);
            var border = new Border
            {
                Width = width,
                Height = height,
                Tag = control,
                Background = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, "HingeM3SurfaceContainerHighestBrush")),
                BorderBrush = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas, _control == control ? "HingeM3PrimaryBrush" : "HingeM3OutlineVariantBrush")),
                BorderThickness = new Thickness(_control == control ? 3 : 2),
                CornerRadius = control.Joystick || Math.Abs(control.Aspect - 1) < .02 ? new CornerRadius(height / 2) : new CornerRadius(14),
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
            border.PointerReleased += Control_PointerReleased;
            border.PointerCanceled += Control_PointerCanceled;
            LayoutCanvas.Children.Add(border);
            _rendered[control] = border;
            PlaceControl(control, border);
        }
    }

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

    private void Control_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: GameLayoutControl control } border) return;
        _control = control;
        _dragPointer = e.Pointer.PointerId;
        border.CapturePointer(e.Pointer);
        RefreshSelectedControl();
        foreach (var pair in _rendered)
        {
            pair.Value.BorderBrush = new SolidColorBrush(ThemeBrushes.Color(LayoutCanvas,
                _control == pair.Key ? "HingeM3PrimaryBrush" : "HingeM3OutlineVariantBrush"));
            pair.Value.BorderThickness = new Thickness(_control == pair.Key ? 3 : 2);
        }
        e.Handled = true;
    }

    private void Control_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: GameLayoutControl control } border || _dragPointer != e.Pointer.PointerId) return;
        Point point = e.GetCurrentPoint(LayoutCanvas).Position;
        control.X = Math.Clamp(point.X / CanvasWidth, 0, 1);
        control.Y = Math.Clamp(point.Y / CanvasHeight, 0, 1);
        PlaceControl(control, border);
        _updating = true;
        PositionXSlider.Value = control.X;
        PositionYSlider.Value = control.Y;
        _updating = false;
    }

    private void Control_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer == e.Pointer.PointerId) _dragPointer = null;
    }

    private void Control_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        Control_PointerReleased(sender, e);

    private void EditorLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 1008;
        EditorLayout.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(1.5, GridUnitType.Star);
        EditorLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(EditorPropertiesCard, compact ? 1 : 0);
        Grid.SetColumn(EditorPropertiesCard, compact ? 0 : 1);
    }
}
