using lucia.Agents.Family;

namespace lucia.Tests.Family;

public sealed class QuietHoursEvaluatorTests
{
    private static QuietHoursWindow Window(int startHour, int endHour, string? zone = "UTC") =>
        new() { Start = new TimeOnly(startHour, 0), End = new TimeOnly(endHour, 0), TimeZoneId = zone };

    [Theory]
    [InlineData(22, true)]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(12, false)]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public void OvernightWindow_SpansMidnight(int utcHour, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 6, utcHour, 30, 0, TimeSpan.Zero);

        Assert.Equal(expected, QuietHoursEvaluator.IsActive(Window(21, 7), now));
    }

    [Theory]
    [InlineData(13, true)]
    [InlineData(15, false)]
    [InlineData(12, false)]
    public void SameDayWindow_IsHalfOpen(int utcHour, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 6, utcHour, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, QuietHoursEvaluator.IsActive(Window(13, 15), now));
    }

    [Fact]
    public void Window_UsesTheConfiguredTimeZone()
    {
        // 02:30 UTC is 21:30 the previous evening in New York (EST, UTC-5).
        var now = new DateTimeOffset(2026, 11, 7, 2, 30, 0, TimeSpan.Zero);
        Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(EasternTimeZone.Id, out _));

        Assert.True(QuietHoursEvaluator.IsActive(Window(21, 23, EasternTimeZone.Id), now));
        Assert.False(QuietHoursEvaluator.IsActive(Window(21, 23, "UTC"), now));
    }
}
