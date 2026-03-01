using System.Collections.Concurrent;
using System.Diagnostics;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using OpenTelemetry;

namespace lucia.AgentHost.Services;

/// <summary>
/// OpenTelemetry processor that captures completed <c>Lucia.*</c> activities
/// and groups them by OTEL trace ID. The <see cref="lucia.Agents.Training.TraceCaptureObserver"/>
/// pulls collected spans when finalizing a <see cref="ConversationTrace"/>.
/// </summary>
public sealed class SpanCollectorProcessor : BaseProcessor<Activity>, ISpanCollector
{
    private const string LuciaSourcePrefix = "Lucia.";

    private readonly ConcurrentDictionary<string, ConcurrentBag<TracedSpan>> _spansByTraceId = new();

    public override void OnEnd(Activity data)
    {
        // Only capture spans from our custom Lucia.* activity sources
        if (data.Source.Name is null || !data.Source.Name.StartsWith(LuciaSourcePrefix, StringComparison.Ordinal))
            return;

        var traceId = data.TraceId.ToString();
        if (string.IsNullOrEmpty(traceId))
            return;

        var span = new TracedSpan
        {
            SpanId = data.SpanId.ToString(),
            ParentSpanId = data.ParentSpanId == default ? null : data.ParentSpanId.ToString(),
            OperationName = data.OperationName,
            Source = data.Source.Name,
            StartTimeUtc = data.StartTimeUtc,
            DurationMs = data.Duration.TotalMilliseconds
        };

        foreach (var tag in data.Tags)
        {
            if (tag.Value is not null)
            {
                span.Tags[tag.Key] = tag.Value;
            }
        }

        var bag = _spansByTraceId.GetOrAdd(traceId, _ => []);
        bag.Add(span);
    }

    /// <summary>
    /// Retrieves and removes all collected spans for the given OTEL trace ID.
    /// Returns an empty list if no spans were captured.
    /// </summary>
    public List<TracedSpan> TakeSpans(string traceId)
    {
        if (_spansByTraceId.TryRemove(traceId, out var bag))
        {
            return [.. bag];
        }

        return [];
    }
}
