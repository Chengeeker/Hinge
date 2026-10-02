using System.IO;
using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.FileProperties;

namespace Hinge.App;

/// <summary>One independent browser/session per configured endpoint, with no phone RPCs.</summary>
internal sealed class WebDavBrowserView : UserControl, IDisposable
{
    private readonly WebDavProfile _profile;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _listing;
    private CancellationTokenSource? _transfer;
    private CancellationTokenSource? _outgoingDragCancellation;
    private WebDavBrowserClient? _client;
    private Uri? _directory;
    private readonly TextBlock _path = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListView _files = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
    private readonly GridView _gridFiles = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Visibility = Visibility.Collapsed };
    private readonly ComboBox _viewMode = new() { MinWidth = 120, Height = 36 };
    private readonly ComboBox _sort = new() { MinWidth = 148, Height = 36 };
    private readonly Button _select = new() { Content = "选择", Height = 36 };
    private readonly Grid _fileHeader = new() { ColumnSpacing = 12, Padding = new Thickness(12, 8, 12, 8) };
    private bool _selectionMode;
    private bool _updatingView;
    private ListViewBase CurrentFiles => _gridFiles.Visibility == Visibility.Visible ? _gridFiles : _files;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly HingeWrapPanel _actions = new() { Spacing = 8, LineSpacing = 8 };
    private readonly Button _cancel = new() { Content = "取消传输", Visibility = Visibility.Collapsed };
    private IReadOnlyList<WebDavEntry> _entries = [];
    private bool _busy;
    private bool _actionPending;
    private bool _activated;
    private Task? _activationTask;
    private long _progressTimestamp;
    private long _lastCompleted;
    private int _generation;
    private readonly SemaphoreSlim _thumbnailGate = new(2, 2);
    private readonly Dictionary<Image, CancellationTokenSource> _thumbnailRequests = new();
    private PreviewCache? _thumbnailCache;

    public event EventHandler? RemoteFileDragStarted;
    public event EventHandler? RemoteFileDragCompleted;

    public WebDavBrowserView(WebDavProfile profile)
    {
        _profile = profile;
        // Construct theme resources directly; do not parse resource alias XAML at runtime.
        Resources.ThemeDictionaries["Light"] = CreateSelectionTheme(false);
        Resources.ThemeDictionaries["Dark"] = CreateSelectionTheme(true);
        var root = new Grid { Margin = new Thickness(24, 12, 24, 24), RowSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(_path);
        AddButton("上一级", async () => { if (_directory is { } current && _client?.Parent(current) is { } parent) await NavigateAsync(parent); });
        AddButton("刷新", async () =>
        {
            if (!_activated) await ActivateAsync();
            else await NavigateAsync(_directory);
        }, requiresClient: false);
        AddButton("上传", PickUploadsAsync);
        AddButton("下载", DownloadSelectionAsync);
        AddButton("新建文件夹", CreateDirectoryAsync);
        AddButton("删除", DeleteSelectionAsync);
        _actions.Children.Add(_select);
        _select.Click += (_, _) =>
        {
            _selectionMode = !_selectionMode;
            _select.Content = _selectionMode ? "取消选择" : "选择";
            _files.SelectionMode = _gridFiles.SelectionMode = _selectionMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
            UpdateSelectionStatus();
        };
        var all = new Button { Content = "全选", Height = 36 };
        all.Click += (_, _) => { if (!_selectionMode) { _selectionMode = true; _select.Content = "取消选择"; _files.SelectionMode = _gridFiles.SelectionMode = ListViewSelectionMode.Multiple; } CurrentFiles.SelectAll(); };
        _actions.Children.Add(all);
        foreach (var label in new[] { "列表", "宫格" }) _viewMode.Items.Add(new ComboBoxItem { Content = label });
        _viewMode.SelectedIndex = 0;
        _actions.Children.Add(_viewMode);
        foreach (var (label, tag) in new[] { ("名称", "name"), ("类型", "type"), ("大小（小到大）", "sizeAsc"), ("大小（大到小）", "sizeDesc"), ("时间（旧到新）", "timeAsc"), ("时间（新到旧）", "timeDesc") })
            _sort.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _sort.SelectedIndex = 0;
        _actions.Children.Add(_sort);
        Grid.SetRow(_actions, 1); root.Children.Add(_actions);
        var transferBar = new Grid { ColumnSpacing = 8 };
        transferBar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        transferBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        transferBar.Children.Add(_progress); Grid.SetColumn(_cancel, 1); transferBar.Children.Add(_cancel);
        Grid.SetRow(transferBar, 2); root.Children.Add(transferBar);
        Grid.SetRow(_status, 3); root.Children.Add(_status);
        var fileArea = new Grid();
        fileArea.RowDefinitions.Add(new() { Height = GridLength.Auto });
        fileArea.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var width in new[] { 3d, 1.6d, 1.1d, 1d }) _fileHeader.ColumnDefinitions.Add(new() { Width = new GridLength(width, GridUnitType.Star) });
        var columnLabels = new[] { "名称", "修改日期", "类型", "大小" };
        for (int i = 0; i < columnLabels.Length; i++) { var label = new TextBlock { Text = columnLabels[i], Opacity = 0.7 }; Grid.SetColumn(label, i); _fileHeader.Children.Add(label); }
        fileArea.Children.Add(_fileHeader);
        Grid.SetRow(_files, 1); fileArea.Children.Add(_files);
        Grid.SetRow(_gridFiles, 1); fileArea.Children.Add(_gridFiles);
        Grid.SetRow(fileArea, 4); root.Children.Add(fileArea);
        Content = root;
        _cancel.Click += (_, _) => _transfer?.Cancel();
        _files.ItemClick += File_Click;
        _gridFiles.ItemClick += File_Click;
        _files.CanDragItems = _gridFiles.CanDragItems = true;
        _files.DragItemsStarting += File_DragItemsStarting;
        _gridFiles.DragItemsStarting += File_DragItemsStarting;
        _files.DragItemsCompleted += File_DragItemsCompleted;
        _gridFiles.DragItemsCompleted += File_DragItemsCompleted;
        // Bind data rather than eagerly building a UI tree for every PROPFIND result.
        _files.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid ColumnSpacing="12" Padding="8,10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="3*"/><ColumnDefinition Width="1.6*"/><ColumnDefinition Width="1.1*"/><ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Grid ColumnSpacing="10"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                        <FontIcon Glyph="{Binding IconGlyph}" FontSize="20" />
                        <TextBlock Grid.Column="1" Text="{Binding Name}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Name}" />
                    </Grid>
                    <TextBlock Grid.Column="1" Text="{Binding ModifiedLabel}" Opacity="0.7" TextTrimming="CharacterEllipsis" />
                    <TextBlock Grid.Column="2" Text="{Binding TypeLabel}" Opacity="0.7" TextTrimming="CharacterEllipsis" />
                    <TextBlock Grid.Column="3" Text="{Binding FileSizeLabel}" Opacity="0.7" TextTrimming="CharacterEllipsis" />
                </Grid>
            </DataTemplate>
            """);
        _files.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="4,0"/>
            </Style>
            """);
        _gridFiles.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Width="180" Padding="12" Spacing="8">
                    <Grid Height="112" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
                        <FontIcon Glyph="{Binding IconGlyph}" FontSize="40" />
                        <Image x:Name="MediaThumbnail" Stretch="UniformToFill" />
                    </Grid>
                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Name}" />
                    <TextBlock Text="{Binding ModifiedLabel}" Opacity="0.7" FontSize="12" />
                    <TextBlock Text="{Binding TypeLabel}" Opacity="0.7" FontSize="12" TextTrimming="CharacterEllipsis" />
                    <TextBlock Text="{Binding FileSizeLabel}" Opacity="0.7" FontSize="12" />
                </StackPanel>
            </DataTemplate>
            """);
        _viewMode.SelectionChanged += (_, _) => ApplyView();
        _sort.SelectionChanged += (_, _) => ApplyView();
        _files.SelectionChanged += (_, _) => UpdateSelectionStatus();
        _gridFiles.SelectionChanged += (_, _) => UpdateSelectionStatus();
        _gridFiles.ContainerContentChanging += ThumbnailContainerChanging;
        Unloaded += (_, _) => { foreach (var request in _thumbnailRequests.Values.ToArray()) request.Cancel(); };
        AllowDrop = true;
        DragOver += (_, e) => HandleDragOver(e);
        Drop += (_, e) => HandleDrop(e);
        _path.Text = profile.DisplayName;
        _status.Text = "选择此标签后连接；点击文件可下载并用默认应用打开。";
    }

    private static ResourceDictionary CreateSelectionTheme(bool dark)
    {
        var resources = new ResourceDictionary();
        var border = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 183, 201, 224) : Windows.UI.Color.FromArgb(255, 77, 95, 117));
        var fill = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 140, 189, 255) : Windows.UI.Color.FromArgb(255, 18, 90, 181));
        var check = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 16, 32, 51) : Windows.UI.Color.FromArgb(255, 255, 255, 255));
        foreach (var key in new[] { "ListViewItemCheckBoxBorderBrush", "ListViewItemCheckBoxPointerOverBorderBrush", "ListViewItemCheckBoxPressedBorderBrush" }) resources[key] = border;
        foreach (var key in new[] { "ListViewItemCheckBoxSelectedBrush", "ListViewItemCheckBoxSelectedPointerOverBrush", "ListViewItemCheckBoxSelectedPressedBrush" }) resources[key] = fill;
        foreach (var key in new[] { "ListViewItemCheckBrush", "ListViewItemCheckPressedBrush" }) resources[key] = check;
        return resources;
    }

    private void ThumbnailContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (FindThumbnail(args.ItemContainer) is { } recycled)
            {
                CancelThumbnail(recycled);
                recycled.Source = null;
                recycled.Tag = null;
            }
            return;
        }
        if (args.Phase == 0) { args.RegisterUpdateCallback(ThumbnailContainerChanging); return; }
        if (args.Item is not WebDavEntry entry || FindThumbnail(args.ItemContainer) is not { } image) return;
        CancelThumbnail(image);
        image.Source = null;
        image.Tag = entry;
        ToolTipService.SetToolTip(image, null);
        image.Loaded -= ThumbnailImageLoaded;
        image.Loaded += ThumbnailImageLoaded;
        image.Unloaded -= ThumbnailImageUnloaded;
        image.Unloaded += ThumbnailImageUnloaded;
        if (image.IsLoaded) _ = LoadThumbnailAsync(image, entry);
    }

    private static Image? FindThumbnail(DependencyObject element)
    {
        if (element is Image { Name: "MediaThumbnail" } image) return image;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindThumbnail(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }

    private void ThumbnailImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image { Tag: WebDavEntry entry } image) _ = LoadThumbnailAsync(image, entry);
    }
    private void ThumbnailImageUnloaded(object sender, RoutedEventArgs e) { if (sender is Image image) CancelThumbnail(image); }
    private void CancelThumbnail(Image image) { if (_thumbnailRequests.TryGetValue(image, out var request)) request.Cancel(); }

    private async Task LoadThumbnailAsync(Image image, WebDavEntry entry)
    {
        if (_client == null || entry.IsDirectory || _busy || image.Source != null || _lifetime.IsCancellationRequested || !image.IsLoaded) return;
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        bool video = new[] { ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".webm", ".wmv" }.Contains(extension);
        bool picture = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".heic", ".heif", ".tif", ".tiff" }.Contains(extension);
        if (!video && !picture) return;
        long limit = (video ? 64L : 16L) * 1048576;
        if (entry.Size > limit)
        {
            ToolTipService.SetToolTip(image, "媒体较大，未自动下载缩略图；可点击文件预览。");
            return;
        }
        if (_thumbnailRequests.TryGetValue(image, out var previous))
        {
            if (!previous.IsCancellationRequested) return;
        }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(30));
        _thumbnailRequests[image] = request;
        bool acquired = false;
        try
        {
            await _thumbnailGate.WaitAsync(request.Token); acquired = true;
            request.Token.ThrowIfCancellationRequested();
            _thumbnailCache ??= new PreviewCache();
            var location = _thumbnailCache.GetLocation(new RemoteFileEntry
            {
                Name = SafeFileName(entry.Name), Uri = "webdav:" + _profile.Id + ":" + entry.Uri.AbsoluteUri,
                SizeBytes = entry.Size, ModifiedAt = entry.Modified?.ToUniversalTime().Ticks ?? 0
            });
            if (!_thumbnailCache.IsUsable(location, entry.Size))
            {
                _thumbnailCache.Prepare(location);
                await _client.DownloadThumbnailSourceAsync(entry, location.FilePath, limit, request.Token);
            }
            _thumbnailCache.Prune(location.Directory);
            request.Token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(location.FilePath);
            Windows.Storage.FileProperties.StorageItemThumbnail? thumbnail = null;
            try { thumbnail = await file.GetThumbnailAsync(video ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView, 256); }
            catch when (picture) { /* Missing Shell handler must not prevent direct image decoding. */ }
            using var thumbnailLifetime = thumbnail;
            var bitmap = new BitmapImage { DecodePixelWidth = 256 };
            if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                await bitmap.SetSourceAsync(thumbnail);
            else if (picture)
            {
                // Shell thumbnail handlers may be absent; decode supported images directly.
                using var original = await file.OpenReadAsync();
                await bitmap.SetSourceAsync(original);
            }
            else return;
            if (!request.IsCancellationRequested && ReferenceEquals(image.Tag, entry) && image.IsLoaded) image.Source = bitmap;
        }
        catch (Exception) { /* Unsupported codecs, cancellation and network failure keep the placeholder usable. */ }
        finally
        {
            if (acquired) _thumbnailGate.Release();
            if (_thumbnailRequests.TryGetValue(image, out var active) && active == request) _thumbnailRequests.Remove(image);
        }
    }

    private void ApplyView()
    {
        var selected = Selected().Select(entry => entry.Uri).ToHashSet();
        _updatingView = true;
        try
        {
            var entries = WebDavEntrySorting.Sort(_entries, (_sort.SelectedItem as ComboBoxItem)?.Tag as string ?? "name");
            _files.ItemsSource = _gridFiles.ItemsSource = null;
            bool grid = _viewMode.SelectedIndex == 1;
            _gridFiles.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            _files.Visibility = _fileHeader.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            CurrentFiles.ItemsSource = entries;
            if (_selectionMode) foreach (var entry in entries.Where(entry => selected.Contains(entry.Uri))) CurrentFiles.SelectedItems.Add(entry);
        }
        finally { _updatingView = false; }
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        if (_busy || _updatingView) return;
        _status.Text = _entries.Count == 0 ? "此目录为空。" : _selectionMode
            ? $"{_entries.Count} 项 · 已选 {CurrentFiles.SelectedItems.Count} 项；点击勾选，可全选、下载或删除。"
            : $"{_entries.Count} 项；点击文件打开，点击“选择”可多选。";
    }

    public async Task ActivateAsync()
    {
        if (_activated) return;
        if (_activationTask is { } pending) { await pending; return; }
        var task = ActivateCoreAsync();
        _activationTask = task;
        try { await task; }
        finally { if (_activationTask == task) _activationTask = null; }
    }

    private async Task ActivateCoreAsync()
    {
        try
        {
            _status.Text = "正在连接 WebDAV…";
            if (_client == null)
            {
                string password = await WebDavSettingsStore.UnprotectAsync(_profile.ProtectedPassword);
                _lifetime.Token.ThrowIfCancellationRequested();
                _client = new WebDavBrowserClient(_profile, password);
            }
            _activated = await NavigateAsync(_directory ?? _client.InitialDirectory);
        }
        catch (Exception exception) { _activated = false; _status.Text = "连接失败，可点击刷新重试：" + exception.Message; }
    }

    private void AddButton(string label, Func<Task> action, bool requiresClient = true)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            if (_busy || _actionPending || (requiresClient && (_client == null || _directory == null))) return;
            _actionPending = true;
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "操作已取消。"; }
            catch (Exception exception) { _status.Text = exception.Message; }
            finally { _actionPending = false; }
        };
        _actions.Children.Add(button);
    }

    private async Task<bool> NavigateAsync(Uri? directory)
    {
        if (_client == null) return false;
        foreach (var request in _thumbnailRequests.Values.ToArray()) request.Cancel();
        directory ??= _client.InitialDirectory;
        _listing?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(20));
        _listing = cancellation;
        int generation = ++_generation;
        _status.Text = "正在读取目录…";
        try
        {
            var entries = await Task.Run(() => _client.ListAsync(directory, cancellation.Token), cancellation.Token);
            if (generation != _generation) return false;
            _directory = directory;
            _entries = entries;
            _path.Text = _profile.DisplayName + " · /" + Uri.UnescapeDataString(_client.Root.MakeRelativeUri(directory).ToString());
            // SelectedItems is only writable in multiple/extended selection mode.
            if (_selectionMode) { _files.SelectedItems.Clear(); _gridFiles.SelectedItems.Clear(); }
            ApplyView();
            return true;
        }
        catch (OperationCanceledException) { if (generation == _generation) _status.Text = "目录读取已取消或超时，可点击刷新重试。"; }
        catch (Exception exception) { if (generation == _generation) _status.Text = "目录读取失败，可点击刷新重试：" + exception.Message; }
        finally { if (_listing == cancellation) _listing = null; cancellation.Dispose(); }
        return false;
    }

    private async void File_Click(object sender, ItemClickEventArgs e)
    {
        if (_busy || _actionPending || _selectionMode || e.ClickedItem is not WebDavEntry entry) return;
        _actionPending = true;
        try
        {
            if (entry.IsDirectory) await NavigateAsync(entry.Uri);
            else await OpenAsync(entry);
        }
        catch (OperationCanceledException) { _status.Text = "操作已取消。"; }
        catch (Exception exception) { _status.Text = "打开失败：" + exception.Message; }
        finally { _actionPending = false; }
    }

    private void File_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_busy || _client == null || e.Items.Count != 1 || e.Items[0] is not WebDavEntry { IsDirectory: false } entry)
        {
            e.Cancel = true;
            return;
        }

        _outgoingDragCancellation?.Cancel();
        _outgoingDragCancellation?.Dispose();
        _outgoingDragCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var dragToken = _outgoingDragCancellation.Token;
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.Properties.Title = entry.Name;
        e.Data.Properties[HingeDragMetadata.WebDavRemoteFile] = true;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, request => ProvideDraggedFileAsync(request, entry, dragToken));
        RemoteFileDragStarted?.Invoke(this, EventArgs.Empty);
    }

    private void File_DragItemsCompleted(object sender, Microsoft.UI.Xaml.Controls.DragItemsCompletedEventArgs e)
    {
        _outgoingDragCancellation?.Dispose();
        _outgoingDragCancellation = null;
        RemoteFileDragCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void CancelOutgoingDrag()
    {
        _outgoingDragCancellation?.Cancel();
        _status.Text = "已取消 WebDAV 文件拖出";
    }

    private async void ProvideDraggedFileAsync(DataProviderRequest request, WebDavEntry entry, CancellationToken dragToken)
    {
        var deferral = request.GetDeferral();
        try
        {
            dragToken.ThrowIfCancellationRequested();
            var cache = _thumbnailCache ??= new PreviewCache();
            var location = cache.GetLocation(new RemoteFileEntry
            {
                Name = SafeFileName(entry.Name),
                Uri = "webdav:" + _profile.Id + ":" + entry.Uri.AbsoluteUri,
                SizeBytes = entry.Size,
                ModifiedAt = entry.Modified?.ToUniversalTime().Ticks ?? 0
            });
            if (!cache.IsUsable(location, entry.Size))
            {
                cache.Prepare(location);
                await _client!.DownloadAsync(entry, location.FilePath, null, dragToken);
            }
            dragToken.ThrowIfCancellationRequested();
            cache.Prune(location.Directory);
            var storageFile = await StorageFile.GetFileFromPathAsync(location.FilePath);
            dragToken.ThrowIfCancellationRequested();
            request.SetData(new[] { storageFile });
        }
        catch (OperationCanceledException) when (dragToken.IsCancellationRequested)
        {
            request.SetData(Array.Empty<StorageFile>());
        }
        catch (Exception exception)
        {
            request.SetData(Array.Empty<StorageFile>());
            DispatcherQueue.TryEnqueue(() => _status.Text = "准备拖出文件失败：" + exception.Message);
        }
        finally { deferral.Complete(); }
    }

    private IReadOnlyList<WebDavEntry> Selected() => CurrentFiles.SelectedItems.OfType<WebDavEntry>().ToArray();

    private void InitializePicker(object picker) => InitializeWithWindow.Initialize(picker,
        WindowNative.GetWindowHandle(((App)Application.Current).MainWindow!));

    private async Task PickUploadsAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0) await UploadPathsAsync(files.Select(file => file.Path).ToArray());
    }

    public async Task UploadPathsAsync(IReadOnlyList<string> paths)
    {
        if (_busy || _client == null || _directory == null) return;
        if (!await ConfirmAsync("上传文件？", $"上传 {paths.Count} 个文件到“{_profile.DisplayName}”当前目录。同名文件会被覆盖。")) return;
        var client = _client;
        var directory = _directory;
        bool success = await RunTransferAsync(async token =>
        {
            long total = paths.Sum(path => new FileInfo(path).Length);
            var completed = new long[paths.Count];
            int parallel = client.ParallelTransfers ? 4 : 1;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), new ParallelOptions
            { MaxDegreeOfParallelism = parallel, CancellationToken = linked.Token }, async (index, ct) =>
            {
                try
                {
                    var progress = new CallbackProgress(value =>
                    {
                        Interlocked.Exchange(ref completed[index], value.Completed);
                        long sum = 0;
                        for (int i = 0; i < completed.Length; i++) sum += Interlocked.Read(ref completed[i]);
                        ReportProgress(new(sum, total));
                    });
                    await client.UploadAsync(directory, paths[index], progress, ct);
                }
                catch { linked.Cancel(); throw; }
            });
        });
        var status = _status.Text;
        await NavigateAsync(directory);
        if (!success) _status.Text = status;
    }

    private async Task DownloadSelectionAsync()
    {
        var entries = Selected().Where(entry => !entry.IsDirectory).ToArray();
        if (entries.Length == 0) { _status.Text = "请先选择文件；不支持直接下载整个文件夹。"; return; }
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
        var folder = await picker.PickSingleFolderAsync(); if (folder == null) return;
        if (!await ConfirmAsync("保存选中文件？", $"保存 {entries.Length} 个文件到所选文件夹；同名本机文件会被覆盖。")) return;
        await RunTransferAsync(async token =>
        {
            long total = entries.Sum(entry => entry.Size);
            long finished = 0;
            foreach (var entry in entries)
            {
                await _client!.DownloadAsync(entry, Path.Combine(folder.Path, SafeFileName(entry.Name)),
                    new CallbackProgress(value => ReportProgress(new(finished + value.Completed, total))), token);
                finished += entry.Size;
            }
        });
    }

    private static string SafeFileName(string name)
    {
        var result = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(result) || result is "." or "..") throw new IOException("文件名无法保存到 Windows。");
        return result;
    }

    private async Task OpenAsync(WebDavEntry entry)
    {
        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".lnk", ".scr", ".com", ".hta" }.Contains(extension))
        { _status.Text = "为避免执行远程程序，请先下载此文件，再自行检查并打开。"; return; }
        var cache = new PreviewCache();
        var location = cache.GetLocation(new RemoteFileEntry
        {
            Name = SafeFileName(entry.Name), Uri = "webdav:" + _profile.Id + ":" + entry.Uri.AbsoluteUri,
            SizeBytes = entry.Size, ModifiedAt = entry.Modified?.ToUniversalTime().Ticks ?? 0
        });
        cache.Prepare(location);
        string target = location.FilePath;
        bool downloaded = await RunTransferAsync(token => _client!.DownloadAsync(entry, target, new CallbackProgress(ReportProgress), token));
        if (downloaded) cache.Prune(location.Directory); else cache.Remove(location);
        if (!downloaded) return;
        if (!await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(target)))
            _status.Text = "文件已下载，但没有找到可打开它的默认应用。";
    }

    private async Task CreateDirectoryAsync()
    {
        var text = new TextBox { Header = "文件夹名称" };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "新建文件夹", Content = text,
            PrimaryButtonText = "创建", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await _client!.CreateDirectoryAsync(_directory!, text.Text.Trim(), _lifetime.Token);
        await NavigateAsync(_directory);
    }

    private async Task DeleteSelectionAsync()
    {
        var entries = Selected();
        if (entries.Count == 0) return;
        if (!await ConfirmAsync("删除服务器文件？", $"将删除 {entries.Count} 项。文件夹会连同内容一起删除，此操作不保证可恢复。")) return;
        bool success = await RunTransferAsync(async token =>
        { foreach (var entry in entries) await _client!.DeleteAsync(entry, token); });
        var status = _status.Text;
        await NavigateAsync(_directory);
        if (!success) _status.Text = status;
    }

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content,
            PrimaryButtonText = "确认", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> RunTransferAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return false;
        foreach (var request in _thumbnailRequests.Values.ToArray()) request.Cancel();
        _busy = true; _actions.IsHitTestVisible = _files.IsEnabled = _gridFiles.IsEnabled = false;
        _viewMode.IsEnabled = _sort.IsEnabled = false;
        foreach (var button in _actions.Children.OfType<Button>()) button.IsEnabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _transfer = cancellation; _cancel.Visibility = _progress.Visibility = Visibility.Visible;
        _lastCompleted = 0; _progress.Value = 0; _status.Text = "正在处理…";
        try { await action(cancellation.Token); _status.Text = "操作完成。"; return true; }
        catch (OperationCanceledException) { _status.Text = "操作已取消；已成功上传的文件不会自动删除。"; return false; }
        catch (Exception exception) { _status.Text = exception.Message; return false; }
        finally
        {
            _transfer = null; _busy = false; _actions.IsHitTestVisible = _files.IsEnabled = _gridFiles.IsEnabled = true;
            _viewMode.IsEnabled = _sort.IsEnabled = true;
            foreach (var button in _actions.Children.OfType<Button>()) button.IsEnabled = true;
            _cancel.Visibility = _progress.Visibility = Visibility.Collapsed;
            ResumeVisibleThumbnails(_gridFiles);
        }
    }

    private void ResumeVisibleThumbnails(DependencyObject root)
    {
        if (_gridFiles.Visibility != Visibility.Visible || _lifetime.IsCancellationRequested) return;
        if (root is Image { Tag: WebDavEntry entry } image) { _ = LoadThumbnailAsync(image, entry); return; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ResumeVisibleThumbnails(VisualTreeHelper.GetChild(root, i));
    }

    private void ReportProgress(WebDavTransferProgress value)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _progressTimestamp) < 100 && value.Completed != value.Total) return;
        Interlocked.Exchange(ref _progressTimestamp, now);
        var transfer = _transfer;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_busy || transfer != _transfer) return;
            _lastCompleted = Math.Max(_lastCompleted, value.Completed);
            _progress.IsIndeterminate = value.Total == 0;
            if (value.Total > 0) _progress.Value = Math.Min(100, 100d * _lastCompleted / value.Total);
            _status.Text = $"正在传输 · {_lastCompleted / 1048576d:F1} MB / {value.Total / 1048576d:F1} MB";
        });
    }

    public void HandleDragOver(DragEventArgs e)
    {
        e.Handled = true;
        e.AcceptedOperation = !_busy && _client != null && _directory != null &&
            e.DataView.Contains(StandardDataFormats.StorageItems) && !HingeDragMetadata.IsWebDavRemoteFileDrag(e.DataView)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = HingeDragMetadata.IsInternalRemoteFileDrag(e.DataView)
            ? "上传手机文件到当前 WebDAV 目录"
            : "上传到当前 WebDAV 目录";
    }
    public async void HandleDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (_busy || !e.DataView.Contains(StandardDataFormats.StorageItems) || HingeDragMetadata.IsWebDavRemoteFileDrag(e.DataView)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(file => file.Path).ToArray();
            if (paths.Length > 0) await UploadPathsAsync(paths);
            else _status.Text = "请选择文件；暂不支持上传整个文件夹。";
        }
        catch (Exception exception) { _status.Text = exception.Message; }
        finally { deferral.Complete(); }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _listing?.Cancel(); _transfer?.Cancel();
        _outgoingDragCancellation?.Cancel(); _outgoingDragCancellation?.Dispose();
        _client?.Dispose();
    }
    private sealed class CallbackProgress(Action<WebDavTransferProgress> action) : IProgress<WebDavTransferProgress>
    { public void Report(WebDavTransferProgress value) => action(value); }
}
