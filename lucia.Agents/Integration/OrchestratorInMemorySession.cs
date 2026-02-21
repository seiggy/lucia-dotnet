using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// In-memory session implementation for orchestrator agent.
/// Used for Phase 3 MVP. Phase 4 will introduce Redis-backed sessions for persistence.
/// </summary>
internal sealed class OrchestratorInMemorySession : InMemoryAgentSession
{
    /// <summary>
    /// Stable session identifier used for Redis conversation cache keying.
    /// </summary>
    internal string SessionId { get; }

    internal OrchestratorInMemorySession()
    {
        SessionId = Guid.NewGuid().ToString("N");
    }

    internal OrchestratorInMemorySession(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions)
    {
        // Try to extract the sessionId from serialized state, or generate a new one
        if (serializedThreadState.TryGetProperty("sessionId", out var idElement)
            && idElement.GetString() is { } id)
        {
            SessionId = id;
        }
        else
        {
            SessionId = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Serializes session state including our custom SessionId so it persists
    /// across multi-turn A2A conversations (contextId reuse).
    /// </summary>
    internal new JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var baseElement = base.Serialize(jsonSerializerOptions);

        // Inject sessionId into the serialized JSON object
        var obj = JsonNode.Parse(baseElement.GetRawText())?.AsObject();
        if (obj is not null)
        {
            obj["sessionId"] = SessionId;
            return JsonSerializer.SerializeToElement(obj);
        }

        return baseElement;
    }
}
