namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration for the orchestrator session cache.
/// </summary>
public sealed class SessionCacheOptions
{
    /// <summary>
    /// How long (in minutes) a session remains in cache after the last interaction.
    /// Defaults to 5 minutes — short window for multi-turn within a single voice interaction.
    /// </summary>
    public int SessionCacheLengthMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of conversation turns (user + assistant pairs) retained per session.
    /// Older turns are evicted first.
    /// </summary>
    public int MaxHistoryItems { get; set; } = 20;
}
