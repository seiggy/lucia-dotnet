namespace lucia.Tests.TestDoubles;

/// <summary>
/// A TimeProvider whose clock can be advanced manually for deterministic TTL tests.
/// </summary>
public sealed class AdvancingTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan interval) => _now += interval;

    public override DateTimeOffset GetUtcNow() => _now;
}
