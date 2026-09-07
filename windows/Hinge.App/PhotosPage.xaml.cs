using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Hinge.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Hinge.App;

public sealed partial class PhotosPage : Page
{
    private const int InitialThumbnailBudget = 48;
    private const int UiBatchSize = 40;
    private const int InitialPhotoBatchSize = 200;
    private const int AdditionalPhotoBatchSize = 200;
    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private Func<RemotePhotoItem, Task>? _openPhoto;
    private SessionConnection? _connection;
    private RemotePhotoAlbum? _currentAlbum;
    private bool _timelineMode;
    private readonly SemaphoreSlim _thumbnailGate = new(4, 4);
    private readonly HashSet<ScrollViewer> _photoScrollViewers = new();
    private readonly List<RemotePhotoAlbum> _albums = new();
    private readonly List<RemotePhotoItem> _loadedPhotos = new();
    private int _loadVersion;
    private int _photoLoadGeneration;
    private int _photoOffset;
    private int _photoTotal;
    private int _loadedPhotoCount;
    private bool _photoBatchLoading;
    private CancellationTokenSource? _photoLoadingCancellation;
    private GridViewItem? _dropTargetAlbumItem;
    private bool _dropFeedbackVisible;

    public event EventHandler<ComputerFilesDroppedEventArgs>? FilesDropped;
    public event EventHandler? InternalRemoteDragStarted;

