using Microsoft.UI.Xaml.Automation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Hinge.Core;
using Windows.UI;
using System.Diagnostics;

namespace Hinge.App;

public sealed partial class CalendarPage : Page
{
    private static readonly string[] WeekdayNames = { "一", "二", "三", "四", "五", "六", "日" };
    private const double MinimumWeekHeight = 112;
    private const double DateHeaderHeight = 44;
    private const double SpanLaneHeight = 18;
    private const double SpanLaneTop = 54;

    private sealed record WeekSpanSegment(
        RemoteCalendarEvent Event,
        bool StartsHere,
        bool EndsHere,
        int StartColumn,
        int EndColumn,
        int Lane);

    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private SessionConnection? _connection;
    private IReadOnlyList<RemoteCalendarEvent> _events = Array.Empty<RemoteCalendarEvent>();
    private Dictionary<DateTime, List<RemoteCalendarEvent>> _eventsByDate = new();
    private DateTime _displayMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime _selectedDate = DateTime.Now.Date;
    private int _loadVersion;
    private int _cellWidthClass = -1;
    private int _cellHeightClass = -1;
    private bool _buildingGrid;

    static CalendarPage()
    {
#if DEBUG
        Debug.Assert(WeeksInMonth(new DateTime(2021, 2, 1)) == 4);
        Debug.Assert(WeeksInMonth(new DateTime(2026, 10, 1)) == 5);
        Debug.Assert(WeeksInMonth(new DateTime(2026, 8, 1)) == 6);
        var middleTone = Color.FromArgb(255, 118, 118, 118);
        Debug.Assert(ContrastRatio(ContrastTextColor(middleTone), middleTone) >= 4.5);
#endif
    }

    public CalendarPage()
    {
        InitializeComponent();
        MonthCellsGrid.SizeChanged += MonthCellsGrid_SizeChanged;
        ActualThemeChanged += (_, _) =>
        {
            BuildCalendarGrid();
            RenderSelectedDay();
        };
        ThemeBrushes.PaletteChanged += CalendarPaletteChanged;
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

        if (_client == null || connection?.IsSessionReady != true)
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

            var (startDate, endDate) = GetEventDateRange(item);

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

    private static (DateTime StartDate, DateTime EndDate) GetEventDateRange(RemoteCalendarEvent item)
    {
        var range = CalendarEventDateRange.GetInclusiveDates(item.Start, item.End, item.AllDay);

        // Keep malformed providers from producing an unbounded month-view layout.
        return (range.StartDate, (range.EndDate - range.StartDate).TotalDays > 366
            ? range.StartDate.AddDays(366)
            : range.EndDate);
    }

    private static int WeeksInMonth(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var mondayOffset = ((int)first.DayOfWeek + 6) % 7;
        return (mondayOffset + DateTime.DaysInMonth(first.Year, first.Month) + 6) / 7;
    }

    private static int CellWidthClass(double width) => width < 116 ? 0 : width < 154 ? 1 : 2;

    private static int CellHeightClass(double height) => (int)(height / 12);

    private void MonthCellsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_buildingGrid) return;
        var weekCount = WeeksInMonth(_displayMonth);
        var widthClass = CellWidthClass(e.NewSize.Width / 7);
        var heightClass = CellHeightClass(e.NewSize.Height / weekCount);
        if (widthClass == _cellWidthClass && heightClass == _cellHeightClass) return;

