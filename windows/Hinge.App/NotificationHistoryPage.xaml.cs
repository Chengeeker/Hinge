using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Hinge.Core;
using Windows.Storage.Streams;

namespace Hinge.App;

public sealed partial class NotificationHistoryPage : Page
{
    private const int PageSize = 100;
    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private Func<RemoteNotificationHistoryItem, Task<bool>>? _openItem;
    private Func<RemoteNotificationHistoryItem, Task<bool>>? _deleteItem;
    private Func<Task<bool>>? _clearItems;
    private SessionConnection? _connection;
    private readonly List<RemoteNotificationHistoryItem> _items = new();
    private int _total;
    private int _loadVersion;
    private bool _loading;
    private bool _ascending = true;
    private bool _suppressFilterEvents;
    private string? _packageName;

    public NotificationHistoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    public void Configure(
        WorkspaceRemoteClient client,
        Func<SessionConnection?> connectionProvider,
        Func<RemoteNotificationHistoryItem, Task<bool>> openItem,
        Func<RemoteNotificationHistoryItem, Task<bool>> deleteItem,
        Func<Task<bool>> clearItems)
    {
        _client = client;
        _connectionProvider = connectionProvider;
        _openItem = openItem;
        _deleteItem = deleteItem;
        _clearItems = clearItems;
    }

    public Task LoadAsync(SessionConnection? connection)
    {
        _connection = connection;
        return LoadPageAsync(reset: true);
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (_loading || (!reset && _items.Count >= _total)) return;
        var connection = _connectionProvider?.Invoke() ?? _connection;
        _connection = connection;
        var version = reset ? ++_loadVersion : _loadVersion;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            if (reset)
            {
                _items.Clear();
                _total = 0;
                HistoryList.Items.Clear();
                SummaryText.Text = string.Empty;
                ClearButton.IsEnabled = false;
            }
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            return;
        }

        _loading = true;
        LoadingIndicator.IsActive = true;
        if (reset) SetStatus("正在读取通知历史", "从手机通知历史数据库读取内容…", InfoBarSeverity.Informational);
        try
        {
            var page = await _client.LoadNotificationHistoryAsync(
                connection,
                reset ? 0 : _items.Count,
                PageSize,
                _ascending,
                _packageName);
            if (version != _loadVersion) return;

            _total = page.Total;
            if (reset) _items.Clear();
            _items.AddRange(page.Items.Where(item =>
                !_items.Any(existing => existing.Id.Equals(item.Id, StringComparison.Ordinal))));
            RenderApplicationFilter(page.Applications);
            RenderItems();

            if (!page.AccessEnabled)
            {
                SetStatus("需要 Android 通知访问权限", "请在 Android 的“工作区 > 通知历史”中打开系统通知访问权限。", InfoBarSeverity.Warning);
            }
            else if (!page.Enabled)
            {
                SetStatus("通知历史未开启", "请在 Android 的“工作区 > 通知历史”中开启通知历史采集。", InfoBarSeverity.Warning);
            }
            else
            {
                SetStatus("通知历史已更新", $"共 {_total} 条记录。", InfoBarSeverity.Success);
            }
        }
        catch (Exception exception)
        {
            SetStatus("读取通知历史失败", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (version == _loadVersion)
            {
                _loading = false;
                LoadingIndicator.IsActive = false;
                ClearButton.IsEnabled = _total > 0 && _clearItems != null;
            }
        }
    }

