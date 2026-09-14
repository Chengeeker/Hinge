namespace Hinge.Core;

public static class CalendarEventDateRange
{
    public static (DateTime StartDate, DateTime EndDate) GetInclusiveDates(
        long startMilliseconds,
        long? endMilliseconds,
        bool allDay,
        TimeZoneInfo? localTimeZone = null)
    {
        var safeEnd = endMilliseconds is { } end && end > startMilliseconds
            ? end - 1
            : startMilliseconds;

        if (allDay)
        {
            // Android stores all-day boundaries at UTC midnight and the end is exclusive.
            // Converting those values to the PC's local zone shifts the exclusive boundary
            // into the following morning in UTC+ time zones and incorrectly adds a day.
            return (
                UnspecifiedDate(DateTimeOffset.FromUnixTimeMilliseconds(startMilliseconds).UtcDateTime),
                UnspecifiedDate(DateTimeOffset.FromUnixTimeMilliseconds(safeEnd).UtcDateTime));
        }

        var zone = localTimeZone ?? TimeZoneInfo.Local;
        return (
            UnspecifiedDate(TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeMilliseconds(startMilliseconds), zone).DateTime),
            UnspecifiedDate(TimeZoneInfo.ConvertTime(
                DateTimeOffset.FromUnixTimeMilliseconds(safeEnd), zone).DateTime));
    }

    private static DateTime UnspecifiedDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
}
