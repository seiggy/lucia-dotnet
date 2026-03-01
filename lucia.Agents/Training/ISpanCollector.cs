using lucia.Agents.Training.Models;

namespace lucia.Agents.Training;

/// <summary>
/// Abstraction for collecting OTEL spans grouped by trace ID.
/// Implemented by the OTEL <c>BaseProcessor&lt;Activity&gt;</c> in the host project
/// and injected into <see cref="TraceCaptureObserver"/> for span attachment.
/// </summary>
public interface ISpanCollector
{
    /// <summary>
    /// Retrieves and removes all collected spans for the given OTEL trace ID.
    /// </summary>
    List<TracedSpan> TakeSpans(string traceId);
}
