using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class TodoPage : Page
{
    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private SessionConnection? _connection;
    private int _loadVersion;

    public TodoPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    public void Configure(WorkspaceRemoteClient client, Func<SessionConnection?> connectionProvider)
    {
        _client = client;
        _connectionProvider = connectionProvider;
    }

    public async Task LoadAsync(SessionConnection? connection)
    {
        _connection = connection;
        int version = ++_loadVersion;
        TasksList.Items.Clear();

        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            return;
        }

        SetStatus("正在读取待办", "从手机工作区读取最新内容…", InfoBarSeverity.Informational);
        try
        {
            var tasks = await _client.LoadTasksAsync(connection);
            if (version != _loadVersion) return;
            foreach (var task in tasks.OrderBy(task => task.Completed).ThenBy(task => task.DueAt ?? long.MaxValue))
            {
                TasksList.Items.Add(new ListViewItem { Tag = task, Content = BuildTaskRow(task) });
            }
            if (tasks.Count == 0)
            {
                TasksList.Items.Add(new ListViewItem
                {
                    IsHitTestVisible = false,
                    Content = new TextBlock
                    {
                        Text = "还没有待办，点击右上角“新建待办”开始安排工作。",
                        Padding = new Thickness(12, 24, 12, 24),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                });
            }
            SetStatus("待办已更新", $"共 {tasks.Count} 项待办。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetStatus("读取待办失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private Grid BuildTaskRow(RemoteWorkspaceTask task)
    {
        var row = new Grid { MinHeight = 64, ColumnSpacing = 14, Padding = new Thickness(8, 8, 4, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var checkBox = new CheckBox { IsChecked = task.Completed, Tag = task, VerticalAlignment = VerticalAlignment.Center };
        checkBox.Checked += TaskCheckBox_Changed;
        checkBox.Unchecked += TaskCheckBox_Changed;
        Grid.SetColumn(checkBox, 0);
        row.Children.Add(checkBox);

        var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(task.Title) ? "未命名待办" : task.Title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = task.Completed ? 0.6 : 1
        });
        var dueText = task.DueAt is { } due ? $"截止：{FormatTime(due)}" : "未设置截止时间";
        details.Children.Add(new TextBlock
        {
            Text = task.Completed ? $"已完成 · {dueText}" : dueText,
            FontSize = 14,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        Grid.SetColumn(details, 1);
        row.Children.Add(details);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var edit = new Button { Content = "编辑", Tag = task };
        edit.Click += EditButton_Click;
        var delete = new Button { Content = "删除", Tag = task };
        delete.Click += DeleteButton_Click;
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private async void TaskCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox ||
            checkBox.Tag is not RemoteWorkspaceTask task ||
            checkBox.IsChecked is not bool completed)
        {
            return;
        }
        await SaveTaskAsync(new RemoteWorkspaceTask
        {
            Id = task.Id,
            Title = task.Title,
            Completed = completed,
            DueAt = task.DueAt,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private async void NewTaskButton_Click(object sender, RoutedEventArgs e) => await EditTaskAsync(null);

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RemoteWorkspaceTask task }) await EditTaskAsync(task);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RemoteWorkspaceTask task }) return;
        var dialog = new ContentDialog
        {
            Title = "删除待办？",
            Content = $"将删除“{(string.IsNullOrWhiteSpace(task.Title) ? "未命名待办" : task.Title)}”。此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法删除", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }
        try
        {
            await _client.DeleteTaskAsync(connection, task.Id);
            await LoadAsync(connection);
        }
        catch (Exception exception)
        {
            SetStatus("删除失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async Task EditTaskAsync(RemoteWorkspaceTask? existing)
    {
        var title = new TextBox { Text = existing?.Title ?? string.Empty, PlaceholderText = "例如：整理会议资料" };
        var due = new DatePicker { Date = existing?.DueAt is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime() : DateTimeOffset.Now };
        var noDue = new CheckBox { Content = "不设置截止日期", IsChecked = existing?.DueAt == null };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "内容" });
        panel.Children.Add(title);
        panel.Children.Add(noDue);
        panel.Children.Add(due);
        var dialog = new ContentDialog
        {
            Title = existing == null ? "新建待办" : "编辑待办",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(title.Text))
        {
            SetStatus("无法保存", "待办内容不能为空。", InfoBarSeverity.Warning);
            return;
        }
        var task = new RemoteWorkspaceTask
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Title = title.Text.Trim(),
            Completed = existing?.Completed == true,
            DueAt = noDue.IsChecked == true ? null : due.Date.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await SaveTaskAsync(task);
    }

    private async Task SaveTaskAsync(RemoteWorkspaceTask task)
    {
        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法保存", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }
        try
        {
            var updated = new RemoteWorkspaceTask
            {
                Id = task.Id,
                Title = task.Title,
                Completed = task.Completed,
                DueAt = task.DueAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await _client.SaveTaskAsync(connection, updated);
            await LoadAsync(connection);
        }
        catch (Exception exception)
        {
            SetStatus("保存失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(_connectionProvider?.Invoke() ?? _connection);

    private void SetStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
    }

    private static string FormatTime(long milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("yyyy/M/d");
}
