namespace lucia.Tests.Family;

/// <summary>
/// A US Eastern time zone ID that resolves on this platform. IANA names need ICU on Windows,
/// which globalization-invariant test runs (the pre-push hook) do not load.
/// </summary>
internal static class EasternTimeZone
{
    public static string Id { get; } =
        TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out _) ? "America/New_York" : "Eastern Standard Time";
}
