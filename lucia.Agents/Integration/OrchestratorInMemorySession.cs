using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// In-memory session implementation for orchestrator agent.
/// Uses <see cref="AgentSessionStateBag"/> to persist the session identifier
/// across multi-turn A2A conversations.
/// </summary>
internal sealed class OrchestratorInMemorySession : AgentSession
{
    private const string SessionIdKey = "sessionId";

    /// <summary>
    /// Stable session identifier used for Redis conversation cache keying.
    /// </summary>
    internal string SessionId { get; }

    internal OrchestratorInMemorySession()
    {
        SessionId = Guid.NewGuid().ToString("N");
        StateBag.SetValue(SessionIdKey, SessionId);
    }

    internal OrchestratorInMemorySession(
        JsonElement serializedSessionState,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // Rehydrate state bag from serialized state
        var bag = AgentSessionStateBag.Deserialize(serializedSessionState);
        if (bag.TryGetValue<string>(SessionIdKey, out var existingId) && existingId is not null)
        {
            SessionId = existingId;
        }
        else
        {
            SessionId = Guid.NewGuid().ToString("N");
        }

        StateBag.SetValue(SessionIdKey, SessionId);
    }

    /// <summary>
    /// Serializes session state including our custom SessionId so it persists
    /// across multi-turn A2A conversations (contextId reuse).
    /// </summary>
    internal JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        StateBag.SetValue(SessionIdKey, SessionId);
        return StateBag.Serialize();
    }
}
