using lucia.Agents.Services;

namespace lucia.AgentHost.Extensions;

/// <summary>Response describing the current status of an optimization job.</summary>
public sealed record JobStatusResponse
{
    public required string JobId { get; init; }
    public required string SkillId { get; init; }
    public required string EmbeddingModel { get; init; }
    public int TestCaseCount { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public OptimizationProgress? Progress { get; init; }
    public OptimizationResult? Result { get; init; }
    public string? Error { get; init; }
}
