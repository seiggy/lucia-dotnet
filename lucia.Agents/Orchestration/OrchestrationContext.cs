using System.Collections.Generic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Context preserved across workflow execution.
/// </summary>
public sealed class OrchestrationContext
{
    /// <summary>
    /// Conversation identifier (A2A contextId).
    /// </summary>
    public required string ConversationId { get; set; }

    /// <summary>
    /// Agent sessions for context preservation keyed by agentId.
    /// </summary>
    public Dictionary<string, AgentSession> AgentSessions { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Previously invoked agent identifier (for handoffs).
    /// </summary>
    public string? PreviousAgentId { get; set; }

    /// <summary>
    /// Conversation history (last N turns).
    /// </summary>
    public List<ChatMessage> History { get; init; } = [];
}
