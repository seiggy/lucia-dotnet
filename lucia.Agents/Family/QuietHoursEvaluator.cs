namespace lucia.Agents.Family;

public static class QuietHoursEvaluator
{
    /// <summary>
    /// True when <paramref name="utcNow"/> falls in the window, which includes its start and
    /// excludes its end. A window whose end is before its start spans midnight.
    /// </summary>
    public static bool IsActive(QuietHoursWindow window, DateTimeOffset utcNow, TimeZoneInfo? fallbackZone = null)
    {
        var zone = window.TimeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(window.TimeZoneId, out var configured)
            ? configured
            : fallbackZone ?? TimeZoneInfo.Local;
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
        return window.Start <= window.End
            ? local >= window.Start && local < window.End
            : local >= window.Start || local < window.End;
    }
}
