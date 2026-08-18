using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlPerformanceData
{
    [JsonPropertyName("runCount")]
    public int RunCount { get; init; }

    [JsonPropertyName("meanLatencyMs")]
    public double MeanLatencyMs { get; init; }

    [JsonPropertyName("medianLatencyMs")]
    public double MedianLatencyMs { get; init; }

    [JsonPropertyName("p95LatencyMs")]
    public double P95LatencyMs { get; init; }

    [JsonPropertyName("minLatencyMs")]
    public double MinLatencyMs { get; init; }

    [JsonPropertyName("maxLatencyMs")]
    public double MaxLatencyMs { get; init; }
}
