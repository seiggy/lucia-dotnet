using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// Factory interface for creating agent sessions.
/// Allows pluggable session implementations (in-memory, Redis-backed, etc.)
/// </summary>
public interface IAgentSessionFactory
{
    AgentSession CreateSession();
    AgentSession DeserializeSession(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions = null);
}
