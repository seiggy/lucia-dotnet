namespace lucia.Agents.Services;

/// <summary>
/// Service for persisting orchestrator conversation sessions.
/// </summary>
public interface ISessionCacheService
{
    /// <summary>
    /// Loads session data for the given session ID, or null if not found / expired.
    /// </summary>
    Task<SessionData?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves session data with a sliding expiration window.
    /// </summary>
    Task SaveSessionAsync(SessionData session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a session from the cache.
    /// </summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
