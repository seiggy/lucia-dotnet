namespace lucia.Wyoming.Telemetry;

/// <summary>
/// Timing record for a single stage of the voice processing pipeline.
/// </summary>
public sealed record PipelineStageTiming
{
    public required string Name { get; init; }
    public required double DurationMs { get; init; }
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
}
