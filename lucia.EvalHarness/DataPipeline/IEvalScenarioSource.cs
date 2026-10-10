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
