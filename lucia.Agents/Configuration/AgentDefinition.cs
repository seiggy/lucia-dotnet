using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Configuration;

/// <summary>
/// User-defined agent configuration stored in MongoDB.
/// Loaded at runtime to dynamically construct agents with MCP tools.
/// </summary>
public sealed class AgentDefinition
{
    /// <summary>
    /// Unique identifier / routing key (e.g., "research-agent").
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Agent name used for routing (must be unique across all agents).
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Human-readable display name (e.g., "Research Agent").
    /// </summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// Description of the agent's purpose — used by the router for agent selection.
    /// </summary>
    public string Description { get; set; } = default!;

    /// <summary>
    /// System prompt / instructions for the agent's LLM.
    /// </summary>
    public string Instructions { get; set; } = default!;

    /// <summary>
    /// Specific MCP tools assigned to this agent (per-tool granularity).
    /// Each reference selects an individual tool from a registered MCP server.
    /// </summary>
    public List<AgentToolReference> Tools { get; set; } = [];

    /// <summary>
    /// Optional connection name for the AI model. When set, a keyed IChatClient
    /// is resolved from DI. If null, the default model is used.
    /// </summary>
    public string? ModelConnectionName { get; set; }

    /// <summary>
    /// Optional ID of a <see cref="ModelProvider"/> (purpose = Embedding) used by
    /// this agent's skills for vector search. When null, the system default
    /// embedding provider is used.
    /// </summary>
    public string? EmbeddingProviderName { get; set; }

    /// <summary>
    /// Whether this agent is enabled for routing and invocation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Indicates the definition was seeded by the system for a built-in agent.
    /// Built-in definitions cannot be deleted, only customized.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Indicates the agent is hosted in a separate process (e.g. A2AHost plugin).
    /// Remote agents register themselves on startup and should not be instantiated
    /// by the main AgentHost's dynamic loader or initialization service.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Indicates this agent is the orchestrator. The orchestrator is not instantiated
    /// by the dynamic agent loader and has its own specialized endpoint mapping.
    /// </summary>
    public bool IsOrchestrator { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string CollectionName = "agent_definitions";
}
