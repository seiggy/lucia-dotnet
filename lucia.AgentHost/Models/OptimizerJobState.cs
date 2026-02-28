using lucia.Agents.Services;

namespace lucia.AgentHost.Models;

/// <summary>Mutable state for a running/completed optimizer job.</summary>
public sealed class OptimizerJobState
{
    public required string JobId { get; init; }
    public required string SkillId { get; init; }
    public required string EmbeddingModel { get; init; }
    public int TestCaseCount { get; init; }
    public OptimizerJobStatus Status { get; set; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public OptimizationProgress? LatestProgress { get; set; }
    public OptimizationResult? Result { get; set; }
    public string? Error { get; set; }
    internal CancellationTokenSource CancellationSource { get; init; } = new();
}
