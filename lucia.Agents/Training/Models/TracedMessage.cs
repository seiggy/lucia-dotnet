namespace lucia.Agents.Training.Models;

/// <summary>
/// A single message in the traced conversation (system, user, assistant, or tool).
/// </summary>
public sealed class TracedMessage
{
    public required string Role { get; set; }

    public string? Content { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
