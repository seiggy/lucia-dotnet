using System.Diagnostics;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Captures per-evaluation performance metrics including latency,
/// time-to-first-token, and token throughput.
/// </summary>
public sealed class PerformanceCollector
{
    /// <summary>
    /// Wraps an async evaluation call and captures timing metrics.
    /// </summary>
    public async Task<PerformanceSnapshot> MeasureAsync(Func<Task> evaluation, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await evaluation();
        sw.Stop();

        return new PerformanceSnapshot
        {
            TotalDuration = sw.Elapsed,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Wraps an async evaluation call that returns a result and captures timing.
    /// </summary>
    public async Task<(T Result, PerformanceSnapshot Perf)> MeasureAsync<T>(
        Func<Task<T>> evaluation, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = await evaluation();
        sw.Stop();

        return (result, new PerformanceSnapshot
        {
            TotalDuration = sw.Elapsed,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
