using lucia.Agents.Services;

namespace lucia.AgentHost.Extensions;

/// <summary>API response describing an optimizable skill.</summary>
public sealed record OptimizableSkillInfo
{
    public required string SkillId { get; init; }
    public required string DisplayName { get; init; }
    public required string ConfigSection { get; init; }
    public required HybridMatchOptions CurrentParams { get; init; }
}
