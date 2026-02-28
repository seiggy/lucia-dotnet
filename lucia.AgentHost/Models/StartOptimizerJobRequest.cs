using lucia.Agents.Services;

namespace lucia.AgentHost.Models;

/// <summary>Request to start an optimizer job.</summary>
public sealed record StartOptimizerJobRequest
{
    public required string SkillId { get; init; }
    public required string EmbeddingModel { get; init; }
    public required IReadOnlyList<OptimizationTestCase> TestCases { get; init; }
}
