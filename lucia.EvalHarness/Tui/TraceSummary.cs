using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Tui;

internal sealed class TraceSummary
{
    [JsonPropertyName("total_tests")]
    public required int TotalTests { get; init; }

    [JsonPropertyName("passed")]
    public required int Passed { get; init; }

    [JsonPropertyName("failed")]
    public required int Failed { get; init; }

    [JsonPropertyName("overall_score")]
    public required double? OverallScore { get; init; }

    [JsonPropertyName("overall_score_status")]
    public string? OverallScoreStatus { get; init; }

    [JsonPropertyName("mean_latency_ms")]
    public required double? MeanLatencyMs { get; init; }
}
