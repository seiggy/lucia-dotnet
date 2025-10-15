using System.Text.Json;
using Microsoft.Agents.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// Factory interface for creating agent threads.
/// Allows pluggable thread implementations (in-memory, Redis-backed, etc.)
/// </summary>
public interface IAgentThreadFactory
{
    /// <summary>
    /// Creates a new agent thread instance.
    /// </summary>
    /// <returns>A new agent thread.</returns>
    AgentThread CreateThread();

    /// <summary>
    /// Deserializes an agent thread from JSON state.
    /// </summary>
    /// <param name="serializedThread">The serialized thread state.</param>
    /// <param name="jsonSerializerOptions">Optional JSON serialization options.</param>
    /// <returns>A deserialized agent thread.</returns>
    AgentThread DeserializeThread(
        JsonElement serializedThread,
        JsonSerializerOptions? jsonSerializerOptions = null);
}