    public PhotosPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        PhotosGrid.Loaded += (_, _) => AttachPhotoScrollViewer();
        Loaded += (_, _) => AttachPhotoScrollViewer();
    }

    public void HandleExternalDragOver(DragEventArgs e) =>
        RootGrid_DragOver(this, e);

    public void HandleExternalDragLeave(DragEventArgs e) =>
        RootGrid_DragLeave(this, e);

    public void HandleExternalDrop(DragEventArgs e) =>
        RootGrid_Drop(this, e);

    public void Configure(
        WorkspaceRemoteClient client,
        Func<SessionConnection?> connectionProvider,
        Func<RemotePhotoItem, Task>? openPhoto = null)
    {
        _client = client;
        _connectionProvider = connectionProvider;
        _openPhoto = openPhoto;
        PhotoViewModeOptions.SelectionChanged += PhotoViewMode_SelectionChanged;
    }

    private void AttachPhotoScrollViewer()
    {
        var scrollViewer = FindScrollViewer(PhotosGrid);
        if (scrollViewer == null || !_photoScrollViewers.Add(scrollViewer)) return;
        scrollViewer.ViewChanged += PhotoScrollViewer_ViewChanged;
    }

    private void PhotoScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            PhotosGrid.Visibility != Visibility.Visible)
        {
            return;
        }

        var threshold = Math.Max(480, scrollViewer.ViewportHeight * 1.5);
        var nearEnd = scrollViewer.ScrollableHeight <= 0 ||
            scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - threshold;
        if (!nearEnd) return;
        _ = LoadNextPhotoBatchAsync();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scrollViewer) return scrollViewer;
            var nested = FindScrollViewer(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void CancelPhotoLoading()
    {
        Interlocked.Increment(ref _photoLoadGeneration);
        _photoLoadingCancellation?.Cancel();
        _photoLoadingCancellation?.Dispose();
        _photoLoadingCancellation = null;
        _photoBatchLoading = false;
    }

    public Task LoadAsync(SessionConnection? connection)
    {
        _connection = connection;
        _currentAlbum = null;
        _timelineMode = false;
        return LoadAlbumsAsync();
    }

    private async Task LoadAlbumsAsync()
    {
        int version = ++_loadVersion;
        CancelPhotoLoading();
        _currentAlbum = null;
        _timelineMode = false;
        if (PhotoViewModeOptions.SelectedIndex != 0) PhotoViewModeOptions.SelectedIndex = 0;
        AlbumsGrid.Items.Clear();
        PhotosGrid.Items.Clear();
        _albums.Clear();
        _loadedPhotos.Clear();
        AlbumsGrid.Visibility = Visibility.Visible;
        PhotosGrid.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        PageHeading.Text = "相册集";
        PageDescription.Text = "先显示相册集，封面使用该相册最新一张图片；进入相册后再读取图片缩略图。";

        var connection = _connectionProvider?.Invoke() ?? _connection;
        _connection = connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            return;
        }

        SetStatus("正在读取相册集", "只读取相册列表和最新照片的缩略图…", InfoBarSeverity.Informational);
        try
        {
            var albums = await _client.LoadPhotoAlbumsAsync(connection);
            if (version != _loadVersion) return;
            _albums.AddRange(albums);
            RenderAlbumTiles(version);

            if (_albums.Count == 0)
            {
                AlbumsGrid.Items.Add(new GridViewItem
                {
                    IsHitTestVisible = false,
                    Content = new TextBlock
                    {
                        Text = "手机相册中没有可读取的相册集。",
                        Padding = new Thickness(16, 24, 16, 24),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                });
            }
            SetStatus("相册集已更新", $"共 {_albums.Count} 个相册集。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetStatus("读取相册集失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void AlbumsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        var album = e.ClickedItem switch
        {
            GridViewItem { Tag: RemotePhotoAlbum item } => item,
            RemotePhotoAlbum item => item,
            _ => null
        };
        if (album != null)
        {
            await LoadAlbumAsync(album);
        }
    }

    private async void AlbumItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is GridViewItem { Tag: RemotePhotoAlbum album })
        {
            e.Handled = true;
            await LoadAlbumAsync(album);
        }
    }

    private void PhotoViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PhotoViewModeOptions.SelectedIndex == 1)
        {
            _ = LoadTimelineAsync();
        }
        else if (_currentAlbum == null)
        {
            _ = LoadAlbumsAsync();
        }
    }

    private async void PhotoSortOptions_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_currentAlbum != null || _timelineMode)
        {
            if (_loadedPhotos.Count > 0)
            {
                await RenderPhotoTilesAsync(_loadVersion);
            }
        }
        else if (_albums.Count > 0)
        {
            RenderAlbumTiles(_loadVersion);
        }
    }

    private void RenderAlbumTiles(int version)
    {
        AlbumsGrid.Items.Clear();
        foreach (var album in SortAlbums(_albums))
        {
            var (tile, image) = BuildAlbumTile(album);
            var item = new GridViewItem { Tag = album, Content = tile };
            // Keep double-click as an explicit desktop affordance. The
            // single-click handler remains for touch and pen input.
            item.DoubleTapped += AlbumItem_DoubleTapped;
            ConfigureAlbumDrop(item, album);
            AlbumsGrid.Items.Add(item);
            _ = LoadIntoImageAsync(image, album.CoverUri, thumbnail: true, version);
        }
    }

    private string SelectedPhotoSort() =>
        (PhotoSortOptions.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "timeDesc";

    private IEnumerable<RemotePhotoAlbum> SortAlbums(IEnumerable<RemotePhotoAlbum> albums)
    {
        return SelectedPhotoSort() switch
        {
            "name" => albums.OrderBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
            "type" => albums.OrderBy(_ => "图片", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
            "sizeAsc" => albums.OrderBy(album => album.TotalSizeBytes)
                .ThenBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
            "sizeDesc" => albums.OrderByDescending(album => album.TotalSizeBytes)
                .ThenBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
            "timeAsc" => albums.OrderBy(album => album.LatestTakenAt)
                .ThenBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => albums.OrderByDescending(album => album.LatestTakenAt)
                .ThenBy(album => album.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }

    private IEnumerable<RemotePhotoItem> SortPhotos(IEnumerable<RemotePhotoItem> photos)
    {
        return SelectedPhotoSort() switch
        {
            "name" => photos.OrderBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            "type" => photos.OrderBy(_ => "图片", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            "sizeAsc" => photos.OrderBy(photo => photo.SizeBytes)
                .ThenBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            "sizeDesc" => photos.OrderByDescending(photo => photo.SizeBytes)
                .ThenBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            "timeAsc" => photos.OrderBy(photo => photo.TakenAt)
                .ThenBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => photos.OrderByDescending(photo => photo.TakenAt)
                .ThenBy(photo => photo.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }

    private async Task LoadTimelineAsync()
    {
        var connection = _connectionProvider?.Invoke() ?? _connection;
        _connection = connection;
        _timelineMode = true;
        _currentAlbum = null;
        int version = ++_loadVersion;
        AlbumsGrid.Items.Clear();
        PhotosGrid.Items.Clear();
        _albums.Clear();
        _loadedPhotos.Clear();
        AlbumsGrid.Visibility = Visibility.Collapsed;
        PhotosGrid.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        PageHeading.Text = "时光轴";
        PageDescription.Text = "按拍摄时间倒序查看手机中的全部图片。";

        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            return;
        }

        SetStatus("正在读取时光轴", "先加载前 200 张，继续滚动会自动读取后续图片…", InfoBarSeverity.Informational);
        await StartPhotoListingAsync(
            null,
            version,
            "时光轴",
            "手机相册中没有可读取的图片。",
            "时光轴已更新");
    }

    private async Task LoadAlbumAsync(RemotePhotoAlbum album)
    {
        var connection = _connectionProvider?.Invoke() ?? _connection;
        _connection = connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法打开相册", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }

        int version = ++_loadVersion;
        _currentAlbum = album;
        AlbumsGrid.Visibility = Visibility.Collapsed;
        PhotosGrid.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        PageHeading.Text = album.Name;
        PageDescription.Text = $"{album.Count} 张图片 · 只在进入相册后加载缩略图";
        PhotosGrid.Items.Clear();
        SetStatus("正在读取图片", "先加载前 200 张，继续滚动会自动读取后续图片…", InfoBarSeverity.Informational);
        await StartPhotoListingAsync(
            album.Id,
            version,
            "相册",
            "这个相册暂时没有可读取的图片。",
            "相册已更新");
    }

    private async Task StartPhotoListingAsync(
        string? albumId,
        int version,
        string title,
        string emptyMessage,
        string completedTitle)
    {
        var connection = _connectionProvider?.Invoke() ?? _connection;
        _connection = connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法读取图片", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }

        CancelPhotoLoading();
        var generation = _photoLoadGeneration;
        var cancellation = new CancellationTokenSource();
        _photoLoadingCancellation = cancellation;
        _photoOffset = 0;
        _photoTotal = 0;
        _loadedPhotoCount = 0;
        _photoBatchLoading = false;
        _loadedPhotos.Clear();

        try
        {
            var page = await _client.LoadPhotoPageAsync(
                connection,
                albumId,
                offset: 0,
                limit: InitialPhotoBatchSize);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _photoLoadGeneration || version != _loadVersion) return;

            _photoTotal = page.Total;
            _photoOffset = page.Items.Count;
            _loadedPhotos.AddRange(page.Items);
            await RenderPhotoTilesAsync(version);
            _loadedPhotoCount = page.Items.Count;
            if (page.Total == 0)
            {
                PhotosGrid.Items.Add(new GridViewItem
                {
                    IsHitTestVisible = false,
                    Content = new TextBlock
                    {
                        Text = emptyMessage,
                        Padding = new Thickness(16, 24, 16, 24),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                });
            }

            UpdatePhotoStatus(title, completedTitle);
            AttachPhotoScrollViewer();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer album/timeline request owns the page now.
        }
        catch (Exception exception)
        {
            if (generation != _photoLoadGeneration || version != _loadVersion) return;
            SetStatus("读取相册失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async Task LoadNextPhotoBatchAsync()
    {
        if (_photoBatchLoading ||
            (_currentAlbum == null && !_timelineMode) ||
            _photoOffset >= _photoTotal ||
            _client == null)
        {
            return;
        }

        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (connection?.State != SessionState.Connected) return;

        var cancellation = _photoLoadingCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested) return;
        var generation = _photoLoadGeneration;
        var version = _loadVersion;
        _photoBatchLoading = true;
        try
        {
            var page = await _client.LoadPhotoPageAsync(
                connection,
                _currentAlbum?.Id,
                _photoOffset,
                AdditionalPhotoBatchSize);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _photoLoadGeneration || version != _loadVersion) return;

            if (page.Items.Count == 0)
            {
                _photoOffset = _photoTotal;
                return;
            }

            _photoTotal = Math.Max(_photoTotal, page.Total);
            _photoOffset += page.Items.Count;
            _loadedPhotos.AddRange(page.Items);
            await RenderPhotoTilesAsync(version);
            _loadedPhotoCount += page.Items.Count;
            UpdatePhotoStatus(
                _currentAlbum == null ? "时光轴" : "相册",
                _currentAlbum == null ? "时光轴已更新" : "相册已更新");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The current listing was replaced.
        }
        catch (Exception exception)
        {
            if (generation == _photoLoadGeneration && version == _loadVersion)
            {
                SetStatus("继续读取相册失败", exception.Message, InfoBarSeverity.Error);
            }
        }
        finally
        {
            _photoBatchLoading = false;
        }
    }

    private void UpdatePhotoStatus(string title, string completedTitle)
    {
        var suffix = _photoOffset < _photoTotal
            ? $"共 {_photoTotal} 张图片 · 已加载 {_loadedPhotoCount} 张，继续滚动加载"
            : $"共 {_photoTotal} 张图片 · 已全部加载";
        SetStatus(
            completedTitle,
            suffix,
            _photoOffset < _photoTotal
                ? InfoBarSeverity.Informational
                : InfoBarSeverity.Success);
        PageDescription.Text = $"{suffix}。";
    }

    private async void PhotosGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GridViewItem { Tag: RemotePhotoItem photo }) return;
        await ShowPhotoPreviewAsync(photo);
    }

    private async void PhotoItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is GridViewItem { Tag: RemotePhotoItem photo })
        {
            e.Handled = true;
            await ShowPhotoPreviewAsync(photo);
        }
    }

    private async Task ShowPhotoPreviewAsync(RemotePhotoItem photo)
    {
        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法预览图片", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }

        if (_openPhoto != null)
        {
            try
            {
                await _openPhoto(photo);
            }
            catch (Exception exception)
            {
                SetStatus("图片打开失败", exception.Message, InfoBarSeverity.Error);
            }
            return;
        }

        SetStatus("正在打开图片", "正在读取预览图…", InfoBarSeverity.Informational);
        byte[]? bytes;
        try
        {
            bytes = await _client.LoadPhotoPreviewBytesAsync(connection, photo.Uri);
        }
        catch (Exception exception)
        {
            SetStatus("预览失败", exception.Message, InfoBarSeverity.Error);
            return;
        }
        if (bytes == null || bytes.Length == 0)
        {
            SetStatus("预览失败", "手机没有返回可显示的图片数据。", InfoBarSeverity.Error);
            return;
        }

        var image = new Image
        {
            MaxWidth = 760,
            MaxHeight = 520,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        await SetImageSourceAsync(image, bytes);
        var details = $"{(photo.Width > 0 ? $"{photo.Width} × {photo.Height} · " : string.Empty)}{(photo.SizeBytes > 0 ? $"{FormatBytes(photo.SizeBytes)} · " : string.Empty)}{FormatTime(photo.TakenAt)}\n{photo.Uri}";
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(image);
        panel.Children.Add(new TextBlock
        {
            Text = details,
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        // This fallback is kept for a page opened without MainWindow's
        // default-app callback (for example, in a design preview).
        var dialog = new ContentDialog
        {
            Title = photo.Name,
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void ConfigureAlbumDrop(GridViewItem item, RemotePhotoAlbum album)
    {
        item.AllowDrop = true;
        item.DragOver += (_, args) =>
        {
            if (!args.DataView.Contains(StandardDataFormats.StorageItems))
            {
                args.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            if (HingeDragMetadata.IsInternalRemoteFileDrag(args.DataView))
            {
                args.AcceptedOperation = DataPackageOperation.Copy;
                args.DragUIOverride.Caption = "松开以取消发送";
                args.DragUIOverride.IsGlyphVisible = true;
                args.Handled = true;
                return;
            }

            SetAlbumDropTarget(item, album);
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.DragUIOverride.Caption = $"发送到相册：{album.Name}";
            args.DragUIOverride.IsGlyphVisible = true;
            args.Handled = true;
        };
        item.DragLeave += (_, _) => ClearAlbumDropTarget(item);
        item.Drop += async (_, args) =>
        {
            ClearAlbumDropTarget(item);
            if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;

            if (HingeDragMetadata.IsInternalRemoteFileDrag(args.DataView))
            {
                args.AcceptedOperation = DataPackageOperation.Copy;
                args.Handled = true;
                return;
            }

            var paths = await GetDroppedFilePathsAsync(args.DataView);
            if (paths.Count == 0) return;
            FilesDropped?.Invoke(
                this,
                new ComputerFilesDroppedEventArgs(paths, AlbumDestinationPath(album)));
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.Handled = true;
        };
    }

    public string ResolveExternalDropDestination(Windows.Foundation.Point pagePoint)
    {
        if (_currentAlbum != null)
        {
            return AlbumDestinationPath(_currentAlbum);
        }

        if (AlbumsGrid.Visibility == Visibility.Visible)
        {
            // WM_DROPFILES gives us a client-pixel point, while WinUI hit
            // testing uses DIPs and the album GridView has its own visual
            // coordinate space. Check both the page and RootGrid spaces and
            // compare against the actual realized GridViewItem bounds. This
            // keeps Explorer drops working when EnableLUA remains disabled.
            var rootPoints = new List<Windows.Foundation.Point> { pagePoint };
            try
            {
                rootPoints.Add(TransformToRootPoint(pagePoint));
            }
            catch
            {
                // The page can be between navigation states; the first point
                // is still useful for the normal loaded path.
            }

            foreach (var rootPoint in rootPoints)
            {
                var destination = FindAlbumDestinationAtRootPoint(rootPoint);
                if (destination != null) return destination;
            }
        }

        return "Pictures";
    }

    public void ShowExternalDropFeedback(string destination)
    {
        var label = string.IsNullOrWhiteSpace(destination)
            ? "相册"
            : destination.Replace("Pictures/", "相册：", StringComparison.OrdinalIgnoreCase);
        ShowDropFeedback("已识别投放位置，正在发送", label);
        _ = HideDropFeedbackLaterAsync();
    }

    public bool UpdateExternalDragPreview(Point pagePoint)
    {
        if (_currentAlbum != null)
        {
            ShowDropFeedback("释放以发送到当前相册", _currentAlbum.Name);
            return true;
        }

        if (AlbumsGrid.Visibility != Visibility.Visible) return false;

        try
        {
            var target = FindAlbumAtRootPoint(TransformToRootPoint(pagePoint));
            if (target == null) return false;

            SetAlbumDropTarget(target.Value.Item, target.Value.Album);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void ClearExternalDragPreview()
    {
        ClearAlbumDropTarget();
        HideDropFeedback();
    }

    private Windows.Foundation.Point TransformToRootPoint(Windows.Foundation.Point pagePoint) =>
        TransformToVisual(RootGrid).TransformPoint(pagePoint);

    private string? FindAlbumDestinationAtRootPoint(Windows.Foundation.Point rootPoint)
    {
        for (var index = 0; index < AlbumsGrid.Items.Count; index++)
        {
            if (AlbumsGrid.ContainerFromIndex(index) is not GridViewItem item ||
                item.Tag is not RemotePhotoAlbum album ||
                item.ActualWidth <= 0 || item.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = item.TransformToVisual(RootGrid).TransformPoint(
                    new Windows.Foundation.Point(0, 0));
                var bounds = new Windows.Foundation.Rect(
                    topLeft,
                    new Windows.Foundation.Size(item.ActualWidth, item.ActualHeight));
                if (bounds.Contains(rootPoint))
                {
                    return AlbumDestinationPath(album);
                }
            }
            catch
            {
                // Ignore a container that is being recycled during layout.
            }
        }

        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, RootGrid))
        {
            if (element is GridViewItem { Tag: RemotePhotoAlbum album })
            {
                return AlbumDestinationPath(album);
            }
        }

        return null;
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            ClearAlbumDropTarget();
            return;
        }

        if (HingeDragMetadata.IsInternalRemoteFileDrag(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "松开以取消发送";
            e.DragUIOverride.IsGlyphVisible = true;
            e.Handled = true;
            return;
        }

        if (_currentAlbum == null && AlbumsGrid.Visibility == Visibility.Visible)
        {
            if (UpdateExternalDragPreview(e.GetPosition(this)))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "发送到当前相册";
                e.DragUIOverride.IsGlyphVisible = true;
                e.Handled = true;
                return;
            }

            ClearAlbumDropTarget();
            HideDropFeedback();
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (_currentAlbum == null)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"发送到相册：{_currentAlbum.Name}";
        e.DragUIOverride.IsGlyphVisible = true;
        ShowDropFeedback("释放以发送到当前相册", _currentAlbum.Name);
        e.Handled = true;
    }

    private void RootGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearAlbumDropTarget();
        HideDropFeedback();
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        var album = _currentAlbum;
        ClearAlbumDropTarget();
        HideDropFeedback();
        if (album == null || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        if (HingeDragMetadata.IsInternalRemoteFileDrag(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
            return;
        }

        var paths = await GetDroppedFilePathsAsync(e.DataView);
        if (paths.Count == 0) return;
        FilesDropped?.Invoke(
            this,
            new ComputerFilesDroppedEventArgs(paths, AlbumDestinationPath(album)));
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private void SetAlbumDropTarget(GridViewItem item, RemotePhotoAlbum album)
    {
        if (_dropTargetAlbumItem != item)
        {
            if (_dropTargetAlbumItem != null) _dropTargetAlbumItem.Opacity = 1;
            _dropTargetAlbumItem = item;
        }

        item.Opacity = 0.72;
        ShowDropFeedback("释放以发送到相册", $"{album.Name} · {AlbumDestinationPath(album)}");
    }

    private (GridViewItem Item, RemotePhotoAlbum Album)? FindAlbumAtRootPoint(
        Windows.Foundation.Point rootPoint)
    {
        for (var index = 0; index < AlbumsGrid.Items.Count; index++)
        {
            if (AlbumsGrid.ContainerFromIndex(index) is not GridViewItem item ||
                item.Tag is not RemotePhotoAlbum album ||
                item.ActualWidth <= 0 || item.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = item.TransformToVisual(RootGrid).TransformPoint(
                    new Windows.Foundation.Point(0, 0));
                var bounds = new Windows.Foundation.Rect(
                    topLeft,
                    new Windows.Foundation.Size(item.ActualWidth, item.ActualHeight));
                if (bounds.Contains(rootPoint)) return (item, album);
            }
            catch
            {
                // Ignore a container that is being recycled during layout.
            }
        }

        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, RootGrid))
        {
            if (element is GridViewItem { Tag: RemotePhotoAlbum album } item)
            {
                return (item, album);
            }
        }

        return null;
    }

    private void ClearAlbumDropTarget(GridViewItem? item = null)
    {
        if (item != null && _dropTargetAlbumItem != item) return;
        if (_dropTargetAlbumItem != null) _dropTargetAlbumItem.Opacity = 1;
        _dropTargetAlbumItem = null;
    }

    private static async Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(DataPackageView dataView)
    {
        var items = await dataView.GetStorageItemsAsync();
        return items
            .OfType<StorageFile>()
            .Select(file => file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string AlbumDestinationPath(RemotePhotoAlbum album)
    {
        var name = album.Name.Trim();
        if (name.Equals("微信", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("WeiXin", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("WeChat", StringComparison.OrdinalIgnoreCase))
        {
            return "Pictures/WeiXin";
        }

        if (name.Equals("QQ", StringComparison.OrdinalIgnoreCase))
        {
            return "Pictures/QQ";
        }

        var relativePath = album.RelativePath
            .Replace('\\', '/')
            .Trim('/');
        if (relativePath.Length > 0 &&
            !relativePath.Split('/').Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            return relativePath;
        }

        var safeName = string.Concat(name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(safeName)
            ? "Pictures"
            : $"Pictures/{safeName}";
    }

    private void ShowDropFeedback(string title, string subtitle)
    {
        DropHintTitle.Text = title;
        DropHintSubtitle.Text = subtitle;
        DropHintOverlay.Visibility = Visibility.Visible;
        if (_dropFeedbackVisible) return;
        _dropFeedbackVisible = true;
        DropHintOverlay.Opacity = 0;
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            To = 0.96,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };
        Storyboard.SetTarget(fade, DropHintOverlay);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    private void HideDropFeedback()
    {
        if (!_dropFeedbackVisible && DropHintOverlay.Visibility == Visibility.Collapsed) return;
        _dropFeedbackVisible = false;
        DropHintOverlay.Opacity = 0;
        DropHintOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task HideDropFeedbackLaterAsync()
    {
        await Task.Delay(1400);
        DispatcherQueue.TryEnqueue(HideDropFeedback);
    }

    private void ConfigurePhotoDrag(GridViewItem item, RemotePhotoItem photo)
    {
        item.CanDrag = true;
        item.DragStarting += (_, args) =>
        {
            var connection = _connectionProvider?.Invoke() ?? _connection;
            if (_client == null || connection?.State != SessionState.Connected ||
                string.IsNullOrWhiteSpace(photo.Uri))
            {
                args.Cancel = true;
                return;
            }

            try
            {
                InternalRemoteDragStarted?.Invoke(this, EventArgs.Empty);
                args.Data.Properties[HingeDragMetadata.InternalRemoteFile] = true;
                args.Data.RequestedOperation = DataPackageOperation.Copy;
                args.Data.Properties.Title = photo.Name;
                args.Data.SetDataProvider(
                    StandardDataFormats.StorageItems,
                    request => ProvidePhotoStorageItemsAsync(
                        request,
                        connection,
                        photo.Uri,
                        photo.Name));
            }
            catch
            {
                args.Cancel = true;
            }
        };
    }

    private async void ProvidePhotoStorageItemsAsync(
        DataProviderRequest request,
        SessionConnection connection,
        string uri,
        string displayName)
    {
        var deferral = request.GetDeferral();
        try
        {
            var bytes = await _client!.LoadPhotoBytesAsync(connection, uri);
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
        catch
        {
            request.SetData(Array.Empty<StorageFile>());
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _ = LoadAlbumsAsync();

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAlbum is { } album) _ = LoadAlbumAsync(album);
        else if (_timelineMode) _ = LoadTimelineAsync();
        else _ = LoadAlbumsAsync();
    }

    private (Grid Tile, Image Image) BuildAlbumTile(RemotePhotoAlbum album)
    {
        var image = new Image { Height = 170, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        var tile = new Grid { Width = 230, MinHeight = 230 };
        tile.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        tile.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tile.Children.Add(new Border { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"], Child = image });
        var details = new StackPanel { Spacing = 3, Padding = new Thickness(8, 8, 8, 8) };
        details.Children.Add(new TextBlock { Text = album.Name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var albumSummary = $"{album.Count} 张图片";
        if (album.TotalSizeBytes > 0) albumSummary += $" · {FormatBytes(album.TotalSizeBytes)}";
        details.Children.Add(new TextBlock { Text = albumSummary, FontSize = 14, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        Grid.SetRow(details, 1);
        tile.Children.Add(details);
        return (tile, image);
    }

    private (Grid Tile, Image Image) BuildPhotoTile(RemotePhotoItem photo)
    {
        var image = new Image { Width = 190, Height = 150, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        var tile = new StackPanel { Width = 190, Spacing = 6 };
        tile.Children.Add(new Border { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"], Child = image });
        tile.Children.Add(new TextBlock { Text = photo.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        return (new Grid { Children = { tile } }, image);
    }

    private async Task RenderPhotoTilesAsync(int version)
    {
        PhotosGrid.Items.Clear();
        await AddPhotoTilesAsync(SortPhotos(_loadedPhotos).ToList(), version);
    }

    private async Task AddPhotoTilesAsync(
        IReadOnlyList<RemotePhotoItem> photos,
        int version)
    {
        int thumbnailCount = 0;
        int itemCount = 0;
        foreach (var photo in photos)
        {
            if (version != _loadVersion) return;

            var (tile, image) = BuildPhotoTile(photo);
            var item = new GridViewItem { Tag = photo, Content = tile };
            item.DoubleTapped += PhotoItem_DoubleTapped;
            ConfigurePhotoDrag(item, photo);
            PhotosGrid.Items.Add(item);
            if (thumbnailCount++ < InitialThumbnailBudget)
            {
                _ = LoadIntoImageAsync(image, photo.Uri, thumbnail: true, version);
            }

            // Yield between small UI batches. This lets WinUI measure/render the
            // virtualized GridView while a large album is still being appended,
            // instead of blocking the dispatcher for the entire album.
            if (++itemCount % UiBatchSize == 0)
            {
                await Task.Yield();
            }
        }
    }

    private async Task LoadIntoImageAsync(Image image, string uri, bool thumbnail, int version)
    {
        if (_client == null || version != _loadVersion) return;
        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (connection?.State != SessionState.Connected || string.IsNullOrWhiteSpace(uri)) return;
        await _thumbnailGate.WaitAsync();
        try
        {
            if (version != _loadVersion) return;
            var bytes = await _client.LoadPhotoThumbnailBytesAsync(connection, uri);
            if (version != _loadVersion || bytes == null) return;
            await SetImageSourceAsync(image, bytes);
        }
        catch
        {
            // A missing thumbnail leaves the tile usable; the full preview reports its own error.
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    private static async Task SetImageSourceAsync(Image image, byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        image.Source = bitmap;
    }

    private void SetStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0) return "时间未知";
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("yyyy/M/d HH:mm");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / 1024d / 1024d:0.0} MB";
        return $"{bytes / 1024d / 1024d / 1024d:0.0} GB";
    }
}
