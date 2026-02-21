namespace lucia.Agents.Services;

/// <summary>
/// A single turn in the conversation history persisted to the session cache.
/// </summary>
public sealed class SessionTurn
{
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