    private void RenderApplicationFilter(IReadOnlyList<RemoteNotificationHistoryApplication> applications)
    {
        var selected = _packageName;
        _suppressFilterEvents = true;
        try
        {
            ApplicationFilter.Items.Clear();
            ApplicationFilter.Items.Add(new ComboBoxItem { Content = "全部应用", Tag = string.Empty });
            foreach (var app in applications)
            {
                ApplicationFilter.Items.Add(new ComboBoxItem
                {
                    Content = $"{(string.IsNullOrWhiteSpace(app.AppName) ? app.PackageName : app.AppName)}（{app.Count}）",
                    Tag = app.PackageName,
                });
            }

            var selectedItem = ApplicationFilter.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected ?? string.Empty, StringComparison.Ordinal));
            ApplicationFilter.SelectedItem = selectedItem ?? ApplicationFilter.Items.FirstOrDefault();
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void RenderItems()
    {
        HistoryList.Items.Clear();
        foreach (var item in _items)
        {
            HistoryList.Items.Add(new ListViewItem
            {
                Tag = item,
                Content = BuildItemRow(item),
            });
        }

        SummaryText.Text = _total == 0
            ? "暂无通知记录。"
            : $"共 {_total} 条 · 已加载 {_items.Count} 条，滚动到底部继续加载";
        ClearButton.IsEnabled = _total > 0 && !_loading && _clearItems != null;
        if (_items.Count == 0)
        {
            HistoryList.Items.Add(new ListViewItem
            {
                IsHitTestVisible = false,
                Content = new TextBlock
                {
                    Text = "暂无符合条件的通知。",
                    Padding = new Thickness(12, 24, 12, 24),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            });
        }
    }

    private Grid BuildItemRow(RemoteNotificationHistoryItem item)
    {
        var row = new Grid
        {
            MinHeight = 76,
            ColumnSpacing = 14,
            Padding = new Thickness(8, 8, 4, 8),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image
        {
            Width = 42,
            Height = 42,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrWhiteSpace(item.IconBase64))
        {
            _ = SetImageSourceAsync(icon, item.IconBase64);
        }
        else
        {
            icon.Source = new BitmapImage(new Uri("ms-appx:///Assets/app_icon.png"));
        }
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Title)
                ? item.AppName
                : $"{item.AppName} · {item.Title}",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Content) ? "（无正文）" : item.Content,
            FontSize = 14,
            MaxLines = 3,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        details.Children.Add(new TextBlock
        {
            Text = FormatTime(item.Timestamp) +
                (item.Ongoing ? " · 持续通知" : string.Empty),
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (IsChatApp(item.PackageName))
        {
            var open = new Button
            {
                Content = "打开",
                Tag = item,
                VerticalAlignment = VerticalAlignment.Center,
            };
            open.Click += OpenButton_Click;
            actions.Children.Add(open);
        }
        var delete = new Button
        {
            Content = "删除",
            Tag = item,
            VerticalAlignment = VerticalAlignment.Center,
        };
        delete.Click += DeleteButton_Click;
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListViewItem { Tag: RemoteNotificationHistoryItem item })
        {
            await OpenItemAsync(item);
        }
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RemoteNotificationHistoryItem item })
        {
            await OpenItemAsync(item);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RemoteNotificationHistoryItem item })
        {
            await DeleteItemAsync(item);
        }
    }

    private async Task DeleteItemAsync(RemoteNotificationHistoryItem item)
    {
        if (_deleteItem == null) return;
        try
        {
            if (!await _deleteItem(item))
            {
                SetStatus("删除失败", "通知记录已经不存在，列表将重新读取。", InfoBarSeverity.Warning);
            }
            else
            {
                SetStatus("通知已删除", $"已删除“{item.AppName}”的一条通知。", InfoBarSeverity.Success);
            }
            await LoadPageAsync(reset: true);
        }
        catch (Exception exception)
        {
            SetStatus("删除通知失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clearItems == null || _total == 0 || XamlRoot == null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清空通知历史？",
            Content = "所有已保存的通知记录都会被删除，且无法恢复。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            if (await _clearItems())
            {
                SetStatus("通知历史已清空", "所有通知记录已从手机的历史数据库中删除。", InfoBarSeverity.Success);
            }
            else
            {
                SetStatus("清空失败", "设备会话已断开，请重新连接手机。", InfoBarSeverity.Warning);
            }
            await LoadPageAsync(reset: true);
        }
        catch (Exception exception)
        {
            SetStatus("清空通知历史失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async Task OpenItemAsync(RemoteNotificationHistoryItem item)
    {
        if (_openItem == null) return;
        try
        {
            if (!await _openItem(item))
            {
                SetStatus("无法打开通知", "Windows 和 Android 都没有找到可打开此通知的应用。", InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            SetStatus("打开通知失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void HistoryScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_loading || _items.Count >= _total) return;
        var threshold = Math.Max(480, HistoryScroll.ViewportHeight * 1.5);
        if (HistoryScroll.ScrollableHeight <= 0 ||
            HistoryScroll.VerticalOffset >= HistoryScroll.ScrollableHeight - threshold)
        {
            _ = LoadPageAsync(reset: false);
        }
    }

    private void ApplicationFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || ApplicationFilter.SelectedItem is not ComboBoxItem item) return;
        var next = item.Tag?.ToString();
        if (string.Equals(next, _packageName, StringComparison.Ordinal)) return;
        _packageName = string.IsNullOrWhiteSpace(next) ? null : next;
        _ = LoadPageAsync(reset: true);
    }

    private void SortOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortOptions.SelectedItem is not ComboBoxItem item) return;
        var next = string.Equals(item.Tag?.ToString(), "asc", StringComparison.Ordinal);
        if (next == _ascending) return;
        _ascending = next;
        _ = LoadPageAsync(reset: true);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadPageAsync(reset: true);
    }

    private void SetStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
    }

    private static bool IsChatApp(string packageName) =>
        packageName.Equals("com.tencent.mm", StringComparison.OrdinalIgnoreCase) ||
        packageName.Equals("com.tencent.mobileqq", StringComparison.OrdinalIgnoreCase);

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0) return "时间未知";
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            .ToLocalTime()
            .ToString("yyyy/M/d HH:mm:ss");
    }

    private static async Task SetImageSourceAsync(Image image, string encoded)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception)
        {
            // Keep the bundled Hinge icon when an OEM icon is malformed.
        }
    }
}
