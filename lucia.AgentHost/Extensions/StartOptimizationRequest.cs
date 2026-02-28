using lucia.Agents.Services;

namespace lucia.AgentHost.Extensions;

/// <summary>Request body to start an optimization job.</summary>
public sealed record StartOptimizationRequest
{
    public required string EmbeddingModel { get; init; }
    public required List<OptimizationTestCase> TestCases { get; init; }
}
