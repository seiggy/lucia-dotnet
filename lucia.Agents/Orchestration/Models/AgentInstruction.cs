using System.Text.Json.Serialization;

namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// A decomposed sub-prompt for a specific agent, extracted by the router
/// from a multi-domain user request.
/// </summary>
public sealed class AgentInstruction
{
    /// <summary>
    /// The agent ID this instruction targets. Must match a catalog agent ID.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }

    /// <summary>
    /// The focused, standalone sub-prompt containing only the part of the
    /// user's request relevant to this agent.
    /// </summary>
    [JsonPropertyName("instruction")]
    public required string Instruction { get; set; }
}
