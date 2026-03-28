namespace lucia.AgentHost.Conversation.Execution;

/// <summary>
/// Thrown when the fast-path entity resolution cannot resolve an entity from the cache
/// and the request should be handed off to the LLM orchestrator.
/// </summary>
internal sealed class EntityResolutionBailException : Exception
{
    /// <summary>
    /// The bail reason tag for telemetry (e.g., <c>cache_miss</c>, <c>no_exact_match</c>).
    /// </summary>
    public string BailReason { get; }

    public EntityResolutionBailException(string bailReason, string message)
        : base(message)
    {
        BailReason = bailReason;
    }
}
