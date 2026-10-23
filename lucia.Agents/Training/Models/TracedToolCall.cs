namespace lucia.Agents.Training.Models;

/// <summary>
/// A captured tool call with its arguments and result.
/// </summary>
public sealed class TracedToolCall
{
    public required string ToolName { get; set; }

    public string? CallId { get; set; }

    public string? Arguments { get; set; }

    public string? Result { get; set; }

    /// <summary>Whether the sanitized preview was shortened to the configured UTF-8 byte limit.</summary>
    public bool ResultTruncated { get; set; }

    /// <summary>True when the SDK supplies an exception. Null means the outcome is unknown.</summary>
    public bool? ResultIsError { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
