namespace lucia.Agents.Services;

/// <summary>
/// Serializable session state persisted to the cache.
/// </summary>
public sealed class SessionData
{
    public required string SessionId { get; set; }
    public List<SessionTurn> History { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}
