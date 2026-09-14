using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class CalendarPage : Page
{
    private static readonly string[] WeekdayNames = { "一", "二", "三", "四", "五", "六", "日" };

    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private SessionConnection? _connection;
    private IReadOnlyList<RemoteCalendarEvent> _events = Array.Empty<RemoteCalendarEvent>();
    private Dictionary<DateTime, List<RemoteCalendarEvent>> _eventsByDate = new();
    private DateTime _displayMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime _selectedDate = DateTime.Now.Date;
    private int _loadVersion;

    public CalendarPage()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) =>
        {
            BuildCalendarGrid();
            RenderSelectedDay();
        };
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    public void Configure(WorkspaceRemoteClient client, Func<SessionConnection?> connectionProvider)
    {
        _client = client;
        _connectionProvider = connectionProvider;
    }

    public async Task LoadAsync(SessionConnection? connection)
    {
        _connection = connection;
        var version = ++_loadVersion;
        _events = Array.Empty<RemoteCalendarEvent>();
        _eventsByDate = new Dictionary<DateTime, List<RemoteCalendarEvent>>();

        if (_client == null || connection?.State != SessionState.Connected)
        {
            CalendarSummaryText.Text = "请先在首页连接 Android 手机。";
            BuildCalendarGrid();
            RenderSelectedDay();
            return;
        }

        CalendarSummaryText.Text = "正在读取手机日程…";
        try
        {
            _events = await _client.LoadCalendarAsync(connection);
            if (version != _loadVersion) return;

            _eventsByDate = BuildEventsByDate(_events);
            CalendarSummaryText.Text = _events.Count == 0
                ? "手机日历中没有可显示的日程。"
                : $"共 {_events.Count} 条日程 · 已同步手机日历中的可见日历";
            BuildCalendarGrid();
            RenderSelectedDay();
        }
        catch (Exception exception)
        {
            if (version != _loadVersion) return;
            CalendarSummaryText.Text = $"读取日历失败：{exception.Message}";
            BuildCalendarGrid();
            RenderSelectedDay();
        }
    }

    private Dictionary<DateTime, List<RemoteCalendarEvent>> BuildEventsByDate(
        IReadOnlyList<RemoteCalendarEvent> events)
    {
        var result = new Dictionary<DateTime, List<RemoteCalendarEvent>>();
        foreach (var item in events)
        {
            if (item.Start <= 0) continue;

            var (startDate, endDate) = CalendarEventDateRange.GetInclusiveDates(
                item.Start,
                item.End,
                item.AllDay);

            // A malformed provider must not make rebuilding the month view
            // iterate forever or create a huge in-memory date map.
            if ((endDate - startDate).TotalDays > 366)
            {
                endDate = startDate.AddDays(366);
            }

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (!result.TryGetValue(date, out var dayEvents))
                {
                    dayEvents = new List<RemoteCalendarEvent>();
                    result[date] = dayEvents;
                }
                dayEvents.Add(item);
            }
        }

        foreach (var dayEvents in result.Values)
        {
            dayEvents.Sort(static (left, right) =>
            {
                var allDay = right.AllDay.CompareTo(left.AllDay);
                if (allDay != 0) return allDay;
                var start = left.Start.CompareTo(right.Start);
                return start != 0
                    ? start
                    : string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
            });
        }
        return result;
    }

    private void BuildCalendarGrid()
    {
        MonthTitle.Text = $"{_displayMonth:yyyy年M月}";
        BuildWeekdayHeaders();

        MonthCellsGrid.Children.Clear();
        MonthCellsGrid.RowDefinitions.Clear();
        MonthCellsGrid.ColumnDefinitions.Clear();
        for (var row = 0; row < 6; row++)
        {
            MonthCellsGrid.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        for (var column = 0; column < 7; column++)
        {
            MonthCellsGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var firstOfMonth = _displayMonth.Date;
        var mondayOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var firstCell = firstOfMonth.AddDays(-mondayOffset);
        for (var index = 0; index < 42; index++)
        {
            var date = firstCell.AddDays(index);
            var cell = CreateDayCell(date, date.Month == _displayMonth.Month);
            Grid.SetRow(cell, index / 7);
            Grid.SetColumn(cell, index % 7);
            MonthCellsGrid.Children.Add(cell);
        }
    }

    private void BuildWeekdayHeaders()
    {
        WeekdayHeaderGrid.Children.Clear();
        WeekdayHeaderGrid.ColumnDefinitions.Clear();
        for (var column = 0; column < WeekdayNames.Length; column++)
        {
            WeekdayHeaderGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var header = new Border
            {
                BorderBrush = SecondaryBorderBrush(),
                BorderThickness = new Thickness(0, 0, column == 6 ? 0 : 1, 1),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new TextBlock
                {
                    Text = WeekdayNames[column],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = SecondaryTextBrush(),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                },
            };
            Grid.SetColumn(header, column);
            WeekdayHeaderGrid.Children.Add(header);
        }
    }

    private Border CreateDayCell(DateTime date, bool inCurrentMonth)
    {
        var selected = date.Date == _selectedDate.Date;
        var today = date.Date == DateTime.Now.Date;
        _eventsByDate.TryGetValue(date.Date, out var dayEvents);
        dayEvents ??= new List<RemoteCalendarEvent>();

        var dayHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        dayHeader.Children.Add(new TextBlock
        {
            Text = date.Day.ToString(),
            FontSize = 19,
            FontWeight = today || selected
                ? Microsoft.UI.Text.FontWeights.Bold
                : Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = selected
                ? AccentOnBrush()
                : inCurrentMonth ? PrimaryTextBrush() : MutedTextBrush(),
        });
        if (today && !selected)
        {
            dayHeader.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = AccentBrush(),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(dayHeader);

        var eventStack = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(0, 8, 0, 0),
        };
        foreach (var item in dayEvents.Take(3))
        {
            var eventRow = new Grid { ColumnSpacing = 6 };
            eventRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            eventRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            eventRow.Children.Add(new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = selected ? AccentOnBrush() : AccentBrush(),
                VerticalAlignment = VerticalAlignment.Stretch,
            });
            var eventText = new TextBlock
            {
                Text = CellEventText(item),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = selected ? AccentOnBrush() : PrimaryTextBrush(),
            };
            Grid.SetColumn(eventText, 1);
            eventRow.Children.Add(eventText);
            eventStack.Children.Add(eventRow);
        }
        if (dayEvents.Count > 3)
        {
            eventStack.Children.Add(new TextBlock
            {
                Text = $"+{dayEvents.Count - 3} 项",
                FontSize = 12,
                Foreground = selected ? AccentOnBrush() : SecondaryTextBrush(),
            });
        }
        Grid.SetRow(eventStack, 1);
        content.Children.Add(eventStack);

        var button = new Button
        {
            Tag = date,
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = selected ? AccentBrush() : TransparentBrush(),
            BorderThickness = new Thickness(0),
            Content = content,
        };
        ConfigureDayButtonVisuals(button, selected);
        button.Click += DayButton_Click;

        return new Border
        {
            BorderBrush = SecondaryBorderBrush(),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = inCurrentMonth ? TransparentBrush() : MutedCellBrush(),
            Child = button,
        };
    }

    private void RenderSelectedDay()
    {
        SelectedDateText.Text = $"{_selectedDate:yyyy年M月d日} · 日程";
        EventsList.Items.Clear();
        if (!_eventsByDate.TryGetValue(_selectedDate.Date, out var events) || events.Count == 0)
        {
            EventsList.Items.Add(new ListViewItem
            {
                IsHitTestVisible = false,
                Content = new TextBlock
                {
                    Text = "当天没有日程。",
                    Padding = new Thickness(4, 16, 4, 16),
                    Foreground = SecondaryTextBrush(),
                },
            });
            return;
        }

        foreach (var item in events)
        {
            var details = new List<string>
            {
                item.AllDay ? "全天" : FormatTime(item.Start, item.End),
            };
            if (!string.IsNullOrWhiteSpace(item.EventType)) details.Add($"类型：{item.EventType}");
            if (!string.IsNullOrWhiteSpace(item.CalendarName)) details.Add($"日历：{item.CalendarName}");
            if (!string.IsNullOrWhiteSpace(item.Location)) details.Add($"地点：{item.Location}");
            if (!string.IsNullOrWhiteSpace(item.Description)) details.Add(item.Description.Trim());

            EventsList.Items.Add(new ListViewItem
            {
                Content = new StackPanel
                {
                    Spacing = 4,
                    Padding = new Thickness(4, 8, 4, 8),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(item.Title) ? "未命名日程" : item.Title,
                            FontSize = 16,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            Text = string.Join(" · ", details),
                            FontSize = 14,
                            Foreground = SecondaryTextBrush(),
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },
            });
        }
    }

    private static string CellEventText(RemoteCalendarEvent item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? "未命名日程" : item.Title.Trim();
        var category = string.IsNullOrWhiteSpace(item.EventType)
            ? string.Empty
            : $"{item.EventType.Trim()} · ";
        return item.AllDay
            ? $"{category}{title}"
            : $"{FormatTime(item.Start, item.End)} {category}{title}";
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date }) return;
        _selectedDate = date.Date;
        _displayMonth = new DateTime(date.Year, date.Month, 1);
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    private void PreviousMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        BuildCalendarGrid();
    }

    private void NextMonthButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        BuildCalendarGrid();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync(_connectionProvider?.Invoke() ?? _connection);
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateTime.Now.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    private async void JumpDateButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new CalendarDatePicker
        {
            Header = "日期",
            Date = new DateTimeOffset(_selectedDate),
            PlaceholderText = "选择日期",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "跳转到日期",
            Content = picker,
            PrimaryButtonText = "跳转",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.Date is not { } date) return;

        _selectedDate = date.Date.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var datePicker = new CalendarDatePicker
        {
            Header = "日期",
            Date = new DateTimeOffset(_selectedDate),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var titleBox = new TextBox
        {
            Header = "标题",
            PlaceholderText = "例如：高铁出行",
        };
        var locationBox = new TextBox
        {
            Header = "地点（可选）",
            PlaceholderText = "输入地点",
        };
        var allDayBox = new CheckBox { Content = "全天", IsChecked = false };
        var startPicker = new TimePicker
        {
            Header = "开始时间",
            Time = new TimeSpan(9, 0, 0),
        };
        var endPicker = new TimePicker
        {
            Header = "结束时间",
            Time = new TimeSpan(10, 0, 0),
        };
        var timePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { startPicker, endPicker },
        };
        allDayBox.Checked += (_, _) => timePanel.Visibility = Visibility.Collapsed;
        allDayBox.Unchecked += (_, _) => timePanel.Visibility = Visibility.Visible;

        var content = new StackPanel
        {
            Spacing = 12,
            Children = { datePicker, titleBox, locationBox, allDayBox, timePanel },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "创建新日程",
            Content = content,
            PrimaryButtonText = "在手机日历中创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var title = titleBox.Text.Trim();
        if (title.Length == 0)
        {
            await ShowMessageAsync("无法创建日程", "日程标题不能为空。", false);
            return;
        }

        var selectedDate = datePicker.Date?.Date.Date ?? _selectedDate;
        var allDay = allDayBox.IsChecked == true;
        var start = allDay
            ? LocalDateTime(selectedDate, TimeSpan.Zero)
            : LocalDateTime(selectedDate, startPicker.Time);
        var end = allDay
            ? start.AddDays(1)
            : LocalDateTime(selectedDate, endPicker.Time);
        if (end <= start) end = start.AddHours(1);

        var connection = _connectionProvider?.Invoke() ?? _connection;
        if (_client == null || connection?.State != SessionState.Connected)
        {
            await ShowMessageAsync("无法创建日程", "请先在首页连接 Android 手机。", false);
            return;
        }

        try
        {
            var opened = await _client.OpenCalendarCreateAsync(
                connection,
                title,
                start.ToUnixTimeMilliseconds(),
                end.ToUnixTimeMilliseconds(),
                allDay,
                locationBox.Text.Trim());
            CalendarSummaryText.Text = opened
                ? "已在手机日历中打开新建日程页面，保存后点击刷新即可同步。"
                : "手机没有可用的日历应用。";
        }
        catch (Exception exception)
        {
            CalendarSummaryText.Text = $"打开新建日程失败：{exception.Message}";
        }
    }

    private async Task ShowMessageAsync(string title, string message, bool primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "知道了",
            DefaultButton = primary ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static DateTimeOffset LocalDateTime(DateTime date, TimeSpan time)
    {
        var local = DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    private static string FormatTime(long start, long? end)
    {
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(start).ToLocalTime();
        if (end is not { } endValue) return startTime.ToString("HH:mm");
        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(endValue).ToLocalTime();
        return $"{startTime:HH:mm}–{endTime:HH:mm}";
    }

    private Brush PrimaryTextBrush() => ThemeBrushes.Primary(RootGrid);

    private Brush SecondaryTextBrush() => ThemeBrushes.Secondary(RootGrid);

    private Brush MutedTextBrush() => ThemeBrushes.Muted(RootGrid);

    private static Brush AccentBrush() => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x0F, 0x6C, 0xBD));

    private static Brush AccentOnBrush() => new SolidColorBrush(Colors.White);

    private Brush HoverCellBrush() => ThemeBrushes.Hover(RootGrid);

    private Brush PressedCellBrush() => ThemeBrushes.Pressed(RootGrid);

    private Brush SecondaryBorderBrush() => ThemeBrushes.Border(RootGrid);

    private Brush MutedCellBrush() => ThemeBrushes.MutedSurface(RootGrid);

    private static Brush TransparentBrush() => new SolidColorBrush(Colors.Transparent);

    private void ConfigureDayButtonVisuals(Button button, bool selected)
    {
        var normalBackground = selected ? AccentBrush() : TransparentBrush();
        var pointerOverBackground = selected ? AccentBrush() : HoverCellBrush();
        var pressedBackground = selected ? AccentBrush() : PressedCellBrush();
        var foreground = selected ? AccentOnBrush() : PrimaryTextBrush();

        // Button's default pointer-over template can replace the explicit
        // Background with a theme fill. Keep selected cells on the accent
        // surface and keep unselected cells readable in both themes.
        button.Resources["ButtonBackground"] = normalBackground;
        button.Resources["ButtonBackgroundPointerOver"] = pointerOverBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonBackgroundFocused"] = normalBackground;
        button.Resources["ButtonBorderBrush"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPointerOver"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPressed"] = TransparentBrush();
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonForegroundFocused"] = foreground;
    }

}
