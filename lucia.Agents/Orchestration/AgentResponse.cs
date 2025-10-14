using System.Text.Json.Serialization;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Response from an agent executor wrapper.
/// </summary>
public sealed class AgentResponse
{
    /// <summary>
    /// ID of the agent that generated this response.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    /// <summary>
    /// The agent's response content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Whether the agent successfully processed the request.
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>
    /// Optional error message if <see cref="Success"/> is false.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; init; }
}
