using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

/// <summary>
/// Aggregated scores for a single parameter profile on a single model.
/// </summary>
public sealed class HtmlProfileScore
{
    [JsonPropertyName("profileName")]
    public required string ProfileName { get; init; }

    [JsonPropertyName("parameters")]
    public required HtmlParameterData Parameters { get; init; }

    [JsonPropertyName("avgOverall")]
    public double? AvgOverall { get; init; }

    [JsonPropertyName("avgToolSelection")]
    public double AvgToolSelection { get; init; }

    [JsonPropertyName("avgToolSuccess")]
    public double AvgToolSuccess { get; init; }

    [JsonPropertyName("avgToolEfficiency")]
    public double AvgToolEfficiency { get; init; }

    [JsonPropertyName("avgTaskCompletion")]
    public double? AvgTaskCompletion { get; init; }

    [JsonPropertyName("passRate")]
    public double PassRate { get; init; }

    [JsonPropertyName("avgLatencyMs")]
    public double AvgLatencyMs { get; init; }
}
