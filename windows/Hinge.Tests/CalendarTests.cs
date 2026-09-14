using Hinge.Core;

namespace Hinge.Tests;

public class CalendarTests
{
    [Fact]
    public void AllDayEvent_UsesUtcDateAndExclusiveEnd()
    {
        var start = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var end = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var range = CalendarEventDateRange.GetInclusiveDates(start, end, allDay: true);

        Assert.Equal(new DateTime(2026, 9, 14), range.StartDate);
        Assert.Equal(new DateTime(2026, 9, 14), range.EndDate);
    }

    [Fact]
    public void MultiDayAllDayEvent_KeepsEveryRealDayOnly()
    {
        var start = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var end = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var range = CalendarEventDateRange.GetInclusiveDates(start, end, allDay: true);

        Assert.Equal(new DateTime(2026, 9, 14), range.StartDate);
        Assert.Equal(new DateTime(2026, 9, 15), range.EndDate);
    }
}
