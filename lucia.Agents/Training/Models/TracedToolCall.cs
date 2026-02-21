namespace lucia.Agents.Training.Models;

/// <summary>
/// A captured tool call with its arguments and result.
/// </summary>
public sealed class TracedToolCall
{
    public required string ToolName { get; set; }

    public string? Arguments { get; set; }

    public string? Result { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
