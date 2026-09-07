using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class NotesPage : Page
{
    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private SessionConnection? _connection;
    private int _loadVersion;

    public NotesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
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
        NotesList.Items.Clear();

        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            return;
        }

        SetStatus("正在读取笔记", "从手机工作区读取最新内容…", InfoBarSeverity.Informational);
        try
        {
            var notes = await _client.LoadNotesAsync(connection);
            if (version != _loadVersion) return;
            foreach (var note in notes.OrderByDescending(note => note.Pinned).ThenByDescending(note => note.UpdatedAt))
            {
                NotesList.Items.Add(new ListViewItem
                {
                    Tag = note,
                    Content = BuildNoteRow(note)
                });
            }

            if (notes.Count == 0)
            {
                NotesList.Items.Add(new ListViewItem
                {
                    IsHitTestVisible = false,
                    Content = new TextBlock
                    {
                        Text = "还没有笔记，点击右上角“新建笔记”开始记录。",
                        Padding = new Thickness(12, 24, 12, 24),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                });
            }
            SetStatus("笔记已更新", $"共 {notes.Count} 条笔记。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetStatus("读取笔记失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private Grid BuildNoteRow(RemoteWorkspaceNote note)
    {
        var row = new Grid { MinHeight = 72, ColumnSpacing = 16, Padding = new Thickness(8, 8, 4, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.Title) ? "未命名笔记" : note.Title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(note.Content) ? "无正文" : note.Content.Replace('\n', ' '),
            FontSize = 14,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = FormatTime(note.UpdatedAt) + (note.Pinned ? " · 已置顶" : string.Empty),
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        Grid.SetColumn(text, 0);
        row.Children.Add(text);

        var delete = new Button { Content = "删除", Tag = note, VerticalAlignment = VerticalAlignment.Center };
        delete.Click += DeleteButton_Click;
        Grid.SetColumn(delete, 1);
        row.Children.Add(delete);
        return row;
    }

    private async void NewNoteButton_Click(object sender, RoutedEventArgs e) => await EditNoteAsync(null);

    private async void NotesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListViewItem { Tag: RemoteWorkspaceNote note })
        {
            await EditNoteAsync(note);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RemoteWorkspaceNote note }) return;
        var dialog = new ContentDialog
        {
            Title = "删除笔记？",
            Content = $"将删除“{(string.IsNullOrWhiteSpace(note.Title) ? "未命名笔记" : note.Title)}”。此操作无法撤销。",
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
            await _client.DeleteNoteAsync(connection, note.Id);
            await LoadAsync(connection);
        }
        catch (Exception exception)
        {
            SetStatus("删除失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async Task EditNoteAsync(RemoteWorkspaceNote? existing)
    {
        var title = new TextBox { Text = existing?.Title ?? string.Empty, PlaceholderText = "标题（可选）" };
        var content = new TextBox
        {
            Text = existing?.Content ?? string.Empty,
            PlaceholderText = "写下你的内容",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 150
        };
        var pinned = new CheckBox { Content = "置顶", IsChecked = existing?.Pinned == true };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "标题" });
        panel.Children.Add(title);
        panel.Children.Add(new TextBlock { Text = "正文" });
        panel.Children.Add(content);
        panel.Children.Add(pinned);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "新建笔记" : "编辑笔记",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(title.Text) && string.IsNullOrWhiteSpace(content.Text))
        {
            SetStatus("无法保存", "标题和正文不能同时为空。", InfoBarSeverity.Warning);
            return;
        }

        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("无法保存", "设备会话已断开。", InfoBarSeverity.Error);
            return;
        }
        var note = new RemoteWorkspaceNote
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名笔记" : title.Text.Trim(),
            Content = content.Text.Trim(),
            Pinned = pinned.IsChecked == true,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        try
        {
            await _client.SaveNoteAsync(connection, note);
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

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0) return "时间未知";
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("yyyy/M/d HH:mm");
    }
}
