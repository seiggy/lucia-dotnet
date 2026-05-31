using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlModelData
{
    [JsonPropertyName("modelName")]
    public required string ModelName { get; init; }

    [JsonPropertyName("overallScore")]
    public double OverallScore { get; init; }

    [JsonPropertyName("toolSelectionScore")]
    public double ToolSelectionScore { get; init; }

    [JsonPropertyName("toolSuccessScore")]
    public double ToolSuccessScore { get; init; }

    [JsonPropertyName("toolEfficiencyScore")]
    public double ToolEfficiencyScore { get; init; }

    [JsonPropertyName("taskCompletionScore")]
    public double TaskCompletionScore { get; init; }

    [JsonPropertyName("testCaseCount")]
    public int TestCaseCount { get; init; }

    [JsonPropertyName("passedCount")]
    public int PassedCount { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HtmlParameterData? Parameters { get; init; }

    [JsonPropertyName("performance")]
    public required HtmlPerformanceData Performance { get; init; }

    [JsonPropertyName("testCases")]
    public required List<HtmlTestCaseData> TestCases { get; init; }
}
