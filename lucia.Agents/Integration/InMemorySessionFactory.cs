using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// Default in-memory session factory for testing and Phase 3 MVP.
/// Phase 4 will introduce Redis-backed session factory for persistence.
/// </summary>
public sealed class InMemorySessionFactory : IAgentSessionFactory
{
    public AgentSession CreateSession() => new OrchestratorInMemorySession();

    public AgentSession DeserializeSession(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions = null) =>
        new OrchestratorInMemorySession(serializedSession, jsonSerializerOptions);
}
