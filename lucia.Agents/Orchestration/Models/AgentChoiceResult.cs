using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Result emitted by <see cref="RouterExecutor"/> describing which agent to invoke next.
/// </summary>
public sealed class AgentChoiceResult
{
    /// <summary>
    /// The primary agent identifier to route to.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }

    /// <summary>
    /// Model-provided reasoning for observability.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; set; }

    /// <summary>
    /// Optional additional agents for parallel execution scenarios.
    /// </summary>
    [JsonPropertyName("additionalAgents")]
    public List<string>? AdditionalAgents { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0) supplied by the route model.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 1.0;
}
