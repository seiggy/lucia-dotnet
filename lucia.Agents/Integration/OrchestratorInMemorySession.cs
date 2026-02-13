using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// In-memory session implementation for orchestrator agent.
/// Used for Phase 3 MVP. Phase 4 will introduce Redis-backed sessions for persistence.
/// </summary>
internal sealed class OrchestratorInMemorySession : InMemoryAgentSession
{
    internal OrchestratorInMemorySession() { }

    internal OrchestratorInMemorySession(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }

    /// <summary>
    /// Exposes the protected internal Serialize method from the base class
    /// so that OrchestratorAIAgent can serialize session state.
    /// </summary>
    internal new JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
        => base.Serialize(jsonSerializerOptions);
}
