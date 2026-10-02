using Hinge.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using System.Collections.ObjectModel;

namespace Hinge.App;

public sealed partial class FileManagementPage : Page
{
    private readonly ObservableCollection<TabViewItem> _sourceTabs = new();
    private readonly Dictionary<string, (WebDavProfile Profile, WebDavBrowserView View)> _webDavViews = new();
    private string _phoneName = "手机（未连接）";
    private bool _rebuildingTabs;
    private WebDavBrowserView? _activeWebDavRemoteDrag;
    public bool IsSourceTabDragActive { get; private set; }
    public event EventHandler? SourceTabDragStarted;
    public event EventHandler? WebDavRemoteDragStarted;
    public bool IsPhoneSelected => (SourceTabs.SelectedItem as TabViewItem)?.Tag as string is null or "phone";
    public event EventHandler? PhoneSelected;
    private WebDavBrowserView? SelectedWebDav => WebDavHost.Content as WebDavBrowserView;

    public void CancelWebDavRemoteDrag() => _activeWebDavRemoteDrag?.CancelOutgoingDrag();

    public void SetPhoneName(string? name)
    {
        _phoneName = string.IsNullOrWhiteSpace(name) ? "手机（未连接）" : name;
        var tab = _sourceTabs.FirstOrDefault(item => (string)item.Tag == "phone");
        if (tab != null) tab.Header = CreateCenteredTabHeader(_phoneName);
    }

    public async Task UploadNativeDropAsync(IReadOnlyList<string> paths)
    {
        try
        {
            if (SelectedWebDav is { } browser) await browser.UploadPathsAsync(paths);
        }
        catch (Exception exception) { StatusText.Text = "WebDAV 上传失败：" + exception.Message; }
    }

