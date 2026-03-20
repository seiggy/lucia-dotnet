namespace lucia.Agents.CommandTracing;

/// <summary>
/// Outcome of a conversation command processing request.
/// </summary>
public enum CommandTraceOutcome
{
    /// <summary>Command pattern matched and skill executed successfully.</summary>
    CommandHandled,

    /// <summary>No pattern match; request forwarded to LLM orchestrator.</summary>
    LlmFallback,

    /// <summary>No pattern match; LLM processed synchronously (no streaming).</summary>
    LlmCompleted,

    /// <summary>Processing failed with an error.</summary>
    Error,
}
