using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// In-memory thread implementation for orchestrator agent.
/// Used for Phase 3 MVP. Phase 4 will introduce Redis-backed threads for persistence.
/// </summary>
internal sealed class OrchestratorInMemoryThread : InMemoryAgentThread
{
    internal OrchestratorInMemoryThread() { }

    internal OrchestratorInMemoryThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}
