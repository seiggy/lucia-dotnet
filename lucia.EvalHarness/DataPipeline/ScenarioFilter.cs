namespace lucia.EvalHarness.DataPipeline;

/// <summary>
/// Filter criteria for scenario retrieval.
/// </summary>
public sealed class ScenarioFilter
{
    /// <summary>
    /// Filter by category (e.g., "control", "query", "stt-robustness").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Filter by agent (e.g., "light", "climate", "orchestration").
    /// </summary>
    public string? Agent { get; set; }

    /// <summary>
    /// Only include scenarios from a specific source type (e.g., "github", "traces").
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// Minimum number of scenarios to retrieve.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Include only scenarios marked as errors/failures for regression testing.
    /// </summary>
    public bool ErrorsOnly { get; set; }
}
