using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lucia.Agents.Training;

/// <summary>
/// OpenTelemetry instrumentation for trace capture and export operations.
/// </summary>
public static class TraceCaptureTelemetry
{
    public static readonly ActivitySource Source = new("Lucia.TraceCapture", "1.0.0");
    
    private static readonly Meter _meter = new("Lucia.TraceCapture", "1.0.0");

    public static readonly Counter<long> TracesCaptured = _meter.CreateCounter<long>(
        "lucia.traces.captured", "traces", "Number of conversation traces captured");

    public static readonly Counter<long> StorageErrors = _meter.CreateCounter<long>(
        "lucia.traces.storage_errors", "errors", "Number of trace storage failures");

    public static readonly Histogram<double> CaptureLatency = _meter.CreateHistogram<double>(
        "lucia.traces.capture_latency_ms", "ms", "Time to persist a trace document");

    public static readonly Histogram<double> ExportDuration = _meter.CreateHistogram<double>(
        "lucia.exports.duration_ms", "ms", "Time to generate a JSONL export");

    public static readonly Counter<long> ExportRecordCount = _meter.CreateCounter<long>(
        "lucia.exports.record_count", "records", "Number of records exported");

    // Standard tag names
    public const string TagTraceId = "lucia.trace.id";
    public const string TagAgentId = "lucia.agent.id";
    public const string TagLabelStatus = "lucia.trace.label";
    public const string TagExportId = "lucia.export.id";
}
