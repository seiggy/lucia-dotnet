namespace lucia.Agents.Family;

/// <summary>
/// Budget periods in the server's local time zone, shared by usage reporting and enforcement.
/// </summary>
public static class UsagePeriods
{
    public static DateTime StartOfDayUtc(TimeProvider timeProvider)
    {
        var zone = timeProvider.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), zone);
    }

    public static DateTime StartOfMonthUtc(TimeProvider timeProvider)
    {
        var zone = timeProvider.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(firstOfMonth, zone);
    }
}
