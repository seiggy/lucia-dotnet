namespace lucia.Agents.CommandTracing;

/// <summary>
/// Outcome of a conversation command processing request.
/// </summary>
public enum CommandTraceOutcome
{
    /// <summary>Request handled locally by a command skill or workflow.</summary>
    CommandHandled,

    /// <summary>No pattern match; request forwarded to LLM orchestrator.</summary>
    LlmFallback,

    /// <summary>No pattern match; LLM processed synchronously (no streaming).</summary>
    LlmCompleted,

    /// <summary>Processing failed with an error.</summary>
    Error,
}
