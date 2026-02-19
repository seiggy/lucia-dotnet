namespace lucia.Agents.Training.Models;

/// <summary>
/// Represents one agent's complete interaction within a traced request.
/// </summary>
public sealed class AgentExecutionRecord
{
    public required string AgentId { get; set; }

    public string? ModelDeploymentName { get; set; }

    public List<TracedMessage> Messages { get; set; } = [];

    public List<TracedToolCall> ToolCalls { get; set; } = [];

    public double ExecutionDurationMs { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ResponseContent { get; set; }
}
