using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

/// <summary>
/// Profile comparison data for a single model across multiple parameter profiles.
/// </summary>
public sealed class HtmlProfileComparisonGroup
{
    [JsonPropertyName("modelName")]
    public required string ModelName { get; init; }

    [JsonPropertyName("profiles")]
    public required List<HtmlProfileScore> Profiles { get; init; }
}
