namespace lucia.Agents.Training.Models;

/// <summary>
/// Represents a single OTEL-style span captured during an orchestration request.
/// Collected by <see cref="SpanCollectorProcessor"/> and attached to the
/// <see cref="ConversationTrace"/> on finalization.
/// </summary>
public sealed class TracedSpan
{
    public required string SpanId { get; set; }

    public string? ParentSpanId { get; set; }

    public required string OperationName { get; set; }

    public required string Source { get; set; }

    public DateTime StartTimeUtc { get; set; }

    public double DurationMs { get; set; }

    /// <summary>
    /// Flat tag dictionary mirroring OTEL span attributes (e.g. cache.result, agent.id).
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = [];
}
