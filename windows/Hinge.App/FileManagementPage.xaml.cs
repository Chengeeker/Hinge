using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Hinge.App;

public sealed partial class FileManagementPage : Page
{
    private readonly HashSet<ScrollViewer> _fileScrollViewers = new();
    private bool _nearEndSignaled;
    private bool _nearEndCheckQueued;
    private bool _dropFeedbackVisible;

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

    public void ShowExternalDropFeedback()
    {
        ShowDropFeedback(
            "正在发送到手机",
            "自动分类保存到 /Download/Hinge/视频、图片或文件");
        _ = HideDropFeedbackLaterAsync();
    }

    public void ShowExternalDragPreview() => ShowDropFeedback(
        "释放以发送到手机",
        "自动分类保存到 /Download/Hinge/视频、图片或文件");

    public void ClearExternalDragPreview() => HideDropFeedback();

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

    private void DropRootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
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

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "发送到手机 Hinge 文件夹";
        e.DragUIOverride.IsGlyphVisible = true;
        ShowDropFeedback(
            "释放以发送到手机",
            "将自动分类保存到 /Download/Hinge/视频、图片或文件");
        e.Handled = true;
    }

    private void DropRootGrid_DragLeave(object sender, DragEventArgs e)
    {
        HideDropFeedback();
    }

    private async void DropRootGrid_Drop(object sender, DragEventArgs e)
    {
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
        FilesDropped?.Invoke(
            this,
            new ComputerFilesDroppedEventArgs(paths, "Download/Hinge"));
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
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
