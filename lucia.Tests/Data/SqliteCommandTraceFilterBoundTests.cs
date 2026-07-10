using lucia.Data.Sqlite;

namespace lucia.Tests.Data;

/// <summary>
/// Tests for SqliteCommandTraceRepository.ToUtcBoundString — the filter-bound formatter
/// that must correctly handle all three DateTimeKind values.
/// </summary>
public sealed class SqliteCommandTraceFilterBoundTests
{
    // ── Kind.Unspecified — reinterpreted as UTC, no shift ────────────────────

    [Fact]
    public void ToUtcBoundString_UnspecifiedKind_TreatedAsUtcNotLocal()
    {
        // ASP.NET Core binds fromDate=2025-06-15 as Kind.Unspecified.
        // Must produce midnight UTC regardless of host timezone.
        var unspecified = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(unspecified);

        Assert.Equal("2025-06-15T00:00:00.0000000+00:00", result);
    }

    [Fact]
    public void ToUtcBoundString_UnspecifiedKind_NonMidnight_NoShift()
    {
        var unspecified = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Unspecified);

        var result = SqliteCommandTraceRepository.ToUtcBoundString(unspecified);

        Assert.Equal("2025-06-15T14:30:00.0000000+00:00", result);
    }

    // ── Kind.Local — CONVERTED (shifted) to UTC, not relabeled ───────────────

    [Fact]
    public void ToUtcBoundString_LocalKind_ConvertsToUtc()
    {
        // Construct a Local DateTime and derive the expected UTC instant the same way
        // ToUniversalTime() does — keeps the test timezone-agnostic on any CI host.
        var local = new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Local);
        var expectedUtcInstant = local.ToUniversalTime();

        var result = SqliteCommandTraceRepository.ToUtcBoundString(local);

        // The result must equal the actual UTC conversion of the local time.
        var parsedBack = DateTimeOffset.Parse(result).UtcDateTime;
        Assert.Equal(expectedUtcInstant, parsedBack);
    }

    [Fact]
    public void ToUtcBoundString_LocalKind_DiffersFromUnspecifiedWhenOffsetNonZero()
    {
        // A Local and an Unspecified DateTime with the same wall-clock digits must
        // produce the SAME string only on UTC hosts. On any offset host they differ,
        // proving Local is converted while Unspecified is reinterpreted.
        // We can assert structural correctness (correct UTC instant) without knowing host tz.
        var wallClock = new DateTime(2025, 6, 15, 10, 0, 0);
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Local);
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        var localResult = SqliteCommandTraceRepository.ToUtcBoundString(local);
        var unspecifiedResult = SqliteCommandTraceRepository.ToUtcBoundString(unspecified);

        // Unspecified always yields the wall-clock digits as UTC.
        Assert.Equal("2025-06-15T10:00:00.0000000+00:00", unspecifiedResult);

        // Local yields the actual UTC conversion — equals what ToUniversalTime() produces.
        var expectedLocalUtc = new DateTimeOffset(local.ToUniversalTime()).ToString("O");
        Assert.Equal(expectedLocalUtc, localResult);
    }

    // ── Kind.Utc — used as-is, no double-conversion ───────────────────────────

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
