using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Default in-memory thread factory for testing and Phase 3 MVP.
/// Phase 4 will introduce Redis-backed thread factory for persistence.
/// </summary>
public class InMemoryThreadFactory : IAgentThreadFactory
{
    public AgentThread CreateThread() => new OrchestratorInMemoryThread();

    public AgentThread DeserializeThread(
        JsonElement serializedThread,
        JsonSerializerOptions? jsonSerializerOptions = null) =>
        new OrchestratorInMemoryThread(serializedThread, jsonSerializerOptions);
}
