using System.Text.Json.Serialization;

namespace lucia.Agents.Services;

/// <summary>
/// MongoDB document representing an archived agent task.
/// Stored as a denormalized snapshot of the task at completion time.
/// </summary>
public sealed class ArchivedTask
{
    /// <summary>
    /// Task ID (same as the original A2A task ID). Used as the MongoDB _id.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Context/session identifier grouping related tasks.
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; init; }

    /// <summary>
    /// Terminal state the task ended in (Completed, Failed, Canceled).
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Agent identifiers involved in processing this task, extracted from history.
    /// </summary>
    [JsonPropertyName("agentIds")]
    public List<string> AgentIds { get; init; } = [];

    /// <summary>
    /// Original user input text (first user message in history).
    /// </summary>
    [JsonPropertyName("userInput")]
    public string? UserInput { get; init; }

    /// <summary>
    /// Final assistant response text (last agent message in history).
    /// </summary>
    [JsonPropertyName("finalResponse")]
    public string? FinalResponse { get; init; }

    /// <summary>
    /// Number of messages in the conversation history.
    /// </summary>
    [JsonPropertyName("messageCount")]
    public int MessageCount { get; init; }

    /// <summary>
    /// Full serialized message history for detailed inspection.
    /// </summary>
    [JsonPropertyName("history")]
    public List<ArchivedMessage> History { get; init; } = [];

    /// <summary>
    /// When the task was first created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the task reached its terminal state and was archived.
    /// </summary>
    [JsonPropertyName("archivedAt")]
    public DateTime ArchivedAt { get; init; }
}
