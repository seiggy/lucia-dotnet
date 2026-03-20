namespace lucia.Agents.CommandTracing;

/// <summary>
/// Details captured when a conversation request falls back to LLM processing.
/// </summary>
public sealed record CommandTraceLlmFallback
{
    /// <summary>
    /// ID of the orchestration trace in the existing trace system.
    /// Links to <c>/traces/{orchestrationTraceId}</c> in the dashboard.
    /// </summary>
    public string? OrchestrationTraceId { get; init; }

    /// <summary>Reconstructed prompt sent to the LLM engine.</summary>
    public string? Prompt { get; init; }

    /// <summary>Duration of the LLM processing in milliseconds.</summary>
    public required double DurationMs { get; init; }
}