        _cellWidthClass = widthClass;
        _cellHeightClass = heightClass;
        BuildCalendarGrid();
    }

    private void CalendarPaletteChanged()
    {
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    private void BuildCalendarGrid()
    {
        if (_buildingGrid) return;
        _buildingGrid = true;
        try
        {
            MonthTitle.Text = $"{_displayMonth:yyyy年M月}";
            BuildWeekdayHeaders();

            MonthCellsGrid.Children.Clear();
            MonthCellsGrid.RowDefinitions.Clear();
            MonthCellsGrid.ColumnDefinitions.Clear();

            var weekCount = WeeksInMonth(_displayMonth);
            MonthCellsGrid.MinHeight = weekCount * MinimumWeekHeight;
            MonthCellsGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var week = 0; week < weekCount; week++)
            {
                MonthCellsGrid.RowDefinitions.Add(
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            var firstOfMonth = new DateTime(_displayMonth.Year, _displayMonth.Month, 1);
            var mondayOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
            var firstCell = firstOfMonth.AddDays(-mondayOffset);
            var cellWidth = MonthCellsGrid.ActualWidth > 0
                ? MonthCellsGrid.ActualWidth / 7
                : 112;
            var weekHeight = MonthCellsGrid.ActualHeight > 0
                ? Math.Max(MinimumWeekHeight, MonthCellsGrid.ActualHeight / weekCount)
                : MinimumWeekHeight;

            for (var week = 0; week < weekCount; week++)
            {
                var weekStart = firstCell.AddDays(week * 7);
                var segments = BuildWeekSpanSegments(weekStart, weekHeight);
                var laneCount = segments.Count == 0 ? 0 : segments.Max(segment => segment.Lane) + 1;
                var weekGrid = new Grid();
                for (var column = 0; column < WeekdayNames.Length; column++)
                {
                    weekGrid.ColumnDefinitions.Add(
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var date = weekStart.AddDays(column);
                    var cell = CreateDayCell(
                        date,
                        date.Month == _displayMonth.Month,
                        column,
                        cellWidth,
                        weekHeight,
                        laneCount,
                        segments);
                    Grid.SetColumn(cell, column);
                    weekGrid.Children.Add(cell);
                }

                foreach (var segment in segments)
                {
                    var bar = CreateSpanEventBar(segment);
                    Grid.SetColumn(bar, segment.StartColumn);
                    Grid.SetColumnSpan(bar, segment.EndColumn - segment.StartColumn + 1);
                    weekGrid.Children.Add(bar);
                }

                Grid.SetRow(weekGrid, week);
                MonthCellsGrid.Children.Add(weekGrid);
            }

            _cellWidthClass = CellWidthClass(cellWidth);
            _cellHeightClass = CellHeightClass(weekHeight);
        }
        finally
        {
            _buildingGrid = false;
        }
    }

    private List<WeekSpanSegment> BuildWeekSpanSegments(DateTime weekStart, double weekHeight)
    {
        var weekEnd = weekStart.AddDays(6);
        var laneLimit = weekHeight >= 148 ? 3 : weekHeight >= 128 ? 2 : 1;
        var seen = new HashSet<RemoteCalendarEvent>(ReferenceEqualityComparer.Instance);
        var candidates = new List<(RemoteCalendarEvent Event, DateTime Start, DateTime End)>();

        for (var day = 0; day < 7; day++)
        {
            if (!_eventsByDate.TryGetValue(weekStart.AddDays(day), out var events)) continue;
            foreach (var item in events)
            {
                if (!seen.Add(item) || item.Start <= 0) continue;
                var range = GetEventDateRange(item);
                if (range.StartDate == range.EndDate || range.EndDate < weekStart || range.StartDate > weekEnd) continue;
                candidates.Add((item, range.StartDate, range.EndDate));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            var start = left.Start.CompareTo(right.Start);
            if (start != 0) return start;
            var end = right.End.CompareTo(left.End);
            return end != 0 ? end : string.Compare(left.Event.Title, right.Event.Title, StringComparison.OrdinalIgnoreCase);
        });

        var laneEnds = new List<int>();
        var result = new List<WeekSpanSegment>();
        foreach (var candidate in candidates)
        {
            var visibleStart = candidate.Start < weekStart ? weekStart : candidate.Start;
            var visibleEnd = candidate.End > weekEnd ? weekEnd : candidate.End;
            var startColumn = (int)(visibleStart - weekStart).TotalDays;
            var endColumn = (int)(visibleEnd - weekStart).TotalDays;
            var lane = laneEnds.FindIndex(lastColumn => startColumn > lastColumn);
            if (lane < 0)
            {
                if (laneEnds.Count >= laneLimit) continue;
                lane = laneEnds.Count;
                laneEnds.Add(-1);
            }

            laneEnds[lane] = endColumn;
            result.Add(new WeekSpanSegment(
                candidate.Event,
                candidate.Start >= weekStart,
                candidate.End <= weekEnd,
                startColumn,
                endColumn,
                lane));
        }

        return result;
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
                BorderBrush = CalendarGridBorderBrush(),
                BorderThickness = new Thickness(0, 0, 0, 0.5),
                Padding = new Thickness(8, 8, 8, 8),
                Child = ThemeBrushes.Text(new TextBlock
                {
                    Text = WeekdayNames[column],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = SecondaryTextBrush(),
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                }, "HingeM3LabelStyle"),
            };
            Grid.SetColumn(header, column);
            WeekdayHeaderGrid.Children.Add(header);
        }
    }

    private Border CreateDayCell(
        DateTime date,
        bool inCurrentMonth,
        int column,
        double cellWidth,
        double cellHeight,
        int reservedSpanLanes,
        IReadOnlyList<WeekSpanSegment> spanSegments)
    {
        var selected = date.Date == _selectedDate.Date;
        var today = date.Date == DateTime.Now.Date;
        _eventsByDate.TryGetValue(date.Date, out var dayEvents);
        dayEvents ??= new List<RemoteCalendarEvent>();

        var selectedDayBackground = new SolidColorBrush(
            ThemeBrushes.Color(RootGrid, "HingeM3SecondaryContainerBrush"));
        var selectedDayForeground = new SolidColorBrush(
            ThemeBrushes.Color(RootGrid, "HingeM3OnSecondaryContainerBrush"));
        var dayText = ThemeBrushes.Text(new TextBlock
        {
            Text = date.Day.ToString(),
            Style = (Style)Resources["CalendarDateTextStyle"],
            FontWeight = today ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = today ? AccentOnBrush() : selected ? selectedDayForeground : inCurrentMonth ? PrimaryTextBrush() : MutedTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        }, "HingeM3LabelStyle");
        var dayIndicator = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = today ? AccentBrush() : selected ? selectedDayBackground : TransparentBrush(),
            BorderBrush = TransparentBrush(),
            BorderThickness = new Thickness(0),
            Child = dayText,
        };
        var dateContainer = new Grid
        {
            Width = 44,
            Height = DateHeaderHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
        };
        dateContainer.Children.Add(dayIndicator);
        var dayHeader = new Border
        {
            Height = DateHeaderHeight,
            Child = dateContainer,
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DateHeaderHeight) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(dayHeader);

        var visibleSpans = spanSegments.Where(segment =>
            segment.StartColumn <= column && segment.EndColumn >= column).ToArray();
        var localEvents = dayEvents.Where(item => item.Start > 0 && !IsMultiDayEvent(item)).ToList();
        var rowHeight = cellWidth < 116 ? 18 : 20;
        var eventContentTop = reservedSpanLanes == 0
            ? DateHeaderHeight
            : SpanLaneTop + reservedSpanLanes * SpanLaneHeight;
        var availableRows = Math.Max(0, (int)((cellHeight - eventContentTop - 8) / rowHeight));
        var visibleLocalCount = Math.Min(localEvents.Count, availableRows);
        var overflow = dayEvents.Count - visibleSpans.Length - visibleLocalCount;
        if (overflow > 0 && availableRows > 0)
        {
            availableRows--;
            visibleLocalCount = Math.Min(localEvents.Count, availableRows);
        }

        overflow = Math.Max(0, dayEvents.Count - visibleSpans.Length - visibleLocalCount);
        var eventStack = new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(0, eventContentTop - DateHeaderHeight + 2, 0, 0),
        };
        foreach (var item in localEvents.Take(visibleLocalCount))
            eventStack.Children.Add(CreateDayEventVisual(item, cellWidth < 116));
        Grid.SetRow(eventStack, 1);
        content.Children.Add(eventStack);

        var button = new HandCursorButton
        {
            Tag = date,
            Style = (Style)Application.Current.Resources["HingeM3TextButtonStyle"],
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = TransparentBrush(),
            BorderThickness = new Thickness(0),
            Content = content,
        };
        AutomationProperties.SetName(button, $"{date:yyyy年M月d日 dddd}，{dayEvents.Count} 项日程{(today ? "，今天" : "")}{(selected ? "，已选中" : "")}");
        AutomationProperties.SetAutomationId(button, $"Calendar.Day.{date:yyyy-MM-dd}");
        ConfigureDayButtonVisuals(button, dayIndicator, today || selected);
        button.Click += DayButton_Click;

        var cellContent = new Grid();
        cellContent.Children.Add(button);
        if (overflow > 0)
        {
            var moreButton = new HandCursorButton
            {
                Tag = date,
                Content = $"+{overflow} 项",
                Style = (Style)Application.Current.Resources["HingeM3TextButtonStyle"],
                Padding = new Thickness(4, 0, 4, 0),
                MinWidth = 0,
                MinHeight = 24,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 1),
                Background = TransparentBrush(),
            };
            AutomationProperties.SetName(moreButton, $"展开 {date:yyyy年M月d日} 的全部 {dayEvents.Count} 项日程");
            AutomationProperties.SetAutomationId(moreButton, $"Calendar.Overflow.{date:yyyy-MM-dd}");
            moreButton.Click += MoreEventsButton_Click;
            cellContent.Children.Add(moreButton);
        }

        return new Border
        {
            BorderBrush = CalendarGridBorderBrush(),
            BorderThickness = new Thickness(0, 0, 0, 0.5),
            CornerRadius = new CornerRadius(0),
            Background = inCurrentMonth ? TransparentBrush() : MutedCellBrush(),
            Child = cellContent,
        };
    }

    private FrameworkElement CreateDayEventVisual(RemoteCalendarEvent item, bool compact)
    {
        var eventColor = ResolveEventColor(item);
        var tooltip = EventTooltip(item);
        if (item.AllDay)
        {
            var fill = EventContainerColor(eventColor);
            var label = ThemeBrushes.Text(new TextBlock
            {
                Text = $"全天 · {EventTitle(item)}",
                Foreground = new SolidColorBrush(ContrastTextColor(fill)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            }, "HingeM3CaptionStyle");
            var chip = new Border
            {
                Background = new SolidColorBrush(fill),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5, 0, 4, 0),
                MinHeight = 18,
                Child = label,
            };
            ToolTipService.SetToolTip(chip, tooltip);
            return chip;
        }

        var row = new Grid { ColumnSpacing = 5, MinHeight = 18 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(eventColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var eventText = ThemeBrushes.Text(new TextBlock
        {
            Text = CellEventText(item, compact),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = PrimaryTextBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        }, "HingeM3CaptionStyle");
        Grid.SetColumn(eventText, 1);
        row.Children.Add(eventText);
        ToolTipService.SetToolTip(row, tooltip);
        return row;
    }

    private Border CreateSpanEventBar(WeekSpanSegment segment)
    {
        var eventColor = ResolveEventColor(segment.Event);
        var fill = EventContainerColor(eventColor);
        var label = segment.StartsHere
            ? segment.Event.AllDay ? $"全天 · {EventTitle(segment.Event)}" : EventTitle(segment.Event)
            : string.Empty;

        var bar = new Border
        {
            Height = SpanLaneHeight - 1,
            Margin = new Thickness(segment.StartsHere ? 4 : 0, SpanLaneTop + segment.Lane * SpanLaneHeight,
                segment.EndsHere ? 4 : 0, 0),
            Padding = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(fill),
            CornerRadius = new CornerRadius(
                segment.StartsHere ? 6 : 0,
                segment.EndsHere ? 6 : 0,
                segment.EndsHere ? 6 : 0,
                segment.StartsHere ? 6 : 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = ThemeBrushes.Text(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(ContrastTextColor(fill)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            }, "HingeM3CaptionStyle"),
        };
        ToolTipService.SetToolTip(bar, EventTooltip(segment.Event));
        return bar;
    }

    private void MoreEventsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DateTime date ||
            !_eventsByDate.TryGetValue(date.Date, out var events)) return;

        var list = new StackPanel { Spacing = 0 };
        foreach (var item in events)
            list.Children.Add(CreateEventDetailsRow(item));

        var content = new StackPanel
        {
            Spacing = 8,
            MinWidth = 280,
            MaxWidth = 420,
            Children =
            {
                ThemeBrushes.Text(new TextBlock
                {
                    Text = $"{date:yyyy年M月d日} · {events.Count} 项日程",
                    Style = (Style)Application.Current.Resources["HingeM3TitleMediumStyle"],
                    Foreground = PrimaryTextBrush(),
                }),
                new ScrollViewer
                {
                    Content = list,
                    MaxHeight = 420,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            },
        };
        new Flyout { Content = content }.ShowAt(button);
    }

    private FrameworkElement CreateEventDetailsRow(RemoteCalendarEvent item)
    {
        var eventColor = ResolveEventColor(item);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (item.AllDay)
        {
            var fill = EventContainerColor(eventColor);
            heading.Children.Add(new Border
            {
                Background = new SolidColorBrush(fill),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Child = ThemeBrushes.Text(new TextBlock
                {
                    Text = "全天",
                    Foreground = new SolidColorBrush(ContrastTextColor(fill)),
                }, "HingeM3CaptionStyle"),
            });
        }
        heading.Children.Add(ThemeBrushes.Text(new TextBlock
        {
            Text = EventTitle(item),
            Foreground = PrimaryTextBrush(),
            TextWrapping = TextWrapping.Wrap,
        }, "HingeM3RowTitleStyle"));

        var metadata = new List<string>();
        if (!item.AllDay) metadata.Add(FormatTime(item.Start, item.End));
        if (!string.IsNullOrWhiteSpace(item.EventType)) metadata.Add($"类型：{item.EventType.Trim()}");
        if (!string.IsNullOrWhiteSpace(item.CalendarName)) metadata.Add($"日历：{item.CalendarName.Trim()}");
        if (!string.IsNullOrWhiteSpace(item.Location)) metadata.Add($"地点：{item.Location.Trim()}");
        if (!string.IsNullOrWhiteSpace(item.Description)) metadata.Add(item.Description.Trim());

        var body = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                heading,
                ThemeBrushes.Text(new TextBlock
                {
                    Text = string.Join(" · ", metadata),
                    Foreground = SecondaryTextBrush(),
                    TextWrapping = TextWrapping.Wrap,
                }, "HingeM3SupportingStyle"),
            },
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(eventColor),
            VerticalAlignment = VerticalAlignment.Stretch,
        });
        Grid.SetColumn(body, 1);
        row.Children.Add(body);

        return new Border
        {
            BorderBrush = CalendarGridBorderBrush(),
            BorderThickness = new Thickness(0, 0, 0, 0.5),
            Padding = new Thickness(8, 9, 8, 9),
            Child = row,
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
                Content = ThemeBrushes.Text(new TextBlock
                {
                    Text = "当天没有日程。",
                    Padding = new Thickness(4, 16, 4, 16),
                    Foreground = SecondaryTextBrush(),
                }),
            });
            return;
        }

        foreach (var item in events)
        {
            var row = new ListViewItem
            {
                Content = CreateEventDetailsRow(item),
                Padding = new Thickness(0),
                Background = TransparentBrush(),
            };
            AutomationProperties.SetName(row, EventTooltip(item));
            EventsList.Items.Add(row);
        }
    }

    private static string EventTitle(RemoteCalendarEvent item) =>
        string.IsNullOrWhiteSpace(item.Title) ? "未命名日程" : item.Title.Trim();

    private static bool IsMultiDayEvent(RemoteCalendarEvent item)
    {
        var range = GetEventDateRange(item);
        return range.StartDate != range.EndDate;
    }

    private static string CellEventText(RemoteCalendarEvent item, bool compact)
    {
        var title = EventTitle(item);
        if (item.AllDay) return title;
        return compact ? title : $"{FormatTime(item.Start, item.End)} · {title}";
    }

    private string EventTooltip(RemoteCalendarEvent item)
    {
        var details = new List<string>
        {
            EventTitle(item),
            item.AllDay ? "全天" : FormatTime(item.Start, item.End),
        };
        if (!string.IsNullOrWhiteSpace(item.EventType)) details.Add($"类型：{item.EventType.Trim()}");
        if (!string.IsNullOrWhiteSpace(item.CalendarName)) details.Add($"日历：{item.CalendarName.Trim()}");
        if (!string.IsNullOrWhiteSpace(item.Location)) details.Add($"地点：{item.Location.Trim()}");
        return string.Join("\n", details);
    }

    private Color ResolveEventColor(RemoteCalendarEvent item)
    {
        if (item.EventColor is not { } value || value == 0)
            return ThemeBrushes.Color(RootGrid, "HingeM3PrimaryBrush");

        var packed = unchecked((uint)value);
        return Color.FromArgb(
            255,
            (byte)(packed >> 16),
            (byte)(packed >> 8),
            (byte)packed);
    }

    private Color EventContainerColor(Color eventColor) => BlendColor(
        eventColor,
        ThemeBrushes.Color(RootGrid, "HingeM3SurfaceContainerLowBrush"),
        0.16);

    private static Color ContrastTextColor(Color background)
    {
        var luminance = RelativeLuminance(background);
        var blackContrast = (luminance + 0.05) / 0.05;
        var whiteContrast = 1.05 / (luminance + 0.05);
        return blackContrast >= whiteContrast
            ? Color.FromArgb(255, 0, 0, 0)
            : Color.FromArgb(255, 255, 255, 255);
    }

    private static double ContrastRatio(Color left, Color right)
    {
        var first = RelativeLuminance(left);
        var second = RelativeLuminance(right);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static Color BlendColor(Color foreground, Color background, double amount)
    {
        static byte Blend(byte foregroundChannel, byte backgroundChannel, double amount) =>
            (byte)Math.Clamp((int)Math.Round(foregroundChannel * amount + backgroundChannel * (1 - amount)), 0, 255);

        return Color.FromArgb(
            255,
            Blend(foreground.R, background.R, amount),
            Blend(foreground.G, background.G, amount),
            Blend(foreground.B, background.B, amount));
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
            Background = (Brush)Application.Current.Resources["HingeM3SurfaceContainerHighestBrush"],
            BorderBrush = (Brush)Application.Current.Resources["HingeM3OutlineVariantBrush"],
            Foreground = (Brush)Application.Current.Resources["HingeM3OnSurfaceBrush"],
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
        if (await MainWindow.ShowM3ContentDialogAsync(dialog) != ContentDialogResult.Primary || picker.Date is not { } date) return;

        _selectedDate = date.Date.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        BuildCalendarGrid();
        RenderSelectedDay();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var datePicker = new CalendarDatePicker
            {
                Header = "日期",
                Date = new DateTimeOffset(_selectedDate),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources["HingeM3SurfaceContainerHighestBrush"],
                BorderBrush = (Brush)Application.Current.Resources["HingeM3OutlineVariantBrush"],
                Foreground = (Brush)Application.Current.Resources["HingeM3OnSurfaceBrush"],
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
                Style = (Style)Application.Current.Resources["HingeM3TimePickerStyle"],
                Header = "开始时间",
                Time = new TimeSpan(9, 0, 0),
            };
            var endPicker = new TimePicker
            {
                Style = (Style)Application.Current.Resources["HingeM3TimePickerStyle"],
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
            var result = await MainWindow.ShowM3ContentDialogAsync(dialog);
            if (result != ContentDialogResult.Primary) return;

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
            if (_client == null || connection?.IsSessionReady != true)
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
        catch (Exception exception)
        {
            CalendarSummaryText.Text = $"无法打开新建日程：{exception.Message}";
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
        await MainWindow.ShowM3ContentDialogAsync(dialog);
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

    private Brush AccentBrush() => ThemeBrushes.Accent(RootGrid);

    private Brush AccentOnBrush() => ThemeBrushes.OnAccent(RootGrid);

    private Brush CalendarGridBorderBrush() => new SolidColorBrush(
        ThemeBrushes.Color(RootGrid, "HingeM3OutlineVariantBrush")) { Opacity = 0.22 };

    private Brush MutedCellBrush() => ThemeBrushes.MutedSurface(RootGrid);

    private static Brush TransparentBrush() => new SolidColorBrush(Colors.Transparent);

    private void ConfigureDayButtonVisuals(Button button, Border dayIndicator, bool emphasized)
    {
        var normalBackground = TransparentBrush();
        var pointerOverBackground = new SolidColorBrush(ThemeBrushes.Color(RootGrid, "HingeM3OnSurfaceBrush"))
        {
            Opacity = 0.06,
        };
        var pressedBackground = new SolidColorBrush(ThemeBrushes.Color(RootGrid, "HingeM3PrimaryBrush"))
        {
            Opacity = 0.12,
        };
        var foreground = PrimaryTextBrush();
        var focusBorder = AccentBrush();
        button.UseSystemFocusVisuals = true;
        button.FocusVisualPrimaryBrush = focusBorder;
        button.FocusVisualSecondaryBrush = new SolidColorBrush(
            ThemeBrushes.Color(RootGrid, "HingeM3SurfaceBrush"));

        // Every pointer state uses the same date marker; events cannot move its center.
        if (!emphasized)
        {
            button.PointerEntered += (_, _) => dayIndicator.Background = pointerOverBackground;
            button.PointerExited += (_, _) => dayIndicator.Background = normalBackground;
            button.PointerPressed += (_, _) => dayIndicator.Background = pressedBackground;
            button.PointerReleased += (_, _) => dayIndicator.Background =
                button.IsPointerOver ? pointerOverBackground : normalBackground;
            button.PointerCanceled += (_, _) => dayIndicator.Background = normalBackground;
        }
        button.Resources["ButtonBackground"] = normalBackground;
        button.Resources["ButtonBackgroundPointerOver"] = normalBackground;
        button.Resources["ButtonBackgroundPressed"] = normalBackground;
        button.Resources["ButtonBackgroundFocused"] = normalBackground;
        button.Resources["ButtonBorderBrush"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPointerOver"] = TransparentBrush();
        button.Resources["ButtonBorderBrushPressed"] = TransparentBrush();
        button.Resources["ButtonBorderBrushFocused"] = focusBorder;
        button.Resources["ButtonBorderThicknessFocused"] = new Thickness(1.5);
        button.Foreground = foreground;
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonForegroundFocused"] = foreground;
    }

}
