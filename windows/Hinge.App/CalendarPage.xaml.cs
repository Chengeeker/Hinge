using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class CalendarPage : Page
{
    private WorkspaceRemoteClient? _client;
    private Func<SessionConnection?>? _connectionProvider;
    private SessionConnection? _connection;
    private IReadOnlyList<RemoteCalendarEvent> _events = Array.Empty<RemoteCalendarEvent>();
    private readonly HashSet<DateTime> _eventDates = new();
    private int _loadVersion;

    public CalendarPage()
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
        _events = Array.Empty<RemoteCalendarEvent>();
        _eventDates.Clear();
        EventsList.Items.Clear();

        if (_client == null || connection?.State != SessionState.Connected)
        {
            SetStatus("等待连接设备", "请先在首页连接 Android 设备。", InfoBarSeverity.Informational);
            SelectedDateText.Text = "当天日程";
            return;
        }

        SetStatus("正在读取日历", "从手机日历读取授权范围内的日程…", InfoBarSeverity.Informational);
        try
        {
            _events = await _client.LoadCalendarAsync(connection);
            if (version != _loadVersion) return;
            foreach (var item in _events)
            {
                _eventDates.Add(ToLocalDate(item.Start));
            }
            if (MonthCalendar.SelectedDates.Count == 0)
            {
                MonthCalendar.SelectedDates.Add(DateTimeOffset.Now);
            }
            RenderEvents(DateTime.Now.Date);
            SetStatus("日历已更新", $"读取到 {_events.Count} 条日程。", InfoBarSeverity.Success);
            DispatcherQueue.TryEnqueue(RefreshDayMarkers);
        }
        catch (Exception exception)
        {
            SetStatus("读取日历失败", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void MonthCalendar_DayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
    {
        ApplyDayMarker(args.Item);
    }

    private void ApplyDayMarker(CalendarViewDayItem item)
    {
        var brush = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as SolidColorBrush;
        var hasEvents = _eventDates.Contains(item.Date.Date);
        // Use the native CalendarView density indicator as a compact three-dot
        // marker. It stays aligned with the day cell and avoids overlaying a
        // custom shape on top of the calendar's selection visuals.
        var markerColor = brush?.Color ?? Microsoft.UI.Colors.DodgerBlue;
        item.SetDensityColors(hasEvents
            ? new[] { markerColor, markerColor, markerColor }
            : Array.Empty<Windows.UI.Color>());
    }

    private void RefreshDayMarkers()
    {
        MonthCalendar.UpdateLayout();
        ApplyMarkersInTree(MonthCalendar);
    }

    private void ApplyMarkersInTree(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is CalendarViewDayItem dayItem) ApplyDayMarker(dayItem);
            ApplyMarkersInTree(child);
        }
    }

    private void MonthCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (args.AddedDates.Count > 0)
        {
            RenderEvents(args.AddedDates[0].Date);
        }
    }

    private void RenderEvents(DateTime date)
    {
        SelectedDateText.Text = $"{date:yyyy年M月d日} · 日程";
        EventsList.Items.Clear();
        var events = _events
            .Where(item => ToLocalDate(item.Start) == date.Date)
            .OrderBy(item => item.AllDay ? 0 : 1)
            .ThenBy(item => item.Start)
            .ToList();
        foreach (var item in events)
        {
            var details = string.IsNullOrWhiteSpace(item.Location) ? string.Empty : $"\n地点：{item.Location}";
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
                            Text = item.Title,
                            FontSize = 16,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = (item.AllDay ? "全天" : FormatTime(item.Start, item.End)) + details,
                            FontSize = 14,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            });
        }
        if (events.Count == 0)
        {
            EventsList.Items.Add(new ListViewItem
            {
                IsHitTestVisible = false,
                Content = new TextBlock
                {
                    Text = "当天没有日程。",
                    Padding = new Thickness(4, 16, 4, 16),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                }
            });
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(_connectionProvider?.Invoke() ?? _connection);

    private void SetStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
    }

    private static DateTime ToLocalDate(long milliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().Date;

    private static string FormatTime(long start, long? end)
    {
        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(start).ToLocalTime();
        if (end is not { } endValue) return startTime.ToString("HH:mm");
        var endTime = DateTimeOffset.FromUnixTimeMilliseconds(endValue).ToLocalTime();
        return $"{startTime:HH:mm}–{endTime:HH:mm}";
    }
}
