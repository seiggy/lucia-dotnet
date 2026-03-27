using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Cascading elimination pipeline for deterministic entity resolution.
/// Uses location grounding, domain filtering, and exact/near-exact name matching
/// to achieve &lt;50ms resolution for the happy path.
/// </summary>
public interface ICascadingEntityResolver
{
    /// <summary>
    /// Resolve entities through cascading elimination.
    /// </summary>
    /// <param name="userQuery">Raw or normalized user text (e.g., "turn off the bedroom lights")</param>
    /// <param name="callerArea">Device area from ConversationContext.DeviceArea (may be null)</param>
    /// <param name="speakerId">Speaker identity from ConversationContext (may be null)</param>
    /// <param name="domains">Domain filter based on agent/skill (e.g., ["light", "switch"])</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Resolution result indicating success or bail reason</returns>
    CascadeResult Resolve(
        string userQuery,
        string? callerArea,
        string? speakerId,
        IReadOnlyList<string> domains,
        CancellationToken ct = default);
}
