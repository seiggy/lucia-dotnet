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

/// <summary>
/// Point-in-time performance data for a single evaluation run.
/// </summary>
public sealed class PerformanceSnapshot
{
    /// <summary>End-to-end evaluation duration including all tool calls.</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>When this measurement was taken.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Aggregated performance statistics across multiple runs for a single model.
/// </summary>
public sealed class ModelPerformanceSummary
{
    public required string ModelName { get; init; }
    public required int RunCount { get; init; }
    public required TimeSpan MeanLatency { get; init; }
    public required TimeSpan MedianLatency { get; init; }
    public required TimeSpan P95Latency { get; init; }
    public required TimeSpan MinLatency { get; init; }
    public required TimeSpan MaxLatency { get; init; }

    /// <summary>
    /// Creates an aggregated summary from a collection of performance snapshots.
    /// </summary>
    public static ModelPerformanceSummary FromSnapshots(string modelName, IReadOnlyList<PerformanceSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new ModelPerformanceSummary
            {
                ModelName = modelName,
                RunCount = 0,
                MeanLatency = TimeSpan.Zero,
                MedianLatency = TimeSpan.Zero,
                P95Latency = TimeSpan.Zero,
                MinLatency = TimeSpan.Zero,
                MaxLatency = TimeSpan.Zero
            };
        }

        var sorted = snapshots.OrderBy(s => s.TotalDuration).ToList();
        var totalMs = sorted.Sum(s => s.TotalDuration.TotalMilliseconds);
        var medianIndex = sorted.Count / 2;
        var p95Index = (int)(sorted.Count * 0.95);

        return new ModelPerformanceSummary
        {
            ModelName = modelName,
            RunCount = sorted.Count,
            MeanLatency = TimeSpan.FromMilliseconds(totalMs / sorted.Count),
            MedianLatency = sorted[medianIndex].TotalDuration,
            P95Latency = sorted[Math.Min(p95Index, sorted.Count - 1)].TotalDuration,
            MinLatency = sorted[0].TotalDuration,
            MaxLatency = sorted[^1].TotalDuration
        };
    }
}
