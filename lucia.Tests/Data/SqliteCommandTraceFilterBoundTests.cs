using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

/// <summary>
/// Tests for SqliteCommandTraceRepository.ToUtcBoundString — the filter-bound formatter
/// that must treat DateTimeKind.Unspecified as UTC to avoid host-timezone drift on
/// date-only API parameters (which ASP.NET Core binds as Unspecified).
/// </summary>
public sealed class SqliteCommandTraceFilterBoundTests
{
    // ── Kind.Unspecified — the primary regression guard ───────────────────────

    [Fact]
    public void ToUtcBoundString_UnspecifiedKind_TreatedAsUtcNotLocal()
    {
        // ASP.NET Core binds fromDate=2025-06-15 as Kind.Unspecified.
        // This MUST produce midnight UTC, not midnight-shifted-by-host-tz.
        var unspecified = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(unspecified);

        // Regardless of the machine's local timezone, Unspecified is treated as UTC.
        Assert.Equal("2025-06-15T00:00:00.0000000+00:00", result);
    }

    [Fact]
    public void ToUtcBoundString_UnspecifiedKind_NonMidnight_NoShift()
    {
        var unspecified = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Unspecified);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(unspecified);

        Assert.Equal("2025-06-15T14:30:00.0000000+00:00", result);
    }

    // ── Kind.Utc — must remain stable (no double-conversion) ─────────────────

    [Fact]
    public void ToUtcBoundString_UtcKind_RemainsUnchanged()
    {
        var utc = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(utc);

        Assert.Equal("2025-06-15T00:00:00.0000000+00:00", result);
    }

    // ── Format contract — suffix must be +00:00, not Z ────────────────────────

    [Fact]
    public void ToUtcBoundString_ProducesDateTimeOffsetFormat_NotZSuffix()
    {
        // Stored timestamps use DateTimeOffset.ToString("O") → +00:00.
        // Filter bounds must use the SAME suffix so text comparisons are exact.
        var dt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(dt);

        Assert.EndsWith("+00:00", result);
        Assert.DoesNotContain("Z", result);
    }
}