    private void WebDavSettingsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshSourceTabs);

    private void RefreshSourceTabs()
    {
        try
        {
            var state = WebDavSettingsStore.Load();
            var selected = (SourceTabs.SelectedItem as TabViewItem)?.Tag as string ?? "phone";
            foreach (var id in _webDavViews.Keys.ToArray())
            {
                if (state.Profiles.FirstOrDefault(profile => profile.Id == id) != _webDavViews[id].Profile)
                {
                    _webDavViews[id].View.Dispose();
                    _webDavViews.Remove(id);
                }
            }
            _rebuildingTabs = true;
            var tabs = new List<TabViewItem>
            { new() { Header = CreateCenteredTabHeader(_phoneName), Tag = "phone", IsClosable = false } };
            tabs.AddRange(state.Profiles.Select(profile => new TabViewItem
            { Header = CreateCenteredTabHeader(profile.DisplayName), Tag = profile.Id, IsClosable = false }));
            foreach (var tab in tabs)
            {
                tab.MinWidth = 160;
            }
            tabs = tabs.OrderBy(tab =>
            {
                int index = state.TabOrder.IndexOf((string)tab.Tag);
                return index >= 0 ? index : state.TabOrder.Count + tabs.IndexOf(tab);
            }).ToList();
            _sourceTabs.Clear();
            foreach (var tab in tabs) _sourceTabs.Add(tab);
            SourceTabs.SelectedItem = tabs.FirstOrDefault(tab => (string)tab.Tag == selected) ?? tabs[0];
            _rebuildingTabs = false;
            SelectSource();
        }
        catch (Exception exception) { _rebuildingTabs = false; StatusText.Text = "WebDAV 配置读取失败：" + exception.Message; }
    }

    private static TextBlock CreateCenteredTabHeader(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            Width = 160,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(header, title);
        return header;
    }

    private async void SelectSource()
    {
        if (_rebuildingTabs || SourceTabs.SelectedItem is not TabViewItem tab) return;
        ClearExternalDragPreview();
        if ((string)tab.Tag == "phone")
        {
            DropRootGrid.Visibility = Visibility.Visible;
            WebDavHost.Visibility = Visibility.Collapsed;
            WebDavHost.Content = null;
            PhoneSelected?.Invoke(this, EventArgs.Empty);
            return;
        }
        DropRootGrid.Visibility = Visibility.Collapsed;
        WebDavHost.Visibility = Visibility.Visible;
        try
        {
            string id = (string)tab.Tag;
            if (!_webDavViews.TryGetValue(id, out var browser))
            {
                var profile = WebDavSettingsStore.Load().Profiles.Single(profile => profile.Id == id);
                var view = new WebDavBrowserView(profile);
                view.RemoteFileDragStarted += (_, _) =>
                {
                    _activeWebDavRemoteDrag = view;
                    WebDavRemoteDragStarted?.Invoke(this, EventArgs.Empty);
                };
                view.RemoteFileDragCompleted += (_, _) =>
                {
                    if (ReferenceEquals(_activeWebDavRemoteDrag, view)) _activeWebDavRemoteDrag = null;
                };
                browser = (profile, view);
                _webDavViews[id] = browser;
            }
            WebDavHost.Content = browser.View;
            await browser.View.ActivateAsync();
        }
        catch (Exception exception)
        {
            WebDavHost.Content = new TextBlock
            {
                Text = $"WebDAV 页面初始化失败：{exception.GetType().Name} (0x{exception.HResult:X8})\n{exception.Message}",
                Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap
            };
        }
    }
    private readonly HashSet<ScrollViewer> _fileScrollViewers = new();
    private bool _nearEndSignaled;
    private bool _nearEndCheckQueued;
    private bool _dropFeedbackVisible;
    private FrameworkElement? _dropTargetFolderItem;

    public string CurrentCategory { get; private set; } = "recent";
    public string CurrentRelativePath { get; private set; } = string.Empty;

    public void SetLocation(string category, string relativePath)
    {
        CurrentCategory = category;
        CurrentRelativePath = relativePath;
        ClearFolderDropTarget();
    }

    public event EventHandler? NearEndReached;
    public event EventHandler? SelectionStateChanged;
    public event EventHandler<ComputerFilesDroppedEventArgs>? FilesDropped;

    public ListView Categories => FileCategoryList;
    public ListView Files => FileListView;
    public GridView GridFiles => FileGridView;
    public ComboBox ViewMode => FileViewModeOptions;
    public TextBlock PathText => FilePathText;
    public TextBlock StatusText => FileManagementStatusText;
    public Button ImportFile => ImportFileButton;
    public Button RefreshFiles => RefreshFilesButton;
    public Button NavigateBack => NavigateBackButton;
    public Button SelectFiles => SelectFilesButton;
    public Button SaveSelectedFiles => SaveSelectedFilesButton;
    public Button DeleteSelectedFiles => DeleteSelectedFilesButton;
    public Button CancelSelection => CancelSelectionButton;
    public ComboBox TypeFilter => FileTypeFilterOptions;
    public ComboBox DocumentFilter => DocumentFilterOptions;
    public ComboBox SortOptions => FileSortOptions;

    public bool IsGridMode => ViewMode.SelectedIndex == 0;
    public bool IsSelectionMode => Files.SelectionMode != ListViewSelectionMode.None;

    public void HandleExternalDragOver(DragEventArgs e) =>
        DropRootGrid_DragOver(this, e);

    public void HandleExternalDragLeave(DragEventArgs e) =>
        DropRootGrid_DragLeave(this, e);

    public void HandleExternalDrop(DragEventArgs e) =>
        DropRootGrid_Drop(this, e);

    public void ShowExternalDropFeedback(string destination)
    {
        var label = string.Equals(destination, "Download/Hinge", StringComparison.OrdinalIgnoreCase)
            ? "自动分类保存到 /Download/Hinge/视频、图片或文件"
            : $"文件夹：/{NormalizeFolderPath(destination)}";
        ShowDropFeedback("已识别投放位置，正在发送", label);
        _ = HideDropFeedbackLaterAsync();
    }

    public void ShowExternalDropFeedback() =>
        ShowExternalDropFeedback("Download/Hinge");

    public void ShowExternalDragPreview()
    {
        if (IsSourceTabDragActive || !IsPhoneSelected) return;
        if (string.Equals(CurrentCategory, "storage", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(CurrentRelativePath))
        {
            ShowDropFeedback(
                "释放以发送到当前文件夹",
                $"/storage/emulated/0/{CurrentRelativePath.Trim('/')}");
        }
        else
        {
            ShowDropFeedback(
                "释放以发送到手机",
                "自动分类保存到 /Download/Hinge/视频、图片或文件");
        }
    }

    public void ClearExternalDragPreview()
    {
        ClearFolderDropTarget();
        HideDropFeedback();
    }

    public IReadOnlyList<RemoteFileEntry> GetSelectedEntries()
    {
        var selected = (IsGridMode ? GridFiles.SelectedItems : Files.SelectedItems)
            .OfType<FrameworkElement>()
            .Select(item => item.Tag)
            .OfType<RemoteFileEntry>()
            .Where(entry => !entry.IsDirectory)
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return selected;
    }

    public void EnterSelectionMode()
    {
        Files.SelectionMode = ListViewSelectionMode.Multiple;
        GridFiles.SelectionMode = ListViewSelectionMode.Multiple;
        SelectionActionBar.Visibility = Visibility.Visible;
        SelectFilesButton.Visibility = Visibility.Collapsed;
        UpdateSelectionSummary();
    }

    public void ExitSelectionMode()
    {
        // Clearing SelectedItems while the control is still in None mode can
        // fail during the first navigation/layout pass on WinUI. Only touch
        // the selection collections when selection mode is actually active.
        if (Files.SelectionMode != ListViewSelectionMode.None)
        {
            Files.SelectedItems.Clear();
        }
        if (GridFiles.SelectionMode != ListViewSelectionMode.None)
        {
            GridFiles.SelectedItems.Clear();
        }
        Files.SelectionMode = ListViewSelectionMode.None;
        GridFiles.SelectionMode = ListViewSelectionMode.None;
        SelectionActionBar.Visibility = Visibility.Collapsed;
        SelectFilesButton.Visibility = Visibility.Visible;
        UpdateSelectionSummary();
    }

    public void UpdateSelectionSummary()
    {
        SelectedCountText.Text = $"已选 {GetSelectedEntries().Count} 项";
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetNearEndTrigger()
    {
        _nearEndSignaled = false;
        RequestNearEndCheck();
    }

    public void RequestNearEndCheck()
    {
        if (_nearEndCheckQueued) return;
        _nearEndCheckQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _nearEndCheckQueued = false;
            foreach (var scrollViewer in _fileScrollViewers.ToArray())
            {
                EvaluateNearEnd(scrollViewer);
            }
        });
    }

    public void ApplyViewMode()
    {
        FileGridView.Visibility = IsGridMode ? Visibility.Visible : Visibility.Collapsed;
        FileListView.Visibility = IsGridMode ? Visibility.Collapsed : Visibility.Visible;
    }

    public FileManagementPage()
    {
        InitializeComponent();
        SourceTabs.TabItemsSource = _sourceTabs;
        SourceTabs.SelectionChanged += (_, _) => SelectSource();
        SourceTabs.TabDragStarting += (_, _) =>
        {
            IsSourceTabDragActive = true;
            ClearExternalDragPreview();
            SourceTabDragStarted?.Invoke(this, EventArgs.Empty);
        };
        SourceTabs.TabDragCompleted += (_, _) =>
        {
            IsSourceTabDragActive = false;
            ClearExternalDragPreview();
            try { WebDavSettingsStore.SaveOrder(_sourceTabs.Select(item => (string)item.Tag)); }
            catch (Exception exception) { StatusText.Text = "标签排序保存失败：" + exception.Message; }
        };
        Loaded += (_, _) =>
        {
            WebDavSettingsStore.Changed -= WebDavSettingsChanged;
            WebDavSettingsStore.Changed += WebDavSettingsChanged;
            RefreshSourceTabs();
        };
        Unloaded += (_, _) =>
        {
            IsSourceTabDragActive = false;
            WebDavSettingsStore.Changed -= WebDavSettingsChanged;
        };
        Loaded += FileManagementPage_Loaded;
        FileGridView.Loaded += (_, _) => AttachScrollViewer(FileGridView);
        FileListView.Loaded += (_, _) => AttachScrollViewer(FileListView);
        FileGridView.SelectionChanged += (_, _) => UpdateSelectionSummary();
        FileListView.SelectionChanged += (_, _) => UpdateSelectionSummary();
    }

    private void FileManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer(FileGridView);
        AttachScrollViewer(FileListView);
        DispatcherQueue.TryEnqueue(() =>
        {
            AttachScrollViewer(FileGridView);
            AttachScrollViewer(FileListView);
        });
    }

    private void AttachScrollViewer(DependencyObject root)
    {
        var scrollViewer = FindScrollViewer(root);
        if (scrollViewer == null || !_fileScrollViewers.Add(scrollViewer)) return;
        scrollViewer.ViewChanged += FileScrollViewer_ViewChanged;
    }

    private void FileScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        EvaluateNearEnd(scrollViewer);
    }

    private void EvaluateNearEnd(ScrollViewer scrollViewer)
    {
        // Start the next page before the thumb reaches the absolute bottom.
        // This matters when a user drags the scrollbar into the latter part
        // of a large directory: only the first 200 visual items exist at
        // first, so waiting for the exact end makes the scrollbar appear
        // stuck and leaves later tiles without a chance to request thumbs.
        var preloadDistance = Math.Max(
            480,
            Math.Min(2400, scrollViewer.ScrollableHeight * 0.35));
        var nearEnd = scrollViewer.ScrollableHeight <= 0 ||
            scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - preloadDistance;
        if (!nearEnd)
        {
            _nearEndSignaled = false;
            return;
        }

        if (_nearEndSignaled) return;
        _nearEndSignaled = true;
        NearEndReached?.Invoke(this, EventArgs.Empty);
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

    public void ConfigureFolderDrop(FrameworkElement item, RemoteFileEntry entry)
    {
        if (!entry.IsDirectory) return;
        item.AllowDrop = true;
        item.DragOver += (sender, args) =>
        {
            if (IsSourceTabDragActive) return;
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

            SetFolderDropTarget(item, entry);
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.DragUIOverride.Caption = $"发送到文件夹：{entry.Name}";
            args.DragUIOverride.IsGlyphVisible = true;
            args.Handled = true;
        };
        item.DragLeave += (_, _) => ClearFolderDropTarget(item);
        item.Drop += async (sender, args) =>
        {
            if (IsSourceTabDragActive) return;
            ClearFolderDropTarget(item);
            if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;

            if (HingeDragMetadata.IsInternalRemoteFileDrag(args.DataView))
            {
                args.AcceptedOperation = DataPackageOperation.Copy;
                args.Handled = true;
                return;
            }

            var paths = await GetDroppedFilePathsAsync(args.DataView);
            if (paths.Count == 0) return;

            var destination = NormalizeFolderPath(entry.RelativePath);
            FilesDropped?.Invoke(
                this,
                new ComputerFilesDroppedEventArgs(paths, destination));
            args.AcceptedOperation = DataPackageOperation.Copy;
            args.Handled = true;
        };
    }

    private void SetFolderDropTarget(FrameworkElement item, RemoteFileEntry entry)
    {
        if (_dropTargetFolderItem != item)
        {
            if (_dropTargetFolderItem != null) _dropTargetFolderItem.Opacity = 1.0;
            _dropTargetFolderItem = item;
        }

        item.Opacity = 0.72;
        var destination = NormalizeFolderPath(entry.RelativePath);
        ShowDropFeedback("释放以发送到文件夹", $"{entry.Name} · /{destination}");
    }

    private void ClearFolderDropTarget(FrameworkElement? item = null)
    {
        if (item != null && _dropTargetFolderItem != item) return;
        if (_dropTargetFolderItem != null) _dropTargetFolderItem.Opacity = 1.0;
        _dropTargetFolderItem = null;
    }

    private void DropRootGrid_DragOver(object sender, DragEventArgs e)
    {
        // Leave TabView's Move operation untouched; no file destination feedback.
        if (IsSourceTabDragActive) { ClearExternalDragPreview(); return; }
        if (!IsPhoneSelected)
        {
            if (SelectedWebDav is { } webDav) webDav.HandleDragOver(e);
            else { e.Handled = true; e.AcceptedOperation = DataPackageOperation.None; }
            return;
        }
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            ClearFolderDropTarget();
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

        if (_dropTargetFolderItem != null)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsGlyphVisible = true;

        if (string.Equals(CurrentCategory, "storage", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(CurrentRelativePath))
        {
            var folderName = GetDisplayFolderName(CurrentRelativePath);
            e.DragUIOverride.Caption = $"发送到当前文件夹：{folderName}";
            ShowDropFeedback(
                "释放以发送到当前文件夹",
                $"/storage/emulated/0/{CurrentRelativePath.Trim('/')}");
        }
        else
        {
            e.DragUIOverride.Caption = "发送到手机 Hinge 文件夹";
            ShowDropFeedback(
                "释放以发送到手机",
                "将自动分类保存到 /Download/Hinge/视频、图片或文件");
        }
        e.Handled = true;
    }

    private void DropRootGrid_DragLeave(object sender, DragEventArgs e)
    {
        ClearFolderDropTarget();
        HideDropFeedback();
    }

    private async void DropRootGrid_Drop(object sender, DragEventArgs e)
    {
        if (IsSourceTabDragActive) { ClearExternalDragPreview(); return; }
        if (!IsPhoneSelected)
        {
            if (!e.Handled) SelectedWebDav?.HandleDrop(e);
            e.Handled = true;
            return;
        }
        ClearFolderDropTarget();
        HideDropFeedback();
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        if (HingeDragMetadata.IsInternalRemoteFileDrag(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
            return;
        }

        var paths = await GetDroppedFilePathsAsync(e.DataView);
        if (paths.Count == 0) return;

        var destination = string.Equals(CurrentCategory, "storage", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(CurrentRelativePath)
            ? NormalizeFolderPath(CurrentRelativePath)
            : "Download/Hinge";

        FilesDropped?.Invoke(
            this,
            new ComputerFilesDroppedEventArgs(paths, destination));
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private Windows.Foundation.Point TransformToRootPoint(Windows.Foundation.Point pagePoint) =>
        TransformToVisual(DropRootGrid).TransformPoint(pagePoint);

    private (FrameworkElement Item, RemoteFileEntry Entry)? FindFolderAtRootPoint(
        Windows.Foundation.Point rootPoint)
    {
        var isGrid = IsGridMode;
        var itemsControl = isGrid ? (ItemsControl)FileGridView : FileListView;
        if (itemsControl.Visibility != Visibility.Visible) return null;

        for (var index = 0; index < itemsControl.Items.Count; index++)
        {
            if (itemsControl.ContainerFromIndex(index) is not FrameworkElement item ||
                item.Tag is not RemoteFileEntry { IsDirectory: true } entry ||
                item.ActualWidth <= 0 || item.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = item.TransformToVisual(DropRootGrid).TransformPoint(
                    new Windows.Foundation.Point(0, 0));
                var bounds = new Windows.Foundation.Rect(
                    topLeft,
                    new Windows.Foundation.Size(item.ActualWidth, item.ActualHeight));
                if (bounds.Contains(rootPoint)) return (item, entry);
            }
            catch
            {
                // Container being recycled during layout.
            }
        }

        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, DropRootGrid))
        {
            DependencyObject? current = element;
            while (current != null && current != DropRootGrid)
            {
                if (current is FrameworkElement { Tag: RemoteFileEntry { IsDirectory: true } entry } item)
                {
                    return (item, entry);
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        return null;
    }

    public bool UpdateExternalDragPreview(Windows.Foundation.Point pagePoint)
    {
        if (IsSourceTabDragActive || !IsPhoneSelected) return false;
        if (string.Equals(CurrentCategory, "storage", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var target = FindFolderAtRootPoint(TransformToRootPoint(pagePoint));
                if (target != null)
                {
                    SetFolderDropTarget(target.Value.Item, target.Value.Entry);
                    return true;
                }
            }
            catch
            {
                // Coordinate transformation during layout/navigation.
            }

            ClearFolderDropTarget();
            if (!string.IsNullOrWhiteSpace(CurrentRelativePath))
            {
                ShowDropFeedback(
                    "释放以发送到当前文件夹",
                    $"/storage/emulated/0/{CurrentRelativePath.Trim('/')}");
                return true;
            }

            ShowDropFeedback(
                "释放以发送到手机",
                "将自动分类保存到 /Download/Hinge/视频、图片或文件");
            return true;
        }

        ClearFolderDropTarget();
        ShowDropFeedback(
            "释放以发送到手机",
            "将自动分类保存到 /Download/Hinge/视频、图片或文件");
        return true;
    }

    public string ResolveExternalDropDestination(Windows.Foundation.Point pagePoint)
    {
        if (string.Equals(CurrentCategory, "storage", StringComparison.OrdinalIgnoreCase))
        {
            var rootPoints = new List<Windows.Foundation.Point> { pagePoint };
            try
            {
                rootPoints.Add(TransformToRootPoint(pagePoint));
            }
            catch
            {
                // Transformation fallback
            }

            foreach (var rootPoint in rootPoints)
            {
                var target = FindFolderAtRootPoint(rootPoint);
                if (target != null)
                {
                    return NormalizeFolderPath(target.Value.Entry.RelativePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(CurrentRelativePath))
            {
                return NormalizeFolderPath(CurrentRelativePath);
            }
        }

        return "Download/Hinge";
    }

    public static string NormalizeFolderPath(string? path) =>
        StoragePathHelper.NormalizeFolderPath(path);

    public static string GetDisplayFolderName(string path) =>
        StoragePathHelper.GetDisplayFolderName(path);

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
}
