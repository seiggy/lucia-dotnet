namespace lucia.Agents.Orchestration;

/// <summary>
/// Response from an agent executor wrapper.
/// </summary>
public sealed class AgentResponse
{
    /// <summary>
    /// ID of the agent that generated this response.
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// The agent's response content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Whether the agent successfully processed the request.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Optional error message if <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    public long ExecutionTimeMs { get; init; }
}
