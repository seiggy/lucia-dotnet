using lucia.EvalHarness.DataPipeline.Models;

namespace lucia.EvalHarness.DataPipeline;

/// <summary>
/// Interface for data sources that can generate evaluation scenarios.
/// Implementations pull from GitHub issues, conversation traces, manual datasets, etc.
/// </summary>
public interface IEvalScenarioSource
{
    /// <summary>
    /// Retrieves all scenarios from this data source.
    /// </summary>
    /// <param name="filter">Optional filter criteria for scenario selection.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of evaluation scenarios.</returns>
    Task<List<EvalScenario>> GetScenariosAsync(ScenarioFilter? filter = null, CancellationToken ct = default);
}

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
